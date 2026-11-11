using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using News.Domain;
using News.Services;
using PeterJuhasz.Repositories.Blobs;
using PeterJuhasz.Text.Yaml.Parsing;
using PeterJuhasz.Text.Yaml.Serialization;

namespace News.Groups;

/// <summary>
/// Reads the groups file straight into groups and their sources, each source picked by its
/// <c>type</c>, then checks what was read: values are trimmed, and ones the sources cannot be read
/// with are turned away here, where they are written, rather than failing every load after.
/// </summary>
public sealed partial class GroupsFileLoader
{
    /// <summary>
    /// Keys as the file spells them, as in <c>channelId</c>; keys it does not know are passed over.
    /// </summary>
    private static readonly YamlSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = YamlNamingPolicy.CamelCase
    };

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Resolved from the container and handed to the catalog as an instance, so tests and callers can substitute it.")]
    public async Task<IReadOnlyList<NewsGroup>> LoadAsync(IBlob blob, CancellationToken cancellationToken)
    {
        var definition = await ReadDefinitionAsync(blob, cancellationToken);

        if (definition.Groups is not { Count: > 0 } groups)
        {
            throw new InvalidDataException($"Groups file '{blob.Name}' must define at least one group.");
        }

        return [.. groups.Select((group, index) => Validate(group, index, blob.Name))];
    }

    private static async Task<GroupsDefinition> ReadDefinitionAsync(IBlob blob, CancellationToken cancellationToken)
    {
        try
        {
            return await blob.AsYamlObjectRepository<GroupsDefinition>(SerializerOptions).GetOrDefaultAsync(cancellationToken)
                ?? throw new InvalidDataException($"Groups file '{blob.Name}' is empty or missing.");
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException($"Groups file '{blob.Name}' is not valid YAML: {exception.Message}", exception);
        }
        catch (YamlSerializationException exception)
        {
            // the message carries the path of the offending node, as in '$.groups[0].sources[1].type'
            throw new InvalidDataException($"Groups file '{blob.Name}' is not laid out as groups of sources: {exception.Message}", exception);
        }
    }

    private static NewsGroup Validate(NewsGroup? group, int index, string path)
    {
        if (string.IsNullOrWhiteSpace(group?.Title))
        {
            throw new InvalidDataException(
                $"Group at index {index} in '{path}' must include a non-empty title.");
        }

        var title = group.Title.Trim();

        if (group.Sources is not { Count: > 0 } sources)
        {
            throw new InvalidDataException($"Group '{title}' in '{path}' must define at least one source.");
        }

        return new(Title: title, Sources: [.. sources.Select((source, index) => Validate(source, index, title))]);
    }

    private static NewsSource Validate(NewsSource? source, int index, string groupTitle)
    {
        if (source is null)
        {
            throw new InvalidDataException(
                $"Source at index {index} in group '{groupTitle}' must be a mapping of keys and values.");
        }

        if (source.Frequency <= TimeSpan.Zero)
        {
            throw new InvalidDataException(
                $"Source frequency in group '{groupTitle}' must be a positive timespan.");
        }

        source = source with { Title = Optional(source.Title) };

        return source switch
        {
            RssNewsSource rss => rss with
            {
                Url = Require(rss.Url, "url", groupTitle),
                Selector = Optional(rss.Selector)
            },
            AtomNewsSource atom => atom with
            {
                Url = Require(atom.Url, "url", groupTitle),
                Selector = Optional(atom.Selector)
            },
            RedditNewsSource reddit => reddit with { Subreddit = Require(reddit.Subreddit, "subreddit", groupTitle) },
            YouTubeNewsSource youTube => youTube with { ChannelId = Require(youTube.ChannelId, "channelId", groupTitle) },
            GitHubNewsSource gitHub => gitHub with
            {
                Repository = ParseRepository(Require(gitHub.Repository, "repository", groupTitle), groupTitle)
            },
            HtmlNewsSource html => html with
            {
                Url = Require(html.Url, "url", groupTitle),
                ItemSelector = ParseSelector(Require(html.ItemSelector, "itemSelector", groupTitle), "itemSelector", groupTitle),
                TitleSelector = ParseOptionalSelector(html.TitleSelector, "titleSelector", groupTitle),
                DateSelector = ParseOptionalSelector(html.DateSelector, "dateSelector", groupTitle),
                LeadSelector = ParseOptionalSelector(html.LeadSelector, "leadSelector", groupTitle),
                UrlSelector = ParseOptionalSelector(html.UrlSelector, "urlSelector", groupTitle),
                Selector = Optional(html.Selector)
            },
            XNewsSource x => x with { Username = ParseUsername(Require(x.Username, "username", groupTitle), groupTitle) },
            XSearchNewsSource search => search with { Query = Require(search.Query, "query", groupTitle) },
            _ => source
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
                $"Unsupported selector '{value}' for '{fieldName}' in group '{groupTitle}'. Expected a CSS selector the HTML parser supports, as in 'ul.news > li'.");

    private static string? ParseOptionalSelector(string? value, string fieldName, string groupTitle) =>
        Optional(value) is { } selector ? ParseSelector(selector, fieldName, groupTitle) : null;

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

    private static string Require(string? value, string fieldName, string groupTitle) =>
        Optional(value) ?? throw new InvalidDataException(
            $"Missing required '{fieldName}' value for a source in group '{groupTitle}'.");

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The file's root, which lists the groups. Groups and sources in it are read as they are,
    /// which is why a group or source written as an empty list item reads as null.
    /// </summary>
    private sealed record GroupsDefinition(IReadOnlyList<NewsGroup?>? Groups);
}
