using Microsoft.Extensions.Logging.Abstractions;
using News.Bookmarks;
using News.Domain;
using News.Storage;

namespace News.Services.Tests;

/// <summary>
/// The list of kept stories the app holds while it runs, and what reaches the file behind it.
/// </summary>
[TestClass]
public sealed class BookmarkServiceTests
{
    private static readonly RssNewsSource Source =
        new("https://example.com/feed", TimeSpan.FromMinutes(30)) { Title = "Example" };

    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task InitializeAsync_NothingStored_HoldsNothing()
    {
        using var service = CreateService(new FakeBookmarkStore());

        await service.InitializeAsync(TestContext.CancellationToken);

        Assert.IsEmpty(service.Current);
    }

    [TestMethod]
    public async Task InitializeAsync_StoredBookmarks_ReadsThemBack()
    {
        var store = new FakeBookmarkStore
        {
            Stored = new BookmarkCollection([CreateBookmark("1"), CreateBookmark("2")])
        };
        using var service = CreateService(store);

        await service.InitializeAsync(TestContext.CancellationToken);

        Assert.HasCount(2, service.Current);
        Assert.IsTrue(service.IsBookmarked("1"));
        Assert.IsTrue(service.IsBookmarked("2"));
    }

    /// <summary>
    /// A file carrying the same story twice, hand-edited or merged from two machines, is read
    /// rather than refused.
    /// </summary>
    [TestMethod]
    public async Task InitializeAsync_TheSameStoryTwice_KeepsItOnce()
    {
        var store = new FakeBookmarkStore
        {
            Stored = new BookmarkCollection([CreateBookmark("1", "First"), CreateBookmark("1", "Second")])
        };
        using var service = CreateService(store);

        await service.InitializeAsync(TestContext.CancellationToken);

        Assert.AreEqual("First", service.Current.Single().Item.Title);
    }

    [TestMethod]
    public async Task ToggleAsync_StoryNotKept_KeepsItAndWritesItDown()
    {
        var store = new FakeBookmarkStore();
        using var service = CreateService(store);
        await service.InitializeAsync(TestContext.CancellationToken);

        await service.ToggleAsync(CreateItem("1"), Source, TestContext.CancellationToken);

        Assert.IsTrue(service.IsBookmarked("1"));
        Assert.AreEqual("1", service.Current.Single().Item.Id);
        Assert.IsNotNull(store.Stored);
        Assert.AreEqual("1", store.Stored.Items.Single().Item.Id);
    }

    [TestMethod]
    public async Task ToggleAsync_StoryAlreadyKept_LetsGoOfItAndWritesThatDown()
    {
        var store = new FakeBookmarkStore { Stored = new BookmarkCollection([CreateBookmark("1")]) };
        using var service = CreateService(store);
        await service.InitializeAsync(TestContext.CancellationToken);

        await service.ToggleAsync(CreateItem("1"), Source, TestContext.CancellationToken);

        Assert.IsFalse(service.IsBookmarked("1"));
        Assert.IsEmpty(service.Current);
        Assert.IsNotNull(store.Stored);
        Assert.IsEmpty(store.Stored.Items);
    }

    /// <summary>
    /// The source is kept as it reads at the moment the story is, so the bookmark still knows
    /// where it was read from once that source has been edited or dropped.
    /// </summary>
    [TestMethod]
    public async Task ToggleAsync_KeepsTheSourceAsItReadsAtTheTime()
    {
        var store = new FakeBookmarkStore();
        using var service = CreateService(store);
        await service.InitializeAsync(TestContext.CancellationToken);

        await service.ToggleAsync(CreateItem("1"), Source, TestContext.CancellationToken);

        Assert.AreEqual(Source, service.Current.Single().Source);
    }

    [TestMethod]
    public async Task Current_SeveralKeptStories_HandsBackTheMostRecentlyKeptFirst()
    {
        using var service = CreateService(new FakeBookmarkStore());
        await service.InitializeAsync(TestContext.CancellationToken);

        await service.ToggleAsync(CreateItem("1"), Source, TestContext.CancellationToken);
        await service.ToggleAsync(CreateItem("2"), Source, TestContext.CancellationToken);

        Assert.AreEqual("2", service.Current[0].Item.Id);
        Assert.AreEqual("1", service.Current[1].Item.Id);
    }

    [TestMethod]
    public async Task ToggleAsync_SaysTheListHasChanged()
    {
        using var service = CreateService(new FakeBookmarkStore());
        await service.InitializeAsync(TestContext.CancellationToken);

        var changes = 0;
        service.Changed += (_, _) => changes++;

        await service.ToggleAsync(CreateItem("1"), Source, TestContext.CancellationToken);
        await service.ToggleAsync(CreateItem("1"), Source, TestContext.CancellationToken);

        Assert.AreEqual(2, changes);
    }

    /// <summary>
    /// The comparison a group merges its sources under, so a story two feeds disagree about the
    /// case of is still the one kept story.
    /// </summary>
    [TestMethod]
    public async Task IsBookmarked_KeyInAnotherCase_StillFindsTheStory()
    {
        using var service = CreateService(new FakeBookmarkStore());
        await service.InitializeAsync(TestContext.CancellationToken);

        await service.ToggleAsync(CreateItem("Story-1"), Source, TestContext.CancellationToken);

        Assert.IsTrue(service.IsBookmarked("story-1"));
    }

    /// <summary>
    /// A story carrying no identifier, address or title could never be recognised again, so
    /// there would be no letting go of it once it was kept.
    /// </summary>
    [TestMethod]
    public async Task ToggleAsync_StoryWithNothingToRecogniseItBy_KeepsNothing()
    {
        var store = new FakeBookmarkStore();
        using var service = CreateService(store);
        await service.InitializeAsync(TestContext.CancellationToken);

        var anonymous = new NewsItem(
            Id: null, Date: DateTimeOffset.UtcNow, Title: string.Empty, Lead: string.Empty, Url: string.Empty);

        await service.ToggleAsync(anonymous, Source, TestContext.CancellationToken);

        Assert.IsEmpty(service.Current);
        Assert.AreEqual(0, store.SaveCount);
    }

    [TestMethod]
    public async Task InitializeAsync_CalledTwice_ReadsTheStoreOnce()
    {
        var store = new FakeBookmarkStore { Stored = new BookmarkCollection([CreateBookmark("1")]) };
        using var service = CreateService(store);

        await service.InitializeAsync(TestContext.CancellationToken);
        await service.InitializeAsync(TestContext.CancellationToken);

        Assert.AreEqual(1, store.LoadCount);
        Assert.HasCount(1, service.Current);
    }

    private static BookmarkService CreateService(IBookmarkStore store) =>
        new(store, NullLogger<BookmarkService>.Instance);

    private static NewsItem CreateItem(string id, string title = "A story") =>
        new(
            Id: id,
            Date: new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero),
            Title: title,
            Lead: "The lead",
            Url: "https://example.com/" + id);

    private static BookmarkedNewsItem CreateBookmark(string id, string title = "A story") =>
        new(CreateItem(id, title), Source, new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero));

    private sealed class FakeBookmarkStore : IBookmarkStore
    {
        public BookmarkCollection? Stored { get; set; }

        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public Task<BookmarkCollection?> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return Task.FromResult(Stored);
        }

        public Task SaveAsync(BookmarkCollection bookmarks, CancellationToken cancellationToken)
        {
            Stored = bookmarks;
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
