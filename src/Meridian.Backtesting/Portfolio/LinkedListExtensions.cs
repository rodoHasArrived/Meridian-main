namespace Meridian.Backtesting.Portfolio;

/// <summary>
/// LINQ-friendly helpers for <see cref="LinkedList{T}"/> that expose individual
/// <see cref="LinkedListNode{T}"/> instances rather than just values.
/// This lets the lot-selection algorithms remove or reorder nodes in-place without
/// re-allocating the collection.
/// </summary>
internal static class LinkedListExtensions
{
    /// <summary>
    /// Enumerates all nodes in a linked list from first to last.
    /// Safe to materialize before mutating the list (e.g. via <c>.ToList()</c>).
    /// </summary>
    public static IEnumerable<LinkedListNode<T>> EnumerateNodes<T>(this LinkedList<T> list)
    {
        var node = list.First;
        while (node is not null)
        {
            var next = node.Next;   // capture before potential removal
            yield return node;
            node = next;
        }
    }
}
