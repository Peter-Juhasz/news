using System.Collections.Frozen;

namespace News.Domain;

/// <summary>
/// Reaction names <see cref="NewsItem.Reactions"/> is keyed by.
/// </summary>
public static class NewsReactions
{
    public const string Like = "Like";

    public const string Comment = "Comment";

    public static readonly IReadOnlyDictionary<string, long> None = FrozenDictionary<string, long>.Empty;
}
