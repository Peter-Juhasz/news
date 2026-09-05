using News.Domain;

namespace News.Storage;

/// <summary>
/// A story kept aside to come back to, together with the source it was read from, as it is
/// written to and read back from disk.
/// </summary>
/// <remarks>
/// The source is stored whole rather than as the handful of settings the reading pane happens to
/// need today, so a bookmark still knows everything about where it came from once that source has
/// been edited or dropped from the groups file — and so a source type that gains a setting later
/// carries it here without this record being taught about it.
/// </remarks>
/// <param name="Item">The story, as its source returned it.</param>
/// <param name="Source">The source it was read from, as that source was configured at the time.</param>
/// <param name="BookmarkedAtUtc">When it was kept.</param>
public sealed record BookmarkedNewsItem(
    NewsItem Item,
    NewsSource Source,
    DateTimeOffset BookmarkedAtUtc);

/// <summary>
/// Every bookmark, as one file. A record rather than a bare list so the file has somewhere to
/// grow without what is already written becoming unreadable.
/// </summary>
public sealed record BookmarkCollection(IReadOnlyList<BookmarkedNewsItem> Items)
{
    public static BookmarkCollection Empty { get; } = new([]);
}
