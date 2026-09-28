using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using News.Domain;

namespace News.Services.Tests;

/// <summary>
/// X's API answers an account's posts and a search alike: the posts, with the accounts and the
/// pictures they refer to gathered beside them. The pages below are cut down to the fields the
/// client asks for.
/// </summary>
[TestClass]
public sealed class XClientTests
{
    private const string User = """
        {
          "data": {
            "id": "2244994945",
            "name": "Developers",
            "username": "XDevelopers",
            "profile_image_url": "https://pbs.twimg.com/profile_images/1683325380441128960/yRsRRjGO_normal.jpg"
          }
        }
        """;

    /// <summary>
    /// A post with a link, a hashtag and a picture, the picture's own link closing the text; and a
    /// long post, whose text is cut off where the note carrying the whole of it is not.
    /// </summary>
    private const string Posts = """
        {
          "data": [
            {
              "id": "1971234567890123456",
              "author_id": "2244994945",
              "created_at": "2026-09-26T16:00:05.000Z",
              "text": "Pay-per-use pricing is live on the X API &amp; you only pay for what you use. Details: https://t.co/Q1w2E3r4T5 #XAPI https://t.co/Z9x8C7v6B5",
              "edit_history_tweet_ids": [ "1971234567890123456" ],
              "attachments": { "media_keys": [ "3_1971234560000000000" ] },
              "entities": {
                "urls": [
                  {
                    "start": 83,
                    "end": 106,
                    "url": "https://t.co/Q1w2E3r4T5",
                    "expanded_url": "https://docs.x.com/x-api/getting-started/pricing",
                    "display_url": "docs.x.com/x-api/getting-…"
                  },
                  {
                    "start": 113,
                    "end": 136,
                    "url": "https://t.co/Z9x8C7v6B5",
                    "expanded_url": "https://x.com/XDevelopers/status/1971234567890123456/photo/1",
                    "display_url": "pic.x.com/Z9x8C7v6B5",
                    "media_key": "3_1971234560000000000"
                  }
                ],
                "hashtags": [ { "start": 107, "end": 112, "tag": "XAPI" } ]
              },
              "public_metrics": { "retweet_count": 120, "reply_count": 45, "like_count": 980, "quote_count": 12, "bookmark_count": 30, "impression_count": 250000 }
            },
            {
              "id": "1970987654321098765",
              "author_id": "2244994945",
              "created_at": "2026-09-25T09:30:00.000Z",
              "text": "Three changes to the v2 API this week:\n\n1. Search counts come back faster, and cost less to ask for\n2. Media uploads take files up to 512 MB, in one request or in chunks\n3. Rate limits are listed in the headers of every response, not only of the ones…",
              "edit_history_tweet_ids": [ "1970987654321098765" ],
              "note_tweet": {
                "text": "Three changes to the v2 API this week:\n\n1. Search counts come back faster, and cost less to ask for\n2. Media uploads take files up to 512 MB, in one request or in chunks\n3. Rate limits are listed in the headers of every response, not only of the ones turned away\n\nThanks to @Support and to everyone who wrote in for the feedback &lt;3",
                "entities": {
                  "mentions": [ { "start": 274, "end": 282, "username": "Support", "id": "17874544" } ]
                }
              },
              "public_metrics": { "retweet_count": 0, "reply_count": 0, "like_count": 0, "quote_count": 0, "bookmark_count": 0, "impression_count": 1200 }
            }
          ],
          "includes": {
            "media": [
              { "media_key": "3_1971234560000000000", "type": "photo", "url": "https://pbs.twimg.com/media/G0aBcDeXcAAq1b2.jpg" }
            ]
          },
          "meta": { "result_count": 2, "newest_id": "1971234567890123456", "oldest_id": "1970987654321098765", "next_token": "7140dibdnow9c7btw4b0" }
        }
        """;

