using News.Domain;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace News.Client.WinUI3.Groups;

public sealed class GroupsFileLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public IReadOnlyList<NewsGroup> LoadFromPath(string path)
    {
        var yaml = File.ReadAllText(path);
        var definition = _deserializer.Deserialize<GroupsDefinition>(yaml)
            ?? throw new InvalidDataException($"Groups file '{path}' is empty.");

        if (definition.Groups.Count == 0)
        {
            throw new InvalidDataException($"Groups file '{path}' must define at least one group.");
        }

        return definition.Groups.Select((group, index) => MapGroup(group, index, path)).ToList();
    }

    private static NewsGroup MapGroup(GroupDefinition group, int index, string path)
    {
        if (string.IsNullOrWhiteSpace(group.Title))
        {
            throw new InvalidDataException(
                $"Group at index {index} in '{path}' must include a non-empty title.");
        }

        var title = group.Title.Trim();

        if (group.Sources.Count == 0)
        {
            throw new InvalidDataException($"Group '{title}' in '{path}' must define at least one source.");
        }

        var sources = group.Sources.Select(source => MapSource(source, title)).ToList();
        return new NewsGroup(title, sources);
    }

    private static NewsSource MapSource(SourceDefinition source, string groupTitle)
    {
        if (source.Frequency <= TimeSpan.Zero)
        {
            throw new InvalidDataException(
                $"Source frequency in group '{groupTitle}' must be a positive timespan.");
        }

        var title = string.IsNullOrWhiteSpace(source.Title) ? null : source.Title.Trim();
        var defaultView = ParseDefaultView(source.DefaultView, groupTitle);

        return source.Type.Trim().ToLowerInvariant() switch
        {
            "rss" => new RssNewsSource(
                Url: RequireValue(source.Url, "url", groupTitle),
                Frequency: source.Frequency)
            {
                Title = title,
                DefaultView = defaultView
            },
            "reddit" => new RedditNewsSource(
                Subreddit: RequireValue(source.Subreddit, "subreddit", groupTitle),
                Frequency: source.Frequency)
            {
                Title = title,
                DefaultView = defaultView
            },
            "youtube" => new YouTubeNewsSource(
                ChannelId: RequireValue(source.ChannelId, "channelId", groupTitle),
                Frequency: source.Frequency)
            {
                Title = title,
                DefaultView = defaultView
            },
            _ => throw new InvalidDataException(
                $"Unsupported source type '{source.Type}' in group '{groupTitle}'. Expected 'rss', 'reddit' or 'youtube'.")
        };
    }

    private static NewsContentMode? ParseDefaultView(string? value, string groupTitle)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Enum.TryParse<NewsContentMode>(value.Trim(), ignoreCase: true, out var mode))
        {
            throw new InvalidDataException(
                $"Unsupported default view '{value}' in group '{groupTitle}'. Expected 'text', 'html' or 'web'.");
        }

        return mode;
    }

    private static string RequireValue(string? value, string fieldName, string groupTitle)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                $"Missing required '{fieldName}' value for a source in group '{groupTitle}'.");
        }

        return value.Trim();
    }

    private sealed class GroupsDefinition
    {
        public List<GroupDefinition> Groups { get; init; } = [];
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

        public string? ChannelId { get; init; }

        public string? Title { get; init; }

        public string? DefaultView { get; init; }
    }
}
