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
[JsonDerivedType(typeof(GitHubNewsSource), "github")]
[JsonDerivedType(typeof(HtmlNewsSource), "html")]
[JsonDerivedType(typeof(XNewsSource), "x")]
[JsonDerivedType(typeof(XSearchNewsSource), "x-search")]
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

/// <param name="Repository">The repository as GitHub names it, owner first: <c>dotnet/runtime</c>.</param>
public sealed record GitHubNewsSource(string Repository, GitHubFeed Feed, TimeSpan Frequency) : NewsSource(Frequency)
{
    [JsonIgnore]
    public override string Key => $"github:{Repository}/{Feed.ToString().ToLowerInvariant()}";

    /// <summary>
    /// The Atom feed GitHub publishes for what this source follows. Null for issues and pull
    /// requests, which GitHub publishes no feed for, so they are read from its REST API instead.
    /// </summary>
    [JsonIgnore]
    public string? FeedUrl => Feed switch
    {
        GitHubFeed.Releases => $"https://github.com/{Repository}/releases.atom",
        GitHubFeed.Tags => $"https://github.com/{Repository}/tags.atom",
        // The repository's default branch, whatever it is called.
        GitHubFeed.Commits => $"https://github.com/{Repository}/commits.atom",
        _ => null
    };
}

/// <summary>
/// A web page listing stories it publishes no feed for. Each story is an element the item
/// selector picks out of the page, and its properties are picked out of that element in turn.
/// </summary>
/// <remarks>
/// The item and property selectors take an element name, classes, IDs and exact attribute values,
/// and descendants of those separated by spaces — not the whole of CSS.
/// </remarks>
/// <param name="Url">The page listing the stories.</param>
/// <param name="ItemSelector">Picks each story out of the page.</param>
public sealed record HtmlNewsSource(string Url, string ItemSelector, TimeSpan Frequency) : NewsSource(Frequency)
{
    [JsonIgnore]
    public override string Key => $"html:{Url}";

    /// <summary>
    /// Picks the story's title out of its element. Optional; where it is unset, the title is the
    /// element's text as a whole, for a story that is nothing but a headline.
    /// </summary>
    public string? TitleSelector { get; init; }

    /// <summary>
    /// Picks the story's date out of its element, read from a <c>datetime</c> or <c>content</c>
    /// attribute where it has one and from its text otherwise. Optional; a story without a date
    /// is dated when it is loaded.
    /// </summary>
    public string? DateSelector { get; init; }

    /// <summary>
    /// Picks the story's teaser out of its element. Optional; a story without one has no body
    /// beyond its title, unless <see cref="Selector"/> fetches one.
    /// </summary>
    public string? LeadSelector { get; init; }

    /// <summary>
    /// Picks the story's link out of its element, the address being the <c>href</c> of that
    /// element or else of the first link inside it. Optional; where it is unset, the link is the
    /// story's element itself or the first link inside it.
    /// </summary>
    public string? UrlSelector { get; init; }

    /// <summary>
    /// CSS selector picking the article body out of the page a story links to. Optional; as with
    /// the feeds, where it is set the body shown for a story is fetched from its own page.
    /// </summary>
    public string? Selector { get; init; }
}

/// <summary>
/// The newest posts of an account on X, read from X's API.
/// </summary>
/// <param name="Username">The account's handle, without the <c>@</c>: <c>XDevelopers</c>.</param>
public sealed record XNewsSource(string Username, TimeSpan Frequency) : NewsSource(Frequency)
{
    [JsonIgnore]
    public override string Key => $"x:{Username}";
}

/// <summary>
/// The newest posts on X matching a search, read from X's API.
/// </summary>
/// <param name="Query">The search in X's query language, as in <c>#dotnet -is:retweet lang:en</c>.</param>
public sealed record XSearchNewsSource(string Query, TimeSpan Frequency) : NewsSource(Frequency)
{
    [JsonIgnore]
    public override string Key => $"x-search:{Query}";
}
