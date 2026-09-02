namespace News.Domain;

/// <summary>
/// A news item together with the source it was loaded from, so per-source
/// presentation settings survive the merge across a group's sources.
/// </summary>
public sealed record SourcedNewsItem(NewsItem Item, NewsSource Source);
