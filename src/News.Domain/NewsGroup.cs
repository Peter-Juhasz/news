namespace News.Domain;

public sealed record NewsGroup(string Title, IReadOnlyList<NewsSource> Sources);
