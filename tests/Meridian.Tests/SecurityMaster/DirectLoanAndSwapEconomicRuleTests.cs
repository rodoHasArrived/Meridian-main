using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// DirectLoan and Swap economic consistency rules (review Priority 1, step 2). They ship as Warning so
/// existing records can be measured before any is promoted to Error; none of them blocks a write.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DirectLoanAndSwapEconomicRuleTests
{
    private static readonly AssetClassValidatorRegistry Registry = AssetClassValidatorRegistry.CreateDefault();
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] DirectLoanCodes =
    [
        "SM_DIRECTLOAN_SPREAD_WITHOUT_INDEX",
        "SM_DIRECTLOAN_PRINCIPAL_SCHEDULE_ORDER",
        "SM_DIRECTLOAN_PRINCIPAL_AMOUNT_INVALID",
        "SM_DIRECTLOAN_PRINCIPAL_AFTER_MATURITY"
    ];

    private static readonly string[] SwapCodes =
    [
        "SM_SWAP_FLOATING_LEG_INDEX_MISSING",
        "SM_SWAP_FIXED_LEG_RATE_MISSING",
        "SM_SWAP_LEG_DAY_COUNT_MISSING",
        "SM_SWAP_LEG_PAYMENT_FREQUENCY_MISSING",
        "SM_SWAP_LEG_NOTIONAL_SCHEDULE_ORDER"
    ];

    [Fact]
    public void WellFormedFloatingLoan_RaisesNoEconomicWarning()
        => Issues("DirectLoan", Loan()).Select(static i => i.Code).Should().NotIntersectWith(DirectLoanCodes);

    [Fact]
    public void LoanSpreadWithoutIndex_IsAWarning()
    {
        var terms = Loan();
        terms.Remove("referenceIndex");

        Single("DirectLoan", terms, "SM_DIRECTLOAN_SPREAD_WITHOUT_INDEX")
            .AffectedFields.Should().BeEquivalentTo("assetSpecificTerms.spreadBps", "assetSpecificTerms.referenceIndex");
    }

    [Fact]
    public void LoanPrincipalScheduleWithRepeatedDate_IsAWarning()
    {
        var terms = Loan(schedule: [Payment("2027-06-30", 250_000m), Payment("2027-06-30", 250_000m)]);

        Single("DirectLoan", terms, "SM_DIRECTLOAN_PRINCIPAL_SCHEDULE_ORDER")
            .AffectedFields.Should().ContainSingle().Which.Should().Be("assetSpecificTerms.principalSchedule.1.paymentDate");
    }

    [Fact]
    public void LoanPrincipalPaymentThatIsNotPositive_IsAWarning()
    {
        var terms = Loan(schedule: [Payment("2027-06-30", 250_000m), Payment("2027-12-31", 0m)]);

        Single("DirectLoan", terms, "SM_DIRECTLOAN_PRINCIPAL_AMOUNT_INVALID")
            .AffectedFields.Should().ContainSingle().Which.Should().Be("assetSpecificTerms.principalSchedule.1.amount");
    }

    [Fact]
    public void LoanPrincipalPaymentAfterMaturity_IsAWarning()
    {
        var terms = Loan(schedule: [Payment("2027-06-30", 250_000m), Payment("2031-01-31", 750_000m)]);

        Single("DirectLoan", terms, "SM_DIRECTLOAN_PRINCIPAL_AFTER_MATURITY")
            .AffectedFields.Should().ContainSingle().Which.Should().Be("assetSpecificTerms.principalSchedule.1.paymentDate");
    }

    [Fact]
    public void WellFormedFixedFloatSwap_RaisesNoEconomicWarning()
        => Issues("Swap", Swap(FixedLeg(), FloatingLeg())).Select(static i => i.Code).Should().NotIntersectWith(SwapCodes);

    [Fact]
    public void FloatingLegWithoutIndex_IsAWarning_OnThatLegOnly()
    {
        var floating = FloatingLeg();
        floating.Remove("index");

        Single("Swap", Swap(FixedLeg(), floating), "SM_SWAP_FLOATING_LEG_INDEX_MISSING")
            .AffectedFields.Should().ContainSingle().Which.Should().Be("assetSpecificTerms.legs.1.index");
    }

    [Fact]
    public void FixedLegWithoutRate_IsAWarning()
    {
        var fixedLeg = FixedLeg();
        fixedLeg.Remove("fixedRate");

        Single("Swap", Swap(fixedLeg, FloatingLeg()), "SM_SWAP_FIXED_LEG_RATE_MISSING")
            .AffectedFields.Should().ContainSingle().Which.Should().Be("assetSpecificTerms.legs.0.fixedRate");
    }

    [Fact]
    public void LegFieldsReadThroughSchemaAliases()
    {
        // "rate" is a declared alias of fixedRate and "indexName" of index: neither leg is flagged.
        var fixedLeg = FixedLeg();
        fixedLeg["rate"] = fixedLeg["fixedRate"];
        fixedLeg.Remove("fixedRate");
        var floating = FloatingLeg();
        floating["indexName"] = floating["index"];
        floating.Remove("index");

        Issues("Swap", Swap(fixedLeg, floating)).Select(static i => i.Code)
            .Should().NotContain(["SM_SWAP_FIXED_LEG_RATE_MISSING", "SM_SWAP_FLOATING_LEG_INDEX_MISSING"]);
    }

    [Fact]
    public void LegsWithoutDayCountOrFrequency_AreWarnings_ListingEveryLeg()
    {
        var fixedLeg = FixedLeg();
        var floating = FloatingLeg();
        fixedLeg.Remove("dayCount");
        floating.Remove("dayCount");
        floating.Remove("paymentFrequency");

        var issues = Issues("Swap", Swap(fixedLeg, floating));

        issues.Should().ContainSingle(i => i.Code == "SM_SWAP_LEG_DAY_COUNT_MISSING")
            .Which.AffectedFields.Should().BeEquivalentTo("assetSpecificTerms.legs.0.dayCount", "assetSpecificTerms.legs.1.dayCount");
        issues.Should().ContainSingle(i => i.Code == "SM_SWAP_LEG_PAYMENT_FREQUENCY_MISSING")
            .Which.AffectedFields.Should().ContainSingle().Which.Should().Be("assetSpecificTerms.legs.1.paymentFrequency");
    }

    [Fact]
    public void LegsWithoutDayCountOrFrequency_AreNotFlagged_WhenTheRecordDeclaresThem()
    {
        // The cash-flow engine falls back to a record-level day count and payment frequency.
        var fixedLeg = FixedLeg();
        var floating = FloatingLeg();
        foreach (var leg in new[] { fixedLeg, floating })
        {
            leg.Remove("dayCount");
            leg.Remove("paymentFrequency");
        }

        var terms = Swap(fixedLeg, floating);
        terms["dayCountConvention"] = "ACT/360";
        terms["paymentFrequency"] = "Quarterly";

        Issues("Swap", terms).Select(static i => i.Code)
            .Should().NotContain(["SM_SWAP_LEG_DAY_COUNT_MISSING", "SM_SWAP_LEG_PAYMENT_FREQUENCY_MISSING"]);
    }

    [Fact]
    public void LegNotionalScheduleOutOfOrder_IsAWarning()
    {
        var floating = FloatingLeg();
        floating["notionalSchedule"] = new object[]
        {
            new Dictionary<string, object?> { ["effectiveDate"] = "2027-12-15", ["notional"] = 8_000_000m },
            new Dictionary<string, object?> { ["effectiveDate"] = "2027-06-15", ["notional"] = 9_000_000m }
        };

        Single("Swap", Swap(FixedLeg(), floating), "SM_SWAP_LEG_NOTIONAL_SCHEDULE_ORDER")
            .AffectedFields.Should().ContainSingle().Which.Should().Be("assetSpecificTerms.legs.1.notionalSchedule");
    }

    private static SecurityValidationIssueDto Single(string assetClass, Dictionary<string, object?> terms, string code)
    {
        var issue = Issues(assetClass, terms).Should().ContainSingle(i => i.Code == code).Subject;
        issue.Severity.Should().Be(SecurityValidationSeverityDto.Warning);
        return issue;
    }

    private static IReadOnlyList<SecurityValidationIssueDto> Issues(string assetClass, Dictionary<string, object?> terms)
    {
        Registry.TryGetValidator(assetClass, out var validator).Should().BeTrue();
        return validator.Validate(new SecurityValidationContext(Record(assetClass, JsonSerializer.SerializeToElement(terms)), EvaluatedAt));
    }

    private static Dictionary<string, object?> Loan(object[]? schedule = null)
        => new()
        {
            ["borrower"] = "Northwind Holdings LLC",
            ["maturity"] = "2030-12-31",
            ["referenceIndex"] = "SOFR",
            ["spreadBps"] = 325m,
            ["resetFrequency"] = "Quarterly",
            ["covenants"] = Array.Empty<object>(),
            ["principalSchedule"] = schedule ?? [Payment("2027-06-30", 250_000m), Payment("2028-06-30", 250_000m), Payment("2030-12-31", 500_000m)]
        };

    private static Dictionary<string, object?> Payment(string date, decimal amount)
        => new() { ["paymentDate"] = date, ["amount"] = amount };

    private static Dictionary<string, object?> Swap(params Dictionary<string, object?>[] legs)
        => new()
        {
            ["effectiveDate"] = "2026-12-15",
            ["maturityDate"] = "2031-12-15",
            ["legs"] = legs
        };

    private static Dictionary<string, object?> FixedLeg()
        => new()
        {
            ["legId"] = "fixed",
            ["legType"] = "Fixed",
            ["currency"] = "USD",
            ["direction"] = "Pay",
            ["fixedRate"] = 3.85m,
            ["notional"] = 10_000_000m,
            ["paymentFrequency"] = "SemiAnnual",
            ["dayCount"] = "30/360",
            ["exchangesPrincipal"] = false
        };

    private static Dictionary<string, object?> FloatingLeg()
        => new()
        {
            ["legId"] = "floating",
            ["legType"] = "Floating",
            ["currency"] = "USD",
            ["direction"] = "Receive",
            ["index"] = "SOFR",
            ["spreadBps"] = -10m,
            ["notional"] = 10_000_000m,
            ["paymentFrequency"] = "Quarterly",
            ["dayCount"] = "ACT/360",
            ["exchangesPrincipal"] = false
        };

    private static SecurityProjectionRecord Record(string assetClass, JsonElement terms)
        => new(
            Guid.NewGuid(),
            assetClass,
            SecurityStatusDto.Active,
            "Test security",
            "USD",
            "Internal",
            "TEST",
            JsonSerializer.SerializeToElement(new { }),
            terms,
            JsonSerializer.SerializeToElement(new { }),
            1,
            EvaluatedAt.AddYears(-1),
            null,
            [],
            []);
}
