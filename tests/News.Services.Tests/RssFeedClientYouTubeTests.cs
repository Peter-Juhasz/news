using News.Domain;

namespace News.Services.Tests;

/// <summary>
/// YouTube's channel feed is Atom with no content or summary element: the body, image and
/// view count live in Media RSS, which is why the parser falls back to media:group.
/// </summary>
[TestClass]
public sealed class RssFeedClientYouTubeTests
{
    private const string Feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns:media="http://search.yahoo.com/mrss/" xmlns="http://www.w3.org/2005/Atom">
        <link rel="self" href="http://www.youtube.com/feeds/videos.xml?channel_id=UC7kIy8fZavEni8Gzl8NLjOQ"/>
        <id>yt:channel:7kIy8fZavEni8Gzl8NLjOQ</id>
        <yt:channelId>7kIy8fZavEni8Gzl8NLjOQ</yt:channelId>
        <title>Alex O'Connor</title>
        <link rel="alternate" href="https://www.youtube.com/channel/UC7kIy8fZavEni8Gzl8NLjOQ"/>
        <author>
        <name>Alex O'Connor</name>
        <uri>https://www.youtube.com/channel/UC7kIy8fZavEni8Gzl8NLjOQ</uri>
        </author>
        <published>2013-02-02T16:23:51+00:00</published>
        <entry>
        <id>yt:video:CyhHz23AHb4</id>
        <yt:videoId>CyhHz23AHb4</yt:videoId>
        <yt:channelId>UC7kIy8fZavEni8Gzl8NLjOQ</yt:channelId>
        <title>All Things Are Empty - Intro to Madhyamaka Buddhism</title>
        <link rel="alternate" href="https://www.youtube.com/watch?v=CyhHz23AHb4"/>
        <author>
        <name>Alex O'Connor</name>
        <uri>https://www.youtube.com/channel/UC7kIy8fZavEni8Gzl8NLjOQ</uri>
        </author>
        <published>2026-08-31T14:13:08+00:00</published>
        <updated>2026-08-31T14:13:52+00:00</updated>
        <media:group>
        <media:title>All Things Are Empty - Intro to Madhyamaka Buddhism</media:title>
        <media:content url="https://www.youtube.com/v/CyhHz23AHb4?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
        <media:thumbnail url="https://i4.ytimg.com/vi/CyhHz23AHb4/hqdefault.jpg" width="480" height="360"/>
        <media:description>Get all sides of every story and be better informed at https://ground.news/AlexOC - subscribe for 40% off unlimited access. Come to my North American tour this November: https://www.alexoconnor.com/p/tour. For early, ad-free access to videos, and to support the channel, subscribe to my Substack: https://www.alexoconnor.com To donate to my PayPal (thank you): http://www.paypal.me/cosmicskeptic - VIDEO NOTES Jan Westerhoff is a German philosopher and orientalist with specific interests in metaphysics and the philosophy of language. He is currently Professor of Buddhist Philosophy in the Faculty of Theology and Religion of the University of Oxford. - LINKS Get Jan Westerhoff's books: https://amzn.to/4xxtY27 - TIMESTAMPS 0:00 Nāgārjuna, The “Second Buddha” 6:03 The Middle Way 9:16 Ontological Nihilism 20:00 Are Madhyamaka Buddhists Nihilists? 38:56 How Emptiness Escapes Causal Arguments for God 45:40 Preempting Immanuel Kant? 50:37 Emptiness 1:10:07 How to Escape Suffering 1:16:54 Visualising Emptiness 1:20:30 Is Consciousness Empty? 1:34:28 Reality Without Substance - CONNECT My Website: https://www.alexoconnor.com SOCIAL LINKS: Twitter: http://www.twitter.com/cosmicskeptic Facebook: http://www.facebook.com/cosmicskeptic Instagram: http://www.instagram.com/cosmicskeptic TikTok: @CosmicSkeptic The Within Reason Podcast: https://open.spotify.com/show/16wUbvDT95dxzpG2KEhakK?si=3196e07690bb489d - CONTACT Business email: contact@alexoconnor.com Brand enquiries: David@modernstoa.co ------------------------------------------</media:description>
        <media:community>
        <media:starRating count="3195" average="5.00" min="1" max="5"/>
        <media:statistics views="313646"/>
        </media:community>
        </media:group>
        </entry>
        </feed>
        """;

    private const string ExpectedContent =
        "Get all sides of every story and be better informed at https://ground.news/AlexOC - subscribe " +
        "for 40% off unlimited access. Come to my North American tour this November: " +
        "https://www.alexoconnor.com/p/tour. For early, ad-free access to videos, and to support the " +
        "channel, subscribe to my Substack: https://www.alexoconnor.com To donate to my PayPal (thank " +
        "you): http://www.paypal.me/cosmicskeptic - VIDEO NOTES Jan Westerhoff is a German philosopher " +
        "and orientalist with specific interests in metaphysics and the philosophy of language. He is " +
        "currently Professor of Buddhist Philosophy in the Faculty of Theology and Religion of the " +
        "University of Oxford. - LINKS Get Jan Westerhoff's books: https://amzn.to/4xxtY27 - TIMESTAMPS " +
        "0:00 Nāgārjuna, The “Second Buddha” 6:03 The Middle Way 9:16 Ontological " +
        "Nihilism 20:00 Are Madhyamaka Buddhists Nihilists? 38:56 How Emptiness Escapes Causal " +
        "Arguments for God 45:40 Preempting Immanuel Kant? 50:37 Emptiness 1:10:07 How to Escape " +
        "Suffering 1:16:54 Visualising Emptiness 1:20:30 Is Consciousness Empty? 1:34:28 Reality " +
        "Without Substance - CONNECT My Website: https://www.alexoconnor.com SOCIAL LINKS: Twitter: " +
        "http://www.twitter.com/cosmicskeptic Facebook: http://www.facebook.com/cosmicskeptic " +
        "Instagram: http://www.instagram.com/cosmicskeptic TikTok: @CosmicSkeptic The Within Reason " +
        "Podcast: https://open.spotify.com/show/16wUbvDT95dxzpG2KEhakK?si=3196e07690bb489d - CONTACT " +
        "Business email: contact@alexoconnor.com Brand enquiries: David@modernstoa.co " +
        "------------------------------------------";

    /// <summary>The description clipped to the 220 characters the lead allows, ellipsis included.</summary>
    private const string ExpectedLead =
        "Get all sides of every story and be better informed at https://ground.news/AlexOC - subscribe " +
        "for 40% off unlimited access. Come to my North American tour this November: " +
        "https://www.alexoconnor.com/p/tour. For early, ad…";

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task LoadAsync_YouTubeChannelFeed_MapsEveryFieldOfTheEntry()
    {
        var item = await FeedTestHost.LoadSingleItemAsync(
            Feed,
            "https://www.youtube.com/feeds/videos.xml?channel_id=UC7kIy8fZavEni8Gzl8NLjOQ",
            TestContext.CancellationToken);

        Assert.AreEqual("yt:video:CyhHz23AHb4", item.Id);
        Assert.AreEqual("All Things Are Empty - Intro to Madhyamaka Buddhism", item.Title);

        // updated wins over published.
        Assert.AreEqual(
            new DateTimeOffset(2026, 8, 31, 14, 13, 52, TimeSpan.Zero),
            item.Date);

        // link rel="alternate", not the media:content flash player URL.
        Assert.AreEqual("https://www.youtube.com/watch?v=CyhHz23AHb4", item.Url);

        // media:thumbnail, since media:content advertises application/x-shockwave-flash.
        Assert.AreEqual("https://i4.ytimg.com/vi/CyhHz23AHb4/hqdefault.jpg", item.ImageUrl);

        Assert.AreEqual(313646L, item.Views);

        // media:starRating carries the like count on YouTube, every vote being five stars.
        Assert.HasCount(1, item.Reactions);
        Assert.AreEqual(3195L, item.Reactions[NewsReactions.Like]);

        Assert.AreEqual(ExpectedContent, item.Content);
        Assert.AreEqual(ExpectedLead, item.Lead);
    }

    /// <summary>
    /// Elsewhere media:starRating means what it says — an average rating — so its count is not
    /// a like tally and the entry gets no reaction.
    /// </summary>
    [TestMethod]
    public async Task LoadAsync_NonYouTubeFeedWithStarRating_MapsNoReactions()
    {
        const string feed = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns:media="http://search.yahoo.com/mrss/" xmlns="http://www.w3.org/2005/Atom">
            <entry>
            <id>urn:example:1</id>
            <title>A rated review</title>
            <link rel="alternate" href="https://example.com/reviews/1"/>
            <updated>2026-08-31T14:13:52+00:00</updated>
            <summary>A review readers have rated.</summary>
            <media:community>
            <media:starRating count="42" average="4.20" min="1" max="5"/>
            </media:community>
            </entry>
            </feed>
            """;

        var item = await FeedTestHost.LoadSingleItemAsync(
            feed,
            "https://example.com/reviews.atom",
            TestContext.CancellationToken);

        Assert.IsEmpty(item.Reactions);
    }
}
