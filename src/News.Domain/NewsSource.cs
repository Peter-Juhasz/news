using System.Text.Json.Serialization;

namespace News.Domain;

/// <summary>
/// Where items are loaded from, and what a group configures about showing them.
/// </summary>
/// <remarks>
/// Sources are stored whole alongside the items bookmarked from them, so a bookmark outlives the
/// source being edited or dropped from the groups file. The discriminators below are the names
/// the groups file already gives these types, so renaming a class does not orphan what was
/// written under its old name; the computed members are left out of the file, being restated by
/// whatever is read back rather than read from it.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RssNewsSource), "rss")]
[JsonDerivedType(typeof(AtomNewsSource), "atom")]
[JsonDerivedType(typeof(RedditNewsSource), "reddit")]
[JsonDerivedType(typeof(YouTubeNewsSource), "youtube")]
public abstract record NewsSource(TimeSpan Frequency)
{
    /// <summary>
    /// Identifies what this source reads, so items stored for it are recognised as its own when
    /// they are read back — including after everything else about it was edited.
    /// </summary>
    [JsonIgnore]
    public abstract string Key { get; }

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
    [JsonIgnore]
    public override string Key => $"rss:{Url}";

    /// <summary>
    /// CSS selector picking the article body out of the page an item links to. Optional; where it
    /// is set, the body shown for an item is fetched from its own page instead of read from the
    /// feed, for the feeds that publish no more than a teaser.
    /// </summary>
    public string? Selector { get; init; }
}

public sealed record AtomNewsSource(string Url, TimeSpan Frequency) : NewsSource(Frequency)
{
    [JsonIgnore]
    public override string Key => $"atom:{Url}";

    /// <summary>
    /// CSS selector picking the article body out of the page an item links to. Optional; where it
    /// is set, the body shown for an item is fetched from its own page instead of read from the
    /// feed, for the feeds that publish no more than a teaser.
    /// </summary>
    public string? Selector { get; init; }
}

public sealed record RedditNewsSource(string Subreddit, TimeSpan Frequency) : NewsSource(Frequency)
{
    [JsonIgnore]
    public override string Key => $"reddit:{Subreddit}";

    [JsonIgnore]
    public string FeedUrl => $"https://reddit.com/r/{Subreddit}.rss";
}

public sealed record YouTubeNewsSource(string ChannelId, TimeSpan Frequency) : NewsSource(Frequency)
{
    [JsonIgnore]
    public override string Key => $"youtube:{ChannelId}";

    [JsonIgnore]
    public string FeedUrl => $"https://www.youtube.com/feeds/videos.xml?channel_id={Uri.EscapeDataString(ChannelId)}";
}
