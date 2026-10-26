using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The service seam of the readable-before-writable terms expansion (ADR-024, priority 5, increment
/// 4a) in a mixed fleet: rows stamped above their class's write version, written by a node that
/// writes the newer version, sit in the store this node reads. Deactivating them, or amending them
/// without a terms patch, re-serializes the record this node decoded, so the block and the stamp
/// must survive byte for byte, an empty block downgrades to the write version, and a block this
/// node cannot reproduce refuses the write instead of silently losing it. Writes that carry the
/// newer version are refused with the write-version reason. Profile envelopes stay outside
/// versioned class terms on every step.
/// </summary>
/// <remarks>
/// The stamped rows cannot be produced through create on this node, so each is seeded directly into
/// the projection store. With no events and no snapshot the rebuilder falls back to that projection,
/// so the stored document reaches the round-trip guards verbatim.
/// </remarks>
public sealed partial class SecurityMasterServiceSnapshotTests
{
    private const string Deactivate = "deactivate";
    private const string AmendWithoutTermsPatch = "amend-without-terms-patch";

    // The bytes the version-1 writer persists for a plain fixed-rate bond (the Bond.fixed write golden).
    private const string BondAtVersion1 =
        """{"callDate":null,"capRate":null,"couponRate":4.25,"couponType":"Fixed","dayCount":"30/360","floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":"2024-01-01","issuerName":"ACME Corp","legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":1000,"paymentFrequency":"SemiAnnual","preRefundDate":null,"principalSchedule":[],"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,"seniority":"Senior","spreadBps":null,"stepSchedule":[],"subclass":"Corporate"}""";

    // The same bond as a node that writes version 2 persists it with a declared block: the frozen
    // canonical form, with all twelve members in alphabetical order and null or [] for unset ones.
    private const string BondAtVersion2 =
        """{"callDate":null,"capRate":null,"couponRate":4.25,"couponType":"Fixed","dayCount":"30/360","floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":"2024-01-01","issuerName":"ACME Corp","legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":1000,"paymentFrequency":"SemiAnnual","preRefundDate":null,"principalSchedule":[],"scheduleConventions":{"accrualAdjusted":true,"businessDayConvention":"ModifiedFollowing","couponCashBasis":null,"couponRateUnit":null,"direction":"Backward","endOfMonthRule":false,"firstRegularDate":"2024-07-01","lastRegularDate":null,"paymentCalendars":["USNY","GBLO"],"paymentLagBusinessDays":2,"rollConvention":"EndOfMonth","stubPolicy":"Short"},"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":2,"seniority":"Senior","spreadBps":null,"stepSchedule":[],"subclass":"Corporate"}""";

    private const string CertificateOfDepositAtVersion2 =
        """{"callableDate":"2026-09-30","couponRate":4.4,"dayCount":"ACT/365","issuerName":"First Meridian Bank","maturity":"2027-09-30","scheduleConventions":{"accrualAdjusted":null,"businessDayConvention":"Following","couponCashBasis":null,"couponRateUnit":null,"direction":null,"endOfMonthRule":null,"firstRegularDate":null,"lastRegularDate":null,"paymentCalendars":[],"paymentLagBusinessDays":null,"rollConvention":null,"stubPolicy":null},"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":2}""";

    private const string StructuredCreditAtVersion3 =
        """{"attachmentPoint":0.12,"collateralType":"CLO","couponOrIndex":"SOFR","currentFactor":0.8235,"detachmentPoint":0.20,"factorSchedule":"See trustee report 2026-07","factorScheduleEntries":[{"asOfDate":"2026-06-01","factor":0.8412},{"asOfDate":"2026-07-01","factor":0.8235}],"maturity":"2031-06-15","nonCallPeriodEnd":"2027-06-15","originalFace":10000000,"poolId":"MRDN-2026-1","reinvestmentPeriodEnd":"2028-06-15","scheduleConventions":{"accrualAdjusted":null,"businessDayConvention":"ModifiedFollowing","couponCashBasis":"Level","couponRateUnit":"Percent","direction":"Forward","endOfMonthRule":true,"firstRegularDate":null,"lastRegularDate":"2031-03-15","paymentCalendars":["USNY"],"paymentLagBusinessDays":0,"rollConvention":"15","stubPolicy":"Long"},"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"seniority":"Mezzanine","structuredCreditKind":"Clo","tranche":"B","weightedAverageCoupon":7.85,"weightedAverageLoanAgeMonths":14,"weightedAverageMaturityMonths":58}""";

    private const string SwapAtVersion1 =
        """{"effectiveDate":"2026-01-15","legs":[{"currency":"USD","currentIndexRate":null,"dayCount":"30/360","direction":"Pay","exchangesPrincipal":false,"fixedRate":4.25,"index":null,"legId":"fixed","legType":"Fixed","notional":10000000,"notionalSchedule":[],"paymentFrequency":"SemiAnnual","resetSchedule":[],"spreadBps":null},{"currency":"USD","currentIndexRate":null,"dayCount":"ACT/360","direction":"Receive","exchangesPrincipal":false,"fixedRate":null,"index":"SOFR","legId":"float","legType":"Floating","notional":10000000,"notionalSchedule":[],"paymentFrequency":"Quarterly","resetSchedule":[],"spreadBps":15}],"maturityDate":"2031-01-15","schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1}""";

    // One declaring leg: from the block's version upward every leg carries the key, null on the plain one.
    private const string SwapAtVersion2 =
        """{"effectiveDate":"2026-01-15","legs":[{"currency":"USD","currentIndexRate":null,"dayCount":"30/360","direction":"Pay","exchangesPrincipal":false,"fixedRate":4.25,"index":null,"legId":"fixed","legType":"Fixed","notional":10000000,"notionalSchedule":[],"paymentFrequency":"SemiAnnual","resetSchedule":[],"scheduleConventions":null,"spreadBps":null},{"currency":"USD","currentIndexRate":null,"dayCount":"ACT/360","direction":"Receive","exchangesPrincipal":false,"fixedRate":null,"index":"SOFR","legId":"float","legType":"Floating","notional":10000000,"notionalSchedule":[],"paymentFrequency":"Quarterly","resetSchedule":[],"scheduleConventions":{"accrualAdjusted":null,"businessDayConvention":"ModifiedFollowing","couponCashBasis":null,"couponRateUnit":null,"direction":null,"endOfMonthRule":null,"firstRegularDate":null,"lastRegularDate":null,"paymentCalendars":["USNY"],"paymentLagBusinessDays":2,"rollConvention":null,"stubPolicy":null},"spreadBps":15}],"maturityDate":"2031-01-15","schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":2}""";

    // Every class that reads ahead of what it writes, stored in the canonical form of the version it
    // reads through, with the accrual convention the economic bridge fills from the block (Swap: none).
    private static readonly IReadOnlyDictionary<string, (string Terms, string? AccrualConvention)> CanonicalAheadOfWriteRows =
        new Dictionary<string, (string, string?)>(StringComparer.Ordinal)
        {
            ["Bond"] = (BondAtVersion2, "ModifiedFollowing"),
            ["CertificateOfDeposit"] = (CertificateOfDepositAtVersion2, "Following"),
            ["StructuredCredit"] = (StructuredCreditAtVersion3, "ModifiedFollowing"),
            ["Swap"] = (SwapAtVersion2, null),
        };

    // Stored ahead-of-write rows whose versioned content the tolerant readers cannot reproduce, with
    // the path the refusal names.
    private static readonly IReadOnlyDictionary<string, (string AssetClass, string Terms, string ChangedPath)> UnreproducibleRows =
        new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
        {
            ["rollConventionAsANumber"] = ("Bond", StampedTwo(BondAtVersion1, terms =>
                terms["scheduleConventions"] = new JsonObject { ["businessDayConvention"] = "ModifiedFollowing", ["rollConvention"] = 15 }),
                "scheduleConventions"),
            ["unknownInnerMember"] = ("Bond", StampedTwo(BondAtVersion1, terms =>
                terms["scheduleConventions"] = new JsonObject { ["businessDayConvention"] = "ModifiedFollowing", ["settlementLagDays"] = 2 }),
                "scheduleConventions"),
            ["blockIsNotAnObject"] = ("Bond", StampedTwo(BondAtVersion1, terms => terms["scheduleConventions"] = "x"),
                "scheduleConventions"),
            ["legBlockIsNotAnObject"] = ("Swap", StampedTwo(SwapAtVersion1, terms => terms["legs"]![1]!["scheduleConventions"] = "x"),
                "legs[1].scheduleConventions"),
            ["populatedCaseVariantKey"] = ("Bond", StampedTwo(BondAtVersion1, terms =>
                terms["ScheduleConventions"] = new JsonObject { ["businessDayConvention"] = "ModifiedFollowing" }),
                "scheduleConventions"),
            // The decoder reads the canonical spelling, so the content round-trips; the second
            // populated spelling is the conflict the guard reports.
            ["canonicalKeyBesideAPopulatedCaseVariant"] = ("Bond", StampedTwo(BondAtVersion1, terms =>
            {
                terms["scheduleConventions"] = new JsonObject { ["businessDayConvention"] = "ModifiedFollowing" };
                terms["ScheduleConventions"] = new JsonObject { ["businessDayConvention"] = "ModifiedFollowing" };
            }),
                "scheduleConventions"),
        };

    public static TheoryData<string, string> CanonicalRowsAndOperations()
    {
        var data = new TheoryData<string, string>();
        foreach (var assetClass in CanonicalAheadOfWriteRows.Keys)
        {
            data.Add(assetClass, Deactivate);
            data.Add(assetClass, AmendWithoutTermsPatch);
        }

        return data;
    }

    public static TheoryData<string, string> UnreproducibleRowsAndOperations()
    {
        var data = new TheoryData<string, string>();
        foreach (var shape in UnreproducibleRows.Keys)
        {
            data.Add(shape, Deactivate);
            data.Add(shape, AmendWithoutTermsPatch);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CanonicalRowsAndOperations))]
    public async Task RowStampedAboveTheWriteVersion_WithADecodableBlock_PersistsTheBlockAndStampByteForByte(
        string assetClass,
        string operation)
    {
        // The node read the block (decoding is gated on the stamp) and the writer stamps the version
        // the record's populated terms need, so re-serializing the row reproduces it exactly.
        var (terms, accrualConvention) = CanonicalAheadOfWriteRows[assetClass];
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, assetClass, terms);

        await RunAsync(harness, securityId, operation);

        harness.Upserted.Should().ContainSingle();
        harness.Upserted[0].AssetSpecificTerms.GetRawText().Should().Be(terms,
            "a row this node reads re-serializes in the canonical form of the version it was stamped at");
        var economic = SecurityMasterMapping.FromEventEnvelope(harness.Appended.Should().ContainSingle().Subject);
        JsonNode.DeepEquals(JsonNode.Parse(economic.LegacyAssetSpecificTerms!.Value.GetRawText()), JsonNode.Parse(terms))
            .Should().BeTrue("the economic event retains the flat terms it was written from");
        if (accrualConvention is null)
        {
            economic.EconomicTerms.GetProperty("accrual").ValueKind.Should().Be(JsonValueKind.Null,
                "a swap leg's block never feeds the instrument-level accrual module");
        }
        else
        {
            economic.EconomicTerms.GetProperty("accrual").GetProperty("businessDayConvention").GetString()
                .Should().Be(accrualConvention, "the declared block reaches the economic accrual module");
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"paymentCalendars":[]}""")]
    [InlineData("""{"accrualAdjusted":null,"businessDayConvention":null,"couponCashBasis":null,"couponRateUnit":null,"direction":null,"endOfMonthRule":null,"firstRegularDate":null,"lastRegularDate":null,"paymentCalendars":[],"paymentLagBusinessDays":null,"rollConvention":null,"stubPolicy":null}""")]
    public async Task DeactivateAsync_BondStampedTwoWithAnEmptyBlock_IsReEmittedAtTheWriteVersion(string block)
    {
        // The pinned downgrade: a block that declares nothing is data-equal to version 1, so the row
        // re-serializes exactly as the version-1 writer writes it, without the key.
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, "Bond", StampedTwo(BondAtVersion1, terms => terms["scheduleConventions"] = JsonNode.Parse(block)));

        await RunAsync(harness, securityId, Deactivate);

        harness.Upserted.Should().ContainSingle().Which.AssetSpecificTerms.GetRawText().Should().Be(BondAtVersion1);
        harness.Appended.Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(UnreproducibleRowsAndOperations))]
    public async Task RowStampedAboveTheWriteVersion_WhoseBlockCannotBeReproduced_RefusesTheWrite(
        string shape,
        string operation)
    {
        // The tolerant readers drop a mistyped value, an inner member this node does not know, a
        // non-object block and a case-variant key. Re-serializing would lose them, so the deactivate
        // or patch-less amend is refused before anything is appended or upserted.
        var (assetClass, terms, changedPath) = UnreproducibleRows[shape];
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, assetClass, terms);

        await FluentActions.Invoking(() => RunAsync(harness, securityId, operation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(
                $"Security '{securityId:D}' is stamped assetClassTermsVersion 2, which this node reads but does not write, " +
                $"and re-serializing it here would change '{changedPath}'. Apply the change from a node that writes that " +
                "version, or amend it with terms at version 1 that settle the block.");

        harness.Appended.Should().BeEmpty();
        harness.Upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task AmendTermsAsync_TermsPatchReSubmittingStampTwo_IsRefusedWithTheWriteVersionReason()
    {
        // A terms patch replaces the kind wholesale and maps in Write mode: the round-trip guard
        // stands aside, and the write guard refuses a stamp this node reads but does not write.
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, "Bond", BondAtVersion2);

        await harness.Service.Invoking(service => service.AmendTermsAsync(AmendRequest(securityId, Parse(BondAtVersion2))))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(BondWriteVersionRefusal);

        harness.Appended.Should().BeEmpty();
        harness.Upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task AmendTermBlockAsync_OnABondStampedTwo_IsRefusedWithTheWriteVersionReason()
    {
        // The block amendment carries every other stored term, the stamp included, into its patch.
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, "Bond", BondAtVersion2);

        await harness.Service.Invoking(service => service.AmendTermBlockAsync(
                securityId,
                "principalSchedule",
                new AmendSecurityTermBlockRequest(
                    ExpectedVersion: 2,
                    Block: Parse("""[{"paymentDate":"2029-01-01","amount":500}]"""),
                    EffectiveFrom: DateTimeOffset.UtcNow,
                    SourceSystem: "test",
                    UpdatedBy: "codex",
                    SourceRecordId: null,
                    Reason: "replace the principal schedule")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(BondWriteVersionRefusal);

        harness.Appended.Should().BeEmpty();
        harness.Upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task AmendTermsAsync_TermsPatchAtVersionOneWithoutTheBlock_IsAcceptedAndClearsTheBlock()
    {
        // The exit both refusals name: terms at the write version that settle the block.
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, "Bond", BondAtVersion2);

        var detail = await harness.Service.AmendTermsAsync(AmendRequest(securityId, Parse(BondAtVersion1)));

        detail.AssetSpecificTerms.TryGetProperty("scheduleConventions", out _).Should().BeFalse();
        harness.Upserted.Should().ContainSingle().Which.AssetSpecificTerms.GetRawText().Should().Be(BondAtVersion1);
        harness.Appended.Should().ContainSingle();
    }

    public static TheoryData<string> UnreproducibleShapes()
    {
        var data = new TheoryData<string>();
        foreach (var shape in UnreproducibleRows.Keys)
        {
            data.Add(shape);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(UnreproducibleShapes))]
    public async Task AmendTermsAsync_TermsPatchAtTheWriteVersion_SettlesARowTheGuardRefusesToReSerialize(string shape)
    {
        // The remedy the round-trip refusal names must stay open: the guard stands aside for a terms
        // patch, which replaces the terms wholesale and is mapped in Write mode instead.
        var (assetClass, terms, _) = UnreproducibleRows[shape];
        var settled = assetClass == "Swap" ? SwapAtVersion1 : BondAtVersion1;
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, assetClass, terms);

        await harness.Service.AmendTermsAsync(AmendRequest(securityId, Parse(settled)));

        harness.Upserted.Should().ContainSingle().Which.AssetSpecificTerms.GetRawText().Should().Be(settled);
        harness.Appended.Should().ContainSingle();
    }

    [Fact]
    public async Task AmendTermsAsync_TermsPatchAtVersionOneStillPopulatingTheBlock_IsRefusedAsNotYetWritable()
    {
        // Dropping the stamp is not a way to write the block: a populated block is refused on every
        // write path until a contract step raises the write version.
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, "Bond", BondAtVersion2);
        var patch = JsonNode.Parse(BondAtVersion2)!.AsObject();
        patch[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 1;

        await harness.Service.Invoking(service => service.AmendTermsAsync(AmendRequest(securityId, Parse(patch.ToJsonString()))))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "'scheduleConventions' is an asset-class terms version 2 term of 'Bond'. This node reads it but writes " +
                "assetClassTermsVersion 1, so a create, amend, import or field edit cannot set it yet. Leave it absent, " +
                "null or empty (ADR-024).");

        harness.Appended.Should().BeEmpty();
        harness.Upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task ProfileBackedStructuredCredit_CarryingScheduleConventions_CreatesAmendsAndMergesVerbatim()
    {
        // Versioned class terms are flat-codec-only: a profile envelope's fields belong to its pinned
        // profile, so a scheduleConventions key at its root or inside profileFields is never refused,
        // decoded or fed to the economic accrual module. Every step persists the envelope verbatim.
        var catalog = StaticSecurityAssetProfileCatalog.CreateDefault();
        var harness = new ServiceHarness(catalog);
        var securityId = Guid.NewGuid();
        var created = ProfileEnvelope(tranche: "A-1");

        await harness.Service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "CustomAsset",
            JsonSerializer.SerializeToElement(new { displayName = "IO strip with schedule conventions", currency = "USD" }),
            Parse(created),
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "88160R101", true, DateTimeOffset.UtcNow.AddDays(-30), null, null),
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "IOPO-CONV", false, DateTimeOffset.UtcNow.AddDays(-30), null, null)
            ],
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "create"));

        harness.Upserted.Should().ContainSingle();
        harness.Upserted[0].AssetClass.Should().Be("StructuredCredit", "the profile reclassifies the record");
        harness.Upserted[0].AssetSpecificTerms.GetRawText().Should().Be(created);

        var amended = ProfileEnvelope(tranche: "A-2");
        await harness.Service.AmendTermsAsync(new AmendSecurityTermsRequest(
            securityId,
            1,
            CommonTerms: null,
            AssetSpecificTermsPatch: Parse(amended),
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "envelope amend"));

        harness.Upserted.Should().HaveCount(2);
        harness.Upserted[1].AssetSpecificTerms.GetRawText().Should().Be(amended);

        // An approved workbench edit of profileFields.scheduleConventions, merged into the canonical
        // envelope through the publish fan-out's merge handler and amended by this service.
        const string fieldPath = "assetSpecificTerms.profileFields.scheduleConventions";
        const string editedConventions = """{"businessDayConvention":"Preceding","stubPolicy":"Long"}""";
        var revisions = new InMemorySecurityMasterRevisionStore();
        var draft = await revisions.CreateDraftAsync(
            securityId, "ops.analyst", fieldPath, DateTimeOffset.UtcNow, "Trustee report restated the conventions.",
            fundProfileId: null, fieldValue: new SecurityMasterRevisionFieldValue(editedConventions));
        await revisions.TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await revisions.TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        var merge = new ApprovedFieldEditCanonicalMergeHandler(
            revisions,
            NullLogger<ApprovedFieldEditCanonicalMergeHandler>.Instance,
            harness.Store,
            harness.Service,
            assetProfileCatalog: catalog);

        await merge.HandleAsync(new SecurityMasterRevisionPublishedEvent(
            SecurityId: securityId,
            RevisionId: draft.RevisionId,
            Version: 2,
            EffectiveFrom: DateTimeOffset.UtcNow,
            ChangedFields: [fieldPath],
            DownstreamImpact: EmptyImpact(),
            AffectedLedgerBookIds: [],
            Actor: "ops.analyst",
            CorrelationId: null));

        var expectedMerged = JsonNode.Parse(amended)!.AsObject();
        expectedMerged["profileFields"]!["scheduleConventions"] = JsonNode.Parse(editedConventions);
        harness.Upserted.Should().HaveCount(3, "the approved profile edit merges into the canonical terms");
        JsonNode.DeepEquals(JsonNode.Parse(harness.Upserted[2].AssetSpecificTerms.GetRawText()), expectedMerged)
            .Should().BeTrue("the merged envelope persists verbatim, its root scheduleConventions included");

        harness.Appended.Should().HaveCount(3);
        foreach (var envelope in harness.Appended)
        {
            var economic = SecurityMasterMapping.FromEventEnvelope(envelope);
            (economic.EconomicTerms.TryGetProperty("accrual", out var accrual) ? accrual.ValueKind : JsonValueKind.Null)
                .Should().Be(JsonValueKind.Null, $"a profile envelope's conventions never reach the accrual module ({envelope.EventType})");
        }
    }

    [Fact]
    public async Task DeactivateAsync_ProfileEnvelopeStampedAtTheReadableVersion_PersistsVerbatimWithoutDecodingItsConventions()
    {
        // A stored profile envelope stamped 3 is readable here. Its stamp reaches the block's
        // version, but a profile envelope decodes only what the write version declares, so its root
        // scheduleConventions is neither decoded into the economic accrual module nor checked by the
        // round-trip guard; the envelope itself is restored verbatim.
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        var stored = JsonNode.Parse(ProfileEnvelope(tranche: "A-1"))!.AsObject();
        stored[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 3;
        harness.Seed(securityId, "StructuredCredit", stored.ToJsonString());

        await RunAsync(harness, securityId, Deactivate);

        harness.Upserted.Should().ContainSingle().Which.AssetSpecificTerms.GetRawText().Should().Be(stored.ToJsonString());
        var economic = SecurityMasterMapping.FromEventEnvelope(harness.Appended.Should().ContainSingle().Subject);
        economic.EconomicTerms.GetProperty("accrual").ValueKind.Should().Be(JsonValueKind.Null,
            "versioned class terms are flat-codec-only; a profile envelope's root block is never decoded");
    }

    private static string BondWriteVersionRefusal =>
        "assetClassTermsVersion '2' for asset class 'Bond' is readable but not writable on this node: it reads versions " +
        "1 through 2 and writes version 1. Submit the terms at version 1, or apply the change from a node that writes version 2.";

    private static Task RunAsync(ServiceHarness harness, Guid securityId, string operation)
        => operation switch
        {
            Deactivate => harness.Service.DeactivateAsync(new DeactivateSecurityRequest(
                securityId,
                2,
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "deactivate")),
            AmendWithoutTermsPatch => harness.Service.AmendTermsAsync(new AmendSecurityTermsRequest(
                SecurityId: securityId,
                ExpectedVersion: 2,
                CommonTerms: null,
                AssetSpecificTermsPatch: null,
                IdentifiersToAdd:
                [
                    new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "AHEAD-OF-WRITE", false, DateTimeOffset.UtcNow.AddDays(-1), null, null)
                ],
                IdentifiersToExpire: [],
                EffectiveFrom: DateTimeOffset.UtcNow,
                SourceSystem: "test",
                UpdatedBy: "codex",
                SourceRecordId: null,
                Reason: "add an identifier")),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private static AmendSecurityTermsRequest AmendRequest(Guid securityId, JsonElement assetSpecificTermsPatch)
        => new(
            SecurityId: securityId,
            ExpectedVersion: 2,
            CommonTerms: null,
            AssetSpecificTermsPatch: assetSpecificTermsPatch,
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "amend");

    /// <summary>A version-1 document restamped at version 2, then edited.</summary>
    private static string StampedTwo(string versionOneTerms, Action<JsonObject> edit)
    {
        var terms = JsonNode.Parse(versionOneTerms)!.AsObject();
        terms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = 2;
        edit(terms);
        return terms.ToJsonString();
    }

    /// <summary>
    /// A structured-credit-io-po envelope carrying a populated scheduleConventions both inside
    /// profileFields and at its root, as a profile may legitimately declare.
    /// </summary>
    private static string ProfileEnvelope(string tranche)
        => $$$"""{"customProfileId":"structured-credit-io-po","profileVersion":1,"profileFields":{"tranche":"{{{tranche}}}","poolId":"POOL-1","currentFactor":0.8,"originalFace":1000000,"couponOrIndex":"SOFR","factorSchedule":"trustee-report","collateralType":"CLO","scheduleConventions":{"businessDayConvention":"ModifiedFollowing","paymentCalendars":["USNY"],"stubPolicy":"Short"}},"profileApproval":{"approvedBy":"Meridian","approvedAtUtc":"2026-05-29T00:00:00Z","approvalReference":"MERIDIAN-SEED-APPROVAL"},"scheduleConventions":{"businessDayConvention":"Following","paymentCalendars":["GBLO","USNY"],"rollConvention":"EndOfMonth"}}""";

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static SecurityMasterDownstreamImpactDto EmptyImpact()
        => new(
            FundProfileId: null,
            IsScoped: false,
            Severity: SecurityMasterImpactSeverity.None,
            Summary: "n/a",
            PortfolioExposureSummary: string.Empty,
            LedgerExposureSummary: string.Empty,
            ReconciliationExposureSummary: string.Empty,
            ReportPackExposureSummary: string.Empty,
            MatchedRunCount: 0,
            PortfolioExposureCount: 0,
            LedgerExposureCount: 0,
            ReconciliationExposureCount: 0,
            ReportPackExposureCount: 0,
            Links: []);

    /// <summary>
    /// A service over substitute stores that record every appended event and upserted projection; an
    /// upsert becomes the projection the next read returns.
    /// </summary>
    private sealed class ServiceHarness
    {
        public ServiceHarness(ISecurityAssetProfileCatalog? assetProfileCatalog = null)
        {
            var eventStore = Substitute.For<ISecurityMasterEventStore>();
            eventStore
                .AppendAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Appended.AddRange(call.ArgAt<IReadOnlyList<SecurityMasterEventEnvelope>>(2));
                    return Task.CompletedTask;
                });

            Store = Substitute.For<ISecurityMasterStore>();
            Store.UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var persisted = call.Arg<SecurityProjectionRecord>();
                    Upserted.Add(persisted);
                    Store.GetProjectionAsync(persisted.SecurityId, Arg.Any<CancellationToken>()).Returns(persisted);
                    return Task.CompletedTask;
                });

            var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
            Service = new SecurityMasterService(
                eventStore,
                snapshotStore,
                Store,
                new SecurityMasterAggregateRebuilder(eventStore, snapshotStore),
                new SecurityMasterOptions
                {
                    SnapshotIntervalVersions = 50,
                    ResolveInactiveByDefault = true
                },
                NullLogger<SecurityMasterService>.Instance,
                assetProfileCatalog: assetProfileCatalog);
        }

        public SecurityMasterService Service { get; }

        public ISecurityMasterStore Store { get; }

        public List<SecurityMasterEventEnvelope> Appended { get; } = [];

        public List<SecurityProjectionRecord> Upserted { get; } = [];

        /// <summary>Stores an active security at version 2 whose asset-specific terms are exactly <paramref name="terms"/>.</summary>
        public void Seed(Guid securityId, string assetClass, string terms)
        {
            var projection = new SecurityProjectionRecord(
                securityId,
                assetClass,
                SecurityStatusDto.Active,
                $"{assetClass} read ahead of its write version",
                "USD",
                "Ticker",
                "ACME",
                JsonSerializer.SerializeToElement(new
                {
                    displayName = $"{assetClass} read ahead of its write version",
                    currency = "USD",
                    exchange = "XNYS",
                    lotSize = 1,
                    tickSize = 0.01m
                }),
                Parse(terms),
                JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = "test",
                    asOf = DateTimeOffset.UtcNow,
                    updatedBy = "codex"
                }),
                2,
                DateTimeOffset.UtcNow.AddDays(-1),
                null,
                [
                    new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "ACME", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
                ],
                Array.Empty<SecurityAliasDto>());
            Store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(projection);
        }
    }
}
