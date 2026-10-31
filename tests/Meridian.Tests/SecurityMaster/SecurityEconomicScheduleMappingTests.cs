using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Canonical decoding must consume the same aliases without changing retained row identity.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityEconomicScheduleMappingTests
{
    [Theory]
    [InlineData("factor-plural-and-case-aliases", "factorScheduleEntries", "asOfDate", "factor", "2026-01-01", "0.90")]
    [InlineData("step-coupon-legacy-alias", "stepSchedule", "effectiveDate", "rate", "2027-01-01", "4.50")]
    public void CanonicalWrite_AcceptsSharedAliasesAndNumericStrings(
        string id, string container, string dateMember, string valueMember, string expectedDate, string expectedValue)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var record = fixture.Record();
        if (fixture.AssetClass == "Bond")
        {
            var terms = JsonNode.Parse(record.AssetSpecificTerms.GetRawText())!.AsObject();
            terms["couponType"] = "Step";
            record = record with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };
        }

        var request = new CreateSecurityRequest(
            record.SecurityId, record.AssetClass,
            JsonSerializer.SerializeToElement(new { displayName = record.DisplayName, currency = record.Currency }),
            record.AssetSpecificTerms, record.Identifiers, record.EffectiveFrom,
            "schedule-corpus", "schedule-corpus", id, "Shared schedule admission regression");
        var command = SecurityMasterMapping.ToCreateCommand(request);
        var result = SecurityMasterCommandFacade.Create(command);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.ErrorDetails.Select(error => $"{error.Code}: {error.Message}")));
        result.Snapshot.Should().NotBeNull();
        using var canonical = JsonDocument.Parse(result.Snapshot.AssetSpecificTermsJson);
        var row = canonical.RootElement.GetProperty(container).EnumerateArray().Should().ContainSingle().Which;
        row.GetProperty(dateMember).GetString().Should().Be(expectedDate);
        row.GetProperty(valueMember).GetDecimal().Should().Be(decimal.Parse(expectedValue, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("typed-factors-before-reference", "factorScheduleEntries")]
    [InlineData("steps-unsorted-including-zero", "stepSchedule")]
    [InlineData("principal-same-date-aggregation", "principalSchedule")]
    public void CanonicalRead_PreservesUnsortedSourceRowsAndSplitSameDatePrincipal(string id, string container)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var projection = fixture.Record();
        var record = SecurityMasterMapping.ToRecord(projection);
        using var serialized = JsonDocument.Parse(new SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson);

        JsonNode.DeepEquals(
            JsonNode.Parse(serialized.RootElement.GetProperty(container).GetRawText()),
            JsonNode.Parse(projection.AssetSpecificTerms.GetProperty(container).GetRawText()))
            .Should().BeTrue("canonical serialization retains source row order and split payments; only calculation entries normalize: {0}", id);
    }

    [Theory]
    [InlineData("typed-factors-before-reference")]
    [InlineData("steps-unsorted-including-zero")]
    [InlineData("principal-same-date-aggregation")]
    [InlineData("common-terms-factor-fallback")]
    [InlineData("common-terms-principal-fallback")]
    [InlineData("common-terms-step-fallback")]
    public void EconomicAdapterReplay_RetainsTheOriginalScheduleEvidenceBytes(string id)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var original = CommonMappingRecord(fixture);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(original);
        var replay = SecurityEconomicDefinitionAdapter.ToProjection(economic);

        replay.AssetSpecificTerms.GetRawText().Should().Be(original.AssetSpecificTerms.GetRawText());
        replay.CommonTerms.GetRawText().Should().Be(original.CommonTerms.GetRawText());
        fixture.AssertTerms(StructuredCashFlowTermsResolver.Resolve(SecurityEconomicScheduleFixtureCorpus.Detail(replay)));
    }

    [Theory]
    [InlineData("common-terms-factor-fallback")]
    [InlineData("common-terms-principal-fallback")]
    [InlineData("common-terms-step-fallback")]
    public void CanonicalWrite_PreservesTheSelectedCommonSchedule(string id)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var original = CommonMappingRecord(fixture);
        var command = SecurityMasterMapping.ToCreateCommand(CreateRequest(original));
        var result = SecurityMasterCommandFacade.Create(command);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.ErrorDetails.Select(error => $"{error.Code}: {error.Message}")));
        result.Snapshot.Should().NotBeNull();
        AssertSelectedCommonSchedule(fixture, SecurityMasterMapping.ToProjection(result.Snapshot));
    }

    [Theory]
    [InlineData("common-terms-factor-fallback")]
    [InlineData("common-terms-principal-fallback")]
    [InlineData("common-terms-step-fallback")]
    public void CanonicalRead_PreservesTheSelectedCommonSchedule(string id)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var original = CommonMappingRecord(fixture);
        var record = SecurityMasterMapping.ToRecord(original);
        var snapshot = new SecurityMasterSnapshotWrapper(record);

        AssertSelectedCommonSchedule(fixture, SecurityMasterMapping.ToProjection(snapshot));
    }

    [Theory]
    [InlineData("common-terms-factor-fallback", "poolId")]
    [InlineData("common-terms-principal-fallback", "borrower")]
    [InlineData("common-terms-step-fallback", "issuerName")]
    public void AssetAmendment_PreservesTheSelectedCurrentCommonSchedule(string id, string unrelatedField)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var original = CommonMappingRecord(fixture);
        var assetTerms = JsonNode.Parse(original.AssetSpecificTerms.GetRawText())!.AsObject();
        assetTerms[unrelatedField] = "Amended reference detail";
        var request = new AmendSecurityTermsRequest(original.SecurityId, original.Version, null,
            JsonSerializer.SerializeToElement(assetTerms), [], [], original.EffectiveFrom.AddDays(1),
            "schedule-corpus", "schedule-corpus", id, "Unrelated asset amendment retains common schedule");
        var command = SecurityMasterMapping.ToAmendCommand(request, original);
        var result = SecurityMasterCommandFacade.Amend(SecurityMasterMapping.ToRecord(original), command);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.ErrorDetails.Select(error => $"{error.Code}: {error.Message}")));
        result.Snapshot.Should().NotBeNull();
        var amended = SecurityMasterMapping.ToProjection(result.Snapshot);
        amended.AssetSpecificTerms.GetProperty(unrelatedField).GetString().Should().Be("Amended reference detail");
        AssertSelectedCommonSchedule(fixture, amended);
    }

    [Theory]
    [InlineData("common-terms-factor-fallback", "factorSchedules")]
    [InlineData("common-terms-principal-fallback", "principalSchedule")]
    [InlineData("common-terms-step-fallback", "couponSteps")]
    public void CanonicalMapping_RefusesMalformedSelectedCommonRows(string id, string container)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var original = CommonMappingRecord(fixture);
        var common = JsonNode.Parse(original.CommonTerms.GetRawText())!.AsObject();
        common[container] = JsonNode.Parse("[null]");
        original = original with { CommonTerms = JsonSerializer.SerializeToElement(common) };

        Action create = () => SecurityMasterMapping.ToCreateCommand(CreateRequest(original));
        Action read = () => SecurityMasterMapping.ToRecord(original);
        foreach (var operation in new[] { create, read })
        {
            var diagnostic = operation.Should().Throw<SecurityScheduleValidationException>().Which.Diagnostics.Should().ContainSingle().Which;
            diagnostic.Code.Should().Be("SM_SCHEDULE_ROW_INVALID");
            diagnostic.Path.Should().Be($"commonTerms.{container}[0]");
        }
    }

    [Theory]
    [InlineData("common-terms-factor-fallback", "factorSchedules", "factor", "0.7")]
    [InlineData("common-terms-principal-fallback", "principalSchedule", "amount", "30")]
    [InlineData("common-terms-step-fallback", "couponSteps", "rate", "6.5")]
    public void CommonOnlyAmendment_ReplacesTheSelectedSchedule(string id, string container, string valueMember, string replacement)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var original = CommonMappingRecord(fixture);
        var expected = decimal.Parse(replacement, System.Globalization.CultureInfo.InvariantCulture);
        var common = JsonNode.Parse(original.CommonTerms.GetRawText())!.AsObject();
        common[container]!.AsArray()[0]!.AsObject()[valueMember] = expected;
        common["displayName"] = "Amended common schedule";
        var command = SecurityMasterMapping.ToAmendCommand(CommonAmendRequest(original, common), original);

        command.Kind.Should().NotBeNull("the selected common schedule must reach the domain amendment");
        var result = SecurityMasterCommandFacade.Amend(SecurityMasterMapping.ToRecord(original), command);
        result.IsSuccess.Should().BeTrue(string.Join("; ", result.ErrorDetails.Select(error => $"{error.Code}: {error.Message}")));
        result.Snapshot.Should().NotBeNull();
        var amended = SecurityMasterMapping.ToProjection(result.Snapshot);
        amended.DisplayName.Should().Be("Amended common schedule");
        SelectedScheduleValues(fixture, amended).Should().Equal(expected);
    }

    [Theory]
    [InlineData("common-terms-factor-fallback", "factorSchedules")]
    [InlineData("common-terms-principal-fallback", "principalSchedule")]
    [InlineData("common-terms-step-fallback", "couponSteps")]
    public void CommonOnlyAmendment_ExplicitEmptyReachesTheCanonicalDomainRule(string id, string container)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var original = CommonMappingRecord(fixture);
        var common = JsonNode.Parse(original.CommonTerms.GetRawText())!.AsObject();
        common[container] = new JsonArray();
        var command = SecurityMasterMapping.ToAmendCommand(CommonAmendRequest(original, common), original);

        command.Kind.Should().NotBeNull("an explicit empty schedule owns the amendment");
        var result = SecurityMasterCommandFacade.Amend(SecurityMasterMapping.ToRecord(original), command);
        if (fixture.AssetClass == "Bond")
        {
            result.IsSuccess.Should().BeFalse("a Step bond still requires a payable rate even though an empty schedule is structurally valid");
            result.ErrorDetails.Should().Contain(error => error.Code == "bond_step_schedule_required");
        }
        else
        {
            result.IsSuccess.Should().BeTrue(string.Join("; ", result.ErrorDetails.Select(error => $"{error.Code}: {error.Message}")));
            result.Snapshot.Should().NotBeNull();
            SelectedScheduleValues(fixture, SecurityMasterMapping.ToProjection(result.Snapshot)).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("common-terms-factor-fallback", "factorSchedules")]
    [InlineData("common-terms-principal-fallback", "principalSchedule")]
    [InlineData("common-terms-step-fallback", "couponSteps")]
    public void CommonOnlyAmendment_RefusesMalformedSelectedRows(string id, string container)
    {
        var original = CommonMappingRecord(SecurityEconomicScheduleFixtureCorpus.Get(id));
        var common = JsonNode.Parse(original.CommonTerms.GetRawText())!.AsObject();
        common[container] = JsonNode.Parse("[null]");

        Action amend = () => SecurityMasterMapping.ToAmendCommand(CommonAmendRequest(original, common), original);
        var diagnostic = amend.Should().Throw<SecurityScheduleValidationException>().Which.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Code.Should().Be("SM_SCHEDULE_ROW_INVALID");
        diagnostic.Path.Should().Be($"commonTerms.{container}[0]");
    }

    [Theory]
    [InlineData("common-terms-factor-fallback", "factorSchedules")]
    [InlineData("common-terms-principal-fallback", "principalSchedule")]
    [InlineData("common-terms-step-fallback", "couponSteps")]
    public void CommonOnlyAmendment_IgnoresDeclarationsShadowedByTheOwningAssetSchedule(string id, string container)
    {
        var fixture = SecurityEconomicScheduleFixtureCorpus.Get(id);
        var source = CommonMappingRecord(fixture);
        // Reconstruct once to obtain a canonical owning asset schedule; the next common declaration
        // is a lower-priority assertion and cannot replace those terms, even if malformed.
        var original = SecurityMasterMapping.ToProjection(new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(source)));
        var common = JsonNode.Parse(original.CommonTerms.GetRawText())!.AsObject();
        common[container] = JsonNode.Parse("[null]");
        common["displayName"] = "Metadata-only amendment";
        var command = SecurityMasterMapping.ToAmendCommand(CommonAmendRequest(original, common), original);

        command.Kind.Should().BeNull("a shadowed common declaration must not replace the existing kind");
        var result = SecurityMasterCommandFacade.Amend(SecurityMasterMapping.ToRecord(original), command);
        result.IsSuccess.Should().BeTrue(string.Join("; ", result.ErrorDetails.Select(error => $"{error.Code}: {error.Message}")));
        result.Snapshot.Should().NotBeNull();
        var amended = SecurityMasterMapping.ToProjection(result.Snapshot);
        amended.DisplayName.Should().Be("Metadata-only amendment");
        AssertSelectedCommonSchedule(fixture, amended);
    }

    private static SecurityProjectionRecord CommonMappingRecord(SecurityEconomicScheduleFixture fixture)
    {
        var record = fixture.Record();
        if (fixture.AssetClass == "Bond" && fixture.StepPresence == "Populated")
        {
            // The resolver reads legacy schedules independently of couponType. The canonical Bond
            // codec only declares steps for its Step variant, so these codec cases select that variant.
            var terms = JsonNode.Parse(record.AssetSpecificTerms.GetRawText())!.AsObject();
            terms["couponType"] = "Step";
            record = record with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };
        }
        return record;
    }

    private static CreateSecurityRequest CreateRequest(SecurityProjectionRecord record)
        => new(record.SecurityId, record.AssetClass, record.CommonTerms, record.AssetSpecificTerms,
            record.Identifiers, record.EffectiveFrom, "schedule-corpus", "schedule-corpus", null,
            "Shared schedule mapping regression");

    private static AmendSecurityTermsRequest CommonAmendRequest(SecurityProjectionRecord record, JsonObject common)
        => new(record.SecurityId, record.Version, JsonSerializer.SerializeToElement(common), null,
            [], [], record.EffectiveFrom.AddDays(1), "schedule-corpus", "schedule-corpus", null,
            "Shared common schedule amendment regression");

    private static IEnumerable<decimal> SelectedScheduleValues(SecurityEconomicScheduleFixture fixture, SecurityProjectionRecord projection)
    {
        var schedules = SecurityEconomicScheduleParser.Parse(projection.AssetSpecificTerms, projection.CommonTerms);
        schedules.ThrowIfInvalid();
        return fixture.AssetClass switch
        {
            "StructuredCredit" => schedules.FactorSchedule.Entries.Select(entry => entry.Factor),
            "DirectLoan" => schedules.PrincipalSchedule.Entries.Select(entry => entry.Amount),
            "Bond" => schedules.StepCouponSchedule.Entries.Select(entry => entry.Rate),
            _ => throw new InvalidOperationException(fixture.AssetClass)
        };
    }

    private static void AssertSelectedCommonSchedule(SecurityEconomicScheduleFixture fixture, SecurityProjectionRecord projection)
    {
        var schedules = SecurityEconomicScheduleParser.Parse(projection.AssetSpecificTerms, projection.CommonTerms);
        schedules.IsValid.Should().BeTrue(fixture.Id);
        // The domain serializer asserts empty arrays for other canonical schedules. Check only the
        // schedule whose CommonTerms fallback is under test, without redefining codec presence.
        if (fixture.FactorPresence == "Populated")
            schedules.FactorSchedule.Entries.Should().Equal(fixture.Factors.Select(value =>
                new StructuredFactorScheduleEntry(value.ParsedDate, value.Value)), fixture.Id);
        if (fixture.PrincipalPresence == "Populated")
            schedules.PrincipalSchedule.Entries.Should().Equal(fixture.Principal.Select(value =>
                new StructuredPrincipalScheduleEntry(value.ParsedDate, value.Value)), fixture.Id);
        if (fixture.StepPresence == "Populated")
            schedules.StepCouponSchedule.Entries.Should().Equal(fixture.Steps.Select(value =>
                new StructuredStepCouponEntry(value.ParsedDate, value.Value)), fixture.Id);
    }
}
