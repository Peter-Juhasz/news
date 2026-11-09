using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using News.Domain;

namespace News.Services.Tests;

/// <summary>
/// Stories read out of a page that publishes no feed of them, each picked out by the item
/// selector and its properties picked out of it in turn.
/// </summary>
[TestClass]
public sealed class HtmlPageClientTests
{
    private const string PageUrl = "https://example.com/news/";

    /// <summary>
    /// Two stories among navigation and an empty slot for an advert, one dated by a machine and one
    /// in words, with relative links, a picture loaded lazily and a teaser that links onwards.
    /// </summary>
    private const string Page = """
        <!doctype html>
        <html lang="en">
        <head><title>News</title></head>
        <body>
            <nav><a href="/">Home</a></nav>
            <ul class="stories">
                <li>
                    <h2><a href="/news/1">First &amp; foremost</a></h2>
                    <time class="date" datetime="2026-09-26T18:33:43Z">Yesterday</time>
                    <img src="data:image/gif;base64,R0lGODlhAQABAAAAACw=" data-src="../img/1.jpg" alt="">
                    <p class="summary">The <a href="1#more">first</a> story, en été.</p>
                </li>
                <li>
                    <h2><a href="https://elsewhere.example/2">
                        Second
                    </a></h2>
                    <span class="date">September 25, 2026</span>
                    <p class="summary">The second story.</p>
                </li>
                <li class="ad"></li>
            </ul>
            <ul class="links"><li><a href="/about">About</a></li></ul>
        </body>
        </html>
        """;

    private static readonly HtmlNewsSource Source = new(
        Url: PageUrl,
        ItemSelector: "ul.stories li")
    {
        TitleSelector = "h2",
        DateSelector = ".date",
        LeadSelector = "p.summary",
        UrlSelector = "h2"
    };

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task LoadAsync_ListingPage_MapsEveryPropertyOfTheStory()
    {
        var items = await LoadAsync(Page, Source);

        var item = items[0];
        Assert.AreEqual("First & foremost", item.Title);
        Assert.AreEqual("https://example.com/news/1", item.Url);
        Assert.AreEqual("https://example.com/news/1", item.Id);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 26, 18, 33, 43, TimeSpan.Zero), item.Date);
        Assert.AreEqual("The first story, en été.", item.Lead);
        Assert.AreEqual("https://example.com/img/1.jpg", item.ImageUrl);
    }

    [TestMethod]
    public async Task LoadAsync_TeaserWithRelativeLinks_KeepsItsMarkupWithTheLinksMadeAbsolute()
    {
        var items = await LoadAsync(Page, Source);

        var content = items[0].Content;
        Assert.IsNotNull(content);
        Assert.StartsWith("<p class=\"summary\">", content);
        Assert.Contains("href=\"https://example.com/news/1#more\"", content);

        // written back with its letters as they are rather than as character references
        Assert.Contains("en été.", content);
    }

    [TestMethod]
    public async Task LoadAsync_SlotWithNothingInIt_IsNoStory()
    {
        var items = await LoadAsync(Page, Source);

        Assert.HasCount(2, items);
        Assert.AreEqual("Second", items[1].Title);
        Assert.AreEqual("https://elsewhere.example/2", items[1].Url);
    }

    [TestMethod]
    public async Task LoadAsync_DateWrittenInWords_IsReadInThePagesLanguage()
    {
        var items = await LoadAsync(Page, Source);

        Assert.AreEqual(new DateOnly(2026, 9, 25), DateOnly.FromDateTime(items[1].Date.DateTime));
    }

    [TestMethod]
    public async Task LoadAsync_PageInHungarian_ReadsItsDatesAsHungarianWritesThem()
    {
        const string page = """
            <html lang="hu">
            <body>
                <article><h3><a href="/cikk/1">Első</a></h3><span>2026. szeptember 25.</span></article>
            </body>
            </html>
            """;

        var items = await LoadAsync(page, new HtmlNewsSource(
            Url: PageUrl,
            ItemSelector: "article")
        {
            DateSelector = "span"
        });

        var item = Assert.ContainsSingle(items);
        Assert.AreEqual(new DateOnly(2026, 9, 25), DateOnly.FromDateTime(item.Date.DateTime));
    }

    [TestMethod]
    public async Task LoadAsync_NoPropertySelectors_TakesTheTitleAndLinkFromTheStoryItself()
    {
        var before = DateTimeOffset.UtcNow;

        var items = await LoadAsync(Page, new HtmlNewsSource(
            Url: PageUrl,
            ItemSelector: "ul.stories h2"));

        var after = DateTimeOffset.UtcNow;

        Assert.HasCount(2, items);
        Assert.AreEqual("First & foremost", items[0].Title);
        Assert.AreEqual("https://example.com/news/1", items[0].Url);

        // nothing to date it by, so it is dated when it was loaded, and has no body beyond its title
        Assert.IsTrue(items[0].Date >= before && items[0].Date <= after);
        Assert.AreEqual(string.Empty, items[0].Content);
        Assert.AreEqual(string.Empty, items[0].Lead);
    }

    [TestMethod]
    public async Task LoadAsync_ItemThatIsALink_TakesItsOwnAddress()
    {
        var items = await LoadAsync(Page, new HtmlNewsSource(
            Url: PageUrl,
            ItemSelector: "ul.links a"));

        var item = Assert.ContainsSingle(items);
        Assert.AreEqual("About", item.Title);
        Assert.AreEqual("https://example.com/about", item.Url);
    }

    /// <summary>
    /// A story inside two of the containers the selector descends through is found from both.
    /// </summary>
    [TestMethod]
    public async Task LoadAsync_StoryInsideNestedMatches_IsListedOnceInPageOrder()
    {
        const string page = """
            <div><div>
                <p><a href="/1">One</a></p>
            </div>
            <p><a href="/2">Two</a></p></div>
            <div><p><a href="/3">Three</a></p></div>
            """;

        var items = await LoadAsync(page, new HtmlNewsSource(
            Url: PageUrl,
            ItemSelector: "div p"));

        Assert.AreSequenceEqual(["One", "Two", "Three"], items.Select(item => item.Title));
    }

    [TestMethod]
    public async Task LoadAsync_PageWithBaseElement_ResolvesAddressesAgainstIt()
    {
        const string page = """
            <html><head><base href="https://cdn.example/archive/"></head>
            <body><article><a href="story">Story</a></article></body></html>
            """;

        var items = await LoadAsync(page, new HtmlNewsSource(
            Url: PageUrl,
            ItemSelector: "article"));

        Assert.AreEqual("https://cdn.example/archive/story", Assert.ContainsSingle(items).Url);
    }

    /// <summary>
    /// A page that has changed its markup is not taken for one with nothing on it, which would
    /// drop the stories already listed from it.
    /// </summary>
    [TestMethod]
    public async Task LoadAsync_ItemSelectorMatchingNothing_Throws()
    {
        await Assert.ThrowsExactlyAsync<FormatException>(() => LoadAsync(Page, Source with { ItemSelector = "div.story" }));
    }

    [TestMethod]
    public async Task LoadAsync_PageThatFailsToLoad_Throws()
    {
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => LoadAsync(Page, Source, HttpStatusCode.NotFound));
    }

    private async Task<IReadOnlyList<NewsItem>> LoadAsync(
        string page,
        HtmlNewsSource source,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        using var handler = new StubHttpMessageHandler(page, statusCode);
        using var httpClient = new HttpClient(handler);
        var client = new HtmlPageClient(httpClient, NullLogger<HtmlPageClient>.Instance);

        return await client.LoadAsync(source, TestContext.CancellationToken);
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
