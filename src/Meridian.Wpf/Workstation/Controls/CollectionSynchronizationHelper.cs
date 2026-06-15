using System.Collections;
using System.Collections.ObjectModel;

namespace Meridian.Wpf.Workstation.Controls;

internal static class CollectionSynchronizationHelper
{
    public static IEnumerable? GetRuntimeRows(object? table)
    {
        if (table is null)
        {
            return null;
        }

        var tableType = table.GetType();
        while (tableType is not null)
        {
            var rowsProperty = tableType.GetProperty("Rows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
            if (rowsProperty?.GetValue(table) is IEnumerable runtimeRows)
            {
                return runtimeRows;
            }

            tableType = tableType.BaseType;
        }

        return null;
    }

    public static bool HasItems(IEnumerable? items)
        => TryGetCount(items, out var count) ? count > 0 : HasAny(items);

    public static bool TryGetCount(IEnumerable? items, out int count)
    {
        switch (items)
        {
            case null:
                count = 0;
                return true;
            case ICollection collection:
                count = collection.Count;
                return true;
        }

        var itemsType = items.GetType();
        foreach (var interfaceType in itemsType.GetInterfaces().Prepend(itemsType))
        {
            if (!interfaceType.IsGenericType)
            {
                continue;
            }

            var definition = interfaceType.GetGenericTypeDefinition();
            if (definition != typeof(ICollection<>) && definition != typeof(IReadOnlyCollection<>))
            {
                continue;
            }

            var countProperty = interfaceType.GetProperty(nameof(ICollection<object>.Count));
            if (countProperty?.GetValue(items) is int genericCount)
            {
                count = genericCount;
                return true;
            }
        }

        count = 0;
        return false;
    }

    public static bool ContainsAnyByReference(IEnumerable? source, IEnumerable? candidates)
    {
        if (source is null || candidates is null)
        {
            return false;
        }

        HashSet<object> sourceItems = new(ReferenceEqualityComparer.Instance);
        foreach (var item in source)
        {
            if (item is not null)
            {
                sourceItems.Add(item);
            }
        }

        foreach (var candidate in candidates)
        {
            if (candidate is not null && sourceItems.Contains(candidate))
            {
                return true;
            }
        }

        return false;
    }

    public static bool AnyMissingByReference(IEnumerable? source, IList selectedItems)
    {
        if (selectedItems.Count == 0)
        {
            return false;
        }

        if (source is null)
        {
            return true;
        }

        HashSet<object> sourceItems = new(ReferenceEqualityComparer.Instance);
        foreach (var item in source)
        {
            if (item is not null)
            {
                sourceItems.Add(item);
            }
        }

        foreach (var selectedItem in selectedItems)
        {
            if (selectedItem is null || !sourceItems.Contains(selectedItem))
            {
                return true;
            }
        }

        return false;
    }

    public static void SynchronizeByReference(ObservableCollection<object> target, IEnumerable source)
    {
        var desired = source.Cast<object>().ToList();

        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(target[index], ReferenceEqualityComparer.Instance))
            {
                target.RemoveAt(index);
            }
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var desiredItem = desired[index];
            var existingIndex = IndexOfReference(target, desiredItem);
            if (existingIndex == index)
            {
                continue;
            }

            if (existingIndex >= 0)
            {
                target.Move(existingIndex, index);
            }
            else
            {
                target.Insert(index, desiredItem);
            }
        }
    }

    private static bool HasAny(IEnumerable? items)
    {
        if (items is null)
        {
            return false;
        }

        var enumerator = items.GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    private static int IndexOfReference(IList target, object item)
    {
        for (var index = 0; index < target.Count; index++)
        {
            if (ReferenceEquals(target[index], item))
            {
                return index;
            }
        }

        return -1;
    }
}
