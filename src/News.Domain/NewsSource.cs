namespace News.Domain;

public abstract record NewsSource(TimeSpan Frequency)
{
    public bool IsStale(DateTimeOffset? lastLoadedAt, DateTimeOffset now) =>
        lastLoadedAt is null || Frequency <= TimeSpan.Zero || now - lastLoadedAt.Value >= Frequency;
}

public sealed record RssNewsSource(string Url, TimeSpan Frequency) : NewsSource(Frequency);

public sealed record RedditNewsSource(string Subreddit, TimeSpan Frequency) : NewsSource(Frequency)
{
    public string FeedUrl => $"https://reddit.com/r/{Subreddit}.rss";
}
