using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using News.Configuration;
using News.Domain;
using News.Storage;

namespace News.Services.Tests;

/// <summary>
/// The stories set aside to come back to, kept in the one file configuration names.
/// </summary>
[TestClass]
public sealed class BookmarkStoreTests
{
    private string _directoryPath = string.Empty;
    private string _filePath = string.Empty;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void CreateStoreDirectory()
    {
        _directoryPath = Path.Combine(Path.GetTempPath(), $"news-bookmarks-{Guid.NewGuid():n}");
        _filePath = Path.Combine(_directoryPath, "bookmarks.json");
    }

    [TestCleanup]
    public void DeleteStoreDirectory()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_NothingStored_ReturnsNothing()
    {
        var store = CreateStore();

        var bookmarks = await store.LoadAsync(TestContext.CancellationToken);

        Assert.IsNull(bookmarks);
    }

    /// <summary>
    /// The source is stored whole, so a setting only one kind of source has — the RSS selector —
    /// comes back on the kind that has it rather than being flattened away.
    /// </summary>
    [TestMethod]
    public async Task LoadAsync_AfterSave_ReturnsTheSourceAsTheKindItWas()
    {
        var store = CreateStore();
        var source = new RssNewsSource("https://example.com/feed", TimeSpan.FromMinutes(30))
        {
            Title = "Example",
            View = NewsContentMode.Html,
            Selector = "article .entry-content"
        };

        await store.SaveAsync(new BookmarkCollection([CreateBookmark(source)]), TestContext.CancellationToken);

        // A second store reads the file rather than anything the first one held onto.
        var bookmarks = await CreateStore().LoadAsync(TestContext.CancellationToken);

        Assert.IsNotNull(bookmarks);
        var stored = bookmarks.Items.Single();
        var storedSource = Assert.IsInstanceOfType<RssNewsSource>(stored.Source);
        Assert.AreEqual("https://example.com/feed", storedSource.Url);
        Assert.AreEqual(TimeSpan.FromMinutes(30), storedSource.Frequency);
        Assert.AreEqual("Example", storedSource.Title);
        Assert.AreEqual(NewsContentMode.Html, storedSource.View);
        Assert.AreEqual("article .entry-content", storedSource.Selector);
        Assert.AreEqual("rss:https://example.com/feed", storedSource.Key);
    }

    [TestMethod]
    public async Task LoadAsync_AfterSave_ReturnsEverySourceKindAsItself()
    {
        var store = CreateStore();

        await store.SaveAsync(
            new BookmarkCollection([
                CreateBookmark(new RssNewsSource("https://example.com/feed", TimeSpan.FromMinutes(30)), "1"),
                CreateBookmark(new AtomNewsSource("https://example.com/atom", TimeSpan.FromMinutes(30)), "2"),
                CreateBookmark(new RedditNewsSource("dotnet", TimeSpan.FromMinutes(15)), "3"),
                CreateBookmark(new YouTubeNewsSource("UC-channel", TimeSpan.FromHours(1)), "4"),
                CreateBookmark(new GitHubNewsSource("dotnet/runtime", GitHubFeed.Pulls, TimeSpan.FromHours(1)), "5")
            ]),
            TestContext.CancellationToken);

        var bookmarks = await CreateStore().LoadAsync(TestContext.CancellationToken);

        Assert.IsNotNull(bookmarks);
        Assert.HasCount(5, bookmarks.Items);
        Assert.IsInstanceOfType<RssNewsSource>(bookmarks.Items[0].Source);
        Assert.IsInstanceOfType<AtomNewsSource>(bookmarks.Items[1].Source);
        Assert.IsInstanceOfType<RedditNewsSource>(bookmarks.Items[2].Source);
        Assert.IsInstanceOfType<YouTubeNewsSource>(bookmarks.Items[3].Source);
        var gitHub = Assert.IsInstanceOfType<GitHubNewsSource>(bookmarks.Items[4].Source);
        Assert.AreEqual("dotnet/runtime", gitHub.Repository);
        Assert.AreEqual(GitHubFeed.Pulls, gitHub.Feed);
    }

    /// <summary>
    /// The members a source works out for itself are restated by whatever is read back, so
    /// writing them would only be a second answer in the file for the file to disagree with.
    /// </summary>
    [TestMethod]
    public async Task SaveAsync_LeavesTheComputedMembersOfASourceOutOfTheFile()
    {
        var store = CreateStore();

        await store.SaveAsync(
            new BookmarkCollection([CreateBookmark(new RedditNewsSource("dotnet", TimeSpan.FromMinutes(15)))]),
            TestContext.CancellationToken);

        var json = await File.ReadAllTextAsync(_filePath, TestContext.CancellationToken);

        // What the source is told is written down; what it works out from that is not.
        Assert.Contains("\"subreddit\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"feedUrl\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"key\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// The type discriminator is the name the groups file already gives the source kind, so a
    /// class renamed later still reads back what was written under it.
    /// </summary>
    [TestMethod]
    public async Task SaveAsync_NamesTheSourceKindAsTheGroupsFileDoes()
    {
        var store = CreateStore();

        await store.SaveAsync(
            new BookmarkCollection([CreateBookmark(new RssNewsSource("https://example.com/feed", TimeSpan.FromMinutes(30)))]),
            TestContext.CancellationToken);

        var json = await File.ReadAllTextAsync(_filePath, TestContext.CancellationToken);

        Assert.Contains("\"type\": \"rss\"", json, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task SaveAsync_Twice_ReplacesTheStoredBookmarksAndLeavesNoTemporaryFile()
    {
        var store = CreateStore();

        await store.SaveAsync(new BookmarkCollection([CreateBookmark(title: "First")]), TestContext.CancellationToken);
        await store.SaveAsync(new BookmarkCollection([CreateBookmark(title: "Second")]), TestContext.CancellationToken);

        var bookmarks = await store.LoadAsync(TestContext.CancellationToken);

        Assert.IsNotNull(bookmarks);
        Assert.AreEqual("Second", bookmarks.Items.Single().Item.Title);
        Assert.HasCount(1, Directory.GetFiles(_directoryPath));
    }

    /// <summary>
    /// Bookmarks are not something a later load brings back, so a file that cannot be read is set
    /// aside to be repaired rather than left to be written over.
    /// </summary>
    [TestMethod]
    public async Task LoadAsync_UnreadableFile_SetsItAsideAndReturnsNothing()
    {
        var store = CreateStore();
        await store.SaveAsync(new BookmarkCollection([CreateBookmark()]), TestContext.CancellationToken);
        await File.WriteAllTextAsync(_filePath, "{ \"items\": [", TestContext.CancellationToken);

        var bookmarks = await store.LoadAsync(TestContext.CancellationToken);

        Assert.IsNull(bookmarks);
        Assert.IsFalse(File.Exists(_filePath), "The unreadable file was left where the next save would replace it.");
        Assert.HasCount(1, Directory.GetFiles(_directoryPath, "bookmarks.json.corrupt-*"));
    }

    private static BookmarkedNewsItem CreateBookmark(
        NewsSource? source = null,
        string id = "1",
        string title = "A story") =>
        new(
            new NewsItem(
                Id: id,
                Date: new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero),
                Title: title,
                Lead: "The lead",
                Url: $"https://example.com/{id}"),
            source ?? new RssNewsSource("https://example.com/feed", TimeSpan.FromMinutes(30)),
            new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero));

    private BookmarkStore CreateStore() =>
        new(
            Options.Create(new NewsReaderOptions { BookmarksFilePath = _filePath }),
            NullLogger<BookmarkStore>.Instance);
}