    /// <summary>
    /// Two authors' posts, the one with a video, the other with nothing attached.
    /// </summary>
    private const string SearchResults = """
        {
          "data": [
            {
              "id": "1971300000000000001",
              "author_id": "9676832",
              "created_at": "2026-09-26T18:00:00.000Z",
              "text": "Announcing .NET 11 RC 1 https://t.co/NeT11rC1aB #dotnet https://t.co/ViDeO12345",
              "edit_history_tweet_ids": [ "1971300000000000001" ],
              "attachments": { "media_keys": [ "7_1971299990000000000" ] },
              "entities": {
                "urls": [
                  {
                    "start": 24,
                    "end": 47,
                    "url": "https://t.co/NeT11rC1aB",
                    "expanded_url": "https://devblogs.microsoft.com/dotnet/announcing-dotnet-11-rc-1/",
                    "display_url": "devblogs.microsoft.com/dotnet/announc…"
                  },
                  {
                    "start": 56,
                    "end": 79,
                    "url": "https://t.co/ViDeO12345",
                    "expanded_url": "https://x.com/dotnet/status/1971300000000000001/video/1",
                    "display_url": "pic.x.com/ViDeO12345",
                    "media_key": "7_1971299990000000000"
                  }
                ],
                "hashtags": [ { "start": 48, "end": 55, "tag": "dotnet" } ]
              },
              "public_metrics": { "retweet_count": 310, "reply_count": 52, "like_count": 1400, "quote_count": 40, "bookmark_count": 90, "impression_count": 180000 }
            },
            {
              "id": "1971200000000000002",
              "author_id": "1500000000000000000",
              "created_at": "2026-09-26T12:15:00.000Z",
              "text": "Upgraded our services to #dotnet 11 RC 1 today, and their startup time dropped by a third.",
              "edit_history_tweet_ids": [ "1971200000000000002" ],
              "entities": { "hashtags": [ { "start": 25, "end": 32, "tag": "dotnet" } ] },
              "public_metrics": { "retweet_count": 1, "reply_count": 0, "like_count": 7, "quote_count": 0, "bookmark_count": 0, "impression_count": 640 }
            }
          ],
          "includes": {
            "users": [
              { "id": "9676832", "name": ".NET", "username": "dotnet", "profile_image_url": "https://pbs.twimg.com/profile_images/1000000000000000000/dotnet_normal.png" },
              { "id": "1500000000000000000", "name": "Contoso Engineering", "username": "contoso_eng", "profile_image_url": "https://pbs.twimg.com/profile_images/1500000000000000001/contoso_normal.png" }
            ],
            "media": [
              { "media_key": "7_1971299990000000000", "type": "video", "preview_image_url": "https://pbs.twimg.com/amplify_video_thumb/1971299990000000000/img/aBcDeFgHiJkLmNoP.jpg" }
            ]
          },
          "meta": { "result_count": 2, "newest_id": "1971300000000000001", "oldest_id": "1971200000000000002" }
        }
        """;

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task LoadUserPostsAsync_PostsPage_MapsEveryFieldOfThePost()
    {
        var (client, _) = CreateClient();

        var items = await client.LoadUserPostsAsync("XDevelopers", TestContext.CancellationToken);

        Assert.HasCount(2, items);
        var item = items[0];
        Assert.AreEqual("https://x.com/XDevelopers/status/1971234567890123456", item.Id);
        Assert.AreEqual("https://x.com/XDevelopers/status/1971234567890123456", item.Url);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 26, 16, 0, 5, TimeSpan.Zero), item.Date);
        Assert.AreEqual(
            "Pay-per-use pricing is live on the X API & you only pay for what you use. Details: docs.x.com/x-api…",
            item.Title);
        Assert.AreEqual(string.Empty, item.Lead);
        Assert.AreEqual(
            "<p>Pay-per-use pricing is live on the X API &amp; you only pay for what you use. Details: " +
            "<a href=\"https://docs.x.com/x-api/getting-started/pricing\">docs.x.com/x-api/getting-…</a> " +
            "<a href=\"https://x.com/hashtag/XAPI\">#XAPI</a></p>" +
            "<p><img src=\"https://pbs.twimg.com/media/G0aBcDeXcAAq1b2.jpg\"></p>",
            item.Content);
        Assert.AreEqual("https://pbs.twimg.com/media/G0aBcDeXcAAq1b2.jpg", item.ImageUrl);
        Assert.AreEqual(250000, item.Views);
        Assert.HasCount(2, item.Reactions);
        Assert.AreEqual(980, item.Reactions[NewsReactions.Like]);
        Assert.AreEqual(45, item.Reactions[NewsReactions.Comment]);
    }

    /// <summary>
    /// The note's text in place of the cut-off one, its blank lines as paragraphs and its other
    /// line breaks kept. With nothing attached, the account's avatar is the post's picture.
    /// </summary>
    [TestMethod]
    public async Task LoadUserPostsAsync_LongPost_ReadsTheWholeOfItsNote()
    {
        var (client, _) = CreateClient();

        var items = await client.LoadUserPostsAsync("XDevelopers", TestContext.CancellationToken);

        var item = items[1];
        Assert.AreEqual(
            "Three changes to the v2 API this week: 1. Search counts come back faster, and cost less to ask for…",
            item.Title);
        Assert.AreEqual(
            "<p>Three changes to the v2 API this week:</p>" +
            "<p>1. Search counts come back faster, and cost less to ask for<br>" +
            "2. Media uploads take files up to 512 MB, in one request or in chunks<br>" +
            "3. Rate limits are listed in the headers of every response, not only of the ones turned away</p>" +
            "<p>Thanks to <a href=\"https://x.com/Support\">@Support</a> and to everyone who wrote in for the feedback &lt;3</p>",
            item.Content);
        Assert.AreEqual("https://pbs.twimg.com/profile_images/1683325380441128960/yRsRRjGO_normal.jpg", item.ImageUrl);
        Assert.AreEqual(1200, item.Views);
        Assert.IsEmpty(item.Reactions);
    }

    /// <summary>
    /// The account is asked for by its handle once, and its posts by its ID from then on; its
    /// address is the handle as X spells it rather than as it was asked for.
    /// </summary>
    [TestMethod]
    public async Task LoadUserPostsAsync_LooksTheAccountUpOnceThenAsksForItsOwnPosts()
    {
        var (client, handler) = CreateClient();

        await client.LoadUserPostsAsync("xdevelopers", TestContext.CancellationToken);
        var items = await client.LoadUserPostsAsync("xdevelopers", TestContext.CancellationToken);

        var postsUrl =
            "https://api.x.com/2/users/2244994945/tweets?max_results=20&exclude=replies,retweets" +
            "&tweet.fields=created_at,author_id,public_metrics,entities,attachments,note_tweet" +
            "&expansions=attachments.media_keys&media.fields=url,preview_image_url";
        Assert.AreSequenceEqual(
            new[]
            {
                "https://api.x.com/2/users/by/username/xdevelopers?user.fields=profile_image_url",
                postsUrl,
                postsUrl
            },
            handler.Requests.Select(request => request.RequestUri?.AbsoluteUri).ToArray());
        Assert.AreEqual("https://x.com/XDevelopers/status/1971234567890123456", items[0].Url);
    }

    /// <summary>
    /// X answers an account it does not have with an error in place of the account, rather than
    /// with an error status.
    /// </summary>
    [TestMethod]
    public async Task LoadUserPostsAsync_NoSuchAccount_Throws()
    {
        var (client, _) = CreateClient(userLookup: """
            {
              "errors": [
                {
                  "value": "nobody_here_1",
                  "detail": "Could not find user with username: [nobody_here_1].",
                  "title": "Not Found Error",
                  "resource_type": "user",
                  "parameter": "username",
                  "resource_id": "nobody_here_1",
                  "type": "https://api.twitter.com/2/problems/resource-not-found"
                }
              ]
            }
            """);

        var exception = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => client.LoadUserPostsAsync("nobody_here_1", TestContext.CancellationToken));

        Assert.Contains("Could not find user with username: [nobody_here_1].", exception.Message);
    }

    [TestMethod]
    public async Task SearchAsync_SearchResults_TakesEachPostsAuthorFromThoseGatheredBesideThem()
    {
        var (client, _) = CreateClient();

        var items = await client.SearchAsync("#dotnet", TestContext.CancellationToken);

        Assert.HasCount(2, items);

        var announcement = items[0];
        Assert.AreEqual("https://x.com/dotnet/status/1971300000000000001", announcement.Url);
        Assert.AreEqual("Announcing .NET 11 RC 1 devblogs.microsoft.com/dotnet/announc… #dotnet", announcement.Title);
        Assert.AreEqual(".NET @dotnet", announcement.Lead);
        Assert.AreEqual(
            "https://pbs.twimg.com/amplify_video_thumb/1971299990000000000/img/aBcDeFgHiJkLmNoP.jpg",
            announcement.ImageUrl);
        Assert.EndsWith(
            "<p><img src=\"https://pbs.twimg.com/amplify_video_thumb/1971299990000000000/img/aBcDeFgHiJkLmNoP.jpg\"></p>",
            announcement.Content);
        Assert.AreEqual(1400, announcement.Reactions[NewsReactions.Like]);

        var post = items[1];
        Assert.AreEqual("https://x.com/contoso_eng/status/1971200000000000002", post.Url);
        Assert.AreEqual("Contoso Engineering @contoso_eng", post.Lead);
        Assert.AreEqual("https://pbs.twimg.com/profile_images/1500000000000000001/contoso_normal.png", post.ImageUrl);
        Assert.AreEqual(640, post.Views);
        Assert.HasCount(1, post.Reactions);
        Assert.AreEqual(7, post.Reactions[NewsReactions.Like]);
    }

    [TestMethod]
    public async Task SearchAsync_AsksForTheNewestPostsMatchingTheQueryWithTheirAuthors()
    {
        var (client, handler) = CreateClient();

        await client.SearchAsync("#dotnet -is:retweet lang:en", TestContext.CancellationToken);

        var request = Assert.ContainsSingle(handler.Requests);
        Assert.AreEqual(
            "https://api.x.com/2/tweets/search/all?query=%23dotnet%20-is%3Aretweet%20lang%3Aen&sort_order=recency&max_results=20" +
            "&tweet.fields=created_at,author_id,public_metrics,entities,attachments,note_tweet" +
            "&expansions=author_id,attachments.media_keys&media.fields=url,preview_image_url&user.fields=profile_image_url",
            request.RequestUri?.AbsoluteUri);
    }

    /// <summary>
    /// A search matching nothing answers with a count of none and no data at all.
    /// </summary>
    [TestMethod]
    public async Task SearchAsync_NothingMatches_ReturnsNoItems()
    {
        var (client, _) = CreateClient(search: """{ "meta": { "result_count": 0 } }""");

        var items = await client.SearchAsync("#nothing_matches_this", TestContext.CancellationToken);

        Assert.IsEmpty(items);
    }

    /// <summary>
    /// The status alone does not tell a plan without the endpoint from an account out of credit;
    /// what X says in the body does.
    /// </summary>
    [TestMethod]
    public async Task SearchAsync_Refused_ThrowsWithWhatXSaid()
    {
        var (client, _) = CreateClient(searchStatus: HttpStatusCode.Forbidden, search: """
            {
              "client_id": "12345678",
              "detail": "When authenticating requests to the Twitter API v2 endpoints, you must use keys and tokens from a Twitter developer App that is attached to a Project.",
              "registration_url": "https://developer.x.com/en/docs/projects/overview",
              "title": "Client Forbidden",
              "required_enrollment": "Appropriate Level of API Access",
              "reason": "client-not-enrolled",
              "type": "https://api.twitter.com/2/problems/client-forbidden"
            }
            """);

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => client.SearchAsync("#dotnet", TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("you must use keys and tokens from a Twitter developer App that is attached to a Project", exception.Message);
    }

    private static (XClient Client, StubHttpMessageHandler Handler) CreateClient(
        string userLookup = User,
        string search = SearchResults,
        HttpStatusCode searchStatus = HttpStatusCode.OK)
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.StartsWith("/2/users/by/username/", StringComparison.Ordinal) => (HttpStatusCode.OK, userLookup),
            "/2/users/2244994945/tweets" => (HttpStatusCode.OK, Posts),
            "/2/tweets/search/all" => (searchStatus, search),
            _ => (HttpStatusCode.NotFound, """{ "title": "Not Found", "detail": "Not Found" }""")
        });

        var client = new XClient(new HttpClient(handler), NullLogger<XClient>.Instance);
        return (client, handler);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Payload)> respond)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            var (status, payload) = respond(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
