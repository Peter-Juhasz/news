using News.Domain;
using News.Services;

namespace News.Client.WinUI3.ViewModels;

public sealed class NewsItemViewModel(NewsItem item, NewsSource source, bool showSourceName = false)
{
    /// <summary>Separates the parts of a metadata line, both here and in the reading pane's byline.</summary>
    public const string Separator = " · ";

    public string? Id { get; } = item.Id;

    public DateTimeOffset Date { get; } = item.Date;

    public string Title { get; } = item.Title;

    public string Lead { get; } = item.Lead;

    public string Url { get; } = item.Url;

    public Uri? UrlUri { get; } = Uri.TryCreate(item.Url, UriKind.Absolute, out var urlUri) ? urlUri : null;

    public string? ImageUrl { get; } = item.ImageUrl;

    public Uri? ImageUri { get; } = Uri.TryCreate(item.ImageUrl, UriKind.Absolute, out var imageUri) ? imageUri : null;

    public string? Content { get; } = item.Content;

    public long? Views { get; } = item.Views;

    /// <summary>
    /// Audience reactions by name, such as "Like", for the sources that publish counts for them.
    /// </summary>
    public IReadOnlyDictionary<string, long> Reactions { get; } = item.Reactions;

    /// <summary>
    /// Name configured on the source, shown instead of the article's host name.
    /// </summary>
    public string? SourceTitle { get; } = source.Title;

    /// <summary>
    /// View this item's source is shown in, if it configures one.
    /// </summary>
    public NewsContentMode? View { get; } = source.View;

    public bool HasImage => ImageUri is not null;

    public string ViewsText => Views is { } views ? $"{FormatCount(views)} views" : string.Empty;

    public string ReactionsText => string.Join(
        Separator,
        Reactions.Select(reaction => $"{FormatCount(reaction.Value)} {reaction.Key}"));

    /// <summary>
    /// Source the item was loaded from, falling back to the article's host name.
    /// </summary>
    public string SourceName => SourceTitle ?? UrlUri?.Host ?? string.Empty;

    /// <summary>
    /// The list row's secondary line: the source name (only where the group merges several
    /// sources and it tells rows apart), then the view count, then the reactions.
    /// </summary>
    public string MetaText => Join(showSourceName ? SourceName : string.Empty, ViewsText, ReactionsText);

    public bool HasMeta => MetaText.Length > 0;

    public string TimeText => Date.ToLocalTime().ToString("HH:mm");

    public string PlainContent => HtmlUtility.ToPlainText(Content);

    public string? HtmlContent => HtmlUtility.LooksLikeHtml(Content) ? Content : null;

    /// <summary>Joins the metadata parts that have something to say, dropping the empty ones.</summary>
    public static string Join(params string?[] parts) =>
        string.Join(Separator, parts.Where(part => !string.IsNullOrEmpty(part)));

    private static string FormatCount(long value) => value switch
    {
        >= 1_000_000_000 => $"{value / 1_000_000_000d:0.#}B",
        >= 1_000_000 => $"{value / 1_000_000d:0.#}M",
        >= 10_000 => $"{value / 1_000d:0.#}K",
        _ => value.ToString("N0")
    };
}
