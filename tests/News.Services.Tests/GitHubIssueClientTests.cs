using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using News.Domain;

namespace News.Services.Tests;

/// <summary>
/// GitHub's issues endpoint lists pull requests among the issues, telling them apart only by the
/// pull_request member the pull requests carry. The page below is cut down from dotnet/runtime's,
/// asked for with bodies rendered to HTML.
/// </summary>
[TestClass]
public sealed class GitHubIssueClientTests
{
    private const string Page = """
        [
          {
            "url": "https://api.github.com/repos/dotnet/runtime/issues/134722",
            "html_url": "https://github.com/dotnet/runtime/issues/134722",
            "id": 3456789012,
            "number": 134722,
            "title": "NET11-RC1 Conversion from double to decimal loosing precision",
            "user": {
              "login": "snakefoot",
              "avatar_url": "https://avatars.githubusercontent.com/u/1234567?v=4"
            },
            "labels": [ { "name": "area-System.Numerics" }, { "name": "untriaged" } ],
            "state": "open",
            "comments": 2,
            "created_at": "2026-09-26T18:33:43Z",
            "updated_at": "2026-09-26T18:55:37Z",
            "closed_at": null,
            "body_html": "<h3 dir=\"auto\">Description</h3>\n<p dir=\"auto\">NET11-RC1 Conversion from double to decimal loosing precision</p>",
            "reactions": { "url": "https://api.github.com/repos/dotnet/runtime/issues/134722/reactions", "total_count": 5, "+1": 4, "-1": 0, "laugh": 0, "hooray": 0, "confused": 0, "heart": 1, "rocket": 0, "eyes": 0 }
          },
          {
            "url": "https://api.github.com/repos/dotnet/runtime/issues/134708",
            "html_url": "https://github.com/dotnet/runtime/pull/134708",
            "id": 3456789000,
            "number": 134708,
            "title": "Use a single comparison in Math.Clamp",
            "user": {
              "login": "EgorBo",
              "avatar_url": "https://avatars.githubusercontent.com/u/523221?v=4"
            },
            "labels": [ { "name": "area-System.Runtime" } ],
            "state": "closed",
            "comments": 0,
            "created_at": "2026-09-26T15:12:08Z",
            "updated_at": "2026-09-26T16:54:23Z",
            "closed_at": "2026-09-26T16:54:22Z",
            "draft": false,
            "pull_request": {
              "url": "https://api.github.com/repos/dotnet/runtime/pulls/134708",
              "html_url": "https://github.com/dotnet/runtime/pull/134708",
              "diff_url": "https://github.com/dotnet/runtime/pull/134708.diff",
              "patch_url": "https://github.com/dotnet/runtime/pull/134708.patch",
              "merged_at": "2026-09-26T16:54:22Z"
            },
            "body_html": null,
            "reactions": { "url": "https://api.github.com/repos/dotnet/runtime/issues/134708/reactions", "total_count": 0, "+1": 0, "-1": 0, "laugh": 0, "hooray": 0, "confused": 0, "heart": 0, "rocket": 0, "eyes": 0 }
          }
        ]
        """;

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task LoadIssuesAsync_IssuesPage_MapsEveryFieldOfTheIssue()
    {
        var (client, _) = CreateClient();

        var items = await client.LoadIssuesAsync("dotnet/runtime", TestContext.CancellationToken);

        var item = Assert.ContainsSingle(items);
        Assert.AreEqual("https://github.com/dotnet/runtime/issues/134722", item.Id);
        Assert.AreEqual("https://github.com/dotnet/runtime/issues/134722", item.Url);
        Assert.AreEqual("NET11-RC1 Conversion from double to decimal loosing precision", item.Title);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 26, 18, 33, 43, TimeSpan.Zero), item.Date);
        Assert.AreEqual("Description NET11-RC1 Conversion from double to decimal loosing precision", item.Lead);
        Assert.StartsWith("<h3 dir=\"auto\">Description</h3>", item.Content);
        Assert.AreEqual("https://avatars.githubusercontent.com/u/1234567?v=4", item.ImageUrl);
        Assert.IsNull(item.Views);
        Assert.HasCount(2, item.Reactions);
        Assert.AreEqual(4, item.Reactions[NewsReactions.Like]);
        Assert.AreEqual(2, item.Reactions[NewsReactions.Comment]);
    }

    /// <summary>
    /// A pull request with no body, reactions or comments — merged, which does not take it off the
    /// list: its being merged is as much the news as its being opened.
    /// </summary>
    [TestMethod]
    public async Task LoadPullRequestsAsync_IssuesPage_ReturnsThePullRequestAlone()
    {
        var (client, _) = CreateClient();

        var items = await client.LoadPullRequestsAsync("dotnet/runtime", TestContext.CancellationToken);

        var item = Assert.ContainsSingle(items);
        Assert.AreEqual("https://github.com/dotnet/runtime/pull/134708", item.Url);
        Assert.AreEqual("Use a single comparison in Math.Clamp", item.Title);
        Assert.AreEqual(string.Empty, item.Content);
        Assert.AreEqual(string.Empty, item.Lead);
        Assert.IsEmpty(item.Reactions);
    }

    [TestMethod]
    public async Task LoadIssuesAsync_AsksForTheNewestOfEveryStateWithBodiesRenderedToHtml()
    {
        var (client, handler) = CreateClient();

        await client.LoadIssuesAsync("dotnet/runtime", TestContext.CancellationToken);

        var request = handler.Request;
        Assert.IsNotNull(request);
        Assert.AreEqual(
            "https://api.github.com/repos/dotnet/runtime/issues?state=all&sort=created&direction=desc&per_page=50",
            request.RequestUri?.AbsoluteUri);
        Assert.AreEqual("application/vnd.github.html+json", request.Headers.Accept.Single().MediaType);
        Assert.AreEqual("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
    }

    private static (GitHubIssueClient Client, StubHttpMessageHandler Handler) CreateClient()
    {
        var handler = new StubHttpMessageHandler(Page);
        var client = new GitHubIssueClient(new HttpClient(handler), NullLogger<GitHubIssueClient>.Instance);
        return (client, handler);
    }

    private sealed class StubHttpMessageHandler(string payload) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
