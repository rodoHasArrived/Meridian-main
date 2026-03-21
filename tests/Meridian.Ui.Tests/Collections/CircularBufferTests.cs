using FluentAssertions;
using Meridian.Ui.Services.Collections;

namespace Meridian.Ui.Tests.Collections;

/// <summary>
/// Tests for <see cref="CircularBuffer{T}"/>.
/// </summary>
public sealed class CircularBufferTests
{
    [Fact]
    public void Constructor_WithCapacity_CreatesEmptyBuffer()
    {
        // Act
        var buffer = new CircularBuffer<int>(capacity: 5);

        // Assert
        buffer.Count.Should().Be(0);
        buffer.Capacity.Should().Be(5);
    }

    [Fact]
    public void Add_WhenUnderCapacity_IncreasesCount()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(capacity: 5);

        // Act
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        // Assert
        buffer.Count.Should().Be(3);
    }

    [Fact]
    public void Add_WhenAtCapacity_OverwritesOldest()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(capacity: 3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        // Act
        buffer.Add(4);

        // Assert
        buffer.Count.Should().Be(3);
        buffer.ToArray().Should().ContainInOrder(2, 3, 4);
    }

    [Fact]
    public void TryGetFromNewest_WhenBufferEmpty_ReturnsFalse()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(capacity: 5);

        // Act
        var result = buffer.TryGetFromNewest(0, out var value);

        // Assert
        result.Should().BeFalse();
        value.Should().Be(0); // Default value for int
    }

    [Fact]
    public void TryGetFromNewest_WithZeroOffset_ReturnsNewestItem()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(capacity: 5);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        // Act
        var result = buffer.TryGetFromNewest(0, out var value);

        // Assert
        result.Should().BeTrue();
        value.Should().Be(3);
    }

    [Fact]
    public void TryGetFromNewest_WithOffset_ReturnsCorrectItem()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(capacity: 5);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        // Act & Assert
        buffer.TryGetFromNewest(0, out var newest).Should().BeTrue();
        newest.Should().Be(3);

        buffer.TryGetFromNewest(1, out var prev1).Should().BeTrue();
        prev1.Should().Be(2);

        buffer.TryGetFromNewest(2, out var prev2).Should().BeTrue();
        prev2.Should().Be(1);

        buffer.TryGetFromNewest(3, out var prev3).Should().BeFalse();
        prev3.Should().Be(0); // Default value for int
    }

    [Fact]
    public void TryGetFromNewest_AfterWrap_ReturnsCorrectItem()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(capacity: 3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4); // Overwrites 1
        buffer.Add(5); // Overwrites 2

        // Act & Assert
        buffer.TryGetFromNewest(0, out var newest).Should().BeTrue();
        newest.Should().Be(5);

        buffer.TryGetFromNewest(1, out var prev1).Should().BeTrue();
        prev1.Should().Be(4);

        buffer.TryGetFromNewest(2, out var prev2).Should().BeTrue();
        prev2.Should().Be(3);
    }

    [Fact]
    public void Clear_RemovesAllItems()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(capacity: 5);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        // Act
        buffer.Clear();

        // Assert
        buffer.Count.Should().Be(0);
        buffer.TryGetFromNewest(0, out _).Should().BeFalse();
    }

    [Fact]
    public void ToArray_ReturnsItemsInInsertionOrder()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(capacity: 5);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        // Act
        var array = buffer.ToArray();

        // Assert
        array.Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public void CalculatePercentageChange_ReturnsCorrectValue()
    {
        // Arrange
        var buffer = new CircularBuffer<double>(capacity: 5);
        buffer.Add(100.0);
        buffer.Add(110.0);

        // Act
        var change = buffer.CalculatePercentageChange(fromOffset: 1, toOffset: 0);

        // Assert
        change.Should().BeApproximately(10.0, 0.01, "110 is 10% higher than 100");
    }

    [Fact]
    public void CalculatePercentageChange_WhenDivisionByZero_ReturnsNull()
    {
        // Arrange
        var buffer = new CircularBuffer<double>(capacity: 5);
        buffer.Add(0.0);
        buffer.Add(10.0);

        // Act
        var change = buffer.CalculatePercentageChange(fromOffset: 1, toOffset: 0);

        // Assert
        change.Should().BeNull("Cannot calculate percentage change from 0");
    }
}
