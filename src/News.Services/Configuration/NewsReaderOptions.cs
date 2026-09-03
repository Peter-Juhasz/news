namespace News.Configuration;

public sealed class NewsReaderOptions
{
    public const string SectionName = "News";

    public string GroupsFilePath { get; set; } = "groups.yml";

    /// <summary>
    /// Folder the items of each group are stored in between runs.
    /// </summary>
    public string CacheDirectoryPath { get; set; } = @"%LOCALAPPDATA%\News\cache";
}
