using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Tests portfolio-specific pricing rule resolution: precedence, instrument-type filtering,
/// effective-date windows, and fallback when no rule matches.
/// </summary>
public sealed class PortfolioPricingRuleTests
{
    private static readonly DateTimeOffset ApprovedAt = new(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);
    private const string Portfolio = "PORT-1";

    [Fact]
    public void Resolve_PicksHighestPrecedenceMatchingRule()
    {
        var book = new PortfolioPricingRuleBook();
        book.Add(new PortfolioPricingRule("default", Portfolio, "VendorComposite", "MarkToMarket", "cfo", ApprovedAt, priority: 100));
        book.Add(new PortfolioPricingRule("bond-override", Portfolio, "MatrixPricing", "MarkToModel", "cfo", ApprovedAt, priority: 10, instrumentType: "Bond", fairValueLevel: FairValueLevel.Level2));

        var bondRule = book.Resolve(Portfolio, "Bond", new DateOnly(2026, 6, 30));
        var equityRule = book.Resolve(Portfolio, "Equity", new DateOnly(2026, 6, 30));

        bondRule!.RuleId.Should().Be("bond-override", "the lower-priority-number bond rule wins for bonds");
        bondRule.FairValueLevel.Should().Be(FairValueLevel.Level2);
        equityRule!.RuleId.Should().Be("default", "equities fall through to the catch-all rule");
    }

    [Fact]
    public void Resolve_RespectsEffectiveDateWindow()
    {
        var book = new PortfolioPricingRuleBook();
        book.Add(new PortfolioPricingRule(
            "h1-only",
            Portfolio,
            "ManualMark",
            "MarkToModel",
            "cfo",
            ApprovedAt,
            priority: 5,
            effectiveFrom: new DateOnly(2026, 1, 1),
            effectiveTo: new DateOnly(2026, 6, 30)));

        book.Resolve(Portfolio, null, new DateOnly(2026, 3, 15)).Should().NotBeNull();
        book.Resolve(Portfolio, null, new DateOnly(2026, 9, 15)).Should().BeNull("the rule has expired by September");
    }

    [Fact]
    public void Resolve_NoMatchingRule_ReturnsNull()
    {
        var book = new PortfolioPricingRuleBook();
        book.Resolve(Portfolio, "Bond", new DateOnly(2026, 6, 30)).Should().BeNull();
    }

    [Fact]
    public void RulesFor_OrdersByPrecedence()
    {
        var book = new PortfolioPricingRuleBook();
        book.Add(new PortfolioPricingRule("low", Portfolio, "A", "M", "cfo", ApprovedAt, priority: 50));
        book.Add(new PortfolioPricingRule("high", Portfolio, "B", "M", "cfo", ApprovedAt, priority: 5));

        var rules = book.RulesFor(Portfolio);

        rules.Should().HaveCount(2);
        rules[0].RuleId.Should().Be("high", "lower priority number sorts first");
    }
}
