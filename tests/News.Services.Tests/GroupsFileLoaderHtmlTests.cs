using News.Domain;
using News.Groups;
using PeterJuhasz.Repositories.FileSystem;

namespace News.Services.Tests;

/// <summary>
/// An HTML source names the page, a selector for its stories and, optionally, selectors for the
/// properties of a story — each checked when the file is read, being a subset of CSS that a
/// page would otherwise turn away on every load.
/// </summary>
[TestClass]
public sealed class GroupsFileLoaderHtmlTests
{
    private string _filePath = string.Empty;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void CreateFilePath() =>
        _filePath = Path.Combine(Path.GetTempPath(), $"news-groups-{Guid.NewGuid():n}.yml");

    [TestCleanup]
    public void DeleteFile() => File.Delete(_filePath);

    [TestMethod]
    public async Task LoadAsync_EveryPropertyNamed_ReadsEverySelector()
    {
        var source = await LoadSingleSourceAsync("""
            - type: html
              url: https://example.com/news
              title: Example
              itemSelector: ul.stories li
              titleSelector: h2
              dateSelector: time
              leadSelector: p.summary
              urlSelector: 'a[rel="bookmark"]'
              selector: article .entry-content
            """);

        var html = Assert.IsInstanceOfType<HtmlNewsSource>(source);
        Assert.AreEqual("https://example.com/news", html.Url);
        Assert.AreEqual("Example", html.Title);
        Assert.AreEqual("ul.stories li", html.ItemSelector);
        Assert.AreEqual("h2", html.TitleSelector);
        Assert.AreEqual("time", html.DateSelector);
        Assert.AreEqual("p.summary", html.LeadSelector);
        Assert.AreEqual("a[rel=\"bookmark\"]", html.UrlSelector);
        Assert.AreEqual("article .entry-content", html.Selector);
        Assert.AreEqual("html:https://example.com/news", html.Key);
    }

    [TestMethod]
    public async Task LoadAsync_NoPropertiesNamed_LeavesEveryPropertySelectorUnset()
    {
        var source = await LoadSingleSourceAsync("""
            - type: html
              url: https://example.com/news
              itemSelector: ul.headlines a
            """);

        var html = Assert.IsInstanceOfType<HtmlNewsSource>(source);
        Assert.IsNull(html.TitleSelector);
        Assert.IsNull(html.DateSelector);
        Assert.IsNull(html.LeadSelector);
        Assert.IsNull(html.UrlSelector);
        Assert.IsNull(html.Selector);
    }

    [TestMethod]
    public async Task LoadAsync_NoItemSelector_Throws()
    {
        WriteSources("""
            - type: html
              url: https://example.com/news
              titleSelector: h2

            """);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => LoadAsync());
    }

    [TestMethod]
    public async Task LoadAsync_ItemSelectorTheParserCannotRead_Throws()
    {
        WriteSources("""
            - type: html
              url: https://example.com/news
              itemSelector: "article["
            """);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => LoadAsync());
    }

    [TestMethod]
    public async Task LoadAsync_PropertySelectorTheParserCannotRead_Throws()
    {
        WriteSources("""
            - type: html
              url: https://example.com/news
              itemSelector: article
              dateSelector: "time["
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
        File.WriteAllText(_filePath, $"groups:\n  - title: Pages\n    sources:\n{indented}\n");
    }
}
