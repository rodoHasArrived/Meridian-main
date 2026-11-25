using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Review A3: every declared consumer has an explicit disposition for every case of the F#
/// BondCouponStructure union. Adding a coupon structure without declaring how each consumer treats
/// it fails here instead of producing a silent zero accrual.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityEconomicDispositionCoverageTests
{
    [Fact]
    public void EveryBondCouponStructureCase_IsDeclaredForEveryConsumer()
    {
        var unionCases = FSharpType.GetUnionCases(typeof(Meridian.FSharp.Domain.BondCouponStructure), null)
            .Select(static unionCase => unionCase.Name)
            .ToArray();

        unionCases.Should().BeEquivalentTo(SecurityEconomicDispositions.CouponStructures,
            "the table's structure list must match the domain union exactly");

        var undeclared = SecurityEconomicDispositions.Consumers
            .SelectMany(consumer => unionCases
                .Where(structure => !SecurityEconomicDispositions.IsDeclared(consumer, structure))
                .Select(structure => $"{consumer} × {structure}"))
            .ToArray();

        undeclared.Should().BeEmpty();
    }

    [Fact]
    public void UnlistedStructure_IsBlocked_NotPassed()
    {
        var disposition = SecurityEconomicDispositions.ForCouponStructure(
            SecurityEconomicDispositions.AccountingAccrual, "PaymentInKind");

        disposition.Kind.Should().Be(SecurityEconomicDispositionKind.Blocked);
        disposition.Code.Should().Be(SecurityEconomicDispositions.CouponStructureUnsupported);
    }
}
