using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;

namespace News.Services.Tests;

/// <summary>
/// Each client parses a document already in hand without going to the network, and turns away
/// one of the other format rather than reading nothing out of it.
/// </summary>
[TestClass]
public sealed class FeedClientParseTests
{
    private const string RssFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
        	<channel>
        		<title>Example News</title>
        		<item>
        			<guid>https://example.com/articles/1</guid>
        			<link>https://example.com/articles/1</link>
        			<title>An RSS story</title>
        			<pubDate>Wed, 02 Sep 2026 08:15:00 +0000</pubDate>
        			<description>A short summary.</description>
        		</item>
        	</channel>
        </rss>
        """;

    private const string AtomFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
        	<title>Example News</title>
        	<entry>
        		<id>tag:example.com,2026:1</id>
        		<title>An Atom story</title>
        		<updated>2026-09-02T08:15:00+00:00</updated>
        		<link rel="alternate" href="https://example.com/articles/1" />
        		<summary>A short summary.</summary>
        	</entry>
        </feed>
        """;

    [TestMethod]
    public void RssParse_RssDocument_ReadsItsItems()
    {
        using var httpClient = new HttpClient();
        var client = new RssFeedClient(httpClient, NullLogger<RssFeedClient>.Instance);

        var items = client.Parse(XDocument.Parse(RssFeed));

        Assert.HasCount(1, items);
        Assert.AreEqual("https://example.com/articles/1", items[0].Id);
        Assert.AreEqual("An RSS story", items[0].Title);
        Assert.AreEqual("https://example.com/articles/1", items[0].Url);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 2, 8, 15, 0, TimeSpan.Zero), items[0].Date);
        Assert.AreEqual("A short summary.", items[0].Lead);
    }

    [TestMethod]
    public void AtomParse_AtomDocument_ReadsItsEntries()
    {
        using var httpClient = new HttpClient();
        var client = new AtomFeedClient(httpClient, NullLogger<AtomFeedClient>.Instance);

        var items = client.Parse(XDocument.Parse(AtomFeed));

        Assert.HasCount(1, items);
        Assert.AreEqual("tag:example.com,2026:1", items[0].Id);
        Assert.AreEqual("An Atom story", items[0].Title);
        Assert.AreEqual("https://example.com/articles/1", items[0].Url);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 2, 8, 15, 0, TimeSpan.Zero), items[0].Date);
        Assert.AreEqual("A short summary.", items[0].Lead);
    }

    [TestMethod]
    public void RssParse_AtomDocument_Throws()
    {
        using var httpClient = new HttpClient();
        var client = new RssFeedClient(httpClient, NullLogger<RssFeedClient>.Instance);

        Assert.ThrowsExactly<FormatException>(() => client.Parse(XDocument.Parse(AtomFeed)));
    }

    [TestMethod]
    public void AtomParse_RssDocument_Throws()
    {
        using var httpClient = new HttpClient();
        var client = new AtomFeedClient(httpClient, NullLogger<AtomFeedClient>.Instance);

        Assert.ThrowsExactly<FormatException>(() => client.Parse(XDocument.Parse(RssFeed)));
    }
}
