using System.Globalization;
using Microsoft.Extensions.Logging;
using News.Domain;
using PeterJuhasz.Text.Html.Model;

namespace News.Services;

/// <summary>
/// Reads the stories a web page lists: the page is downloaded and parsed, every element the
/// source's item selector picks is a story, and its title, date, lead and link are picked out of
/// that element with the selectors the source gives for them.
/// </summary>
public sealed partial class HtmlPageClient(HttpClient httpClient, ILogger<HtmlPageClient> logger) : IHtmlPageClient
{
    public async Task<IReadOnlyList<NewsItem>> LoadAsync(HtmlNewsSource source, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
        request.Headers.Accept.ParseAdd("text/html, application/xhtml+xml");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = HtmlDocument.Parse(html);

        // A redirect moves the page, and the addresses inside it resolve against wherever it landed.
        var page = new Page(
            BaseUri: HtmlAddresses.ResolveBaseUri(document, response.RequestMessage?.RequestUri ?? new Uri(source.Url)),
            Culture: ReadCulture(document),
            LoadedAt: DateTimeOffset.UtcNow);

        var items = HtmlSelector.SelectAll(document, source.ItemSelector)
            .Select(element => ParseItem(element, source, page))
            .OfType<NewsItem>()
            .ToList();

        // A page the item selector finds nothing in has most likely changed its markup, or is not
        // the page asked for at all — a consent wall, a sign-in form. Taking it for a page with no
        // stories would drop the ones already listed, so it fails the load instead, as a feed that
        // cannot be read does.
        if (items.Count == 0)
        {
            throw new FormatException($"The item selector '{source.ItemSelector}' matched no story in {source.Url}.");
        }

        LogLoaded(items.Count, source.Url);
        return items;
    }

    private static NewsItem? ParseItem(HtmlElement element, HtmlNewsSource source, Page page)
    {
        var titleElement = Pick(element, source.TitleSelector, fallback: element);
        var linkElement = Pick(element, source.UrlSelector, fallback: element);
        var dateElement = Pick(element, source.DateSelector, fallback: null);
        var leadElement = Pick(element, source.LeadSelector, fallback: null);

        var title = titleElement is null ? string.Empty : HtmlUtility.CollapseWhitespace(titleElement.TextContent);
        var url = linkElement is null ? null : ReadLink(linkElement, page.BaseUri);

        // Matched, but with nothing in it to show or to open: a spacer, a slot left for an advert.
        if (title.Length == 0 && url is null)
        {
            return null;
        }

        var content = leadElement is null ? string.Empty : HtmlAddresses.WriteWithAbsoluteAddresses(leadElement, page.BaseUri);

        return new NewsItem(
            Id: url,
            Date: ReadDate(dateElement, page.Culture) ?? page.LoadedAt,
            Title: title.Length == 0 ? "Untitled" : title,
            Lead: HtmlUtility.BuildLead(content),
            Url: url ?? string.Empty,
            ImageUrl: ReadImageUrl(element, page.BaseUri),
            Content: content);
    }

    /// <summary>
    /// The element a property's selector picks inside the story, or the fallback where the source
    /// sets no selector for the property.
    /// </summary>
    private static HtmlElement? Pick(HtmlElement item, string? selector, HtmlElement? fallback) =>
        selector is null ? fallback : HtmlSelector.Select(item, selector);

    /// <summary>
    /// The address of the element where it is a link itself, or else of the first link inside it
    /// that leads anywhere on the web.
    /// </summary>
    private static string? ReadLink(HtmlElement element, Uri baseUri) =>
        element.Descendants("a")
            .Prepend(element)
            .Select(candidate => HtmlAddresses.ResolveAddress(candidate.GetAttribute("href")?.Value, baseUri))
            .FirstOrDefault(address => address is not null);

    /// <summary>
    /// The first picture in the story that can be fetched, as the feeds take the first one in an
    /// item's body. A picture loaded lazily names its source in <c>data-src</c>, and leaves a
    /// placeholder carried inside the page in <c>src</c>.
    /// </summary>
    private static string? ReadImageUrl(HtmlElement item, Uri baseUri) =>
        item.Descendants("img")
            .Select(image =>
                HtmlAddresses.ResolveAddress(image.GetAttribute("src")?.Value, baseUri) ??
                HtmlAddresses.ResolveAddress(image.GetAttribute("data-src")?.Value, baseUri))
            .FirstOrDefault(address => address is not null);

    /// <summary>
    /// Reads a date where the page gives one a machine can read — the <c>datetime</c> of a
    /// &lt;time&gt;, the <c>content</c> of a &lt;meta&gt; — and from the element's text otherwise,
    /// as it would be written in the language the page declares, or failing that in English.
    /// </summary>
    private static DateTimeOffset? ReadDate(HtmlElement? element, CultureInfo? culture)
    {
        if (element is null)
        {
            return null;
        }

        var value = HtmlUtility.CollapseWhitespace(
            element.GetAttribute("datetime")?.Value ??
            element.GetAttribute("content")?.Value ??
            element.TextContent);

        IEnumerable<CultureInfo> cultures = culture is null
            ? [CultureInfo.InvariantCulture]
            : [culture, CultureInfo.InvariantCulture];

        foreach (var candidate in cultures)
        {
            if (DateTimeOffset.TryParse(value, candidate, DateTimeStyles.AllowWhiteSpaces, out var date))
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>
    /// The language the page declares itself written in, which is the one its dates are written in.
    /// </summary>
    private static CultureInfo? ReadCulture(HtmlDocument document)
    {
        var language = document.GetElementsByTagName("html").FirstOrDefault()?.GetAttribute("lang")?.Value?.Trim();

        if (string.IsNullOrEmpty(language))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private sealed record Page(Uri BaseUri, CultureInfo? Culture, DateTimeOffset LoadedAt);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded {Count} items from {Url}")]
    private partial void LogLoaded(int count, string url);
}
