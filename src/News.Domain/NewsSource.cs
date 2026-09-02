namespace News.Domain;

public abstract record NewsSource(TimeSpan Frequency)
{
    /// <summary>
    /// Display name shown instead of the article's host name. Optional.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// View the details pane opens with for items from this source. Optional.
    /// </summary>
    public NewsContentMode? DefaultView { get; init; }

    public bool IsStale(DateTimeOffset? lastLoadedAt, DateTimeOffset now) =>
        lastLoadedAt is null || Frequency <= TimeSpan.Zero || now - lastLoadedAt.Value >= Frequency;
}

public sealed record RssNewsSource(string Url, TimeSpan Frequency) : NewsSource(Frequency);

public sealed record RedditNewsSource(string Subreddit, TimeSpan Frequency) : NewsSource(Frequency)
{
    public string FeedUrl => $"https://reddit.com/r/{Subreddit}.rss";
}

public sealed record YouTubeNewsSource(string ChannelId, TimeSpan Frequency) : NewsSource(Frequency)
{
    public string FeedUrl => $"https://www.youtube.com/feeds/videos.xml?channel_id={Uri.EscapeDataString(ChannelId)}";
}
