using Microsoft.Extensions.Logging.Abstractions;
using News.Domain;
using News.Storage;
using PeterJuhasz.Repositories.FileSystem;

namespace News.Services.Tests;

/// <summary>
/// The items a group holds, kept between runs so the list is not empty while the sources are
/// fetched again.
/// </summary>
[TestClass]
public sealed class NewsItemStoreTests
{
    private const string GroupTitle = "Tech";

    private string _directoryPath = string.Empty;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void CreateStoreDirectory()
    {
        _directoryPath = Path.Combine(Path.GetTempPath(), $"news-store-{Guid.NewGuid():n}");
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
    public async Task LoadAsync_GroupNothingWasStoredFor_ReturnsNothing()
    {
        var store = CreateStore();

        var snapshot = await store.LoadAsync(GroupTitle, TestContext.CancellationToken);

        Assert.IsNull(snapshot);
    }

    [TestMethod]
    public async Task LoadAsync_AfterSave_ReturnsTheStoredItems()
    {
        var store = CreateStore();
        var loadedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var item = new NewsItem(
            Id: "1",
            Date: new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero),
            Title: "A story",
            Lead: "The lead",
            Url: "https://example.com/1",
            ImageUrl: "https://example.com/1.jpg",
            Content: "<p>The body</p>",
            Views: 1234,
            Reactions: new Dictionary<string, long> { [NewsReactions.Like] = 42 });

        await store.SaveAsync(
            GroupTitle,
            new NewsGroupSnapshot([new NewsSourceSnapshot("rss:https://example.com/feed", loadedAt, [item])]),
            TestContext.CancellationToken);

        // A second store reads the file rather than anything the first one held onto.
        var snapshot = await CreateStore().LoadAsync(GroupTitle, TestContext.CancellationToken);

        Assert.IsNotNull(snapshot);
        var source = snapshot.Sources.Single();
        Assert.AreEqual("rss:https://example.com/feed", source.SourceKey);
        Assert.AreEqual(loadedAt, source.LoadedAtUtc);

        var storedItem = source.Items.Single();
        Assert.AreEqual(item.Id, storedItem.Id);
        Assert.AreEqual(item.Date, storedItem.Date);
        Assert.AreEqual(item.Title, storedItem.Title);
        Assert.AreEqual(item.Lead, storedItem.Lead);
        Assert.AreEqual(item.Url, storedItem.Url);
        Assert.AreEqual(item.ImageUrl, storedItem.ImageUrl);
        Assert.AreEqual(item.Content, storedItem.Content);
        Assert.AreEqual(item.Views, storedItem.Views);
        Assert.AreEqual(42, storedItem.Reactions[NewsReactions.Like]);
    }

    [TestMethod]
    public async Task SaveAsync_Twice_ReplacesTheStoredItemsAndLeavesNoTemporaryFile()
    {
        var store = CreateStore();

        await store.SaveAsync(GroupTitle, CreateSnapshot("First"), TestContext.CancellationToken);
        await store.SaveAsync(GroupTitle, CreateSnapshot("Second"), TestContext.CancellationToken);

        var snapshot = await store.LoadAsync(GroupTitle, TestContext.CancellationToken);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual("Second", snapshot.Sources.Single().Items.Single().Title);
        Assert.HasCount(1, Directory.GetFiles(_directoryPath));
    }

    /// <summary>
    /// Groups are stored one file each, so the one being written leaves the others alone.
    /// </summary>
    [TestMethod]
    public async Task SaveAsync_TwoGroups_KeepsThemApart()
    {
        var store = CreateStore();

        await store.SaveAsync("Tech", CreateSnapshot("A tech story"), TestContext.CancellationToken);
        await store.SaveAsync("World", CreateSnapshot("A world story"), TestContext.CancellationToken);

        var tech = await store.LoadAsync("Tech", TestContext.CancellationToken);
        var world = await store.LoadAsync("World", TestContext.CancellationToken);

        Assert.IsNotNull(tech);
        Assert.IsNotNull(world);
        Assert.AreEqual("A tech story", tech.Sources.Single().Items.Single().Title);
        Assert.AreEqual("A world story", world.Sources.Single().Items.Single().Title);
    }

    /// <summary>
    /// The group a title names is the same group whatever case it is written in, as it is
    /// everywhere else the title identifies one.
    /// </summary>
    [TestMethod]
    public async Task LoadAsync_TitleInAnotherCase_ReturnsTheSameStoredItems()
    {
        var store = CreateStore();

        await store.SaveAsync("Tech", CreateSnapshot("A story"), TestContext.CancellationToken);
        var snapshot = await store.LoadAsync("TECH", TestContext.CancellationToken);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual("A story", snapshot.Sources.Single().Items.Single().Title);
    }

    /// <summary>
    /// A file left half-written, or written by an older shape of these records, is passed over
    /// rather than faulting the load.
    /// </summary>
    [TestMethod]
    public async Task LoadAsync_UnreadableFile_ReturnsNothing()
    {
        var store = CreateStore();
        await store.SaveAsync(GroupTitle, CreateSnapshot("A story"), TestContext.CancellationToken);
        var filePath = Directory.GetFiles(_directoryPath).Single();
        await File.WriteAllTextAsync(filePath, "{ \"sources\": [", TestContext.CancellationToken);

        var snapshot = await store.LoadAsync(GroupTitle, TestContext.CancellationToken);

        Assert.IsNull(snapshot);
    }

    private static NewsGroupSnapshot CreateSnapshot(string title) =>
        new([
            new NewsSourceSnapshot(
                "rss:https://example.com/feed",
                DateTimeOffset.UtcNow,
                [new NewsItem(Id: "1", Date: DateTimeOffset.UtcNow, Title: title, Lead: string.Empty, Url: "https://example.com/1")])
        ]);

    private NewsItemStore CreateStore() =>
        new(
            new DirectoryInfo(_directoryPath).GetPartition(),
            NullLogger<NewsItemStore>.Instance);
}
