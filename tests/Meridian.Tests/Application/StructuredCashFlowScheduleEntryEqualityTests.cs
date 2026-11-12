using System.Reflection;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.Application;

/// <summary>
/// Guards persisted row equality on <see cref="StructuredCashFlowScheduleEntry"/>. Every writable
/// serialized member must affect equality; any always-ignored working state must not. Reflection
/// keeps new persisted members, including the versioned interest interval, in this regression.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StructuredCashFlowScheduleEntryEqualityTests
{
    private static readonly StructuredCashFlowScheduleEntry Baseline =
        new(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), 0m, 30m, 1m);

    public static TheoryData<string> SerializedMembers()
    {
        var data = new TheoryData<string>();
        foreach (var property in WritableMembers().Where(static property => !IsNeverSerialized(property)))
            data.Add(property.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(SerializedMembers))]
    public void Equals_DistinguishesRowsThatDifferInASerializedMember(string member)
    {
        var changed = WithChanged(member);

        changed.Equals(Baseline).Should().BeFalse(
            $"'{member}' is serialized into retained receipts, so StructuredCashFlowScheduleEntry.Equals must compare it");
    }

    [Fact]
    public void Equals_IgnoresWorkingDataThatIsNeverSerialized()
    {
        foreach (var property in WritableMembers().Where(IsNeverSerialized))
        {
            var changed = WithChanged(property.Name);
            changed.Equals(Baseline).Should().BeTrue(
                $"'{property.Name}' is never serialized, so a replayed row must equal the retained row whatever it holds");
            changed.GetHashCode().Should().Be(Baseline.GetHashCode());
        }
    }

    [Fact]
    public void EveryWritableMemberIsCovered()
    {
        // A member type with no sample value below would otherwise drop out of both theories.
        foreach (var property in WritableMembers())
            SampleFor(property.PropertyType).Should().NotBeNull($"add a sample value for '{property.Name}' ({property.PropertyType})");
    }

    private static IEnumerable<PropertyInfo> WritableMembers()
        => typeof(StructuredCashFlowScheduleEntry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.CanWrite && property.Name != "EqualityContract");

    private static bool IsNeverSerialized(PropertyInfo property)
        => property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always };

    private static StructuredCashFlowScheduleEntry WithChanged(string member)
    {
        var property = typeof(StructuredCashFlowScheduleEntry).GetProperty(member)!;
        var clone = (StructuredCashFlowScheduleEntry)typeof(StructuredCashFlowScheduleEntry)
            .GetMethod("<Clone>$")!.Invoke(Baseline, null)!;
        var sample = SampleFor(property.PropertyType)
            ?? throw new InvalidOperationException($"No sample value for '{member}' ({property.PropertyType}).");
        sample.Should().NotBe(property.GetValue(Baseline), "the sample must differ from the baseline row");
        property.SetValue(clone, sample);
        return clone;
    }

    private static object? SampleFor(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(DateTimeOffset))
            return new DateTimeOffset(2031, 1, 2, 0, 0, 0, TimeSpan.Zero);
        if (underlying == typeof(DateOnly))
            return new DateOnly(2031, 1, 2);
        if (underlying == typeof(int))
            return 1;
        if (underlying == typeof(decimal))
            return 12.34m;
        if (underlying == typeof(BondFloatingRateEvidence))
        {
            var constructor = typeof(BondFloatingRateEvidence).GetConstructors().OrderByDescending(static c => c.GetParameters().Length).First();
            var arguments = constructor.GetParameters().Select(static parameter => DefaultFor(parameter.ParameterType)).ToArray();
            return constructor.Invoke(arguments);
        }

        return null;
    }

    private static object? DefaultFor(Type type)
    {
        if (type == typeof(string))
            return "SOFR";
        if (type == typeof(DateOnly))
            return new DateOnly(2031, 1, 2);
        return type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;
    }
}
