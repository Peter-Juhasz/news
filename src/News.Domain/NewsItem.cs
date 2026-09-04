namespace News.Domain;

public sealed record NewsItem(
    string? Id,
    DateTimeOffset Date,
    string Title,
    string Lead,
    string Url,
    string? ImageUrl = null,
    string? Content = null,
    long? Views = null,
    IReadOnlyDictionary<string, long>? Reactions = null)
{
    /// <summary>
    /// Audience reactions by name, such as "Like", for the sources that publish counts for them.
    /// </summary>
    public IReadOnlyDictionary<string, long> Reactions { get; init; } =
        Reactions ?? NewsReactions.None;

    /// <summary>
    /// Identifies the story across the loads that return it again, so the same story loaded twice
    /// is recognised as the one already listed rather than a second one. Feeds that publish no
    /// identifier are told apart by their address, and the ones that publish neither by their
    /// title — which is all that is left to tell them apart by.
    /// </summary>
    public string Key =>
        !string.IsNullOrWhiteSpace(Id) ? Id
        : !string.IsNullOrWhiteSpace(Url) ? Url
        : Title;
}
