namespace News.Domain;

/// <summary>
/// Reaction names <see cref="NewsItem.Reactions"/> is keyed by.
/// </summary>
public static class NewsReactions
{
    public const string Like = "Like";

    public static IReadOnlyDictionary<string, long> None { get; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
}
