using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using News.Domain;

namespace News.Services;

/// <summary>
/// Reads issues and pull requests from GitHub's REST API, which answers unauthenticated requests
/// up to 60 an hour from one address.
/// </summary>
/// <remarks>
/// Both come from the one issues endpoint, since GitHub counts every pull request as an issue:
/// it is the only listing that carries the comment and reaction counts, and it can be asked for
/// the bodies already rendered to HTML. What is left of a page once the other kind is filtered out
/// is the kind asked for, opened over the same stretch of time.
/// </remarks>
public sealed partial class GitHubIssueClient(HttpClient httpClient, ILogger<GitHubIssueClient> logger) : IGitHubIssueClient
{
    /// <summary>
    /// Enough to cover a busy repository's day. Bodies are the bulk of a page, and a busy
    /// repository's run to hundreds of kilobytes for this many.
    /// </summary>
    private const int PageSize = 50;

    public Task<IReadOnlyList<NewsItem>> LoadIssuesAsync(string repository, CancellationToken cancellationToken) =>
        LoadAsync(repository, pullRequests: false, cancellationToken);

    public Task<IReadOnlyList<NewsItem>> LoadPullRequestsAsync(string repository, CancellationToken cancellationToken) =>
        LoadAsync(repository, pullRequests: true, cancellationToken);

    private async Task<IReadOnlyList<NewsItem>> LoadAsync(
        string repository,
        bool pullRequests,
        CancellationToken cancellationToken)
    {
        // Closed ones included: an issue closed before the next load was still opened, and a pull
        // request merged in the meantime is the news rather than a reason to drop it.
        var url = $"https://api.github.com/repos/{repository}/issues?state=all&sort=created&direction=desc&per_page={PageSize}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.html+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var issues = await JsonSerializer.DeserializeAsync(stream, GitHubJsonContext.Default.ListIssue, cancellationToken)
            ?? throw new FormatException("GitHub returned no issue list.");

        var items = issues
            .Where(issue => (issue.PullRequest is not null) == pullRequests)
            .Select(ToNewsItem)
            .ToList();

        logger.LogDebug("Loaded {Count} items from {Url}", items.Count, url);
        return items;
    }

    private static NewsItem ToNewsItem(Issue issue)
    {
        var content = issue.BodyHtml ?? string.Empty;

        return new NewsItem(
            Id: issue.HtmlUrl,
            // When it was opened rather than last touched, which every comment would move.
            Date: issue.CreatedAt,
            Title: string.IsNullOrWhiteSpace(issue.Title) ? "Untitled" : issue.Title.Trim(),
            Lead: HtmlUtility.BuildLead(content),
            Url: issue.HtmlUrl,
            // The author's avatar, as GitHub's own Atom feeds give for a release or commit.
            ImageUrl: issue.User?.AvatarUrl,
            Content: content,
            Reactions: ReadReactions(issue));
    }

    /// <summary>
    /// Thumbs-up as the like, alongside the comment count. Counts of nothing are left out, as most
    /// issues have neither and a row of noughts says nothing.
    /// </summary>
    private static Dictionary<string, long> ReadReactions(Issue issue)
    {
        var reactions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        if (issue.Reactions?.ThumbsUp is > 0 and var likes)
        {
            reactions[NewsReactions.Like] = likes;
        }

        if (issue.Comments > 0)
        {
            reactions[NewsReactions.Comment] = issue.Comments;
        }

        return reactions;
    }

    private sealed record Issue(
        string HtmlUrl,
        string? Title,
        DateTimeOffset CreatedAt,
        string? BodyHtml,
        User? User,
        long Comments,
        Reactions? Reactions,
        PullRequestLinks? PullRequest);

    private sealed record User(string? AvatarUrl);

    private sealed record Reactions([property: JsonPropertyName("+1")] long ThumbsUp);

    /// <summary>
    /// Present on the pull requests the issues endpoint returns, and only on them.
    /// </summary>
    private sealed class PullRequestLinks;

    /// <summary>
    /// Generated at build time rather than reflected over at run time, which a natively compiled
    /// app has nothing to reflect with.
    /// </summary>
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
    [JsonSerializable(typeof(List<Issue>))]
    private sealed partial class GitHubJsonContext : JsonSerializerContext;
}
