using News.Domain;

namespace News.Client.WinUI3.ViewModels;

/// <summary>
/// One entry in the group bar, and the stories behind it. A group reading feeds is one of these;
/// so is the bookmarks list, which reads none and is browsed the same way.
/// </summary>
public abstract class NewsGroupViewModelBase
{
    public abstract string Title { get; }

    /// <summary>
    /// Whether the stories behind this entry come from more than one source, so each row is worth
    /// labelling with the one it came from.
    /// </summary>
    public abstract bool HasMultipleSources { get; }

    /// <summary>
    /// Whether there is anything to fetch, which decides whether the refresh button and the pull
    /// gesture do anything here.
    /// </summary>
    public abstract bool CanRefresh { get; }

    /// <summary>
    /// Symbol font glyph shown beside the title in the group bar, for the entries that are not
    /// one of the reader's own groups. Null leaves the entry as a title alone.
    /// </summary>
    public virtual string? Glyph => null;

    public bool HasGlyph => Glyph is not null;

    /// <summary>
    /// Fills this entry with what it was holding when the app last ran.
    /// </summary>
    public abstract Task RestoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Loads whatever has gone stale since it was last read.
    /// </summary>
    public abstract Task RefreshStaleAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Loads everything, however recently it was read.
    /// </summary>
    public abstract Task RefreshAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The stories this entry is holding, as one list, most recent first.
    /// </summary>
    public abstract IReadOnlyList<SourcedNewsItem> GetMergedItems();
}
