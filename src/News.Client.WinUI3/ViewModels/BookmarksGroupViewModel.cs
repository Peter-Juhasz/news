using News.Bookmarks;
using News.Domain;
using News.Groups;

namespace News.Client.WinUI3.ViewModels;

/// <summary>
/// The stories set aside to come back to, browsed as though they were a group of their own. It
/// reads no feeds: its list is whatever is bookmarked at the moment it is asked.
/// </summary>
public sealed class BookmarksGroupViewModel(
    IBookmarkService bookmarkService,
    IGroupCatalog groupCatalog) : NewsGroupViewModelBase
{
    /// <summary>Segoe Fluent's filled star, which is also what the reading pane's button shows.</summary>
    private const string FavoriteStarFillGlyph = "\uE735";

    public override string Title => "Bookmarks";

    /// <summary>
    /// Bookmarks are kept from wherever they were read, so every row names where it came from.
    /// </summary>
    public override bool HasMultipleSources => true;

    public override bool CanRefresh => false;

    public override string? Glyph => FavoriteStarFillGlyph;

    /// <summary>
    /// Bookmarks are read once, when the app starts; there is nothing left for a group to
    /// restore or to fetch.
    /// </summary>
    public override Task RestoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override Task RefreshStaleAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override Task RefreshAllAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Every bookmark, ordered as a group orders its own stories so the two read the same way.
    /// </summary>
    /// <remarks>
    /// A bookmark whose source is still configured is shown under that source as it reads now,
    /// rather than as it read when the story was kept — so a selector or a title corrected in the
    /// groups file corrects what the bookmarks show too. The source stored with the bookmark
    /// stands in for the ones that have since been edited away or dropped.
    /// </remarks>
    public override IReadOnlyList<SourcedNewsItem> GetMergedItems()
    {
        var configuredSources = ReadConfiguredSources();

        return bookmarkService.Current
            .Select(bookmark => new SourcedNewsItem(
                bookmark.Item,
                configuredSources.GetValueOrDefault(bookmark.Source.Key) ?? bookmark.Source))
            .OrderByDescending(entry => entry.Item.Date)
            .ThenBy(entry => entry.Item.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The sources the groups file currently defines, by the key that identifies what each of
    /// them reads.
    /// </summary>
    private Dictionary<string, NewsSource> ReadConfiguredSources()
    {
        var sources = new Dictionary<string, NewsSource>(StringComparer.Ordinal);

        foreach (var group in groupCatalog.CurrentGroups)
        {
            foreach (var source in group.Sources)
            {
                // The same feed listed in two groups is the one source as far as a bookmark is
                // concerned; the first of them speaks for it.
                sources.TryAdd(source.Key, source);
            }
        }

        return sources;
    }
}
