using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;

namespace News.Services;

/// <summary>
/// Reads an article's body out of its own page: the page is downloaded and parsed, and the element
/// the source's selector picks is handed back as HTML. Feeds that publish only a teaser can be read
/// in full this way.
/// </summary>
public sealed class ArticleContentClient(HttpClient httpClient, ILogger<ArticleContentClient> logger)
    : IArticleContentClient
{
    private static readonly HtmlParser Parser = new();

    public async Task<string?> LoadAsync(string url, string selector, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await Parser.ParseDocumentAsync(stream, cancellationToken);

        IElement? element;
        try
        {
            element = document.QuerySelector(selector);
        }
        catch (DomException exception)
        {
            logger.LogWarning(exception, "Selector '{Selector}' is not a valid CSS selector.", selector);
            return null;
        }

        if (element is null)
        {
            logger.LogDebug("Selector '{Selector}' matched nothing in {Url}.", selector, url);
            return null;
        }

        // A redirect moves the page, and the addresses inside it resolve against wherever it landed.
        var baseUri = ResolveBaseUri(document, response.RequestMessage?.RequestUri ?? new Uri(url));
        MakeAddressesAbsolute(element, baseUri);

        logger.LogDebug("Loaded the '{Selector}' content of {Url}.", selector, url);
        return element.OuterHtml;
    }

    /// <summary>
    /// The address relative links resolve against: the page's own, unless it declares another
    /// with a &lt;base&gt; element.
    /// </summary>
    private static Uri ResolveBaseUri(IDocument document, Uri requestUri)
    {
        var declared = document.QuerySelector("base[href]")?.GetAttribute("href");

        return Uri.TryCreate(requestUri, declared, out var baseUri) ? baseUri : requestUri;
    }

    /// <summary>
    /// Rewrites the addresses in the extracted element to absolute ones. The fragment leaves the
    /// page it came from, so a relative source or link would have nothing left to resolve against.
    /// </summary>
    private static void MakeAddressesAbsolute(IElement root, Uri baseUri)
    {
        foreach (var element in root.QuerySelectorAll("[src], [href]").Prepend(root))
        {
            MakeAddressAbsolute(element, "src", baseUri);
            MakeAddressAbsolute(element, "href", baseUri);
        }
    }

    private static void MakeAddressAbsolute(IElement element, string attributeName, Uri baseUri)
    {
        var value = element.GetAttribute(attributeName);

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // Anything that does not resolve to a web address — a data URI, a mailto link — is left as
        // it stands, since making it absolute would say nothing more than it already does.
        if (Uri.TryCreate(baseUri, value, out var absolute) && absolute.Scheme is "http" or "https")
        {
            element.SetAttribute(attributeName, absolute.AbsoluteUri);
        }
    }
}
