using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using News.Domain;
using News.Services;
using WinRT;

namespace News.Client.WinUI3.ViewModels;

/// <summary>
/// One row of the list. A row outlives the load it was built for: a story its source returns
/// again is handed the item it came back as, rather than being replaced by a second row built
/// for the same story — which is what lets the list be updated where it changed instead of
/// being rebuilt.
/// </summary>
/// <remarks>
/// The row's picture is bound by name rather than compiled in (see the list's item template), so
/// the properties a binding can find are generated here at build time: a natively compiled app has
/// no reflection to look them up with.
/// </remarks>
[GeneratedBindableCustomProperty]
public sealed partial class NewsItemViewModel : ObservableObject
{
    /// <summary>Separates the parts of a metadata line, both here and in the reading pane's byline.</summary>
    public const string Separator = " · ";

    private NewsItem _item;
    private NewsSource _source;
    private bool _showSourceName;
    private Uri? _urlUri;
    private Uri? _imageUri;
    private string? _fetchedContent;
    private string? _searchQuery;

    public NewsItemViewModel(NewsItem item, NewsSource source, bool showSourceName = false)
    {
        _item = item;
        _source = source;
        _showSourceName = showSourceName;
        _urlUri = ParseUri(item.Url);
        _imageUri = ParseUri(item.ImageUrl);
        Key = item.Key;
    }

    /// <summary>
    /// Identifies the story this row shows, so a load that returns it again finds the row it
    /// already has. Fixed for the life of the row.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The story this row shows, as its source returned it. Handed over whole when the row is
    /// bookmarked, so what is kept is the story rather than the row's reading of it.
    /// </summary>
    public NewsItem Item => _item;

    /// <summary>
    /// The source it was read from, as that source is configured now.
    /// </summary>
    public NewsSource Source => _source;

    public string? Id => _item.Id;

    public DateTimeOffset Date => _item.Date;

    public string Title => _item.Title;

    public string Lead => _item.Lead;

    public string Url => _item.Url;

    public Uri? UrlUri => _urlUri;

    public string? ImageUrl => _item.ImageUrl;

    public Uri? ImageUri => _imageUri;

    public string? Content => _item.Content;

    public long? Views => _item.Views;

    /// <summary>
    /// Audience reactions by name, such as "Like", for the sources that publish counts for them.
    /// </summary>
    public IReadOnlyDictionary<string, long> Reactions => _item.Reactions;

    /// <summary>
    /// Name configured on the source, shown instead of the article's host name.
    /// </summary>
    public string? SourceTitle => _source.Title;

    /// <summary>
    /// View this item's source is shown in, if it configures one.
    /// </summary>
    public NewsContentMode? View => _source.View;

    /// <summary>
    /// CSS selector the source configures for picking the body out of the item's own page.
    /// </summary>
    public string? ContentSelector => _source switch
    {
        RssNewsSource rss => rss.Selector,
        AtomNewsSource atom => atom.Selector,
        HtmlNewsSource html => html.Selector,
        _ => null
    };

    /// <summary>
    /// Whether this item's body is fetched from its page rather than taken from the feed: its
    /// source names a selector, and there is a page to run it against.
    /// </summary>
    public bool HasFetchedContent => ContentSelector is not null && UrlUri is not null;

    /// <summary>
    /// Body fetched from the item's own page, kept so returning to this story shows it again
    /// without downloading the page a second time.
    /// </summary>
    public string? FetchedContent
    {
        get => _fetchedContent;
        set
        {
            if (SetProperty(ref _fetchedContent, value))
            {
                RaiseContentChanged();
            }
        }
    }

    /// <summary>
    /// Body the reading pane shows: the one fetched from the page where there is one, and the
    /// feed's own otherwise.
    /// </summary>
    public string? DisplayContent => FetchedContent ?? Content;

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
    public string MetaText => Join(_showSourceName ? SourceName : string.Empty, ViewsText, ReactionsText);

    public bool HasMeta => MetaText.Length > 0;

    public string TimeText => Date.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

