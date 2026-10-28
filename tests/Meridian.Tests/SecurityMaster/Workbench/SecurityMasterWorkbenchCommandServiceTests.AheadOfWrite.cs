using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Workbench staging under the readable-before-writable terms expansion (ADR-024, priority 5,
/// increment 4a). The field-edit path reconstructs the record's effective terms, which this node may
/// only READ: a row stamped above the class's write version, or one populating a term introduced
/// above it. The approved merge would map those terms in Write mode and refuse them after approval,
/// so the edit is refused at submission with the precise reason, before any override, draft revision
/// or provenance row exists. Profile envelopes keep only their stamp check, so a profile-backed
/// record whose envelope carries scheduleConventions still stages, publishes and merges. Increment 4b
/// raises the readable versions (Bond and CertificateOfDeposit 3, StructuredCredit 4) for the
/// callSchedule and putSchedule arrays and adds no gate: the same generic refusals cover a row or
/// profile envelope stamped at the new readable version and a stray populated array, and a profile
/// envelope's schedules stay the profile's own.
/// </summary>
public sealed partial class SecurityMasterWorkbenchCommandServiceTests
{
    // A structured-credit-io-po envelope carrying a populated scheduleConventions both inside
    // profileFields and at its root, as the pinned profile may legitimately declare.
    private const string ProfileBackedStructuredCreditEnvelope =
        """{"customProfileId":"structured-credit-io-po","profileVersion":1,"profileFields":{"tranche":"A-1","poolId":"POOL-1","currentFactor":0.8,"originalFace":1000000,"couponOrIndex":"SOFR","factorSchedule":"trustee-report","collateralType":"CLO","scheduleConventions":{"businessDayConvention":"ModifiedFollowing","paymentCalendars":["USNY"],"stubPolicy":"Short"}},"profileApproval":{"approvedBy":"Meridian","approvedAtUtc":"2026-05-29T00:00:00Z","approvalReference":"MERIDIAN-SEED-APPROVAL"},"scheduleConventions":{"businessDayConvention":"Following","paymentCalendars":["GBLO","USNY"],"rollConvention":"EndOfMonth"}}""";

    private const string EditedProfileConventions = """{"businessDayConvention":"Preceding","stubPolicy":"Long"}""";

    // Schedules a profile envelope carries as its own fields: versioned class terms are flat-codec-only,
    // so neither the root nor the profileFields copies are StructuredCredit's version-4 arrays.
    private const string ProfileCallSchedule =
        """[{"callDate":"2028-06-15","callPrice":100,"priceBasis":"PercentOfPar","priceType":"Fixed"}]""";

    private const string ProfilePutSchedule = """[{"putDate":"2029-06-15","putPrice":100}]""";

    // Flat rows stamped by hand at their class's new readable version, each declaring a call schedule
    // (no node writes these versions yet).
    private const string CertificateOfDepositStampedThree =
        """{"callSchedule":[{"callDate":"2026-09-30","callPrice":100,"priceBasis":"PercentOfPar","priceType":"Fixed"}],"callableDate":"2026-09-30","couponRate":4.4,"dayCount":"ACT/365","issuerName":"First Meridian Bank","maturity":"2027-09-30","putSchedule":[],"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3}""";

    private const string StructuredCreditStampedFour =
        """{"attachmentPoint":0.12,"callSchedule":[{"callDate":"2028-06-15","callPrice":100,"priceBasis":"PercentOfPar","priceType":"Fixed"}],"collateralType":"CLO","couponOrIndex":"SOFR+250","currentFactor":0.8235,"detachmentPoint":0.20,"factorSchedule":"See trustee report 2026-07","maturity":"2031-06-15","originalFace":10000000,"poolId":"MRDN-2026-1","putSchedule":[{"putDate":"2029-12-15","putPrice":100}],"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":4,"seniority":"Mezzanine","structuredCreditKind":"Clo","tranche":"B"}""";

    private const string ReplacedProfileFields =
        """{"tranche":"A-2","poolId":"POOL-1","currentFactor":0.75,"originalFace":1000000,"couponOrIndex":"SOFR","factorSchedule":"trustee-report","collateralType":"CLO","scheduleConventions":{"businessDayConvention":"Preceding","paymentCalendars":["USNY"],"stubPolicy":"Long"}}""";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateSecurityField_OnALegacyBond_StagesUnlessDeclaredConventionsContradict(bool withBlock)
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", AheadOfWriteFixtures.BondTerms(stamp: 2, withBlock));
        if (!withBlock)
        {
            var staged = await harness.Service.UpdateSecurityFieldAsync(IssuerNameEdit());
            staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft, "a readable old stamp without declared terms is writable after the raise");
            return;
        }
        var refused = await harness.Service.Invoking(service => service.UpdateSecurityFieldAsync(IssuerNameEdit()))
            .Should().ThrowAsync<ArgumentException>();
        refused.Which.Message.Should().Contain("Declared rollConvention 'EndOfMonth' conflicts with endOfMonthRule false");
        await AssertNothingStagedAsync(harness);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateSecurityField_OnABondWithInvalidDeclaredSemantics_IsRefusedWithoutStaging(bool withBlock)
    {
        // A contract node wrote this row at version 3 with declared schedules. This node reads it
        // (increment 4b) but writes version 1, so an edit of an unrelated version-1 term is refused by
        // the stamp, with or without a block, before any override, draft or provenance row exists.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", AheadOfWriteFixtures.BondTerms(
            stamp: 3, withBlock, AheadOfWriteFixtures.PopulatedCallSchedule, AheadOfWriteFixtures.PopulatedPutSchedule));

        var refused = await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(IssuerNameEdit()))
            .Should().ThrowAsync<ArgumentException>();

        refused.Which.Message.Should().Contain(withBlock ? "conflicts with endOfMonthRule false" : "exerciseStyle must be explicitly stated");
        await AssertNothingStagedAsync(harness);
    }

    [Theory]
    [InlineData("CertificateOfDeposit", CertificateOfDepositStampedThree, "assetSpecificTerms.issuerName", "First Meridian Bancorp",
        AheadOfWriteFixtures.CertificateOfDepositStampThreeWriteVersionReason)]
    [InlineData("StructuredCredit", StructuredCreditStampedFour, "assetSpecificTerms.tranche", "C",
        AheadOfWriteFixtures.StructuredCreditStampFourWriteVersionReason)]
    public async Task UpdateSecurityField_OnAFlatRowMissingExerciseStyle_IsRefusedWithoutStaging(
        string assetClass, string terms, string fieldPath, string newValue, string writeVersionReason)
    {
        // The other two classes whose readable version 4b raises, through the same first-class gate as
        // the bond above: the row reads here, but an edit of an unrelated term is refused by the stamp.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass(assetClass);
        harness.SetProjectionAssetTerms(assetClass, AheadOfWriteFixtures.Parse(terms));

        var refused = await harness.Service
            .Invoking(s => s.UpdateSecurityFieldAsync(ProfileFieldEdit(fieldPath, newValue)))
            .Should().ThrowAsync<ArgumentException>();

        refused.Which.Message.Should().Contain("exerciseStyle must be explicitly stated");
        await AssertNothingStagedAsync(harness);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public async Task UpdateSecurityField_OnABondCarryingAStrayCallScheduleBelowItsVersion_IsRefusedAsUnrelatedStrayActivation(int? stamp)
    {
        // A row stamped 1 (or unstamped) carrying a populated callSchedule, which no writer emits.
        // Before 4b the key was undeclared, so the edit staged and the merge dropped it; now it is a
        // version-3 term this node reads but cannot write, so the edit names it instead of staging a
        // revision the merge refuses (see the merge-handler twin).
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms(
            "Bond", AheadOfWriteFixtures.BondTerms(stamp, withBlock: false, callSchedule: AheadOfWriteFixtures.StrayCallSchedule));

        var refused = await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(IssuerNameEdit()))
            .Should().ThrowAsync<ArgumentException>();

        refused.Which.Message.Should().Be(
            $"{AheadOfWriteFixtures.StrayContainerReason("callSchedule", 3)} (Parameter 'request')");
        await AssertNothingStagedAsync(harness);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public async Task UpdateSecurityField_OnABondPopulatingABlockAboveItsStamp_IsRefusedAsUnrelatedStrayActivation(int? stamp)
    {
        // A row stamped 1 (or unstamped) that carries a populated block: the write version cannot
        // persist the block, so the edit names it instead of staging a revision the merge refuses.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", AheadOfWriteFixtures.BondTerms(stamp, withBlock: true));

        var refused = await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(IssuerNameEdit()))
            .Should().ThrowAsync<ArgumentException>();

        refused.Which.Message.Should().Be(
            $"{AheadOfWriteFixtures.StrayContainerReason("scheduleConventions", 2)} (Parameter 'request')");
        await AssertNothingStagedAsync(harness);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public async Task UpdateSecurityField_PlainEditOnABondAtTheWriteVersion_StagesAsBefore(int? stamp)
    {
        // Regression guard: a version-1 (or unstamped) bond without a block stages exactly as before
        // the expansion: one override, one draft revision carrying the value, one provenance row.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", AheadOfWriteFixtures.BondTerms(stamp, withBlock: false));

        var result = await harness.Service.UpdateSecurityFieldAsync(IssuerNameEdit());

        result.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        var revision = (await harness.Revisions.GetAsync(result.RevisionId))!;
        revision.FieldPath.Should().Be("assetSpecificTerms.issuerName");
        revision.FieldValue.Should().Be("ACME Holdings");
        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId,
                It.Is<OperatorOverridesPatchRequest>(p =>
                    p.SetValues != null
                    && p.SetValues.Count == 1
                    && p.SetValues["assetSpecificTerms.issuerName"] == "ACME Holdings"),
                "ops.analyst",
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once);
        harness.FieldProvenance.Verify(
            p => p.UpsertAsync(
                It.Is<SecurityFieldProvenanceRecord>(r => r.FieldPath == "assetSpecificTerms.issuerName"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task UpdateSecurityField_SwapLegsReplacementPopulatingALegBlock_IsRefusedAtSubmission(int legIndex)
    {
        // New legs revisions carrying a block can no longer stage, so only revisions staged before the
        // deploy reach the merge refusal (see the merge-handler tests and the publish drain below).
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Swap");
        harness.SetProjectionAssetTerms("Swap", AheadOfWriteFixtures.Parse(AheadOfWriteFixtures.SwapAtWriteVersion));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: AheadOfWriteFixtures.LegsPath,
            NewValue: AheadOfWriteFixtures.SwapLegsWith(legIndex, AheadOfWriteFixtures.PopulatedLegBlock),
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Leg schedule conventions from the confirmation.");

        var refused = await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>();

        refused.Which.Message.Should().Be(
            $"{AheadOfWriteFixtures.NotYetWritableReason("Swap", $"legs[{legIndex}].scheduleConventions", sinceVersion: 2, writes: 1)} (Parameter 'request')");
        await AssertNothingStagedAsync(harness);
    }

    [Fact]
    public async Task Publish_InFlightLegsRevisionPopulatingALegBlock_FailsInTheMergeAndStaysApproved()
    {
        // The in-flight drain of the 4a deploy notes: a legs revision staged and approved before this
        // node refused leg blocks cannot publish here. The merge's amendment is refused for the
        // populated block, the publish fails naming the merge handler, nothing reaches the canonical
        // record, and the revision stays Approved.
        var publish = await ApprovedLegsRevisionPublishAsync(AheadOfWriteFixtures.PopulatedLegBlock);

        var failed = await publish.Harness.Service.Invoking(s => s.PublishRevisionAsync(publish.Request))
            .Should().ThrowAsync<SecurityMasterPublishFailedException>();

        failed.Which.FailedHandlers.Should().Equal(nameof(ApprovedFieldEditCanonicalMergeHandler));
        publish.Amender.Failures.Should().ContainSingle("the merge reached the canonical amend seam exactly once")
            .Which.Should().BeOfType<SecurityMasterWriteRefusedException>()
            .Which.Message.Should().Be(
                AheadOfWriteFixtures.NotYetWritableReason("Swap", "legs[1].scheduleConventions", sinceVersion: 2, writes: 1));
        (await publish.Revisions.GetAsync(publish.Request.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
        publish.Canonical.Appended.Should().BeEmpty();
        publish.Canonical.Upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_InFlightLegsRevisionWithAnUnsetLegBlock_PublishesAndMergesAtTheWriteVersion()
    {
        // Positive control for the drain above, through the same harness and projection: an explicit
        // null block is unset, so the identical legs revision publishes and merges. The populated
        // block is the only difference between the two.
        var publish = await ApprovedLegsRevisionPublishAsync("null");

        await publish.Harness.Service.PublishRevisionAsync(publish.Request);

        publish.Amender.Failures.Should().BeEmpty();
        (await publish.Revisions.GetAsync(publish.Request.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
        var persisted = JsonNode.Parse(
            publish.Canonical.Upserted.Should().ContainSingle().Subject.AssetSpecificTerms.GetRawText())!.AsObject();
        persisted[SecurityTermsEnvelope.AssetClassTermsVersionKey]!.GetValue<int>().Should().Be(1);
        var legs = persisted["legs"]!.AsArray();
        legs.Should().HaveCount(2);
        legs.Should().OnlyContain(
            leg => !leg!.AsObject().ContainsKey("scheduleConventions"),
            "a writer below the block's version never emits its key");
        legs[1]!["spreadBps"]!.GetValue<decimal>().Should().Be(20m, "the approved leg change itself merges");
        publish.Canonical.Appended.Should().ContainSingle();
    }

    [Theory]
    [InlineData("assetSpecificTerms.profileFields.tranche", "A-2")]
    [InlineData("assetSpecificTerms.profileFields.scheduleConventions", EditedProfileConventions)]
    [InlineData("assetSpecificTerms.profileFields", ReplacedProfileFields)]
    public async Task UpdateSecurityField_ProfileFieldsEditOnAProfileBackedStructuredCreditCarryingScheduleConventions_Stages(
        string fieldPath, string newValue)
    {
        // Versioned class terms are flat-codec-only: the envelope's root scheduleConventions and its
        // profileFields.scheduleConventions belong to the pinned profile, so neither refuses the edit.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("StructuredCredit");
        harness.SetProjectionAssetTerms("StructuredCredit", AheadOfWriteFixtures.Parse(ProfileBackedStructuredCreditEnvelope));

        var result = await harness.Service.UpdateSecurityFieldAsync(ProfileFieldEdit(fieldPath, newValue));

        result.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        var revision = (await harness.Revisions.GetAsync(result.RevisionId))!;
        revision.FieldPath.Should().Be(fieldPath);
        revision.FieldValue.Should().Be(newValue);
    }

    [Fact]
    public async Task ProfileConventionsEdit_OnAProfileBackedStructuredCredit_StagesPublishesAndMergesVerbatim()
    {
        // The whole workbench lifecycle on a profile envelope: the staged profileFields.scheduleConventions
        // revision publishes, and the merge hands the canonical amend seam an envelope it writes as is,
        // root scheduleConventions included.
        var catalog = StaticSecurityAssetProfileCatalog.CreateDefault();
        var revisions = new InMemorySecurityMasterRevisionStore();
        var mergeStore = new Mock<ISecurityMasterStore>(MockBehavior.Loose);
        var canonical = new CanonicalAmendHarness(mergeStore, catalog);
        var merge = new ApprovedFieldEditCanonicalMergeHandler(
            revisions, NullLogger<ApprovedFieldEditCanonicalMergeHandler>.Instance, mergeStore.Object, canonical.Service,
            assetProfileCatalog: catalog);
        var harness = new Harness(currentVersion: 3, handlers: [merge], assetProfileCatalog: catalog, revisions: revisions);
        harness.SetPassportAssetClass("StructuredCredit");
        harness.SetProjectionAssetTerms("StructuredCredit", AheadOfWriteFixtures.Parse(ProfileBackedStructuredCreditEnvelope));
        // The pinned profile requires CUSIP coverage for the canonical write, so the stored record carries one.
        var projection = (await harness.ProjectionStore.Object.GetProjectionAsync(SecurityId))! with
        {
            Identifiers =
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "88160R101", false, DateTimeOffset.UtcNow.AddDays(-30)),
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "CUST-1", true, DateTimeOffset.UtcNow.AddDays(-30))
            ]
        };
        harness.ProjectionStore.Setup(s => s.GetProjectionAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(projection);
        mergeStore.Setup(s => s.GetProjectionAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(projection);

        const string fieldPath = "assetSpecificTerms.profileFields.scheduleConventions";
        var staged = await harness.Service.UpdateSecurityFieldAsync(ProfileFieldEdit(fieldPath, EditedProfileConventions));
        await revisions.TransitionAsync(
            staged.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await revisions.TransitionAsync(
            staged.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");

        await harness.Service.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(
            SecurityId, staged.RevisionId, "ops.analyst", "ops.reviewer"));

        (await revisions.GetAsync(staged.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
        var expected = JsonNode.Parse(ProfileBackedStructuredCreditEnvelope)!.AsObject();
        expected["profileFields"]!["scheduleConventions"] = JsonNode.Parse(EditedProfileConventions);
        var persisted = canonical.Upserted.Should().ContainSingle("the approved profile edit merges into the canonical terms").Subject;
        JsonNode.DeepEquals(JsonNode.Parse(persisted.AssetSpecificTerms.GetRawText()), expected)
            .Should().BeTrue("the merged envelope persists verbatim, its root scheduleConventions included");
        canonical.Appended.Should().ContainSingle();
    }

    [Theory]
    [InlineData("assetSpecificTerms.profileFields.callSchedule", ProfileCallSchedule)]
    [InlineData("assetSpecificTerms.profileFields.putSchedule", ProfilePutSchedule)]
    public async Task ProfileScheduleEdit_OnAProfileBackedStructuredCreditCarryingSchedules_StagesPublishesAndMergesVerbatim(
        string fieldPath, string newValue)
    {
        // Increment 4b declares callSchedule and putSchedule on flat StructuredCredit terms at version
        // 4 only. An unstamped profile envelope carrying them at its root and editing them inside
        // profileFields keeps its stamp check alone, so the edit stages, publishes and merges, and the
        // canonical amend seam writes the envelope as is, root schedules included.
        var envelope = JsonNode.Parse(ProfileBackedStructuredCreditEnvelope)!.AsObject();
        envelope["callSchedule"] = JsonNode.Parse(ProfileCallSchedule);
        envelope["putSchedule"] = JsonNode.Parse(ProfilePutSchedule);
        var catalog = StaticSecurityAssetProfileCatalog.CreateDefault();
        var revisions = new InMemorySecurityMasterRevisionStore();
        var mergeStore = new Mock<ISecurityMasterStore>(MockBehavior.Loose);
        var canonical = new CanonicalAmendHarness(mergeStore, catalog);
        var amender = new RecordingAmender(canonical.Service);
        var merge = new ApprovedFieldEditCanonicalMergeHandler(
            revisions, NullLogger<ApprovedFieldEditCanonicalMergeHandler>.Instance, mergeStore.Object, amender,
            assetProfileCatalog: catalog);
        var harness = new Harness(currentVersion: 3, handlers: [merge], assetProfileCatalog: catalog, revisions: revisions);
        harness.SetPassportAssetClass("StructuredCredit");
        harness.SetProjectionAssetTerms("StructuredCredit", AheadOfWriteFixtures.Parse(envelope.ToJsonString()));
        // The pinned profile requires CUSIP coverage for the canonical write, so the stored record carries one.
        var projection = (await harness.ProjectionStore.Object.GetProjectionAsync(SecurityId))! with
        {
            Identifiers =
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "88160R101", false, DateTimeOffset.UtcNow.AddDays(-30)),
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "CUST-1", true, DateTimeOffset.UtcNow.AddDays(-30))
            ]
        };
        harness.ProjectionStore.Setup(s => s.GetProjectionAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(projection);
        mergeStore.Setup(s => s.GetProjectionAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(projection);

        var staged = await harness.Service.UpdateSecurityFieldAsync(ProfileFieldEdit(fieldPath, newValue));

        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        var revision = (await revisions.GetAsync(staged.RevisionId))!;
        revision.FieldPath.Should().Be(fieldPath);
        revision.FieldValue.Should().Be(newValue);
        await revisions.TransitionAsync(
            staged.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await revisions.TransitionAsync(
            staged.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");

        await harness.Service.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(
            SecurityId, staged.RevisionId, "ops.analyst", "ops.reviewer"));

        amender.Failures.Should().BeEmpty();
        (await revisions.GetAsync(staged.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
        var expected = envelope.DeepClone().AsObject();
        expected["profileFields"]![fieldPath["assetSpecificTerms.profileFields.".Length..]] = JsonNode.Parse(newValue);
        var persisted = canonical.Upserted.Should().ContainSingle("the approved profile edit merges into the canonical terms").Subject;
        JsonNode.DeepEquals(JsonNode.Parse(persisted.AssetSpecificTerms.GetRawText()), expected)
            .Should().BeTrue("the merged envelope persists verbatim, its root callSchedule and putSchedule included");
        canonical.Appended.Should().ContainSingle();
    }

    [Theory]
    [InlineData("assetSpecificTerms.profileFields.tranche", "A-2")]
    [InlineData("assetSpecificTerms.profileFields.scheduleConventions", EditedProfileConventions)]
    [InlineData("assetSpecificTerms.profileFields", ReplacedProfileFields)]
    public async Task UpdateSecurityField_ProfileEnvelopeAtVersionThree_IsWritableAfterTheRaise(
        string fieldPath, string newValue)
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("StructuredCredit");
        var stamped = JsonNode.Parse(ProfileBackedStructuredCreditEnvelope)!.AsObject();
        stamped[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 3;
        harness.SetProjectionAssetTerms("StructuredCredit", AheadOfWriteFixtures.Parse(stamped.ToJsonString()));
        var staged = await harness.Service.UpdateSecurityFieldAsync(ProfileFieldEdit(fieldPath, newValue));
        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        (await harness.Revisions.GetAsync(staged.RevisionId))!.FieldPath.Should().Be(fieldPath);
    }

    [Fact]
    public async Task UpdateSecurityField_RootTermOfAProfileEnvelopeAtVersionThree_StagesAfterTheRaise()
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("StructuredCredit");
        var stamped = JsonNode.Parse(ProfileBackedStructuredCreditEnvelope)!.AsObject();
        stamped[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 3;
        harness.SetProjectionAssetTerms("StructuredCredit", AheadOfWriteFixtures.Parse(stamped.ToJsonString()));
        var staged = await harness.Service.UpdateSecurityFieldAsync(ProfileFieldEdit("assetSpecificTerms.tranche", "B"));
        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
    }

    [Fact]
    public async Task UpdateSecurityField_ProfileEnvelopeAtVersionFour_StagesAfterTheRaise()
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("StructuredCredit");
        var stamped = JsonNode.Parse(ProfileBackedStructuredCreditEnvelope)!.AsObject();
        stamped[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 4;
        harness.SetProjectionAssetTerms("StructuredCredit", AheadOfWriteFixtures.Parse(stamped.ToJsonString()));
        var staged = await harness.Service.UpdateSecurityFieldAsync(ProfileFieldEdit("assetSpecificTerms.profileFields.tranche", "A-2"));
        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
    }

    [Fact]
    public async Task UpdateSecurityField_ProfileEnvelopeStampedAboveWhatThisNodeReads_IsRefusedWithTheReadReason()
    {
        // Stamped 5, the envelope is not readable here at all, so the refusal must say so rather than
        // call it readable but not writable.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("StructuredCredit");
        var stamped = JsonNode.Parse(ProfileBackedStructuredCreditEnvelope)!.AsObject();
        stamped[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 5;
        harness.SetProjectionAssetTerms("StructuredCredit", AheadOfWriteFixtures.Parse(stamped.ToJsonString()));

        var refused = await harness.Service
            .Invoking(s => s.UpdateSecurityFieldAsync(ProfileFieldEdit("assetSpecificTerms.profileFields.tranche", "A-2")))
            .Should().ThrowAsync<ArgumentException>();

        refused.Which.Message.Should().Be(
            $"The effective profileFields overlay for security '{SecurityId:D}' could not be reconstructed and validated " +
            "against the resolved asset class 'StructuredCredit' (Unsupported assetClassTermsVersion '5' for asset class " +
            "'StructuredCredit'; this node reads versions 1 through 4.); the namespace only accepts validated writes.");
        await AssertNothingStagedAsync(harness);
    }

    private static UpdateSecurityFieldRequest IssuerNameEdit()
        => new(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.issuerName",
            NewValue: "ACME Holdings",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Issuer renamed after the merger.");

    private static UpdateSecurityFieldRequest ProfileFieldEdit(string fieldPath, string newValue)
        => new(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: fieldPath,
            NewValue: newValue,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Trustee report restated the profile terms.");

    /// <summary>
    /// A swap legs revision seeded straight into the revision store and approved, as one staged before
    /// this node refused leg blocks would be, with the real merge handler wired to the real canonical
    /// amend seam (through <see cref="RecordingAmender"/>) as the publish fan-out.
    /// </summary>
    private static async Task<ApprovedLegsPublish> ApprovedLegsRevisionPublishAsync(string legBlock)
    {
        var revisions = new InMemorySecurityMasterRevisionStore();
        var mergeStore = new Mock<ISecurityMasterStore>(MockBehavior.Loose);
        var canonical = new CanonicalAmendHarness(mergeStore);
        var amender = new RecordingAmender(canonical.Service);
        var merge = new ApprovedFieldEditCanonicalMergeHandler(
            revisions, NullLogger<ApprovedFieldEditCanonicalMergeHandler>.Instance, mergeStore.Object, amender);
        var harness = new Harness(currentVersion: 3, handlers: [merge], revisions: revisions);
        harness.SetProjectionAssetTerms("Swap", AheadOfWriteFixtures.Parse(AheadOfWriteFixtures.SwapAtWriteVersion));
        var projection = await harness.ProjectionStore.Object.GetProjectionAsync(SecurityId);
        mergeStore.Setup(s => s.GetProjectionAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(projection);

        var draft = await revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", AheadOfWriteFixtures.LegsPath, DateTimeOffset.UtcNow,
            "Leg schedule conventions staged before the 4a deploy.", fundProfileId: null,
            fieldValue: new SecurityMasterRevisionFieldValue(AheadOfWriteFixtures.SwapLegsWith(legIndex: 1, legBlock)));
        await revisions.TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await revisions.TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");

        return new ApprovedLegsPublish(
            harness, revisions, canonical, amender,
            new PublishSecurityMasterRevisionRequest(SecurityId, draft.RevisionId, "ops.analyst", "ops.reviewer"));
    }

    private sealed record ApprovedLegsPublish(
        Harness Harness,
        InMemorySecurityMasterRevisionStore Revisions,
        CanonicalAmendHarness Canonical,
        RecordingAmender Amender,
        PublishSecurityMasterRevisionRequest Request);

    /// <summary>A refused edit leaves no override, draft revision, provenance row or event behind.</summary>
    private static async Task AssertNothingStagedAsync(Harness harness)
    {
        harness.Overrides.Verify(
            o => o.PatchAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()),
            Times.Never,
            "a refused edit must not stage an override");
        (await harness.Revisions.ListBySecurityAsync(SecurityId)).Should().BeEmpty("a refused edit must not open a draft revision");
        harness.FieldProvenance.Verify(
            p => p.UpsertAsync(It.IsAny<SecurityFieldProvenanceRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a refused edit must not record operator provenance");
        harness.FieldProvenance.Verify(
            p => p.RemoveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.EventStore.Appends.Should().BeEmpty("field edits never append to the economic event stream");
    }
}
