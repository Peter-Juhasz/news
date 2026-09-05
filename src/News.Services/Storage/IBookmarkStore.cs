namespace News.Storage;

/// <summary>
/// Keeps the stories set aside to come back to, in a file of the reader's choosing.
/// </summary>
public interface IBookmarkStore
{
    /// <summary>
    /// Reads back every stored bookmark, or <see langword="null"/> where nothing has been stored
    /// yet or what was stored can no longer be read.
    /// </summary>
    Task<BookmarkCollection?> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores every bookmark, replacing whatever was stored before.
    /// </summary>
    Task SaveAsync(BookmarkCollection bookmarks, CancellationToken cancellationToken);
}
