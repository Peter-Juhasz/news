using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Microsoft.Extensions.Logging;
using News.Domain;

namespace News.Services;

/// <summary>
/// Reads posts from X's API, signed as the app by <see cref="XAuthenticationHandler"/>: an
/// account's own posts, or the whole archive's matching a search, newest first.
/// </summary>
/// <remarks>
/// X charges for every post, account and picture it returns, though not for one it has already
/// returned that day (UTC); so a page is kept short, and only what is shown is asked for.
/// </remarks>
public sealed partial class XClient(HttpClient httpClient, ILogger<XClient> logger) : IXClient
{
    private const string ApiUrl = "https://api.x.com/2";

    /// <summary>
    /// The newest posts and no more, as each is paid for. Searches take no fewer than 10.
    /// </summary>
    private const int PageSize = 20;

    /// <summary>
    /// A post has no title, so its opening stands in for one: enough to fill the two lines the list
    /// gives a title, without the reading pane's heading repeating the whole of the post under it.
    /// </summary>
    private const int TitleLength = 100;

    /// <summary>
    /// What is asked for with each post beyond its ID and text: its date and author, its counts,
    /// the links in its text, what is attached to it, and the whole of a long post, whose text is
    /// otherwise cut off at 280 characters.
    /// </summary>
    private const string PostFields = "tweet.fields=created_at,author_id,public_metrics,entities,attachments,note_tweet";

    /// <summary>
    /// A picture's address, or a video's still.
    /// </summary>
    private const string MediaFields = "media.fields=url,preview_image_url";

    /// <summary>
    /// The avatar, beyond the ID, name and handle every account comes with.
    /// </summary>
    private const string UserFields = "user.fields=profile_image_url";

    /// <summary>
    /// Writes the text back out with its letters as they are, rather than turning everything
    /// outside Basic Latin into a character reference.
    /// </summary>
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>
    /// Accounts by handle. An account keeps its ID for good, so it is looked up once for as long as
    /// the client lives — the app's run, held as it is by the news source client — rather than on
    /// every load, each of which X would charge for.
    /// </summary>
    private readonly ConcurrentDictionary<string, User> _users = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<NewsItem>> LoadUserPostsAsync(string username, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(username, cancellationToken);

        // The account's own posts: a reply is half of a conversation, and a repost's text is cut
        // short, both of them better read on X.
        var url = $"{ApiUrl}/users/{user.Id}/tweets?max_results={PageSize}&exclude=replies,retweets" +
                  $"&{PostFields}&expansions=attachments.media_keys&{MediaFields}";

        var page = await GetAsync(url, XJsonContext.Default.Page, cancellationToken);
        var items = ToNewsItems(page, _ => user, showsAuthor: false);

        logger.LogDebug("Loaded {Count} items from {Url}", items.Count, url);
        return items;
    }

    public async Task<IReadOnlyList<NewsItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var url = $"{ApiUrl}/tweets/search/all?query={Uri.EscapeDataString(query)}&sort_order=recency&max_results={PageSize}" +
                  $"&{PostFields}&expansions=author_id,attachments.media_keys&{MediaFields}&{UserFields}";

        var page = await GetAsync(url, XJsonContext.Default.Page, cancellationToken);

        var authors = new Dictionary<string, User>(StringComparer.Ordinal);
        foreach (var user in page.Includes?.Users ?? [])
        {
            authors.TryAdd(user.Id, user);
        }

        var items = ToNewsItems(
            page,
            post => post.AuthorId is { } authorId ? authors.GetValueOrDefault(authorId) : null,
            showsAuthor: true);

