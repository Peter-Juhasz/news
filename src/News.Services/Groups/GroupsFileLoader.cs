using System.Text.RegularExpressions;
using News.Domain;
using News.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace News.Groups;

public sealed partial class GroupsFileLoader
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
        var view = ParseView(source.View, groupTitle);
        var selector = string.IsNullOrWhiteSpace(source.Selector) ? null : source.Selector.Trim();

        return source.Type.Trim().ToLowerInvariant() switch
        {
            "rss" => new RssNewsSource(
                Url: RequireValue(source.Url, "url", groupTitle),
                Frequency: source.Frequency)
            {
                Title = title,
                View = view,
                Selector = selector
            },
            "atom" => new AtomNewsSource(
                Url: RequireValue(source.Url, "url", groupTitle),
                Frequency: source.Frequency)
            {
                Title = title,
                View = view,
                Selector = selector
            },
            "reddit" => new RedditNewsSource(
                Subreddit: RequireValue(source.Subreddit, "subreddit", groupTitle),
                Frequency: source.Frequency)
            {
                Title = title,
                View = view
            },
            "youtube" => new YouTubeNewsSource(
                ChannelId: RequireValue(source.ChannelId, "channelId", groupTitle),
                Frequency: source.Frequency)
            {
                Title = title,
                View = view
            },
            "github" => new GitHubNewsSource(
                Repository: ParseRepository(RequireValue(source.Repository, "repository", groupTitle), groupTitle),
                Feed: ParseGitHubFeed(source.Feed, groupTitle),
                Frequency: source.Frequency)
            {
                Title = title,
                View = view
            },
            "html" => new HtmlNewsSource(
                Url: RequireValue(source.Url, "url", groupTitle),
                ItemSelector: ParseSelector(RequireValue(source.Item, "item", groupTitle), "item", groupTitle),
                Frequency: source.Frequency)
            {
                Title = title,
                View = view,
                Selector = selector,
                TitleSelector = ParseOptionalSelector(source.Properties?.Title, "properties.title", groupTitle),
                DateSelector = ParseOptionalSelector(source.Properties?.Date, "properties.date", groupTitle),
                LeadSelector = ParseOptionalSelector(source.Properties?.Lead, "properties.lead", groupTitle),
                UrlSelector = ParseOptionalSelector(source.Properties?.Url, "properties.url", groupTitle)
            },
            "x" => new XNewsSource(
                Username: ParseUsername(RequireValue(source.Username, "username", groupTitle), groupTitle),
                Frequency: source.Frequency)
            {
                Title = title,
                View = view
            },
            "x-search" => new XSearchNewsSource(
                Query: RequireValue(source.Query, "query", groupTitle),
                Frequency: source.Frequency)
            {
                Title = title,
                View = view
            },
            _ => throw new InvalidDataException(
                $"Unsupported source type '{source.Type}' in group '{groupTitle}'. Expected 'rss', 'atom', 'reddit', 'youtube', 'github', 'html', 'x' or 'x-search'.")
        };
    }

    /// <summary>
    /// A handle as X allows one, with or without the @ it is written with: the value is set into
    /// X's URLs as it is written.
    /// </summary>
    private static string ParseUsername(string value, string groupTitle)
    {
        var username = value.TrimStart('@');

        return UsernameRegex().IsMatch(username)
            ? username
            : throw new InvalidDataException(
                $"Invalid username '{value}' in group '{groupTitle}'. Expected a handle on X, as in 'XDevelopers'.");
    }

    [GeneratedRegex("^[A-Za-z0-9_]{1,15}$")]
    private static partial Regex UsernameRegex();

    /// <summary>
    /// A selector the page's parser can run. One it cannot is turned away here, where it is written,
    /// rather than failing every load of the page after.
    /// </summary>
    private static string ParseSelector(string value, string fieldName, string groupTitle) =>
        HtmlSelector.IsSupported(value)
            ? value
            : throw new InvalidDataException(
                $"Unsupported selector '{value}' for '{fieldName}' in group '{groupTitle}'. Expected an element name, classes, IDs and exact attribute values, with descendants separated by spaces, as in 'ul.news li'.");

    private static string? ParseOptionalSelector(string? value, string fieldName, string groupTitle) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseSelector(value.Trim(), fieldName, groupTitle);

    /// <summary>
    /// Owner and name, and nothing else: the value is set into GitHub's URLs as it is written.
    /// </summary>
    private static string ParseRepository(string value, string groupTitle) =>
        RepositoryRegex().IsMatch(value)
            ? value
            : throw new InvalidDataException(
                $"Invalid repository '{value}' in group '{groupTitle}'. Expected owner and name, as in 'dotnet/runtime'.");

    [GeneratedRegex("^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$")]
    private static partial Regex RepositoryRegex();

    /// <summary>
    /// Releases where none is named, being what a repository publishes for its readers.
    /// </summary>
    private static GitHubFeed ParseGitHubFeed(string? value, string groupTitle)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GitHubFeed.Releases;
        }

        // Names only: Enum.Parse would take a number too, a position in the list that reordering it
        // would quietly change.
        var name = Enum.GetNames<GitHubFeed>()
            .FirstOrDefault(candidate => string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                $"Unsupported feed '{value}' in group '{groupTitle}'. Expected 'releases', 'tags', 'commits', 'issues' or 'pulls'.");

        return Enum.Parse<GitHubFeed>(name);
    }

    private static NewsContentMode? ParseView(string? value, string groupTitle)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Enum.TryParse<NewsContentMode>(value.Trim(), ignoreCase: true, out var mode))
        {
            throw new InvalidDataException(
                $"Unsupported view '{value}' in group '{groupTitle}'. Expected 'text', 'html' or 'web'.");
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

        public string? Repository { get; init; }

        public string? Feed { get; init; }

        public string? Username { get; init; }

        public string? Query { get; init; }

        public string? Title { get; init; }

        public string? View { get; init; }

        public string? Selector { get; init; }

        public string? Item { get; init; }

        public ItemPropertiesDefinition? Properties { get; init; }
    }

    /// <summary>
    /// Selectors for the properties of a story an HTML source picks out of its page, each run
    /// inside the story's element.
    /// </summary>
    private sealed class ItemPropertiesDefinition
    {
        public string? Title { get; init; }

        public string? Date { get; init; }

        public string? Lead { get; init; }

        public string? Url { get; init; }
    }
}
