namespace News.Configuration;

public sealed class NewsReaderOptions
{
    public const string SectionName = "News";

    public string GroupsFilePath { get; set; } = "groups.yml";
}
