namespace News.Storage;

/// <summary>
/// Keeps the items a group last loaded, so the list has something to show on the next start
/// while the sources are fetched again.
/// </summary>
public interface INewsItemStore
{
    /// <summary>
    /// Reads back what was stored for the group, or <see langword="null"/> where nothing was
    /// stored for it or what was stored can no longer be read.
    /// </summary>
    Task<NewsGroupSnapshot?> LoadAsync(string groupTitle, CancellationToken cancellationToken);

    /// <summary>
    /// Stores what the group is showing, replacing whatever it stored before.
    /// </summary>
    Task SaveAsync(string groupTitle, NewsGroupSnapshot snapshot, CancellationToken cancellationToken);
}
