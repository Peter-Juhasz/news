using System.Xml;
using Microsoft.Extensions.Logging;
using News.Domain;
using News.Services;

namespace News.Client.WinUI3.ViewModels;

public sealed class NewsGroupViewModel(
    NewsGroup group,
    INewsSourceClient newsSourceClient,
    ILogger<NewsGroupViewModel> logger)
{
    private readonly Dictionary<NewsSource, SourceCacheState> _sourceStates = group.Sources.ToDictionary(
        keySelector: source => source,
        elementSelector: _ => SourceCacheState.Empty);

    public NewsGroup Group { get; } = group;

    public string Title => Group.Title;

    public IReadOnlyDictionary<NewsSource, DateTimeOffset?> SourceLastLoadedAt =>
        _sourceStates.ToDictionary(
            keySelector: entry => entry.Key,
            elementSelector: entry => entry.Value.LoadedAtUtc);

    public async Task RefreshStaleAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var source in Group.Sources)
        {
            var currentState = _sourceStates[source];
            if (!source.IsStale(currentState.LoadedAtUtc, now))
            {
                continue;
            }

            await TryRefreshSourceAsync(source, cancellationToken);
        }
    }

    public async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        foreach (var source in Group.Sources)
        {
            await TryRefreshSourceAsync(source, cancellationToken);
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

    private async Task TryRefreshSourceAsync(NewsSource source, CancellationToken cancellationToken)
    {
        try
        {
            var loadedItems = await newsSourceClient.LoadAsync(source, cancellationToken);
            _sourceStates[source] = new SourceCacheState(DateTimeOffset.UtcNow, loadedItems);
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
    }

    private sealed record SourceCacheState(DateTimeOffset? LoadedAtUtc, IReadOnlyList<NewsItem> Items)
    {
        public static SourceCacheState Empty { get; } = new(null, []);
    }
}
