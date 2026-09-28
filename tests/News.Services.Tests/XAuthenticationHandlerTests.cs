using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using News.Configuration;

namespace News.Services.Tests;

/// <summary>
/// X's application-only authentication: the app's key and secret are traded for its bearer token,
/// which then signs every request. The key and secret below are X's documented example.
/// </summary>
[TestClass]
public sealed class XAuthenticationHandlerTests
{
    private const string ApiKey = "xvz1evFS4wEEPTGEFPHBog";

    private const string ApiSecret = "L8qq9PZyRg6ieKGEKhZolGC0vJWLw8iEJ88DRdyOg";

    private const string PostsUrl = "https://api.x.com/2/users/2244994945/tweets";

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task SendAsync_FirstRequest_TradesTheKeyAndSecretForTheAppsToken()
    {
        var server = new StubServer();
        using var client = CreateClient(server, new XAccessTokenCache());

        using var response = await client.GetAsync(PostsUrl, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.HasCount(2, server.Requests);

        var tokenRequest = server.Requests[0];
        Assert.AreEqual(HttpMethod.Post, tokenRequest.Method);
        Assert.AreEqual("https://api.x.com/oauth2/token", tokenRequest.Url);
        Assert.AreEqual(
            "Basic eHZ6MWV2RlM0d0VFUFRHRUZQSEJvZzpMOHFxOVBaeVJnNmllS0dFS2hab2xHQzB2SldMdzhpRUo4OERSZHlPZw==",
            tokenRequest.Authorization);
        Assert.AreEqual("application/x-www-form-urlencoded", tokenRequest.ContentType);
        Assert.AreEqual("grant_type=client_credentials", tokenRequest.Body);

        var apiRequest = server.Requests[1];
        Assert.AreEqual(PostsUrl, apiRequest.Url);
        Assert.AreEqual("Bearer AAAA-token-1", apiRequest.Authorization);
    }

    /// <summary>
    /// The token outlives the handler, which the client factory builds anew as it renews a
    /// client's pipeline; so a second handler sharing the cache signs with the token already had.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_LaterRequests_SignedWithTheTokenAlreadyHad()
    {
        var server = new StubServer();
        var cache = new XAccessTokenCache();

        using (var client = CreateClient(server, cache))
        {
            (await client.GetAsync(PostsUrl, TestContext.CancellationToken)).Dispose();
            (await client.GetAsync(PostsUrl, TestContext.CancellationToken)).Dispose();
        }

        using (var client = CreateClient(server, cache))
        {
            (await client.GetAsync(PostsUrl, TestContext.CancellationToken)).Dispose();
        }

        Assert.AreEqual(1, server.Requests.Count(request => request.Method == HttpMethod.Post));
        Assert.IsTrue(server.Requests.Where(request => request.Url == PostsUrl).All(request => request.Authorization == "Bearer AAAA-token-1"));
    }

    [TestMethod]
    public async Task SendAsync_TokenTurnedAway_TradesForANewOneAndTriesOnceMore()
    {
        var server = new StubServer { RevokedTokens = { "AAAA-token-1" } };
        using var client = CreateClient(server, new XAccessTokenCache());

        using var response = await client.GetAsync(PostsUrl, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreSequenceEqual(
            new[] { "Basic", "Bearer AAAA-token-1", "Basic", "Bearer AAAA-token-2" },
            server.Requests.Select(request => request.Authorization.StartsWith("Basic ", StringComparison.Ordinal) ? "Basic" : request.Authorization).ToArray());
    }

    /// <summary>
    /// Tried once more, and no more: a token turned away as soon as it is issued is not going to
    /// be let in by asking again.
    /// </summary>
    [TestMethod]
    public async Task SendAsync_NewTokenTurnedAwayToo_ReturnsTheRefusal()
    {
        var server = new StubServer { RevokedTokens = { "AAAA-token-1", "AAAA-token-2" } };
        using var client = CreateClient(server, new XAccessTokenCache());

        using var response = await client.GetAsync(PostsUrl, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.HasCount(4, server.Requests);
    }

    [TestMethod]
    [DataRow(null, ApiSecret)]
    [DataRow(ApiKey, "")]
    [DataRow(" ", " ")]
    public async Task SendAsync_NoCredentials_ThrowsWithoutSendingAnything(string? apiKey, string? apiSecret)
    {
        var server = new StubServer();
        using var client = CreateClient(server, new XAccessTokenCache(), new XOptions { ApiKey = apiKey, ApiSecret = apiSecret });

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => client.GetAsync(PostsUrl, TestContext.CancellationToken));

        Assert.Contains("X:ApiKey", exception.Message);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task SendAsync_TokenOfAnotherKind_Throws()
    {
        var server = new StubServer { TokenType = "mac" };
        using var client = CreateClient(server, new XAccessTokenCache());

        await Assert.ThrowsExactlyAsync<FormatException>(() => client.GetAsync(PostsUrl, TestContext.CancellationToken));
    }

    private static HttpClient CreateClient(StubServer server, XAccessTokenCache cache, XOptions? options = null)
    {
        var handler = new XAuthenticationHandler(cache, Options.Create(options ?? new XOptions { ApiKey = ApiKey, ApiSecret = ApiSecret }))
        {
            InnerHandler = server
        };

        return new HttpClient(handler);
    }

    private sealed record ReceivedRequest(HttpMethod Method, string Url, string Authorization, string? ContentType, string? Body);

    /// <summary>
    /// Issues tokens numbered in the order asked for, and turns away the API requests signed with
    /// a revoked one.
    /// </summary>
    private sealed class StubServer : HttpMessageHandler
    {
        private int _tokensIssued;

        public List<ReceivedRequest> Requests { get; } = [];

        public HashSet<string> RevokedTokens { get; } = [];

        public string TokenType { get; init; } = "bearer";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new ReceivedRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString() ?? string.Empty,
                request.Content?.Headers.ContentType?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            if (request.RequestUri.AbsolutePath == "/oauth2/token")
            {
                var token = $"AAAA-token-{++_tokensIssued}";
                return Respond(request, HttpStatusCode.OK, $$"""{ "token_type": "{{TokenType}}", "access_token": "{{token}}" }""");
            }

            return RevokedTokens.Contains(request.Headers.Authorization?.Parameter ?? string.Empty)
                ? Respond(request, HttpStatusCode.Unauthorized, """{ "title": "Unauthorized", "type": "about:blank", "status": 401, "detail": "Unauthorized" }""")
                : Respond(request, HttpStatusCode.OK, """{ "meta": { "result_count": 0 } }""");
        }

        private static HttpResponseMessage Respond(HttpRequestMessage request, HttpStatusCode status, string payload) =>
            new(status)
            {
                RequestMessage = request,
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
    }
}
