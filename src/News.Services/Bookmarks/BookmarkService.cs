using Microsoft.Extensions.Logging;
using News.Domain;
using News.Storage;

namespace News.Bookmarks;

/// <summary>
/// Holds the bookmarks for as long as the app runs, and is the only thing that writes them.
/// </summary>
public sealed class BookmarkService : IBookmarkService, IDisposable
{
    /// <summary>
    /// How bookmarks are told apart. The comparison a group merges its sources under, so a story
    /// carried by two feeds is one bookmark rather than one per feed that carries it.
    /// </summary>
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    private readonly IBookmarkStore _store;
    private readonly ILogger<BookmarkService> _logger;

    /// <summary>
    /// Guards the list below. Bookmarks are kept and let go of from the UI thread, but the first
    /// read may finish on another one.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Serializes the writes, so two bookmarks kept in quick succession reach the file in the
    /// order they were made rather than racing each other onto it.
    /// </summary>
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private readonly Dictionary<string, BookmarkedNewsItem> _byKey = new(KeyComparer);

    /// <summary>
    /// The bookmarks in the order they are handed out, most recently kept first.
    /// </summary>
    private readonly List<BookmarkedNewsItem> _order = [];

    private IReadOnlyList<BookmarkedNewsItem> _current = [];
    private SynchronizationContext? _uiContext;
    private bool _initialized;

    public BookmarkService(IBookmarkStore store, ILogger<BookmarkService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<BookmarkedNewsItem> Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        _uiContext ??= SynchronizationContext.Current;
        _initialized = true;

        var stored = await _store.LoadAsync(cancellationToken);
        if (stored is null)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var bookmark in stored.Items)
            {
                var key = bookmark.Item.Key;

                // A file carrying the same story twice — hand-edited, or merged from two
                // machines — keeps the first of them rather than being refused outright.
                if (key.Length == 0 || !_byKey.TryAdd(key, bookmark))
                {
                    continue;
                }

                _order.Add(bookmark);
            }

            Publish();
        }

        _logger.LogDebug("Restored {Count} bookmarks.", _current.Count);
        RaiseChanged();
    }

    public bool IsBookmarked(string itemKey)
    {
        lock (_gate)
        {
            return _byKey.ContainsKey(itemKey);
        }
    }

    public async Task ToggleAsync(NewsItem item, NewsSource source, CancellationToken cancellationToken)
    {
        var key = item.Key;
        if (key.Length == 0)
        {
            // Nothing to recognise it by later, so nothing that could be let go of again.
            return;
        }

        lock (_gate)
        {
            if (_byKey.Remove(key, out var kept))
            {
                _order.Remove(kept);
            }
            else
            {
                var added = new BookmarkedNewsItem(item, source, DateTimeOffset.UtcNow);
                _byKey.Add(key, added);
                _order.Insert(0, added);
            }

            Publish();
        }

        RaiseChanged();
        await SaveAsync(cancellationToken);
    }

    public void Dispose() => _saveLock.Dispose();

    /// <summary>
    /// Takes a copy of the list for readers to hold, so one being walked is not the one a
    /// bookmark is being added to. Called while <see cref="_gate"/> is held.
    /// </summary>
    private void Publish() => _current = _order.ToArray();

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            // Every save writes the whole list, so one that waited here writes what the list
            // says now rather than what it said when the save was asked for.
            await _store.SaveAsync(new BookmarkCollection(Current), cancellationToken);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void RaiseChanged()
    {
        var handler = Changed;
        if (handler is null)
        {
            return;
        }

        if (_uiContext is null)
        {
            handler(this, EventArgs.Empty);
            return;
        }

        _uiContext.Post(_ => handler(this, EventArgs.Empty), null);
    }
}