        logger.LogDebug("Loaded {Count} items from {Url}", items.Count, url);
        return items;
    }

    private async Task<User> FindUserAsync(string username, CancellationToken cancellationToken)
    {
        if (_users.TryGetValue(username, out var user))
        {
            return user;
        }

        var result = await GetAsync(
            $"{ApiUrl}/users/by/username/{Uri.EscapeDataString(username)}?{UserFields}",
            XJsonContext.Default.UserResult,
            cancellationToken);

        // An account that is not there, or is suspended, is answered with an error in place of it.
        user = result.Data ?? throw new InvalidDataException(
            $"X has no account '{username}' to read: {Describe(result.Errors)}");

        return _users[username] = user;
    }

    private async Task<T> GetAsync<T>(string url, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // What went wrong is in the body — no credit left, a plan without the endpoint — where
            // the status alone would not say.
            throw new HttpRequestException(
                $"X answered {(int)response.StatusCode} ({response.ReasonPhrase}) to {url}: {await ReadProblemAsync(stream, cancellationToken)}",
                inner: null,
                response.StatusCode);
        }

        return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken)
            ?? throw new FormatException($"X returned nothing from {url}.");
    }

    private static async Task<string> ReadProblemAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await JsonSerializer.DeserializeAsync(stream, XJsonContext.Default.Problem, cancellationToken);
            return problem?.Detail ?? problem?.Title ?? "no reason given.";
        }
        catch (JsonException)
        {
            return "no reason given.";
        }
    }

    private static string Describe(List<Problem>? errors) =>
        errors is [var error, ..] ? error.Detail ?? error.Title ?? "no reason given." : "no reason given.";

    private static List<NewsItem> ToNewsItems(Page page, Func<Post, User?> findAuthor, bool showsAuthor)
    {
        // A page with no posts on it comes without data, as does one X would not return — a
        // protected or suspended account's — which says why in an error instead.
        if (page.Data is null)
        {
            return page.Errors is { Count: > 0 }
                ? throw new InvalidDataException($"X returned no posts: {Describe(page.Errors)}")
                : [];
        }

        var media = new Dictionary<string, Media>(StringComparer.Ordinal);
        foreach (var attachment in page.Includes?.Media ?? [])
        {
            media.TryAdd(attachment.MediaKey, attachment);
        }

        return page.Data
            .Select(post => ToNewsItem(post, findAuthor(post), media, showsAuthor))
            .ToList();
    }

    private static NewsItem ToNewsItem(
        Post post,
        User? author,
        Dictionary<string, Media> media,
        bool showsAuthor)
    {
        // A long post's text is cut off at 280 characters, and comes whole as a note besides.
        var (text, entities) = post.NoteTweet is { } note ? (note.Text, note.Entities) : (post.Text, post.Entities);

        // Pictures as they are, and a still for a video or GIF.
        var images = (post.Attachments?.MediaKeys ?? [])
            .Select(key => media.GetValueOrDefault(key))
            .Select(attachment => attachment?.Url ?? attachment?.PreviewImageUrl)
            .OfType<string>()
            .ToList();

        var content = BuildContent(text, entities, images);
        var title = HtmlUtility.BuildLead(content, TitleLength);

        // A post is found by its ID whatever handle its address gives, x.com/i/ being X's own
        // stand-in for one it does not know.
        var url = $"https://x.com/{author?.Username ?? "i"}/status/{post.Id}";

        return new NewsItem(
            Id: url,
            Date: post.CreatedAt,
            Title: string.IsNullOrEmpty(title) ? "Untitled" : title,
            // The author, for the search, whose posts are anyone's; an account's own posts are all
            // its own, and saying so on every one would say nothing.
            Lead: showsAuthor && author is not null ? $"{author.Name} @{author.Username}".TrimStart() : string.Empty,
            Url: url,
            ImageUrl: images.FirstOrDefault() ?? author?.ProfileImageUrl,
            Content: content,
            Views: post.PublicMetrics?.ImpressionCount,
            Reactions: ReadReactions(post.PublicMetrics));
    }

    /// <summary>
    /// The text as HTML: a paragraph for each run of lines, with its short links spelled out and
    /// its mentions and hashtags linked as X links them, followed by the pictures posted with it.
    /// </summary>
    private static string BuildContent(string text, Entities? entities, List<string> images)
    {
        var links = new Dictionary<string, UrlEntity>(StringComparer.Ordinal);
        foreach (var link in entities?.Urls ?? [])
        {
            links.TryAdd(link.Url, link);
        }

        var mentions = (entities?.Mentions ?? []).Select(mention => mention.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hashtags = (entities?.Hashtags ?? []).Select(hashtag => hashtag.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // X escapes the ampersands and angle brackets in a post's text as HTML would. A picture's
        // link is dropped, being shown as the picture itself below.
        text = WebUtility.HtmlDecode(text).ReplaceLineEndings("\n");
        foreach (var link in links.Values.Where(link => link.MediaKey is not null))
        {
            text = text.Replace(link.Url, string.Empty, StringComparison.Ordinal);
        }

        var html = new StringBuilder();

        foreach (var paragraph in ParagraphBreakRegex().Split(text.Trim()).Where(paragraph => paragraph.Length > 0))
        {
            html.Append("<p>");

            var lines = paragraph.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (index > 0)
                {
                    html.Append("<br>");
                }

                AppendLine(html, lines[index], links, mentions, hashtags);
            }

            html.Append("</p>");
        }

        foreach (var image in images)
        {
            html.Append("<p><img src=\"").Append(Encoder.Encode(image)).Append("\"></p>");
        }

        return html.ToString();
    }

    private static void AppendLine(
        StringBuilder html,
        string line,
        Dictionary<string, UrlEntity> links,
        HashSet<string> mentions,
        HashSet<string> hashtags)
    {
        var position = 0;

        foreach (Match match in EntityRegex().Matches(line))
        {
            html.Append(Encoder.Encode(line[position..match.Index]));

            var link = match.Groups switch
            {
                var groups when groups["url"].Success =>
                    links.TryGetValue(match.Value, out var entity)
                        ? Anchor(entity.ExpandedUrl ?? entity.Url, entity.DisplayUrl ?? entity.ExpandedUrl ?? entity.Url)
                        : Anchor(match.Value, match.Value),
                var groups when groups["mention"].Success && mentions.Contains(groups["mention"].Value) =>
                    Anchor($"https://x.com/{groups["mention"].Value}", match.Value),
                var groups when groups["hashtag"].Success && hashtags.Contains(groups["hashtag"].Value) =>
                    Anchor($"https://x.com/hashtag/{Uri.EscapeDataString(groups["hashtag"].Value)}", match.Value),
                _ => Encoder.Encode(match.Value)
            };

            html.Append(link);
            position = match.Index + match.Length;
        }

        html.Append(Encoder.Encode(line[position..]));
    }

    private static string Anchor(string href, string text) =>
        $"<a href=\"{Encoder.Encode(href)}\">{Encoder.Encode(text)}</a>";

    /// <summary>
    /// Counts of nothing are left out, as a row of noughts says nothing.
    /// </summary>
    private static Dictionary<string, long> ReadReactions(PublicMetrics? metrics)
    {
        var reactions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        if (metrics?.LikeCount is > 0 and var likes)
        {
            reactions[NewsReactions.Like] = likes;
        }

        if (metrics?.ReplyCount is > 0 and var replies)
        {
            reactions[NewsReactions.Comment] = replies;
        }

        return reactions;
    }

    /// <summary>
    /// A blank line, which X's apps show as the start of a new paragraph.
    /// </summary>
    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphBreakRegex();

    /// <summary>
    /// What X links in a post's text: its short links, and the mentions and hashtags it recognised
    /// as such — an address's @ or a C#'s # is neither.
    /// </summary>
    [GeneratedRegex(@"(?<url>https?://t\.co/\w+)|(?<![\w@])@(?<mention>\w{1,15})|(?<![\w#])#(?<hashtag>[\p{L}\p{M}\p{N}_]+)")]
    private static partial Regex EntityRegex();

    private sealed record Page(List<Post>? Data, Includes? Includes, List<Problem>? Errors);

    private sealed record Post(
        string Id,
        string Text,
        DateTimeOffset CreatedAt,
        string? AuthorId,
        PublicMetrics? PublicMetrics,
        Entities? Entities,
        Attachments? Attachments,
        NoteTweet? NoteTweet);

    private sealed record NoteTweet(string Text, Entities? Entities);

    private sealed record Entities(List<UrlEntity>? Urls, List<Mention>? Mentions, List<Hashtag>? Hashtags);

    /// <summary>
    /// A short link in the text, spelled out. One carrying a media key links to a picture posted
    /// with the post.
    /// </summary>
    private sealed record UrlEntity(string Url, string? ExpandedUrl, string? DisplayUrl, string? MediaKey);

    private sealed record Mention(string Username);

    private sealed record Hashtag(string Tag);

    private sealed record Attachments(List<string>? MediaKeys);

    private sealed record PublicMetrics(long LikeCount, long ReplyCount, long? ImpressionCount);

    private sealed record Includes(List<User>? Users, List<Media>? Media);

    private sealed record Media(string MediaKey, string? Url, string? PreviewImageUrl);

    private sealed record UserResult(User? Data, List<Problem>? Errors);

    private sealed record User(string Id, string Username, string? Name, string? ProfileImageUrl);

    /// <summary>
    /// What X says went wrong, whether it answered with an error status or with an error in place
    /// of what was asked for.
    /// </summary>
    private sealed record Problem(string? Title, string? Detail);

    /// <summary>
    /// Generated at build time rather than reflected over at run time, which a natively compiled
    /// app has nothing to reflect with.
    /// </summary>
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
    [JsonSerializable(typeof(Page))]
    [JsonSerializable(typeof(UserResult))]
    [JsonSerializable(typeof(Problem))]
    private sealed partial class XJsonContext : JsonSerializerContext;
}
