namespace News.Collections;

/// <summary>
/// Brings a list in line with the one it should be showing by applying the difference between
/// them: entries that are gone are taken out, entries that are new are put in, and entries that
/// are in both are left where they are.
/// </summary>
/// <remarks>
/// Written for the observable collections a list view is bound to, where leaving an entry alone
/// is what keeps the row it is drawn in — and with it the reader's place in the list, the
/// selection, and the images already decoded for it. Replacing the collection's contents would
/// throw all of that away on every load.
/// </remarks>
public static class ObservableListReconciler
{
    /// <summary>
    /// Brings <paramref name="target"/> in line with <paramref name="desired"/>, building the
    /// entries it is missing and handing the ones it already has whatever they are now supposed
    /// to show.
    /// </summary>
    /// <param name="target">The list to change, in place.</param>
    /// <param name="desired">What the list should hold afterwards, in order. No two entries may
    /// share a key.</param>
    /// <param name="targetKeySelector">Reads the key of an entry the list holds.</param>
    /// <param name="desiredKeySelector">Reads the key of an entry the list should hold.</param>
    /// <param name="create">Builds an entry for the list out of one it does not hold yet.</param>
    /// <param name="update">Hands an entry the list already holds what it should show now.
    /// Optional.</param>
    /// <param name="keyComparer">How keys are compared. Optional.</param>
    public static void Reconcile<TTarget, TDesired, TKey>(
        IList<TTarget> target,
        IReadOnlyList<TDesired> desired,
        Func<TTarget, TKey> targetKeySelector,
        Func<TDesired, TKey> desiredKeySelector,
        Func<TDesired, TTarget> create,
        Action<TTarget, TDesired>? update = null,
        IEqualityComparer<TKey>? keyComparer = null)
    {
        var comparer = keyComparer ?? EqualityComparer<TKey>.Default;

        var desiredKeys = new HashSet<TKey>(desired.Count, comparer);
        foreach (var entry in desired)
        {
            if (!desiredKeys.Add(desiredKeySelector(entry)))
            {
                throw new ArgumentException(
                    "The desired list holds more than one entry under the same key.",
                    nameof(desired));
            }
        }

        // Taken out from the back, so the entries still to be looked at keep the positions they
        // were found at.
        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!desiredKeys.Contains(targetKeySelector(target[index])))
            {
                target.RemoveAt(index);
            }
        }

        // Everything below this index is settled: it holds what the desired list holds, in the
        // order the desired list holds it.
        for (var index = 0; index < desired.Count; index++)
        {
            var entry = desired[index];
            var key = desiredKeySelector(entry);

            if (index < target.Count && comparer.Equals(targetKeySelector(target[index]), key))
            {
                update?.Invoke(target[index], entry);
                continue;
            }

            // The entry is in the list but further along — it moved, which is a step back to
            // where it belongs rather than a rebuild.
            var currentIndex = IndexOfKey(target, key, index + 1, targetKeySelector, comparer);
            if (currentIndex >= 0)
            {
                var moved = target[currentIndex];
                target.RemoveAt(currentIndex);
                target.Insert(index, moved);
                update?.Invoke(moved, entry);
                continue;
            }

            target.Insert(index, create(entry));
        }

        // Anything past the end is a second copy of an entry the list was already holding twice.
        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    /// <summary>
    /// Brings <paramref name="target"/> in line with <paramref name="desired"/>, where the two
    /// hold entries of the same kind. An entry the list already holds is kept as it is, so this
    /// is for the case where the desired entries are the very ones the list is holding.
    /// </summary>
    public static void Reconcile<TEntry, TKey>(
        IList<TEntry> target,
        IReadOnlyList<TEntry> desired,
        Func<TEntry, TKey> keySelector,
        IEqualityComparer<TKey>? keyComparer = null) =>
        Reconcile(
            target,
            desired,
            keySelector,
            keySelector,
            static entry => entry,
            update: null,
            keyComparer);

    private static int IndexOfKey<TEntry, TKey>(
        IList<TEntry> entries,
        TKey key,
        int startIndex,
        Func<TEntry, TKey> keySelector,
        IEqualityComparer<TKey> comparer)
    {
        for (var index = startIndex; index < entries.Count; index++)
        {
            if (comparer.Equals(keySelector(entries[index]), key))
            {
                return index;
            }
        }

        return -1;
    }
}
