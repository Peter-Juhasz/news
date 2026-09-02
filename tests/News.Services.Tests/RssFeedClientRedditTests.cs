namespace News.Services.Tests;

/// <summary>
/// Reddit's subreddit feed is Atom whose entries carry an escaped HTML table as content, a
/// media:thumbnail outside any media:group, and a link element with no rel attribute.
/// </summary>
[TestClass]
public sealed class RssFeedClientRedditTests
{
    private const string Feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/">
        	<category term="hungary" label="r/hungary"/>
        	<updated>2026-09-02T18:02:54+00:00</updated>
        	<icon>https://www.redditstatic.com/icon.png/</icon>
        	<id>/r/hungary.rss</id>
        	<link rel="self" href="https://www.reddit.com/r/hungary.rss" type="application/atom+xml" />
        	<link rel="alternate" href="https://www.reddit.com/r/hungary" type="text/html" />
        	<logo>https://f.thumbs.redditmedia.com/NTq5oGxcUaHy9frw.png</logo>
        	<subtitle>Tartalom, ami Magyarországgal, a magyar kultúrával kapcsolatos, illetve témák amik érdekesek lehetnek a tagok számára.</subtitle>
        	<title>Üdvözlünk az /r/hungary-n!</title>
        	<entry>
        		<author>
        			<name>/u/bencee03458</name>
        			<uri>https://www.reddit.com/user/bencee03458</uri>
        		</author>
        		<category term="hungary" label="r/hungary"/>
        		<content type="html">&lt;table&gt; &lt;tr&gt;&lt;td&gt; &lt;a href=&quot;https://www.reddit.com/r/hungary/comments/1w5fnae/elindult_a_nyomozás_orbán_viktor_közösségi_médiás/&quot;&gt; &lt;img src=&quot;https://external-preview.redd.it/VwaAI5HEWO8Ii6tftdyl95FeKmygeyyUXG18xWV3-jg.jpeg?width=640&amp;amp;crop=smart&amp;amp;auto=webp&amp;amp;s=5eff02fce5ed81c4dd3d35264ca1cfb256f9749b&quot; alt=&quot;Elindult a nyomozás Orbán Viktor közösségi médiás szerződései miatt&quot; title=&quot;Elindult a nyomozás Orbán Viktor közösségi médiás szerződései miatt&quot; /&gt; &lt;/a&gt; &lt;/td&gt;&lt;td&gt; &amp;#32; submitted by &amp;#32; &lt;a href=&quot;https://www.reddit.com/user/bencee03458&quot;&gt; /u/bencee03458 &lt;/a&gt; &lt;br/&gt; &lt;span&gt;&lt;a href=&quot;https://telex.hu/belfold/2026/09/02/kaminski-fanny-orban-viktor-facebook-feljelentes-buntetoeljaras?isMobileApp=1&quot;&gt;[link]&lt;/a&gt;&lt;/span&gt; &amp;#32; &lt;span&gt;&lt;a href=&quot;https://www.reddit.com/r/hungary/comments/1w5fnae/elindult_a_nyomozás_orbán_viktor_közösségi_médiás/&quot;&gt;[hozzászólás]&lt;/a&gt;&lt;/span&gt; &lt;/td&gt;&lt;/tr&gt;&lt;/table&gt;</content>
        		<id>t3_1w5fnae</id>
        		<media:thumbnail url="https://external-preview.redd.it/VwaAI5HEWO8Ii6tftdyl95FeKmygeyyUXG18xWV3-jg.jpeg?width=640&amp;crop=smart&amp;auto=webp&amp;s=5eff02fce5ed81c4dd3d35264ca1cfb256f9749b" />
        		<link href="https://www.reddit.com/r/hungary/comments/1w5fnae/elindult_a_nyomozás_orbán_viktor_közösségi_médiás/" />
        		<updated>2026-09-02T16:42:12+00:00</updated>
        		<published>2026-09-02T16:42:12+00:00</published>
        		<title>Elindult a nyomozás Orbán Viktor közösségi médiás szerződései miatt</title>
        	</entry>
        </feed>
        """;

    private const string PostUrl =
        "https://www.reddit.com/r/hungary/comments/1w5fnae/elindult_a_nyomozás_orbán_viktor_közösségi_médiás/";

    private const string ThumbnailUrl =
        "https://external-preview.redd.it/VwaAI5HEWO8Ii6tftdyl95FeKmygeyyUXG18xWV3-jg.jpeg" +
        "?width=640&crop=smart&auto=webp&s=5eff02fce5ed81c4dd3d35264ca1cfb256f9749b";

    /// <summary>
    /// The entry body once XML decoding is done: still HTML, and still carrying the entity-escaped
    /// ampersands and the numeric spaces that only the plain-text pass resolves.
    /// </summary>
    private const string ExpectedContent =
        "<table> <tr><td> <a href=\"" + PostUrl + "\"> " +
        "<img src=\"https://external-preview.redd.it/VwaAI5HEWO8Ii6tftdyl95FeKmygeyyUXG18xWV3-jg.jpeg" +
        "?width=640&amp;crop=smart&amp;auto=webp&amp;s=5eff02fce5ed81c4dd3d35264ca1cfb256f9749b\" " +
        "alt=\"Elindult a nyomozás Orbán Viktor közösségi médiás szerződései miatt\" " +
        "title=\"Elindult a nyomozás Orbán Viktor közösségi médiás szerződései miatt\" /> </a> </td>" +
        "<td> &#32; submitted by &#32; <a href=\"https://www.reddit.com/user/bencee03458\"> " +
        "/u/bencee03458 </a> <br/> <span><a href=\"https://telex.hu/belfold/2026/09/02/" +
        "kaminski-fanny-orban-viktor-facebook-feljelentes-buntetoeljaras?isMobileApp=1\">[link]</a>" +
        "</span> &#32; <span><a href=\"" + PostUrl + "\">[hozzászólás]</a></span> </td></tr></table>";

    /// <summary>Markup gone, &#32; resolved and runs of whitespace collapsed.</summary>
    private const string ExpectedLead = "submitted by /u/bencee03458 [link] [hozzászólás]";

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task LoadAsync_RedditSubredditFeed_MapsEveryFieldOfTheEntry()
    {
        var item = await FeedTestHost.LoadSingleItemAsync(
            Feed,
            "https://www.reddit.com/r/hungary.rss",
            TestContext.CancellationToken);

        Assert.AreEqual("t3_1w5fnae", item.Id);
        Assert.AreEqual("Elindult a nyomozás Orbán Viktor közösségi médiás szerződései miatt", item.Title);

        Assert.AreEqual(
            new DateTimeOffset(2026, 9, 2, 16, 42, 12, TimeSpan.Zero),
            item.Date);

        // The entry's own link, which carries no rel attribute, and not the feed-level alternate.
        Assert.AreEqual(PostUrl, item.Url);

        // media:thumbnail sits directly under the entry here rather than in a media:group.
        Assert.AreEqual(ThumbnailUrl, item.ImageUrl);

        // Reddit publishes no media:statistics.
        Assert.IsNull(item.Views);

        Assert.AreEqual(ExpectedContent, item.Content);
        Assert.AreEqual(ExpectedLead, item.Lead);
    }
}
