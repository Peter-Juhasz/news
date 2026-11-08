using News.Domain;

namespace News.Services;

public sealed class HtmlNewsSourceProvider(IHtmlPageClient htmlPageClient) : INewsSourceProvider
{
    public bool CanLoad(NewsSource source) => source is HtmlNewsSource;

    public Task<IReadOnlyList<NewsItem>> LoadAsync(NewsSource source, CancellationToken cancellationToken) =>
        source switch
        {
            HtmlNewsSource html => htmlPageClient.LoadAsync(html, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported source type: {source.GetType().Name}.")
        };
}
