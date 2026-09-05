namespace News.Configuration;

public sealed class NewsReaderOptions
{
    public const string SectionName = "News";

    public string GroupsFilePath { get; set; } = "groups.yml";

    /// <summary>
    /// Folder the items of each group are stored in between runs.
    /// </summary>
    public string CacheDirectoryPath { get; set; } = @"%LOCALAPPDATA%\News\cache";

    /// <summary>
    /// File the stories set aside to come back to are kept in.
    /// </summary>
    public string BookmarksFilePath { get; set; } = @"%LOCALAPPDATA%\News\bookmarks.json";
}
