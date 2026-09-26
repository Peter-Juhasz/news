using News.Domain;
using News.Groups;

namespace News.Services.Tests;

/// <summary>
/// A GitHub source names its repository and what to follow in it, the one set into GitHub's URLs
/// as written and the other defaulting to the releases.
/// </summary>
[TestClass]
public sealed class GroupsFileLoaderGitHubTests
{
    private string _filePath = string.Empty;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void CreateFilePath() =>
        _filePath = Path.Combine(Path.GetTempPath(), $"news-groups-{Guid.NewGuid():n}.yml");

    [TestCleanup]
    public void DeleteFile() => File.Delete(_filePath);

    [TestMethod]
    public void LoadFromPath_NoFeedNamed_FollowsTheReleasesFeed()
    {
        var source = LoadSingleSource("""
            - type: github
              repository: dotnet/runtime
            """);

        var gitHub = Assert.IsInstanceOfType<GitHubNewsSource>(source);
        Assert.AreEqual("dotnet/runtime", gitHub.Repository);
        Assert.AreEqual(GitHubFeed.Releases, gitHub.Feed);
        Assert.AreEqual("https://github.com/dotnet/runtime/releases.atom", gitHub.FeedUrl);
        Assert.AreEqual("github:dotnet/runtime/releases", gitHub.Key);
    }

    [TestMethod]
    [DataRow("tags", "https://github.com/dotnet/aspnetcore/tags.atom")]
    [DataRow("Commits", "https://github.com/dotnet/aspnetcore/commits.atom")]
    [DataRow("issues", null)]
    [DataRow("pulls", null)]
    public void LoadFromPath_FeedNamed_ReadsTheFeedFromItsOwnUrlOrTheApi(string feed, string? feedUrl)
    {
        var source = LoadSingleSource($"""
            - type: github
              repository: dotnet/aspnetcore
              feed: {feed}
            """);

        var gitHub = Assert.IsInstanceOfType<GitHubNewsSource>(source);
        Assert.AreEqual(feedUrl, gitHub.FeedUrl);
    }

    [TestMethod]
    [DataRow("runtime")]
    [DataRow("https://github.com/dotnet/runtime")]
    [DataRow("dotnet/runtime/issues")]
    [DataRow("dotnet/run time")]
    public void LoadFromPath_RepositoryNotOwnerAndName_Throws(string repository)
    {
        WriteSources($"""
            - type: github
              repository: {repository}
            """);

        Assert.ThrowsExactly<InvalidDataException>(() => new GroupsFileLoader().LoadFromPath(_filePath));
    }

    [TestMethod]
    [DataRow("stars")]
    [DataRow("3")]
    public void LoadFromPath_FeedNotOneGitHubPublishes_Throws(string feed)
    {
        WriteSources($"""
            - type: github
              repository: dotnet/runtime
              feed: "{feed}"
            """);

        Assert.ThrowsExactly<InvalidDataException>(() => new GroupsFileLoader().LoadFromPath(_filePath));
    }

    private NewsSource LoadSingleSource(string sources)
    {
        WriteSources(sources);
        var group = Assert.ContainsSingle(new GroupsFileLoader().LoadFromPath(_filePath));
        return Assert.ContainsSingle(group.Sources);
    }

    private void WriteSources(string sources)
    {
        var indented = string.Join('\n', sources.Split('\n').Select(line => "      " + line));
        File.WriteAllText(_filePath, $"groups:\n  - title: Code\n    sources:\n{indented}\n");
    }
}
