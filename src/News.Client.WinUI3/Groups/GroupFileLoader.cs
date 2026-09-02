using News.Domain;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace News.Client.WinUI3.Groups;

public sealed class GroupFileLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public NewsGroup LoadFromPath(string path)
    {
        var yaml = File.ReadAllText(path);
        var definition = _deserializer.Deserialize<GroupDefinition>(yaml)
            ?? throw new InvalidDataException($"Group definition in '{path}' is empty.");

        if (string.IsNullOrWhiteSpace(definition.Title))
        {
            throw new InvalidDataException($"Group file '{path}' must include a non-empty title.");
        }

        if (definition.Sources.Count == 0)
        {
            throw new InvalidDataException($"Group file '{path}' must define at least one source.");
        }

        var sources = definition.Sources.Select(MapSource).ToList();
        return new NewsGroup(definition.Title.Trim(), sources);
    }

    private static NewsSource MapSource(SourceDefinition source)
    {
        if (source.Frequency <= TimeSpan.Zero)
        {
            throw new InvalidDataException("Source frequency must be a positive timespan.");
        }

        return source.Type.Trim().ToLowerInvariant() switch
        {
            "rss" => new RssNewsSource(
                Url: RequireValue(source.Url, "url"),
                Frequency: source.Frequency),
            "reddit" => new RedditNewsSource(
                Subreddit: RequireValue(source.Subreddit, "subreddit"),
                Frequency: source.Frequency),
            _ => throw new InvalidDataException(
                $"Unsupported source type '{source.Type}'. Expected 'rss' or 'reddit'.")
        };
    }

    private static string RequireValue(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Missing required '{fieldName}' value for source.");
        }

        return value.Trim();
    }

    private sealed class GroupDefinition
    {
        public string Title { get; init; } = string.Empty;

        public List<SourceDefinition> Sources { get; init; } = [];
    }

    private sealed class SourceDefinition
    {
        public string Type { get; init; } = string.Empty;

        public TimeSpan Frequency { get; init; } = TimeSpan.FromMinutes(30);

        public string? Url { get; init; }

        public string? Subreddit { get; init; }
    }
}
