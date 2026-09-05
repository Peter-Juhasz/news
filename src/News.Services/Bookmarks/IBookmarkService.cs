using News.Domain;
using News.Storage;

namespace News.Bookmarks;

/// <summary>
/// The stories set aside to come back to: one flat list, held for as long as the app runs and
/// written to disk as it changes.
/// </summary>
public interface IBookmarkService
{
    /// <summary>
    /// Raised once the list has changed, on the thread the service was initialized on.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Every bookmark, most recently kept first.
    /// </summary>
    IReadOnlyList<BookmarkedNewsItem> Current { get; }

    /// <summary>
    /// Reads back what was stored. Does nothing on a second call.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether the story <paramref name="itemKey"/> identifies is kept.
    /// </summary>
    bool IsBookmarked(string itemKey);

    /// <summary>
    /// Keeps the story, or lets go of it where it was already kept.
    /// </summary>
    Task ToggleAsync(NewsItem item, NewsSource source, CancellationToken cancellationToken);
}
