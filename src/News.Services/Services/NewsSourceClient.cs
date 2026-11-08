using News.Domain;

namespace News.Services;

/// <summary>
/// Hands a source to the first registered provider that reads its kind.
/// </summary>
public sealed class NewsSourceClient(IEnumerable<INewsSourceProvider> providers) : INewsSourceClient
{
    private readonly INewsSourceProvider[] _providers = [.. providers];

    public Task<IReadOnlyList<NewsItem>> LoadAsync(NewsSource source, CancellationToken cancellationToken) =>
        Array.Find(_providers, provider => provider.CanLoad(source)) switch
        {
            { } provider => provider.LoadAsync(source, cancellationToken),
            null => throw new NotSupportedException($"Unsupported source type: {source.GetType().Name}.")
        };
}
