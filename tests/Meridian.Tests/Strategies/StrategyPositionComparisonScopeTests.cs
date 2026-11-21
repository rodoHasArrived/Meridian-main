using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyPositionComparisonScopeTests
{
    [Theory]
    [InlineData("currency")]
    [InlineData("unit")]
    [InlineData("multiplier")]
    [InlineData("security")]
    [InlineData("empty-security")]
    [InlineData("account")]
    [InlineData("missing-account")]
    [InlineData("cutoff")]
    public void IncomparablePopulations_DoNotLabelRawAmountsWithOneSharedUnit(string change)
    {
        var expected = new StrategyPositionAccountingEvidence("TEST", "book", Guid.NewGuid(), false, 10m, 100m, "Shares", 1m, "USD");
        var actual = change switch
        {
            "currency" => expected with { Currency = "EUR" },
            "unit" => expected with { QuantityUnit = "Contracts" },
            "multiplier" => expected with { Multiplier = 100m },
            "security" => expected with { SecurityId = Guid.NewGuid() },
            "empty-security" => expected with { SecurityId = Guid.Empty },
            "account" => expected with { FinancialAccountId = "another-book" },
            "missing-account" => expected with { FinancialAccountId = null },
            _ => expected
        };
        if (change == "missing-account")
            expected = actual;
        var at = DateTimeOffset.Parse("2026-11-21T20:00:00Z");
        var inputs = new ReconciliationNormalizedInputs(
            new(at, 0m, 0m, []) { AccountingPositions = [expected] },
            new(change == "cutoff" ? at.AddDays(1) : at, 0m, 0m, []) { AccountingPositions = [actual] }, [], []);

        var checks = new ReconciliationProjectionService().BuildPositionChecks(inputs, new("scope-test"));

        checks.Should().NotBeEmpty();
        foreach (var check in checks)
        {
            check.IsMatch.Should().BeFalse();
            check.Measure.UnavailableReason.Should().NotBeNullOrWhiteSpace();
            check.Measure.Expected.Should().BeNull();
            check.Measure.Actual.Should().BeNull();
            check.Measure.Variance.Should().BeNull();
        }
    }
}
