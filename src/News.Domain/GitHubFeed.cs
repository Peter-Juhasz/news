using System.Text.Json.Serialization;

namespace News.Domain;

/// <summary>
/// What a GitHub source follows in its repository. Written down by name, as the groups file
/// spells it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GitHubFeed>))]
public enum GitHubFeed
{
    Releases,
    Tags,
    Commits,
    Issues,
    Pulls
}
