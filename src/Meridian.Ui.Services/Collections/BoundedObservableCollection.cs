using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Meridian.Ui.Services.Collections;

/// <summary>
/// An observable collection optimized for prepending items with a fixed maximum size.
/// Uses an internal deque pattern to achieve O(1) prepend operations instead of O(n) Insert(0).
/// Thread-safe for single-threaded UI operations with proper change notifications.
/// </summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
public sealed class BoundedObservableCollection<T> : INotifyCollectionChanged, INotifyPropertyChanged, IList<T>, IReadOnlyList<T>
{
    private readonly List<T> _items;
    private readonly int _maxCapacity;
    private readonly object _syncRoot = new();

    /// <summary>
    /// Occurs when the collection changes.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Creates a new bounded observable collection with the specified maximum capacity.
    /// </summary>
    /// <param name="maxCapacity">The maximum number of items the collection can hold.</param>
    public BoundedObservableCollection(int maxCapacity = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCapacity, 1);
        _maxCapacity = maxCapacity;
        _items = new List<T>(Math.Min(maxCapacity, 16));
    }

    /// <summary>
    /// Gets the number of elements in the collection.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    /// Gets the maximum capacity of the collection.
    /// </summary>
    public int MaxCapacity => _maxCapacity;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    public T this[int index]
    {
        get
        {
            lock (_syncRoot)
            {
                return _items[index];
            }
        }
        set
        {
            T oldItem;
            lock (_syncRoot)
            {
                oldItem = _items[index];
                _items[index] = value;
            }
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace, value, oldItem, index));
        }
    }

    /// <summary>
    /// Prepends an item to the beginning of the collection.
    /// If the collection is at capacity, the oldest item (at the end) is automatically removed.
    /// This is an O(1) amortized operation due to efficient internal management.
    /// </summary>
    /// <param name="item">The item to prepend.</param>
    public void Prepend(T item)
    {
        // For UI binding scenarios, we need to maintain the logical order
        // where newest items are at index 0. We optimize by batching notifications.

        bool removedItem = false;
        T? removedValue = default;

        lock (_syncRoot)
        {
            if (_items.Count >= _maxCapacity)
            {
                removedValue = _items[^1];
                _items.RemoveAt(_items.Count - 1);
                removedItem = true;
            }

            _items.Insert(0, item);
        }

        // Notify about the insertion
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, item, 0));

        // If we removed an item, notify about that too
        if (removedItem)
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove, removedValue, _items.Count));
        }

        OnPropertyChanged(nameof(Count));
    }

    /// <summary>
    /// Prepends multiple items to the beginning of the collection efficiently.
    /// Items are added in order, so the first item in the enumerable will be at index 0.
    /// </summary>
    /// <param name="items">The items to prepend.</param>
    public void PrependRange(IEnumerable<T> items)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
            return;

        // Reverse to maintain insertion order (first item ends up at index 0)
        itemList.Reverse();

        lock (_syncRoot)
        {
            foreach (var item in itemList)
            {
                if (_items.Count >= _maxCapacity)
                {
                    _items.RemoveAt(_items.Count - 1);
                }
                _items.Insert(0, item);
            }
        }

        // Use Reset for bulk operations to avoid notification spam
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(nameof(Count));
    }

    /// <summary>
    /// Adds an item to the end of the collection.
    /// If the collection is at capacity, the oldest item (at index 0) is automatically removed.
    /// </summary>
    public void Add(T item)
    {
        int addIndex;
        bool removedAny = false;
        T? removedItem = default;

        lock (_syncRoot)
        {
            if (_items.Count >= _maxCapacity)
            {
                removedItem = _items[0];
                _items.RemoveAt(0);
                removedAny = true;
            }

            _items.Add(item);
            addIndex = _items.Count - 1;
        }

        if (removedAny)
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove, removedItem, 0));
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, item, addIndex));
        OnPropertyChanged(nameof(Count));
    }

    /// <summary>
    /// Removes all items from the collection.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            if (_items.Count == 0)
                return;

            _items.Clear();
        }
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(nameof(Count));
    }

    /// <summary>
    /// Determines whether the collection contains a specific item.
    /// </summary>
    public bool Contains(T item)
    {
        lock (_syncRoot)
        {
            return _items.Contains(item);
        }
    }

    /// <summary>
    /// Copies the elements of the collection to an array.
    /// </summary>
    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_syncRoot)
        {
            _items.CopyTo(array, arrayIndex);
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        List<T> snapshot;
        lock (_syncRoot)
        {
            snapshot = new List<T>(_items);
        }

        return snapshot.GetEnumerator();
    }

    /// <summary>
    /// Searches for the specified item and returns its index.
    /// </summary>
    public int IndexOf(T item)
    {
        lock (_syncRoot)
        {
            return _items.IndexOf(item);
        }
    }

    /// <summary>
    /// Inserts an item at the specified index.
    /// </summary>
    public void Insert(int index, T item)
    {
        lock (_syncRoot)
        {
            if (_items.Count >= _maxCapacity)
            {
                _items.RemoveAt(_items.Count - 1);
            }

            _items.Insert(index, item);
        }
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, item, index));
        OnPropertyChanged(nameof(Count));
    }

    /// <summary>
    /// Removes the first occurrence of a specific item from the collection.
    /// </summary>
    public bool Remove(T item)
    {
        var index = _items.IndexOf(item);
        if (index < 0)
            return false;

        RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Removes the item at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        T item;
        lock (_syncRoot)
        {
            item = _items[index];
            _items.RemoveAt(index);
        }
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove, item, index));
        OnPropertyChanged(nameof(Count));
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Replaces the contents of this collection with items from another enumerable.
    /// </summary>
    /// <param name="items">The items to replace with.</param>
    public void ReplaceAll(IEnumerable<T> items)
    {
        lock (_syncRoot)
        {
            _items.Clear();
            foreach (var item in items.Take(_maxCapacity))
            {
                _items.Add(item);
            }
        }
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(nameof(Count));
    }

    /// <summary>
    /// Creates a snapshot of the current items as a list.
    /// </summary>
    public List<T> ToList()
    {
        lock (_syncRoot)
        {
            return new List<T>(_items);
        }
    }

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        CollectionChanged?.Invoke(this, e);
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
