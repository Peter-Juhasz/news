using System.Collections.ObjectModel;
using System.Collections.Specialized;
using News.Collections;

namespace News.Services.Tests;

/// <summary>
/// Bringing a list in line with the one it should be showing, by the difference between them
/// rather than by replacing what it holds.
/// </summary>
[TestClass]
public sealed class ObservableListReconcilerTests
{
    [TestMethod]
    public void Reconcile_EmptyList_PutsInEveryEntryInOrder()
    {
        var target = new ObservableCollection<Row>();

        Reconcile(target, [Row.Of("a"), Row.Of("b"), Row.Of("c")]);

        Assert.AreSequenceEqual(new[] { "a", "b", "c" }, Keys(target));
    }

    [TestMethod]
    public void Reconcile_UnchangedList_ChangesNothing()
    {
        var target = Rows("a", "b", "c");
        var changes = new ChangeLog(target);

        Reconcile(target, [Row.Of("a"), Row.Of("b"), Row.Of("c")]);

        Assert.AreEqual(0, changes.Count, "An unchanged list should not be reported as having changed at all.");
    }

    [TestMethod]
    public void Reconcile_EntriesAddedAtTheFront_KeepsTheEntriesItAlreadyHeld()
    {
        var target = Rows("b", "c");
        var held = target.ToList();
        var changes = new ChangeLog(target);

        Reconcile(target, [Row.Of("a"), Row.Of("b"), Row.Of("c")]);

        Assert.AreSequenceEqual(new[] { "a", "b", "c" }, Keys(target));
        Assert.AreSame(held[0], target[1]);
        Assert.AreSame(held[1], target[2]);
        Assert.AreEqual(1, changes.Count, "One entry arrived, so the list should report one change.");
        Assert.AreEqual(NotifyCollectionChangedAction.Add, changes.Last);
    }

    [TestMethod]
    public void Reconcile_EntryTakenOutOfTheMiddle_LeavesTheRestWhereTheyAre()
    {
        var target = Rows("a", "b", "c");
        var held = target.ToList();

        Reconcile(target, [Row.Of("a"), Row.Of("c")]);

        Assert.AreSequenceEqual(new[] { "a", "c" }, Keys(target));
        Assert.AreSame(held[0], target[0]);
        Assert.AreSame(held[2], target[1]);
    }

    [TestMethod]
    public void Reconcile_EntriesInAnotherOrder_MovesThemWithoutRebuildingThem()
    {
        var target = Rows("a", "b", "c");
        var held = target.ToList();

        Reconcile(target, [Row.Of("c"), Row.Of("a"), Row.Of("b")]);

        Assert.AreSequenceEqual(new[] { "c", "a", "b" }, Keys(target));
        Assert.AreSame(held[2], target[0]);
        Assert.AreSame(held[0], target[1]);
        Assert.AreSame(held[1], target[2]);
    }

    [TestMethod]
    public void Reconcile_ListReplacedOutright_HoldsOnlyTheNewEntries()
    {
        var target = Rows("a", "b");

        Reconcile(target, [Row.Of("x"), Row.Of("y"), Row.Of("z")]);

        Assert.AreSequenceEqual(new[] { "x", "y", "z" }, Keys(target));
    }

    [TestMethod]
    public void Reconcile_NothingDesired_EmptiesTheList()
    {
        var target = Rows("a", "b");

        Reconcile(target, []);

        Assert.IsEmpty(target);
    }

    [TestMethod]
    public void Reconcile_EntryTheListAlreadyHolds_HandsItWhatItShouldShowNow()
    {
        var target = Rows("a", "b");
        var held = target[1];

        ObservableListReconciler.Reconcile(
            target,
            [new Row("a", "first"), new Row("b", "second")],
            static row => row.Key,
            static row => row.Key,
            static row => row,
            update: static (listed, desired) => listed.Text = desired.Text,
            StringComparer.Ordinal);

        Assert.AreSame(held, target[1]);
        Assert.AreEqual("second", target[1].Text);
    }

    [TestMethod]
    public void Reconcile_EntryTheListDoesNotHold_BuildsItFromWhatWasAskedFor()
    {
        var target = new ObservableCollection<Row>();

        ObservableListReconciler.Reconcile(
            target,
            new[] { "a", "b" },
            static row => row.Key,
            static key => key,
            create: static key => new Row(key, $"built {key}"),
            update: null,
            StringComparer.Ordinal);

        Assert.AreSequenceEqual(new[] { "built a", "built b" }, target.Select(row => row.Text).ToArray());
    }

    [TestMethod]
    public void Reconcile_TwoEntriesUnderTheSameKey_IsRefused()
    {
        var target = new ObservableCollection<Row>();

        Assert.ThrowsExactly<ArgumentException>(() => Reconcile(target, [Row.Of("a"), Row.Of("a")]));
    }

    [TestMethod]
    public void Reconcile_ListHoldingTheSameKeyTwice_KeepsOneOfThem()
    {
        var target = Rows("a", "a", "b");

        Reconcile(target, [Row.Of("a"), Row.Of("b")]);

        Assert.AreSequenceEqual(new[] { "a", "b" }, Keys(target));
    }

    [TestMethod]
    public void Reconcile_KeysComparedTheWayItWasAskedFor_TreatsThemAsTheSameEntry()
    {
        var target = Rows("A");
        var held = target[0];

        ObservableListReconciler.Reconcile(
            target,
            [Row.Of("a")],
            static row => row.Key,
            StringComparer.OrdinalIgnoreCase);

        Assert.AreSame(held, target[0]);
    }

    private static void Reconcile(ObservableCollection<Row> target, IReadOnlyList<Row> desired) =>
        ObservableListReconciler.Reconcile(target, desired, static row => row.Key, StringComparer.Ordinal);

    private static ObservableCollection<Row> Rows(params string[] keys) =>
        [.. keys.Select(Row.Of)];

    private static string[] Keys(IEnumerable<Row> rows) =>
        rows.Select(row => row.Key).ToArray();

    private sealed class Row(string key, string text)
    {
        public string Key { get; } = key;

        public string Text { get; set; } = text;

        public static Row Of(string key) => new(key, key);
    }

    /// <summary>
    /// Counts what a list reported, so a reconcile that had nothing to do can be told apart from
    /// one that replaced everything.
    /// </summary>
    private sealed class ChangeLog
    {
        public ChangeLog(ObservableCollection<Row> target) =>
            target.CollectionChanged += (_, args) =>
            {
                Count++;
                Last = args.Action;
            };

        public int Count { get; private set; }

        public NotifyCollectionChangedAction? Last { get; private set; }
    }
}
