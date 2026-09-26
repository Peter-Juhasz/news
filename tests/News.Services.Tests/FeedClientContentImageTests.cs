namespace News.Services.Tests;

/// <summary>
/// Plenty of publishers ship no enclosure and no Media RSS at all, leaving the article body as the
/// only place an image can be found.
/// </summary>
[TestClass]
public sealed class FeedClientContentImageTests
{
    private const string ImageUrl =
        "https://example.com/photos/lead.jpg?width=640&crop=smart";

    /// <summary>
    /// The lead image is preceded by a spacer with a relative source, which cannot be resolved
    /// without the article's base address, and the real one arrives entity-escaped.
    /// </summary>
    private const string RssFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/">
        	<channel>
        		<title>Example News</title>
        		<link>https://example.com</link>
        		<item>
        			<guid>https://example.com/articles/1</guid>
        			<link>https://example.com/articles/1</link>
        			<title>A story with a picture in its body</title>
        			<pubDate>Wed, 02 Sep 2026 08:15:00 +0000</pubDate>
        			<description>A short summary.</description>
        			<content:encoded>&lt;p&gt;&lt;img src="/assets/spacer.gif" /&gt;&lt;/p&gt;&lt;p&gt;&lt;img src="https://example.com/photos/lead.jpg?width=640&amp;amp;crop=smart" alt="Lead" /&gt;&lt;/p&gt;&lt;p&gt;&lt;img src="https://example.com/photos/second.jpg" /&gt;&lt;/p&gt;</content:encoded>
        		</item>
        	</channel>
        </rss>
        """;

    private const string AtomFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/">
        	<title>Example News</title>
        	<entry>
        		<id>tag:example.com,2026:1</id>
        		<title>A story with both a thumbnail and a picture in its body</title>
        		<updated>2026-09-02T08:15:00+00:00</updated>
        		<link rel="alternate" href="https://example.com/articles/1" />
        		<media:thumbnail url="https://example.com/photos/thumbnail.jpg" />
        		<content type="html">&lt;p&gt;&lt;img src="https://example.com/photos/lead.jpg" /&gt;&lt;/p&gt;</content>
        	</entry>
        </feed>
        """;

    private const string ImagelessRssFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/">
        	<channel>
        		<title>Example News</title>
        		<link>https://example.com</link>
        		<item>
        			<guid>https://example.com/articles/2</guid>
        			<link>https://example.com/articles/2</link>
        			<title>A story with no picture at all</title>
        			<pubDate>Wed, 02 Sep 2026 08:15:00 +0000</pubDate>
        			<description>A short summary.</description>
        			<content:encoded>&lt;p&gt;No pictures here.&lt;/p&gt;</content:encoded>
        		</item>
        	</channel>
        </rss>
        """;

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task LoadAsync_RssItemWithoutMediaImage_FallsBackToTheFirstImageInTheContent()
    {
        var item = await FeedTestHost.LoadSingleRssItemAsync(
            RssFeed,
            "https://example.com/rss",
            TestContext.CancellationToken);

        // The relative spacer is skipped, the second photo never reached, and &amp; is decoded.
        Assert.AreEqual(ImageUrl, item.ImageUrl);
    }

    [TestMethod]
    public async Task LoadAsync_EntryWithMediaThumbnail_PrefersItOverTheContentImage()
    {
        var item = await FeedTestHost.LoadSingleAtomItemAsync(
            AtomFeed,
            "https://example.com/atom",
            TestContext.CancellationToken);

        Assert.AreEqual("https://example.com/photos/thumbnail.jpg", item.ImageUrl);
    }

    [TestMethod]
    public async Task LoadAsync_ItemWithNoImageAnywhere_LeavesTheImageUrlUnset()
    {
        var item = await FeedTestHost.LoadSingleRssItemAsync(
            ImagelessRssFeed,
            "https://example.com/rss",
            TestContext.CancellationToken);

        Assert.IsNull(item.ImageUrl);
    }
}
