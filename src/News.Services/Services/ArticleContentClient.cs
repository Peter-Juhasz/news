using Microsoft.Extensions.Logging;
using PeterJuhasz.Text.Html.Model;

namespace News.Services;

/// <summary>
/// Reads an article's body out of its own page: the page is downloaded and parsed, and the element
/// the source's selector picks is handed back as HTML. Feeds that publish only a teaser can be read
/// in full this way.
/// </summary>
public sealed class ArticleContentClient(HttpClient httpClient, ILogger<ArticleContentClient> logger)
    : IArticleContentClient
{
    public async Task<string?> LoadAsync(string url, string selector, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = HtmlDocument.Parse(html);

        HtmlElement? element;
        try
        {
            element = document.QuerySelector(selector);
        }
        catch (ArgumentException exception)
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
        var baseUri = HtmlAddresses.ResolveBaseUri(document, response.RequestMessage?.RequestUri ?? new Uri(url));

        logger.LogDebug("Loaded the '{Selector}' content of {Url}.", selector, url);
        return HtmlAddresses.WriteWithAbsoluteAddresses(element, baseUri);
    }
}
