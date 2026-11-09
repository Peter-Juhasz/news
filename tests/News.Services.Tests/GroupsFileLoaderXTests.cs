using News.Domain;
using News.Groups;
using PeterJuhasz.Repositories.FileSystem;

namespace News.Services.Tests;

/// <summary>
/// An X source names the account whose posts it follows, and an X search source the search whose
/// posts it follows; the one set into X's URLs as written, the other sent as a query.
/// </summary>
[TestClass]
public sealed class GroupsFileLoaderXTests
{
    private string _filePath = string.Empty;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void CreateFilePath() =>
        _filePath = Path.Combine(Path.GetTempPath(), $"news-groups-{Guid.NewGuid():n}.yml");

    [TestCleanup]
    public void DeleteFile() => File.Delete(_filePath);

    [TestMethod]
    [DataRow("XDevelopers")]
    [DataRow("\"@XDevelopers\"")]
    public async Task LoadAsync_XSource_ReadsTheHandleWithoutItsAt(string username)
    {
        var source = await LoadSingleSourceAsync($"""
            - type: x
              username: {username}
            """);

        var x = Assert.IsInstanceOfType<XNewsSource>(source);
        Assert.AreEqual("XDevelopers", x.Username);
        Assert.AreEqual("x:XDevelopers", x.Key);
    }

    [TestMethod]
    [DataRow("https://x.com/XDevelopers")]
    [DataRow("X Developers")]
    [DataRow("XDevelopersXDevs")]
    [DataRow("\"@\"")]
    public async Task LoadAsync_UsernameNotAHandle_Throws(string username)
    {
        WriteSources($"""
            - type: x
              username: {username}
            """);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => LoadAsync());
    }

    [TestMethod]
    public async Task LoadAsync_XSearchSource_ReadsTheQueryAsWritten()
    {
        var source = await LoadSingleSourceAsync("""
            - type: x-search
              query: "#dotnet -is:retweet lang:en"
            """);

        var search = Assert.IsInstanceOfType<XSearchNewsSource>(source);
        Assert.AreEqual("#dotnet -is:retweet lang:en", search.Query);
        Assert.AreEqual("x-search:#dotnet -is:retweet lang:en", search.Key);
    }

    [TestMethod]
    [DataRow("x")]
    [DataRow("x-search")]
    public async Task LoadAsync_XSourceNamingNothingToRead_Throws(string type)
    {
        WriteSources($"""
            - type: {type}
              title: X
            """);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => LoadAsync());
    }

    private async Task<NewsSource> LoadSingleSourceAsync(string sources)
    {
        WriteSources(sources);
        var group = Assert.ContainsSingle(await LoadAsync());
        return Assert.ContainsSingle(group.Sources);
    }

    private Task<IReadOnlyList<NewsGroup>> LoadAsync() =>
        new GroupsFileLoader().LoadAsync(new FileInfo(_filePath).AsBlob(), TestContext.CancellationToken);

    private void WriteSources(string sources)
    {
        var indented = string.Join('\n', sources.Split('\n').Select(line => "      " + line));
        File.WriteAllText(_filePath, $"groups:\n  - title: Social\n    sources:\n{indented}\n");
    }
}
