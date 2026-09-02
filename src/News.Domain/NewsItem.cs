namespace News.Domain;

public sealed record NewsItem(
    string? Id,
    DateTimeOffset Date,
    string Title,
    string Lead,
    string Url,
    string? ImageUrl = null,
    string? Content = null,
    long? Views = null,
    IReadOnlyDictionary<string, long>? Reactions = null)
{
    /// <summary>
    /// Audience reactions by name, such as "Like", for the sources that publish counts for them.
    /// </summary>
    public IReadOnlyDictionary<string, long> Reactions { get; init; } =
        Reactions ?? NewsReactions.None;
}
