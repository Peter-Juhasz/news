using System.Xml;
using Microsoft.Extensions.Logging;
using News.Domain;
using News.Services;
using News.Storage;

namespace News.Client.WinUI3.ViewModels;

public sealed class NewsGroupViewModel(
    NewsGroup group,
    INewsSourceClient newsSourceClient,
    INewsItemStore newsItemStore,
    ILogger<NewsGroupViewModel> logger)
{
    private readonly Dictionary<NewsSource, SourceCacheState> _sourceStates = group.Sources.ToDictionary(
        keySelector: source => source,
        elementSelector: _ => SourceCacheState.Empty);

    public NewsGroup Group { get; } = group;

    public string Title => Group.Title;

    /// <summary>
    /// Whether the group merges more than one source, so items are worth labelling with theirs.
    /// </summary>
    public bool HasMultipleSources => Group.Sources.Count > 1;

    public IReadOnlyDictionary<NewsSource, DateTimeOffset?> SourceLastLoadedAt =>
        _sourceStates.ToDictionary(
            keySelector: entry => entry.Key,
            elementSelector: entry => entry.Value.LoadedAtUtc);

    /// <summary>
    /// Fills the group with the items its sources last returned, read back from disk, so the list
    /// shows those the moment the group is opened rather than staying empty until the sources are
    /// fetched again. Sources already loaded in this session keep what they have.
    /// </summary>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var snapshot = await newsItemStore.LoadAsync(Group.Title, cancellationToken);
        if (snapshot is null)
        {
            return;
        }

        var storedBySourceKey = new Dictionary<string, NewsSourceSnapshot>(StringComparer.Ordinal);
        foreach (var storedSource in snapshot.Sources)
        {
            storedBySourceKey[storedSource.SourceKey] = storedSource;
        }

        foreach (var source in Group.Sources)
        {
            if (_sourceStates[source].LoadedAtUtc is not null)
            {
                continue;
            }

            if (!storedBySourceKey.TryGetValue(source.Key, out var stored))
            {
                continue;
            }

            // The load time is restored along with the items, so a source read again moments
            // later is only fetched once the frequency it configures has passed.
            _sourceStates[source] = new SourceCacheState(stored.LoadedAtUtc, stored.Items);
        }
    }

    public async Task RefreshStaleAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var loadedAnySource = false;

        foreach (var source in Group.Sources)
        {
            var currentState = _sourceStates[source];
            if (!source.IsStale(currentState.LoadedAtUtc, now))
            {
                continue;
            }

            loadedAnySource |= await TryRefreshSourceAsync(source, cancellationToken);
        }

        if (loadedAnySource)
        {
            await SaveAsync(cancellationToken);
        }
    }

    public async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        var loadedAnySource = false;

        foreach (var source in Group.Sources)
        {
            loadedAnySource |= await TryRefreshSourceAsync(source, cancellationToken);
        }

        if (loadedAnySource)
        {
            await SaveAsync(cancellationToken);
        }
    }

    public IReadOnlyList<SourcedNewsItem> GetMergedItems()
    {
        var merged = new Dictionary<string, SourcedNewsItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var (source, state) in _sourceStates)
        {
            foreach (var item in state.Items)
            {
                var key = string.IsNullOrWhiteSpace(item.Id) ? item.Url : item.Id;
                if (key is null)
                {
                    continue;
                }

                if (!merged.TryGetValue(key, out var current) || item.Date > current.Item.Date)
                {
                    merged[key] = new SourcedNewsItem(item, source);
                }
            }
        }

        return merged
            .Values
            .OrderByDescending(entry => entry.Item.Date)
            .ToList();
    }

    /// <summary>
    /// Writes down what every loaded source is holding, once the group has finished the load it
    /// was asked for. Sources that have never loaded are left out rather than stored empty, so a
    /// feed that was down keeps the items stored for it last time.
    /// </summary>
    private Task SaveAsync(CancellationToken cancellationToken)
    {
        var sources = _sourceStates
            .Where(entry => entry.Value.LoadedAtUtc is not null)
            .Select(entry => new NewsSourceSnapshot(entry.Key.Key, entry.Value.LoadedAtUtc!.Value, entry.Value.Items))
            .ToList();

        return newsItemStore.SaveAsync(Group.Title, new NewsGroupSnapshot(sources), cancellationToken);
    }

    /// <summary>
    /// Loads one source, keeping its last items where it cannot be reached. Returns whether the
    /// items it holds were replaced, so a group that loaded nothing new is not stored again.
    /// </summary>
    private async Task<bool> TryRefreshSourceAsync(NewsSource source, CancellationToken cancellationToken)
    {
        try
        {
            var loadedItems = await newsSourceClient.LoadAsync(source, cancellationToken);
            _sourceStates[source] = new SourceCacheState(DateTimeOffset.UtcNow, loadedItems);
            return true;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Failed to load source {SourceType}. Keeping last successful items.", source.GetType().Name);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Loading source {SourceType} timed out. Keeping last successful items.", source.GetType().Name);
        }
        catch (FormatException exception)
        {
            logger.LogWarning(exception, "Feed parsing failed for source {SourceType}. Keeping last successful items.", source.GetType().Name);
        }
        catch (XmlException exception)
        {
            logger.LogWarning(exception, "Feed XML parsing failed for source {SourceType}. Keeping last successful items.", source.GetType().Name);
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(exception, "Feed data was invalid for source {SourceType}. Keeping last successful items.", source.GetType().Name);
        }

        return false;
    }

    private sealed record SourceCacheState(DateTimeOffset? LoadedAtUtc, IReadOnlyList<NewsItem> Items)
    {
        public static SourceCacheState Empty { get; } = new(null, []);
    }
}
