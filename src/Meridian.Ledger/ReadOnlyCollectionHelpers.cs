using System.Collections.ObjectModel;

namespace Meridian.Ledger;

internal static class ReadOnlyCollectionHelpers
{
    public static IReadOnlyList<T> FreezeList<T>(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Array.AsReadOnly(items.ToArray());
    }

    public static IReadOnlyDictionary<TKey, TValue> FreezeDictionary<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>> items,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(items);

        Dictionary<TKey, TValue> copy = items switch
        {
            Dictionary<TKey, TValue> dictionary => new(dictionary, comparer ?? dictionary.Comparer),
            _ when comparer is not null => items.ToDictionary(pair => pair.Key, pair => pair.Value, comparer),
            _ => items.ToDictionary(pair => pair.Key, pair => pair.Value),
        };

        return new ReadOnlyDictionary<TKey, TValue>(copy);
    }
}
