using News.Domain;
using News.Groups;
using PeterJuhasz.Repositories.FileSystem;

namespace News.Services.Tests;

/// <summary>
/// The layout of the groups file: a list of groups, each with a list of sources, read by key.
/// </summary>
[TestClass]
public sealed class GroupsFileLoaderTests
{
    private string _filePath = string.Empty;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void CreateFilePath() =>
        _filePath = Path.Combine(Path.GetTempPath(), $"news-groups-{Guid.NewGuid():n}.yml");

    [TestCleanup]
    public void DeleteFile() => File.Delete(_filePath);

    /// <summary>
    /// The file the app ships with, comments and all, reads as it is written.
    /// </summary>
    [TestMethod]
    public async Task LoadAsync_ShippedGroupsFile_ReadsEverySource()
    {
        File.Copy(FindShippedGroupsFile(), _filePath);

        var group = Assert.ContainsSingle(await LoadAsync());

        Assert.AreEqual("Tech", group.Title);
        Assert.HasCount(5, group.Sources);
        var youTube = Assert.IsInstanceOfType<YouTubeNewsSource>(group.Sources[2]);
        Assert.AreEqual("UCvtT19MZW8dq5Wwfu6B0oxw", youTube.ChannelId);
        Assert.AreEqual(TimeSpan.FromHours(1), youTube.Frequency);
        Assert.AreEqual(NewsContentMode.Web, group.Sources[1].View);
    }

    [TestMethod]
    public async Task LoadAsync_OptionalKeysLeftOut_ReadsTheirDefaults()
    {
        await File.WriteAllTextAsync(_filePath, """
            groups:
              - title: Tech
                sources:
                  - type: RSS
                    url: " https://example.com/feed "
                    title:
                    unknown: passed over
                  - type: github
                    repository: dotnet/runtime
            """, TestContext.CancellationToken);

        var group = Assert.ContainsSingle(await LoadAsync());

        var rss = Assert.IsInstanceOfType<RssNewsSource>(group.Sources[0]);
        Assert.AreEqual("https://example.com/feed", rss.Url);
        Assert.AreEqual(NewsSource.DefaultFrequency, rss.Frequency);
        Assert.IsNull(rss.Title);
        Assert.IsNull(rss.View);
        var gitHub = Assert.IsInstanceOfType<GitHubNewsSource>(group.Sources[1]);
        Assert.AreEqual(GitHubFeed.Releases, gitHub.Feed);
        Assert.AreEqual(NewsSource.DefaultFrequency, gitHub.Frequency);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("# nothing but a comment\n")]
    [DataRow("groups:\n")]
    [DataRow("- title: Tech\n")]
    [DataRow("groups: Tech\n")]
    [DataRow("groups:\n  - Tech\n")]
    [DataRow("groups:\n  - title: Tech\n    sources:\n      - type: rss\n        url: [a, b]\n")]
    [DataRow("groups:\n  - title: Tech\n    sources:\n      - type: rss\n        url: https://example.com/feed\n        frequency: soon\n")]
    [DataRow("groups:\n  - title: Tech\n    sources:\n      -\n")]
    [DataRow("groups: [\n")]
    [DataRow("groups:\n  - title: Tech\n    sources:\n      - url: https://example.com/feed\n")]
    [DataRow("groups:\n  - title: Tech\n    sources:\n      - type: gopher\n        url: https://example.com/feed\n")]
    [DataRow("groups:\n  - title: Tech\n    sources:\n      - type: rss\n")]
    [DataRow("groups:\n  - title: Tech\n    sources:\n      - type: rss\n        url: https://example.com/feed\n        frequency: -00:30:00\n")]
    [DataRow("groups:\n  - title: Tech\n    sources:\n      - type: rss\n        url: https://example.com/feed\n        view: comic\n")]
    public async Task LoadAsync_NotLaidOutAsGroupsOfSources_Throws(string yaml)
    {
        await File.WriteAllTextAsync(_filePath, yaml, TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => LoadAsync());
    }

    private Task<IReadOnlyList<NewsGroup>> LoadAsync() =>
        new GroupsFileLoader().LoadAsync(new FileInfo(_filePath).AsBlob(), TestContext.CancellationToken);

    private static string FindShippedGroupsFile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "News.Client.WinUI3", "groups.yml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("The shipped groups file was not found above the test output.");
    }
}
