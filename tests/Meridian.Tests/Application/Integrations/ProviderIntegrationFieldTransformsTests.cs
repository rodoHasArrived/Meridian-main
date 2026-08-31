using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationFieldTransformsTests
{
    private static FieldMappingDto Mapping(params (string Key, string Value)[] transformParameters) =>
        new(
            Capability: ProviderCapabilityKindDto.Transactions,
            SourcePath: "amount",
            TargetField: "Amount",
            Transform: new TransformRuleDto(
                "signedamount",
                transformParameters.ToDictionary(
                    static parameter => parameter.Key,
                    static parameter => parameter.Value,
                    StringComparer.OrdinalIgnoreCase)),
            Required: true,
            Confidence: ProviderMappingConfidenceDto.High,
            DefaultValue: null,
            ConstantValue: null);

    // --- ParseDecimal -------------------------------------------------------

    [Theory]
    [InlineData("100", 100)]
    [InlineData("  100.25  ", 100.25)]
    [InlineData("-42.5", -42.5)]
    [InlineData("1,234.56", 1234.56)]      // comma read as a thousands separator
    [InlineData("1,234,567", 1234567)]
    public void ParseDecimal_ParsesInvariantNumbers(string value, decimal expected)
    {
        var issues = new List<ValidationIssueDto>();

        ProviderIntegrationFieldTransforms.ParseDecimal(value, "Amount", issues)
            .Should()
            .Be(expected);
        issues.Should().BeEmpty();
    }

    // Pins existing behavior rather than endorsing it: commas are stripped unconditionally, so a
    // European-formatted amount is silently reinterpreted instead of rejected. Consolidating the
    // helper means this decision now lives in exactly one place if it is ever revisited.
    [Fact]
    public void ParseDecimal_ReinterpretsEuropeanFormatRatherThanRejectingIt()
    {
        var issues = new List<ValidationIssueDto>();

        ProviderIntegrationFieldTransforms.ParseDecimal("1.234,56", "Amount", issues)
            .Should()
            .Be(1.23456m);
        issues.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12.34.56")]
    public void ParseDecimal_RecordsACriticalIssueForUnparseableInput(string value)
    {
        var issues = new List<ValidationIssueDto>();

        ProviderIntegrationFieldTransforms.ParseDecimal(value, "Amount", issues).Should().BeNull();

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be("transform.decimal.invalid");
        issue.Severity.Should().Be(ProviderIntegrationIssueSeverityDto.Critical);
        issue.TargetField.Should().Be("Amount");
    }

    // --- ParseDate ----------------------------------------------------------

    [Theory]
    [InlineData("2026-03-04", "2026-03-04")]
    [InlineData("2026-03-04T22:15:00Z", "2026-03-04")]
    // Invariant culture reads an ambiguous slash date as month-first, so 03/04 is 4 March.
    [InlineData("03/04/2026", "2026-03-04")]
    public void ParseDate_NormalizesToUtcIsoDate(string value, string expected)
    {
        var issues = new List<ValidationIssueDto>();

        ProviderIntegrationFieldTransforms.ParseDate(value, "TradeDate", issues)
            .Should()
            .Be(expected);
        issues.Should().BeEmpty();
    }

    [Fact]
    public void ParseDate_ConvertsAnOffsetToUtcBeforeRendering() =>
        ProviderIntegrationFieldTransforms
            .ParseDate("2026-03-04T23:30:00+02:00", "TradeDate", [])
            .Should()
            .Be("2026-03-04");

    [Theory]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("2026-13-45")]
    public void ParseDate_RecordsACriticalIssueForUnparseableInput(string value)
    {
        var issues = new List<ValidationIssueDto>();

        ProviderIntegrationFieldTransforms.ParseDate(value, "TradeDate", issues).Should().BeNull();

        var issue = issues.Should().ContainSingle().Subject;
        issue.Code.Should().Be("transform.date.invalid");
        issue.Severity.Should().Be(ProviderIntegrationIssueSeverityDto.Critical);
        issue.TargetField.Should().Be("TradeDate");
    }

    // --- Transform parameters ----------------------------------------------

    [Fact]
    public void GetTransformParameter_ReadsAParameterOrReturnsNull()
    {
        var mapping = Mapping(("negativeValues", "DEBIT"));

        ProviderIntegrationFieldTransforms.GetTransformParameter(mapping, "negativeValues")
            .Should()
            .Be("DEBIT");
        ProviderIntegrationFieldTransforms.GetTransformParameter(mapping, "missing")
            .Should()
            .BeNull();
    }

    [Fact]
    public void GetTransformParameter_ReturnsNullWhenNoTransformIsConfigured()
    {
        var mapping = Mapping() with { Transform = null };

        ProviderIntegrationFieldTransforms.GetTransformParameter(mapping, "negativeValues")
            .Should()
            .BeNull();
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("DEBIT", 1)]
    [InlineData("DEBIT,CREDIT", 2)]
    [InlineData(" DEBIT , , CREDIT ", 2)]
    public void SplitTransformList_TrimsAndDropsEmptyEntries(string? value, int expectedCount) =>
        ProviderIntegrationFieldTransforms.SplitTransformList(value).Should().HaveCount(expectedCount);

    [Fact]
    public void SplitTransformList_TrimsEachEntry() =>
        ProviderIntegrationFieldTransforms.SplitTransformList(" DEBIT , CREDIT ")
            .Should()
            .Equal("DEBIT", "CREDIT");

    // --- ParseSignedAmount --------------------------------------------------

    [Fact]
    public void ParseSignedAmount_FlipsTheSignWhenTheConditionMatches()
    {
        var mapping = Mapping(("conditionColumn", "drCr"), ("negativeValues", "DEBIT,DR"));

        ProviderIntegrationFieldTransforms
            .ParseSignedAmount("100.00", mapping, _ => "DEBIT", [])
            .Should()
            .Be(-100.00m);
    }

    [Fact]
    public void ParseSignedAmount_MatchesTheConditionCaseInsensitivelyAndTrimmed()
    {
        var mapping = Mapping(("conditionColumn", "drCr"), ("negativeValues", "DEBIT"));

        ProviderIntegrationFieldTransforms
            .ParseSignedAmount("100", mapping, _ => "  debit  ", [])
            .Should()
            .Be(-100m);
    }

    [Fact]
    public void ParseSignedAmount_ForcesNegativeRatherThanNegating()
    {
        var mapping = Mapping(("conditionColumn", "drCr"), ("negativeValues", "DEBIT"));

        // An already-negative source amount stays negative instead of flipping back to positive.
        ProviderIntegrationFieldTransforms
            .ParseSignedAmount("-100", mapping, _ => "DEBIT", [])
            .Should()
            .Be(-100m);
    }

    [Fact]
    public void ParseSignedAmount_LeavesTheAmountAloneWhenTheConditionDoesNotMatch()
    {
        var mapping = Mapping(("conditionColumn", "drCr"), ("negativeValues", "DEBIT"));

        ProviderIntegrationFieldTransforms
            .ParseSignedAmount("100", mapping, _ => "CREDIT", [])
            .Should()
            .Be(100m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseSignedAmount_LeavesTheAmountAloneWhenTheConditionFieldIsAbsent(string? conditionValue)
    {
        var mapping = Mapping(("conditionColumn", "drCr"), ("negativeValues", "DEBIT"));

        ProviderIntegrationFieldTransforms
            .ParseSignedAmount("100", mapping, _ => conditionValue, [])
            .Should()
            .Be(100m);
    }

    [Fact]
    public void ParseSignedAmount_SkipsTheConditionLookupWhenNoConditionPathIsConfigured()
    {
        var mapping = Mapping(("negativeValues", "DEBIT"));
        var lookups = 0;

        ProviderIntegrationFieldTransforms
            .ParseSignedAmount("100", mapping, _ => { lookups++; return "DEBIT"; }, [])
            .Should()
            .Be(100m);
        lookups.Should().Be(0);
    }

    [Fact]
    public void ParseSignedAmount_PrefersConditionSourcePathOverConditionColumn()
    {
        var mapping = Mapping(
            ("conditionSourcePath", "$.drCr"),
            ("conditionColumn", "legacyDrCr"),
            ("negativeValues", "DEBIT"));
        string? requestedPath = null;

        ProviderIntegrationFieldTransforms.ParseSignedAmount(
            "100",
            mapping,
            path => { requestedPath = path; return "DEBIT"; },
            []);

        requestedPath.Should().Be("$.drCr");
    }

    [Fact]
    public void ParseSignedAmount_ReturnsNullAndReportsWhenTheAmountIsUnparseable()
    {
        var mapping = Mapping(("conditionColumn", "drCr"), ("negativeValues", "DEBIT"));
        var issues = new List<ValidationIssueDto>();

        ProviderIntegrationFieldTransforms
            .ParseSignedAmount("not-a-number", mapping, _ => "DEBIT", issues)
            .Should()
            .BeNull();
        issues.Should().ContainSingle().Which.Code.Should().Be("transform.decimal.invalid");
    }
}
