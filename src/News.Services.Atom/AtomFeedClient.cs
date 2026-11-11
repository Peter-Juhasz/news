using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using News.Domain;

namespace News.Services;

public sealed class AtomFeedClient(HttpClient httpClient, ILogger<AtomFeedClient> logger)
    : FeedClient(httpClient, logger), IAtomFeedClient
{
    /// <summary>
    /// YouTube's own feed namespace, which identifies the feed as YouTube's and so tells the
    /// parser that its media:starRating can be read as a like count.
    /// </summary>
    private const string YouTubeNamespace = "http://www.youtube.com/xml/schemas/2015";

    public override IReadOnlyList<NewsItem> Parse(XDocument document)
    {
        var root = ReadRoot(document, "feed", "Atom");
        var isYouTubeFeed = IsYouTubeFeed(root);
        var entryNodes = root.Elements().Where(element => element.Name.LocalName == "entry");
        return entryNodes.Select(entry => ParseEntry(entry, isYouTubeFeed)).ToList();
    }

    private static bool IsYouTubeFeed(XElement root) =>
        root.Attributes().Any(attribute =>
            attribute.IsNamespaceDeclaration &&
            attribute.Value == YouTubeNamespace) ||
        root.Elements().Any(element => element.Name.NamespaceName == YouTubeNamespace);

    private static NewsItem ParseEntry(XElement entry, bool isYouTubeFeed)
    {
        var linkElement = entry
            .Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "link" &&
                string.Equals((string?)element.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase))
            ?? entry.Elements().FirstOrDefault(element => element.Name.LocalName == "link");

        // Entries without content or summary (YouTube's, for one) carry their body in media:description.
        var mediaDescription = ReadMediaValue(entry, "description");
        var summary = ReadElementValue(entry, "summary", fallback: null);
        var contentValue = ReadElementValue(entry, "content", fallback: null) ??
                           summary ??
                           mediaDescription ??
                           string.Empty;
        var lead = HtmlUtility.BuildLead(summary ?? mediaDescription ?? contentValue);
        var url = (string?)linkElement?.Attribute("href") ?? ReadMediaContentUrl(entry);

        return new NewsItem(
            Id: ReadElementValue(entry, "id", fallback: null),
            Date: ReadDate(
                ReadElementValue(entry, "updated", fallback: null) ??
                ReadElementValue(entry, "published", fallback: null)),
            Title: ReadElementValue(entry, "title", fallback: null) ??
                   ReadMediaValue(entry, "title") ??
                   "Untitled",
            Lead: string.IsNullOrWhiteSpace(lead) ? HtmlUtility.BuildLead(contentValue) : lead,
            Url: url ?? string.Empty,
            ImageUrl: ReadImageUrl(entry) ?? HtmlUtility.FindFirstImageUrl(contentValue),
            Content: contentValue,
            Views: ReadViews(entry),
            Reactions: ReadReactions(entry, isYouTubeFeed));
    }

    /// <summary>
    /// YouTube states its like count as a media:starRating count, every vote being five stars.
    /// Other feeds use the element for an actual rating, so the like only follows from YouTube.
    /// </summary>
    private static Dictionary<string, long>? ReadReactions(XElement entry, bool isYouTubeFeed)
    {
        if (!isYouTubeFeed)
        {
            return null;
        }

        var likes = (string?)MediaElements(entry, "starRating").FirstOrDefault()?.Attribute("count");
        return long.TryParse(likes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { [NewsReactions.Like] = parsed }
            : null;
    }
}
