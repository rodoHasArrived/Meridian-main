using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Every consumer must agree on one independently specified schedule corpus.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityEconomicScheduleValidationTests
{
    public static IEnumerable<object[]> Cases()
        => SecurityEconomicScheduleFixtureCorpus.All.Select(fixture => new object[] { fixture.Id });

    // Submitted-fields validation receives only the submitted asset document, never CommonTerms.
    // Full-record validation below covers fixtures whose winning source lives in CommonTerms.
    public static IEnumerable<object[]> SubmittedCases()
        => SecurityEconomicScheduleFixtureCorpus.All.Where(fixture => !fixture.HasCommonTerms)
            .Select(fixture => new object[] { fixture.Id });

    [Theory]
    [MemberData(nameof(Cases))]
    public void SharedParser_MatchesExpectedTypedValuesPresenceAndPaths(string id)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var record = fixture.Record();
        var result = SecurityEconomicScheduleParser.Parse(record.AssetSpecificTerms, record.CommonTerms);

        fixture.AssertSchedules(result);
        Action requireValid = result.ThrowIfInvalid;
        if (fixture.IsValid)
            requireValid.Should().NotThrow(id);
        else
            fixture.AssertDiagnostics(requireValid.Should().Throw<SecurityScheduleValidationException>(id).Which.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CashFlowResolver_UsesTheCorpusWithoutPartialSchedules(string id)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var security = SecurityEconomicScheduleFixtureCorpus.Detail(fixture.Record());

        if (fixture.IsValid)
            fixture.AssertTerms(StructuredCashFlowTermsResolver.Resolve(security));
        else
        {
            Action resolve = () => StructuredCashFlowTermsResolver.Resolve(security);
            fixture.AssertDiagnostics(resolve.Should().Throw<SecurityScheduleValidationException>(id).Which.Diagnostics);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SchemaReadAndWriteValidation_ReportEquivalentScheduleDiagnostics(string id)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var context = new SecurityValidationContext(fixture.Record(), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var validator = new SchemaTermsAssetClassValidator(fixture.AssetClass);

        AssertSchemaDiagnostics(fixture, validator.Validate(context));
        AssertSchemaDiagnostics(fixture, validator.ValidateForWrite(context));
    }

    [Theory]
    [MemberData(nameof(SubmittedCases))]
    public void SubmittedFieldsValidation_RefusesMalformedSchedulesBeforeTolerantDecoding(string id)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var validator = new SchemaTermsAssetClassValidator(fixture.AssetClass);

        AssertSchemaDiagnostics(fixture, validator.ValidateSubmittedFieldsForWrite(fixture.Record().AssetSpecificTerms));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void PostgresProjectionPlan_UsesIdenticalTypedRowsOrDiagnostics(string id)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var record = fixture.Record();
        var descriptor = SecurityEconomicScheduleFixtureCorpus.Descriptor(fixture.AssetClass);

        if (fixture.IsValid)
        {
            PostgresSecurityMasterStore.TryBuildTermsProjection(descriptor, record, out var plan).Should().BeTrue(id);
            fixture.AssertProjection(plan);
        }
        else
        {
            Action build = () => PostgresSecurityMasterStore.TryBuildTermsProjection(descriptor, record, out _);
            fixture.AssertDiagnostics(build.Should().Throw<SecurityScheduleValidationException>(id).Which.Diagnostics);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RetainedHistoricalReplay_PreservesSourceBytesAndValidationOutcome(string id)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var original = fixture.Record();
        var rawTerms = original.AssetSpecificTerms.GetRawText();
        var economic = new SecurityEconomicDefinitionRecord(
            original.SecurityId, "FixedIncome", "CorporateDebt", fixture.AssetClass, fixture.AssetClass,
            null, null, original.Status, original.DisplayName, original.Currency,
            JsonSerializer.SerializeToElement(new { }), original.CommonTerms,
            JsonSerializer.SerializeToElement(new { termsFormat = "economic", schemaVersion = 2 }),
            original.Provenance, original.Version, original.EffectiveFrom, null, original.Identifiers,
            fixture.AssetClass, original.AssetSpecificTerms);

        var replay = SecurityEconomicDefinitionAdapter.ToProjection(economic);

        replay.AssetSpecificTerms.GetRawText().Should().Be(rawTerms, "replay retains evidence instead of repairing schedule rows");
        fixture.AssertSchedules(SecurityEconomicScheduleParser.Parse(replay.AssetSpecificTerms, replay.CommonTerms));
    }

    [Fact]
    public void HistoricalFactorAndCouponLookups_UseTheRequestedDateAfterSorting()
    {
        var factorFixture = SecurityEconomicScheduleFixtureCorpus.Get("typed-factors-before-reference");
        var factors = StructuredCashFlowTermsResolver.Resolve(SecurityEconomicScheduleFixtureCorpus.Detail(factorFixture.Record()));
        factors.FactorAsOf(new DateOnly(2025, 12, 31)).Should().Be(1m);
        factors.FactorAsOf(new DateOnly(2026, 6, 30)).Should().Be(1m);
        factors.FactorAsOf(new DateOnly(2026, 7, 1)).Should().Be(0.8m);

        var stepFixture = SecurityEconomicScheduleFixtureCorpus.Get("steps-unsorted-including-zero");
        var steps = StructuredCashFlowTermsResolver.Resolve(SecurityEconomicScheduleFixtureCorpus.Detail(stepFixture.Record()));
        steps.CouponRateAsOf(new DateOnly(2026, 12, 31)).Should().Be(5m);
        steps.CouponRateAsOf(new DateOnly(2027, 1, 1)).Should().Be(0m);
        steps.CouponRateAsOf(new DateOnly(2028, 1, 1)).Should().Be(5m);
    }

    private static void AssertSchemaDiagnostics(SecurityEconomicScheduleFixture fixture, IReadOnlyList<SecurityValidationIssueDto> issues)
    {
        issues.Where(issue => issue.Code.StartsWith("SM_SCHEDULE_", StringComparison.Ordinal))
            .SelectMany(issue => issue.AffectedFields.Select(path => new SecurityEconomicScheduleExpectedDiagnostic(issue.Code, path)))
            .Should().Equal(fixture.Diagnostics, fixture.Id);
        issues.Where(issue => !issue.Code.StartsWith("SM_SCHEDULE_", StringComparison.Ordinal))
            .Should().BeEmpty("the fixture supplies unrelated required terms, and shared schedules must not produce competing schema diagnostics: {0}", fixture.Id);
    }
}
