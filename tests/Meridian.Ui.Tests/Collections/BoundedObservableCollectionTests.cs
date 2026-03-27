using System.Collections.Specialized;
using FluentAssertions;
using Meridian.Ui.Services.Collections;

namespace Meridian.Ui.Tests.Collections;

/// <summary>
/// Tests for <see cref="BoundedObservableCollection{T}"/>.
/// </summary>
public sealed class BoundedObservableCollectionTests
{
    [Fact]
    public void Constructor_WithCapacity_CreatesEmptyCollection()
    {
        // Act
        var collection = new BoundedObservableCollection<int>(maxCapacity: 5);

        // Assert
        collection.Should().BeEmpty();
        collection.MaxCapacity.Should().Be(5);
    }

    [Fact]
    public void Add_WhenUnderCapacity_AddsItem()
    {
        // Arrange
        var collection = new BoundedObservableCollection<int>(maxCapacity: 5);

        // Act
        collection.Add(1);
        collection.Add(2);
        collection.Add(3);

        // Assert
        collection.Should().HaveCount(3);
        collection.Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public void Add_WhenAtCapacity_RemovesOldestAndAddsNew()
    {
        // Arrange
        var collection = new BoundedObservableCollection<int>(maxCapacity: 3);
        collection.Add(1);
        collection.Add(2);
        collection.Add(3);

        // Act
        collection.Add(4);

        // Assert
        collection.Should().HaveCount(3);
        collection.Should().ContainInOrder(2, 3, 4);
        collection.Should().NotContain(1);
    }

    [Fact]
    public void Add_WhenAtCapacity_RemovesMultipleOldestItems()
    {
        // Arrange
        var collection = new BoundedObservableCollection<int>(maxCapacity: 3);
        collection.Add(1);
        collection.Add(2);
        collection.Add(3);

        // Act
        collection.Add(4);
        collection.Add(5);

        // Assert
        collection.Should().HaveCount(3);
        collection.Should().ContainInOrder(3, 4, 5);
        collection.Should().NotContain(new[] { 1, 2 });
    }

    [Fact]
    public void CollectionChanged_FiresWhenItemAdded()
    {
        // Arrange
        var collection = new BoundedObservableCollection<int>(maxCapacity: 5);
        var eventFired = false;
        NotifyCollectionChangedEventArgs? eventArgs = null;

        collection.CollectionChanged += (sender, args) =>
        {
            eventFired = true;
            eventArgs = args;
        };

        // Act
        collection.Add(1);

        // Assert
        eventFired.Should().BeTrue();
        eventArgs.Should().NotBeNull();
        eventArgs!.Action.Should().Be(NotifyCollectionChangedAction.Add);
    }

    [Fact]
    public void CollectionChanged_FiresWhenItemRemovedDueToCapacity()
    {
        // Arrange
        var collection = new BoundedObservableCollection<int>(maxCapacity: 2);
        collection.Add(1);
        collection.Add(2);

        var removeFired = false;
        var addFired = false;

        collection.CollectionChanged += (sender, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Remove)
                removeFired = true;
            if (args.Action == NotifyCollectionChangedAction.Add)
                addFired = true;
        };

        // Act
        collection.Add(3);

        // Assert
        removeFired.Should().BeTrue("Item 1 should have been removed");
        addFired.Should().BeTrue("Item 3 should have been added");
    }

    [Fact]
    public void Clear_RemovesAllItems()
    {
        // Arrange
        var collection = new BoundedObservableCollection<int>(maxCapacity: 5);
        collection.Add(1);
        collection.Add(2);
        collection.Add(3);

        // Act
        collection.Clear();

        // Assert
        collection.Should().BeEmpty();
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentAdds_DoesNotExceedCapacity()
    {
        // Arrange
        var collection = new BoundedObservableCollection<int>(maxCapacity: 100);
        var tasks = new List<Task>();

        // Act - Add 1000 items concurrently from 10 threads
        for (int i = 0; i < 10; i++)
        {
            int threadId = i;
            var task = Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    collection.Add(threadId * 100 + j);
                }
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Assert
        collection.Count.Should().BeLessThanOrEqualTo(100, "Collection should never exceed capacity");
    }
}