    /// <summary>
    /// Query the list is filtered by, so a row can mark where it matches it. A row that stays in
    /// the list as the query changes is told about it rather than drawn again, so the marks are
    /// watched rather than read once.
    /// </summary>
    public string? SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    /// <summary>
    /// Hands this row the story as its source has just returned it. Returns whether anything the
    /// row shows actually changed, so a load that brought back the same story again costs the
    /// list nothing.
    /// </summary>
    public bool Update(NewsItem item, NewsSource source, bool showSourceName)
    {
        if (Describes(item) && ReferenceEquals(_source, source) && _showSourceName == showSourceName)
        {
            return false;
        }

        var previousUrl = _item.Url;

        _item = item;
        _source = source;
        _showSourceName = showSourceName;
        _urlUri = ParseUri(item.Url);
        _imageUri = ParseUri(item.ImageUrl);

        // A story that moved to another address is another page; whatever was fetched from the
        // old one no longer describes it.
        if (!string.Equals(previousUrl, item.Url, StringComparison.Ordinal))
        {
            _fetchedContent = null;
        }

        RaiseItemChanged();
        return true;
    }

    /// <summary>
    /// Whether <paramref name="item"/> says the same as the one this row is showing. Compared
    /// field by field rather than as a whole, because two loads of the same story carry equal
    /// reaction counts in dictionaries of their own, which are not equal to each other.
    /// </summary>
    private bool Describes(NewsItem item) =>
        string.Equals(_item.Id, item.Id, StringComparison.Ordinal) &&
        _item.Date == item.Date &&
        string.Equals(_item.Title, item.Title, StringComparison.Ordinal) &&
        string.Equals(_item.Lead, item.Lead, StringComparison.Ordinal) &&
        string.Equals(_item.Url, item.Url, StringComparison.Ordinal) &&
        string.Equals(_item.ImageUrl, item.ImageUrl, StringComparison.Ordinal) &&
        string.Equals(_item.Content, item.Content, StringComparison.Ordinal) &&
        _item.Views == item.Views &&
        HaveSameReactions(_item.Reactions, item.Reactions);

    private static bool HaveSameReactions(
        IReadOnlyDictionary<string, long> current,
        IReadOnlyDictionary<string, long> loaded)
    {
        if (ReferenceEquals(current, loaded))
        {
            return true;
        }

        if (current.Count != loaded.Count)
        {
            return false;
        }

        foreach (var (name, count) in loaded)
        {
            if (!current.TryGetValue(name, out var currentCount) || currentCount != count)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tells everything drawn from the item that it is drawn from a newer one now.
    /// </summary>
    private void RaiseItemChanged()
    {
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(Date));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Lead));
        OnPropertyChanged(nameof(Url));
        OnPropertyChanged(nameof(UrlUri));
        OnPropertyChanged(nameof(ImageUrl));
        OnPropertyChanged(nameof(ImageUri));
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Views));
        OnPropertyChanged(nameof(Reactions));
        OnPropertyChanged(nameof(SourceTitle));
        OnPropertyChanged(nameof(SourceName));
        OnPropertyChanged(nameof(View));
        OnPropertyChanged(nameof(ContentSelector));
        OnPropertyChanged(nameof(HasFetchedContent));
        OnPropertyChanged(nameof(FetchedContent));
        OnPropertyChanged(nameof(ViewsText));
        OnPropertyChanged(nameof(ReactionsText));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(HasMeta));
        OnPropertyChanged(nameof(TimeText));
        RaiseContentChanged();
    }

    private void RaiseContentChanged()
    {
        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(PlainContent));
        OnPropertyChanged(nameof(HtmlContent));
    }

    /// <summary>
    /// Whether the title or the lead contains <paramref name="query"/> — the two lines a reader
    /// scans down the list, so the two the search box looks through.
    /// </summary>
    public bool Matches(string query) =>
        Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Lead.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    public string PlainContent => HtmlUtility.ToPlainText(DisplayContent);

    public string? HtmlContent => HtmlUtility.LooksLikeHtml(DisplayContent) ? DisplayContent : null;

    /// <summary>Joins the metadata parts that have something to say, dropping the empty ones.</summary>
    public static string Join(params string?[] parts) =>
        string.Join(Separator, parts.Where(part => !string.IsNullOrEmpty(part)));

    private static Uri? ParseUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    private static string FormatCount(long value) => value switch
    {
        >= 1_000_000_000 => $"{value / 1_000_000_000d:0.#}B",
        >= 1_000_000 => $"{value / 1_000_000d:0.#}M",
        >= 10_000 => $"{value / 1_000d:0.#}K",
        _ => value.ToString("N0", CultureInfo.CurrentCulture)
    };
}
