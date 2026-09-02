namespace News.Domain;

public sealed record NewsItem(
    string? Id,
    DateTimeOffset Date,
    string Title,
    string Lead,
    string Url,
    string? ImageUrl = null,
    string? Content = null);
