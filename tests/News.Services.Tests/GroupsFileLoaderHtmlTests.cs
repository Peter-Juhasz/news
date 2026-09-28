using News.Domain;
using News.Groups;

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
    public void LoadFromPath_EveryPropertyNamed_ReadsEverySelector()
    {
        var source = LoadSingleSource("""
            - type: html
              url: https://example.com/news
              title: Example
              item: ul.stories li
              properties:
                title: h2
                date: time
                lead: p.summary
                url: 'a[rel="bookmark"]'
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
    public void LoadFromPath_NoPropertiesNamed_LeavesEveryPropertySelectorUnset()
    {
        var source = LoadSingleSource("""
            - type: html
              url: https://example.com/news
              item: ul.headlines a
            """);

        var html = Assert.IsInstanceOfType<HtmlNewsSource>(source);
        Assert.IsNull(html.TitleSelector);
        Assert.IsNull(html.DateSelector);
        Assert.IsNull(html.LeadSelector);
        Assert.IsNull(html.UrlSelector);
        Assert.IsNull(html.Selector);
    }

    [TestMethod]
    public void LoadFromPath_NoItemSelector_Throws()
    {
        WriteSources("""
            - type: html
              url: https://example.com/news
              properties:
                title: h2
            """);

        Assert.ThrowsExactly<InvalidDataException>(() => new GroupsFileLoader().LoadFromPath(_filePath));
    }

    [TestMethod]
    [DataRow("ul > li")]
    [DataRow("li:first-child")]
    [DataRow("h2, h3")]
    [DataRow("a[href^=http]")]
    [DataRow("article[")]
    public void LoadFromPath_ItemSelectorBeyondWhatTheParserTakes_Throws(string selector)
    {
        WriteSources($"""
            - type: html
              url: https://example.com/news
              item: "{selector}"
            """);

        Assert.ThrowsExactly<InvalidDataException>(() => new GroupsFileLoader().LoadFromPath(_filePath));
    }

    [TestMethod]
    public void LoadFromPath_PropertySelectorBeyondWhatTheParserTakes_Throws()
    {
        WriteSources("""
            - type: html
              url: https://example.com/news
              item: article
              properties:
                date: "time:first-of-type"
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
        File.WriteAllText(_filePath, $"groups:\n  - title: Pages\n    sources:\n{indented}\n");
    }
}
