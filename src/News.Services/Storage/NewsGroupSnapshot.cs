using News.Domain;

namespace News.Storage;

/// <summary>
/// What a group was showing when its sources were last loaded, as it is written to and read
/// back from disk.
/// </summary>
public sealed record NewsGroupSnapshot(IReadOnlyList<NewsSourceSnapshot> Sources);

/// <summary>
/// The items one source of a group last returned, and when it returned them — the load time is
/// kept so a source fetched moments before the app closed is not fetched again on the next start.
/// </summary>
public sealed record NewsSourceSnapshot(
    string SourceKey,
    DateTimeOffset LoadedAtUtc,
    IReadOnlyList<NewsItem> Items);
