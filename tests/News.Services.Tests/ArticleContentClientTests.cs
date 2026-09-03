using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace News.Services.Tests;

/// <summary>
/// The body of an article read out of its own page, for the feeds that publish no more than a
/// teaser.
/// </summary>
[TestClass]
public sealed class ArticleContentClientTests
{
    private const string PageUrl = "https://example.com/articles/1";

    /// <summary>
    /// The body sits among navigation and a script, and carries a relative image, a relative link,
    /// a link that is already absolute and one that resolves to nothing on the web.
    /// </summary>
    private const string Page = """
        <!doctype html>
        <html>
        <head><title>A story</title></head>
        <body>
            <nav><a href="/">Home</a></nav>
            <article class="entry">
                <p><img src="../photos/lead.jpg" alt="Lead" /></p>
                <p>The <a href="/articles/2">next story</a>, the
                   <a href="https://elsewhere.example/story">source</a>
                   and the <a href="mailto:desk@example.com">desk</a>.</p>
            </article>
            <script>tracking();</script>
        </body>
        </html>
        """;

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task LoadAsync_SelectorMatchingTheBody_ReturnsThatElementsHtml()
    {
        var content = await LoadAsync("article.entry");

        Assert.IsNotNull(content);
        Assert.StartsWith("<article class=\"entry\">", content);
        Assert.Contains("The <a", content);
        Assert.DoesNotContain("tracking()", content);
        Assert.DoesNotContain("<nav>", content);
    }

    [TestMethod]
    public async Task LoadAsync_BodyWithRelativeAddresses_ResolvesThemAgainstThePage()
    {
        var content = await LoadAsync("article.entry");

        Assert.IsNotNull(content);
        Assert.Contains("src=\"https://example.com/photos/lead.jpg\"", content);
        Assert.Contains("href=\"https://example.com/articles/2\"", content);

        // An address that was already absolute, and one that is not a web address at all, stand.
        Assert.Contains("href=\"https://elsewhere.example/story\"", content);
        Assert.Contains("href=\"mailto:desk@example.com\"", content);
    }

    [TestMethod]
    public async Task LoadAsync_SelectorMatchingNothing_ReturnsNull()
    {
        Assert.IsNull(await LoadAsync(".story-body"));
    }

    [TestMethod]
    public async Task LoadAsync_SelectorThatIsNotValidCss_ReturnsNull()
    {
        Assert.IsNull(await LoadAsync("article["));
    }

    [TestMethod]
    public async Task LoadAsync_PageThatFailsToLoad_Throws()
    {
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            LoadAsync("article.entry", HttpStatusCode.NotFound));
    }

    private async Task<string?> LoadAsync(
        string selector,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        using var handler = new StubHttpMessageHandler(Page, statusCode);
        using var httpClient = new HttpClient(handler);
        var client = new ArticleContentClient(httpClient, NullLogger<ArticleContentClient>.Instance);

        return await client.LoadAsync(PageUrl, selector, TestContext.CancellationToken);
    }

    private sealed class StubHttpMessageHandler(string payload, HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                RequestMessage = request,
                Content = new StringContent(payload, Encoding.UTF8, "text/html")
            });
    }
}
