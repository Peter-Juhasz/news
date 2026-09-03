namespace News.Domain;

public abstract record NewsSource(TimeSpan Frequency)
{
    /// <summary>
    /// Display name shown instead of the article's host name. Optional.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// View the details pane shows for items from this source. Optional; the text view is used when unset.
    /// </summary>
    public NewsContentMode? View { get; init; }

    public bool IsStale(DateTimeOffset? lastLoadedAt, DateTimeOffset now) =>
        lastLoadedAt is null || Frequency <= TimeSpan.Zero || now - lastLoadedAt.Value >= Frequency;
}

public sealed record RssNewsSource(string Url, TimeSpan Frequency) : NewsSource(Frequency)
{
    /// <summary>
    /// CSS selector picking the article body out of the page an item links to. Optional; where it
    /// is set, the body shown for an item is fetched from its own page instead of read from the
    /// feed, for the feeds that publish no more than a teaser.
    /// </summary>
    public string? Selector { get; init; }
}

public sealed record RedditNewsSource(string Subreddit, TimeSpan Frequency) : NewsSource(Frequency)
{
    public string FeedUrl => $"https://reddit.com/r/{Subreddit}.rss";
}

public sealed record YouTubeNewsSource(string ChannelId, TimeSpan Frequency) : NewsSource(Frequency)
{
    public string FeedUrl => $"https://www.youtube.com/feeds/videos.xml?channel_id={Uri.EscapeDataString(ChannelId)}";
}
