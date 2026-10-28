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
/// The service seam of the readable-before-writable terms expansion (ADR-024, priority 5, increments
/// 4a and 4b) in a mixed fleet: rows stamped above their class's write version, written by a node that
/// writes the newer version, sit in the store this node reads. Deactivating them, or amending them
/// without a terms patch, re-serializes the record this node decoded, so the versioned terms (the
/// scheduleConventions block, the callSchedule and putSchedule arrays) and the stamp must survive byte
/// for byte, empty versioned terms downgrade to the version the rest of the row needs, and versioned
/// terms this node cannot reproduce refuse the write instead of silently losing them. Only the terms up
/// to a row's own stamp are compared: a key above the stamp is undeclared on that row and is dropped
/// exactly as increment 4a drops it. Writes that carry the newer version, or set a term only it
/// declares, are refused. Profile envelopes stay outside versioned class terms on every step.
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

    // The bond of BondAtVersion2 as a node that writes version 3 persists it with declared call and put
    // schedules: every entry in stored order (the calls out of date order), each with all its members
    // in alphabetical order and null for an unset one (ADR-024's frozen written form).
    private const string BondAtVersion3 =
        """{"callDate":null,"callSchedule":[{"callDate":"2028-01-01","callEndDate":null,"callPrice":101.5,"exerciseFrequency":"SemiAnnual","exerciseStyle":"Bermudan","makeWholeSpreadBps":null,"maximumNoticeDays":60,"minimumNoticeDays":30,"noticeDayType":"Calendar","partialRedemption":"WholeOnly","priceBasis":"PercentOfPar","priceType":"Fixed"},{"callDate":"2026-01-01","callEndDate":"2027-12-31","callPrice":100,"exerciseFrequency":null,"exerciseStyle":"American","makeWholeSpreadBps":25,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"partialRedemption":"ProRata","priceBasis":"PercentOfPar","priceType":"MakeWhole"}],"capRate":null,"couponRate":4.25,"couponType":"Fixed","dayCount":"30/360","floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":"2024-01-01","issuerName":"ACME Corp","legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":1000,"paymentFrequency":"SemiAnnual","preRefundDate":null,"principalSchedule":[],"putSchedule":[{"exerciseFrequency":null,"exerciseStyle":"European","maximumNoticeDays":15,"minimumNoticeDays":5,"noticeDayType":"Business","priceBasis":"PercentOfPar","putDate":"2028-06-15","putEndDate":null,"putPrice":100}],"scheduleConventions":{"accrualAdjusted":true,"businessDayConvention":"ModifiedFollowing","couponCashBasis":null,"couponRateUnit":null,"direction":"Backward","endOfMonthRule":false,"firstRegularDate":"2024-07-01","lastRegularDate":null,"paymentCalendars":["USNY","GBLO"],"paymentLagBusinessDays":2,"rollConvention":"EndOfMonth","stubPolicy":"Short"},"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"seniority":"Senior","spreadBps":null,"stepSchedule":[],"subclass":"Corporate"}""";

    // The tranche of StructuredCreditAtVersion3 as its write version persists it: no block, stamped 2.
    private static readonly string StructuredCreditAtVersion2 =
        Stamped(StructuredCreditAtVersion3, 2, static terms => terms.Remove("scheduleConventions"));

    // What each class writes at its write version for the rows below: the version the round-trip
    // refusal names, and the terms patch that settles such a row.
    private static readonly IReadOnlyDictionary<string, (int Version, string Terms)> WriteVersionTerms =
        new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
            ["Bond"] = (1, BondAtVersion1),
            ["StructuredCredit"] = (2, StructuredCreditAtVersion2),
            ["Swap"] = (1, SwapAtVersion1),
        };

    // Every class that reads ahead of what it writes, stored in the canonical form of the version that
    // introduced its scheduleConventions block, with the accrual convention the economic bridge fills
    // from the block (Swap: none).
    private static readonly IReadOnlyDictionary<string, (string Terms, string? AccrualConvention)> CanonicalAheadOfWriteRows =
        new Dictionary<string, (string, string?)>(StringComparer.Ordinal)
        {
            ["Bond"] = (BondAtVersion2, "ModifiedFollowing"),
            ["CertificateOfDeposit"] = (CertificateOfDepositAtVersion2, "Following"),
            ["StructuredCredit"] = (StructuredCreditAtVersion3, "ModifiedFollowing"),
            ["Swap"] = (SwapAtVersion2, null),
        };

    // Stored ahead-of-write rows whose versioned content the tolerant readers cannot reproduce, with
    // the stamp and the path the refusal names.
    private static readonly IReadOnlyDictionary<string, (string AssetClass, string Terms, int Stamp, string ChangedPath)> UnreproducibleRows =
        new Dictionary<string, (string, string, int, string)>(StringComparer.Ordinal)
        {
            ["rollConventionAsANumber"] = ("Bond", StampedTwo(BondAtVersion1, terms =>
                terms["scheduleConventions"] = new JsonObject { ["businessDayConvention"] = "ModifiedFollowing", ["rollConvention"] = 15 }),
                2, "scheduleConventions"),
            ["unknownInnerMember"] = ("Bond", StampedTwo(BondAtVersion1, terms =>
                terms["scheduleConventions"] = new JsonObject { ["businessDayConvention"] = "ModifiedFollowing", ["settlementLagDays"] = 2 }),
                2, "scheduleConventions"),
            ["blockIsNotAnObject"] = ("Bond", StampedTwo(BondAtVersion1, terms => terms["scheduleConventions"] = "x"),
                2, "scheduleConventions"),
            ["legBlockIsNotAnObject"] = ("Swap", StampedTwo(SwapAtVersion1, terms => terms["legs"]![1]!["scheduleConventions"] = "x"),
                2, "legs[1].scheduleConventions"),
            ["populatedCaseVariantKey"] = ("Bond", StampedTwo(BondAtVersion1, terms =>
                terms["ScheduleConventions"] = new JsonObject { ["businessDayConvention"] = "ModifiedFollowing" }),
                2, "scheduleConventions"),
            // The decoder reads the canonical spelling, so the content round-trips; the second
            // populated spelling is the conflict the guard reports.
            ["canonicalKeyBesideAPopulatedCaseVariant"] = ("Bond", StampedTwo(BondAtVersion1, terms =>
            {
                terms["scheduleConventions"] = new JsonObject { ["businessDayConvention"] = "ModifiedFollowing" };
                terms["ScheduleConventions"] = new JsonObject { ["businessDayConvention"] = "ModifiedFollowing" };
            }),
                2, "scheduleConventions"),

            // Increment 4b: call and put schedules on rows stamped at the version that declares them. Each
            // bond keeps BondAtVersion2's decodable block, so the refusal names the schedule.
            ["callEntryUnknownMember"] = ("Bond", StampedThreeWithCalls("""[{"callDate":"2031-06-15","callPrice":101,"redemptionAgent":"Trustee"}]"""),
                3, "callSchedule"),
            ["callPriceAsAString"] = ("Bond", StampedThreeWithCalls("""[{"callDate":"2031-06-15","callPrice":"101"}]"""),
                3, "callSchedule"),
            ["minimumNoticeDaysAsAFraction"] = ("Bond", StampedThreeWithCalls("""[{"callDate":"2031-06-15","minimumNoticeDays":30.5}]"""),
                3, "callSchedule"),
            ["callEntryIsNotAnObject"] = ("Bond", StampedThreeWithCalls("""[{"callDate":"2031-06-15"},"2032-06-15"]"""),
                3, "callSchedule"),
            // An entry without a parseable date is not a term and is dropped, whatever else it carries;
            // a date the reader does parse is re-emitted in ISO form.
            ["datelessCallEntryWithAPrice"] = ("Bond", StampedThreeWithCalls("""[{"callPrice":101}]"""),
                3, "callSchedule"),
            ["callDateWithATimePart"] = ("Bond", StampedThreeWithCalls("""[{"callDate":"2031-06-15T00:00:00"}]"""),
                3, "callSchedule"),
            ["callDateNotInIsoForm"] = ("Bond", StampedThreeWithCalls("""[{"callDate":"06/15/2031"}]"""),
                3, "callSchedule"),
            // A blank text member is read as unset, so it is not re-emitted.
            ["blankExerciseStyle"] = ("Bond", StampedThreeWithCalls("""[{"callDate":"2031-06-15","exerciseStyle":""}]"""),
                3, "callSchedule"),
            ["populatedCaseVariantScheduleKey"] = ("Bond", Stamped(BondAtVersion2, 3, terms =>
                terms["CallSchedule"] = JsonNode.Parse("""[{"callDate":"2031-06-15"}]""")),
                3, "callSchedule"),
            ["canonicalScheduleKeyBesideAPopulatedCaseVariant"] = ("Bond", Stamped(BondAtVersion2, 3, terms =>
            {
                terms["callSchedule"] = JsonNode.Parse("""[{"callDate":"2031-06-15"}]""");
                terms["CallSchedule"] = JsonNode.Parse("""[{"callDate":"2031-06-15"}]""");
            }),
                3, "callSchedule"),
            ["caseVariantCallDateMember"] = ("Bond", StampedThreeWithCalls("""[{"CallDate":"2031-06-15"}]"""),
                3, "callSchedule"),
            ["callScheduleIsAnObject"] = ("Bond", StampedThreeWithCalls("""{"callDate":"2031-06-15"}"""),
                3, "callSchedule"),
            ["putEntryUnknownMember"] = ("StructuredCredit", Stamped(StructuredCreditAtVersion3, 4, terms =>
                terms["putSchedule"] = JsonNode.Parse("""[{"putDate":"2029-06-15","putPrice":100,"electionAgent":"Trustee"}]""")),
                4, "putSchedule"),
        };

    // The stray rows of the step-1 write golden (SecurityTermsWriteGoldenTests) whose re-serialization
    // it pins: a populated callSchedule on a row stamped below the version that declares it.
    private static readonly string[] StrayCallScheduleGoldenKeys =
    [
        "Bond.v2StrayCallSchedule",
        "StructuredCredit.v3StrayCallSchedule",
    ];

    private const string WriteGoldenFileName = "security-terms-write-golden.json";

    // The committed step-1 write golden, located as SecurityTermsWriteGoldenTests locates it.
    private static readonly Lazy<IReadOnlyDictionary<string, string>?> WriteGolden = new(static () =>
        LocateWriteGolden() is { } path
            ? JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(path))
            : null);

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

    public static TheoryData<string, string> StrayCallScheduleRowsAndOperations()
    {
        var data = new TheoryData<string, string>();
        foreach (var goldenKey in StrayCallScheduleGoldenKeys)
        {
            data.Add(goldenKey, Deactivate);
            data.Add(goldenKey, AmendWithoutTermsPatch);
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
        // The tolerant readers drop a mistyped value, a blank text member, an inner member this node
        // does not know, a non-object block, entry or array, an entry without a parseable date and a
        // case-variant key. Re-serializing would lose them, so the deactivate or patch-less amend is
        // refused before anything is appended or upserted.
        var (assetClass, terms, stamp, changedPath) = UnreproducibleRows[shape];
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, assetClass, terms);

        await FluentActions.Invoking(() => RunAsync(harness, securityId, operation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(
                $"Security '{securityId:D}' is stamped assetClassTermsVersion {stamp}, which this node reads but does not write, " +
                $"and re-serializing it here would change '{changedPath}'. Apply the change from a node that writes that " +
                $"version, or amend it with terms at version {WriteVersionTerms[assetClass].Version} that settle its versioned terms.");

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
            .WithMessage(BondWriteVersionRefusal(2));

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
            .WithMessage(BondWriteVersionRefusal(2));

        harness.Appended.Should().BeEmpty();
        harness.Upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task AmendTermsAsync_TermsPatchAtVersionOneWithoutTheBlock_IsAcceptedAndClearsTheBlock()
    {
        // The exit both refusals name: terms at the write version that settle its versioned terms.
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
        var (assetClass, terms, _, _) = UnreproducibleRows[shape];
        var settled = WriteVersionTerms[assetClass].Terms;
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

    // ── Increment 4b: call and put schedules ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(Deactivate)]
    [InlineData(AmendWithoutTermsPatch)]
    public async Task RowStampedAtTheSchedulesVersion_WithDecodableSchedules_PersistsTheSchedulesAndStampByteForByte(string operation)
    {
        // The node read the schedules (decoding is gated on the stamp) and the writer stamps the
        // highest version the record's populated terms need, so re-serializing the row reproduces it
        // exactly: every entry in stored order, every member in canonical order.
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, "Bond", BondAtVersion3);

        await RunAsync(harness, securityId, operation);

        harness.Upserted.Should().ContainSingle().Which.AssetSpecificTerms.GetRawText().Should().Be(BondAtVersion3,
            "a row this node reads re-serializes in the canonical form of the version it was stamped at");
        var economic = SecurityMasterMapping.FromEventEnvelope(harness.Appended.Should().ContainSingle().Subject);
        JsonNode.DeepEquals(JsonNode.Parse(economic.LegacyAssetSpecificTerms!.Value.GetRawText()), JsonNode.Parse(BondAtVersion3))
            .Should().BeTrue("the economic event retains the flat terms it was written from");
        var call = economic.EconomicTerms.GetProperty("call");
        call.GetProperty("callSchedule").GetArrayLength().Should().Be(2, "both declared calls reach the economic call module");
        call.GetProperty("isPuttable").GetBoolean().Should().BeTrue("the declared put reaches the economic call module");
        economic.EconomicTerms.GetProperty("accrual").GetProperty("businessDayConvention").GetString()
            .Should().Be("ModifiedFollowing", "the declared block still reaches the economic accrual module");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("""[{"exerciseFrequency":null}]""")]
    public async Task DeactivateAsync_BondStampedThreeWithConventionsAndEmptySchedules_IsReEmittedAtStampTwo(string schedule)
    {
        // Schedules that declare nothing are data-equal to absent, so the row re-serializes at the
        // version its block needs, exactly as a node that writes version 2 writes it, without the keys.
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, "Bond", Stamped(BondAtVersion2, 3, terms =>
        {
            terms["callSchedule"] = JsonNode.Parse(schedule);
            terms["putSchedule"] = JsonNode.Parse(schedule);
        }));

        await RunAsync(harness, securityId, Deactivate);

        harness.Upserted.Should().ContainSingle().Which.AssetSpecificTerms.GetRawText().Should().Be(BondAtVersion2);
        harness.Appended.Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(StrayCallScheduleRowsAndOperations))]
    public async Task RowStampedBelowTheSchedulesVersion_CarryingAStrayCallSchedule_PersistsTheStepOneGoldenBytes(
        string goldenKey,
        string operation)
    {
        // The round-trip guard compares only the terms up to the row's own stamp. A populated
        // callSchedule on a Bond stamped 2 or a StructuredCredit stamped 3 is an undeclared key of that
        // row, neither decoded nor compared, so the write goes ahead and drops it exactly as increment
        // 4a does: it persists the bytes the step-1 golden pinned before increment 4b.
        var golden = WriteGolden.Value;
        golden.Should().NotBeNull($"the golden fixture '{WriteGoldenFileName}' must be committed under tests/fixtures/security-master/terms-golden/");
        var (assetClass, terms) = StrayCallScheduleRow(goldenKey, golden!);
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, assetClass, terms);

        await RunAsync(harness, securityId, operation);

        harness.Upserted.Should().ContainSingle().Which.AssetSpecificTerms.GetRawText().Should().Be(
            golden![$"readable.write.{goldenKey}"],
            "a stray key above the row's stamp is dropped, and the rest of the row re-serializes as increment 4a writes it");
        harness.Appended.Should().ContainSingle();
    }

    [Fact]
    public async Task DeactivateAsync_ProfileEnvelopeStampedAtTheSchedulesVersion_PersistsVerbatimWithoutDecodingItsSchedules()
    {
        // Increment 4b's twin of the stamp-3 envelope above: stamped 4, the version that declares the
        // schedules, with populated callSchedule and putSchedule at its root and inside profileFields.
        // A profile envelope decodes only what the write version declares, so neither its schedules nor
        // its block reach the economic modules or the round-trip guard; the envelope is restored verbatim.
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        var stored = ProfileEnvelopeWithSchedules(stamp: 4, exerciseFrequency: "3");
        harness.Seed(securityId, "StructuredCredit", stored);

        await RunAsync(harness, securityId, Deactivate);

        harness.Upserted.Should().ContainSingle().Which.AssetSpecificTerms.GetRawText().Should().Be(stored);
        var economic = SecurityMasterMapping.FromEventEnvelope(harness.Appended.Should().ContainSingle().Subject);
        economic.EconomicTerms.GetProperty("call").ValueKind.Should().Be(JsonValueKind.Null,
            "versioned class terms are flat-codec-only; a profile envelope's schedules are never decoded");
        economic.EconomicTerms.GetProperty("accrual").ValueKind.Should().Be(JsonValueKind.Null,
            "versioned class terms are flat-codec-only; a profile envelope's root block is never decoded");
    }

    [Theory]
    [InlineData("\"Fortnightly\"")]
    [InlineData("3")]
    public async Task CreateAsync_ProfileEnvelopeCarryingSchedulesAtStampOne_IsAcceptedAndPersistedVerbatim(string exerciseFrequency)
    {
        // The write-mode element-convention walk enforces only conventions this node writes, and never
        // inside a versioned container of a profile envelope: the schedules at its root and in
        // profileFields belong to its pinned profile, so an exercise frequency the coupon-frequency
        // convention cannot type is not refused, and the envelope persists byte for byte.
        var harness = new ServiceHarness(StaticSecurityAssetProfileCatalog.CreateDefault());
        var securityId = Guid.NewGuid();
        var envelope = ProfileEnvelopeWithSchedules(stamp: 1, exerciseFrequency);

        await harness.Service.CreateAsync(ProfileEnvelopeCreateRequest(securityId, envelope));

        harness.Upserted.Should().ContainSingle();
        harness.Upserted[0].AssetClass.Should().Be("StructuredCredit", "the profile resolves to its class");
        harness.Upserted[0].AssetSpecificTerms.GetRawText().Should().Be(envelope);
        var economic = SecurityMasterMapping.FromEventEnvelope(harness.Appended.Should().ContainSingle().Subject);
        economic.EconomicTerms.GetProperty("call").ValueKind.Should().Be(JsonValueKind.Null,
            "a profile envelope's schedules never reach the economic call module");
    }

    [Theory]
    [InlineData(3, "\"Fortnightly\"")]
    [InlineData(3, "3")]
    [InlineData(4, "\"Fortnightly\"")]
    [InlineData(4, "3")]
    public async Task CreateAsync_ProfileEnvelopeCarryingSchedulesStampedAboveTheWriteVersion_IsRefusedByItsStamp(
        int stamp,
        string exerciseFrequency)
    {
        // A profile envelope has no versioned class terms, but its stamp is still checked, and first:
        // the refusal is the readable-but-not-writable reason, never the coupon-frequency convention's.
        var harness = new ServiceHarness(StaticSecurityAssetProfileCatalog.CreateDefault());
        var securityId = Guid.NewGuid();
        var envelope = ProfileEnvelopeWithSchedules(stamp, exerciseFrequency);

        await harness.Service.Invoking(service => service.CreateAsync(ProfileEnvelopeCreateRequest(securityId, envelope)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(
                $"assetClassTermsVersion '{stamp}' for asset class 'StructuredCredit' is readable but not writable on this node: " +
                "it reads versions 1 through 4 and writes version 2. Submit the terms at version 2, or apply the change from a " +
                $"node that writes version {stamp}.");

        harness.Appended.Should().BeEmpty();
        harness.Upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task AmendTermsAsync_TermsPatchReSubmittingStampThree_IsRefusedWithTheWriteVersionReason()
    {
        // A terms patch maps in Write mode: the round-trip guard stands aside, and the write guard
        // refuses a stamp this node reads but does not write.
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, "Bond", BondAtVersion3);

        await harness.Service.Invoking(service => service.AmendTermsAsync(AmendRequest(securityId, Parse(BondAtVersion3))))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(BondWriteVersionRefusal(3));

        harness.Appended.Should().BeEmpty();
        harness.Upserted.Should().BeEmpty();
    }

    [Theory]
    [InlineData("callSchedule", """[{"callDate":"2031-06-15","callPrice":101}]""")]
    [InlineData("putSchedule", """[{"putDate":"2029-06-15","putPrice":100}]""")]
    public async Task AmendTermBlockAsync_ScheduleOnABondStampedThree_IsRefusedAsNotYetWritable(string blockKey, string block)
    {
        // A schedule this node only reads is not a block it amends, even on a row that declares it.
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, "Bond", BondAtVersion3);

        await harness.Service.Invoking(service => service.AmendTermBlockAsync(
                securityId,
                blockKey,
                new AmendSecurityTermBlockRequest(
                    ExpectedVersion: 2,
                    Block: Parse(block),
                    EffectiveFrom: DateTimeOffset.UtcNow,
                    SourceSystem: "test",
                    UpdatedBy: "codex",
                    SourceRecordId: null,
                    Reason: "replace the schedule")))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage(
                $"'{blockKey}' is an asset-class terms version 3 term of 'Bond'. This node reads it but writes " +
                "assetClassTermsVersion 1, so a create, amend, import or field edit cannot set it yet. " +
                "Leave it absent, null or empty (ADR-024). (Parameter 'blockKey')");

        harness.Appended.Should().BeEmpty();
        harness.Upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task AmendTermsAsync_TermsPatchAtVersionOneWithoutTheSchedules_IsAcceptedAndClearsThem()
    {
        // The exit the refusals name: terms at the write version that settle the row's versioned terms.
        var securityId = Guid.NewGuid();
        var harness = new ServiceHarness();
        harness.Seed(securityId, "Bond", BondAtVersion3);

        var detail = await harness.Service.AmendTermsAsync(AmendRequest(securityId, Parse(BondAtVersion1)));

        foreach (var key in new[] { "callSchedule", "putSchedule", "scheduleConventions" })
        {
            detail.AssetSpecificTerms.TryGetProperty(key, out _).Should().BeFalse($"the version-1 patch clears '{key}'");
        }

        harness.Upserted.Should().ContainSingle().Which.AssetSpecificTerms.GetRawText().Should().Be(BondAtVersion1);
        harness.Appended.Should().ContainSingle();
    }

    private static string BondWriteVersionRefusal(int stamp) =>
        $"assetClassTermsVersion '{stamp}' for asset class 'Bond' is readable but not writable on this node: it reads versions " +
        $"1 through 3 and writes version 1. Submit the terms at version 1, or apply the change from a node that writes version {stamp}.";

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
        => Stamped(versionOneTerms, 2, edit);

    /// <summary>A document restamped at <paramref name="stamp"/> in place, then edited.</summary>
    private static string Stamped(string storedTerms, int stamp, Action<JsonObject> edit)
    {
        var terms = JsonNode.Parse(storedTerms)!.AsObject();
        terms[SecurityTermsEnvelope.AssetClassTermsVersionKey] = stamp;
        edit(terms);
        return terms.ToJsonString();
    }

    /// <summary>The version-2 bond restamped at version 3, carrying <paramref name="callSchedule"/>.</summary>
    private static string StampedThreeWithCalls(string callSchedule)
        => Stamped(BondAtVersion2, 3, terms => terms["callSchedule"] = JsonNode.Parse(callSchedule));

    /// <summary>
    /// The stored stray row behind a step-1 golden key, rebuilt as SecurityTermsWriteGoldenTests builds
    /// it from the golden's own write bytes: the class's write golden with increment 4a's readable block,
    /// stamped at the block's version, then a populated callSchedule the stamp does not reach.
    /// No golden key holds the stored row itself, so the stray literals here must stay equal to
    /// SecurityTermsWriteGoldenTests.StrayCallSchedule (the Bond stamp-2 row) and the "x" that its
    /// StoredOnlyCases writes into the StructuredCredit stamp-3 row; both are private there.
    /// </summary>
    private static (string AssetClass, string Terms) StrayCallScheduleRow(string goldenKey, IReadOnlyDictionary<string, string> golden)
    {
        var (assetClass, writeGoldenKey, stamp, callSchedule) = goldenKey switch
        {
            "Bond.v2StrayCallSchedule" => ("Bond", "write.Bond.fixed", 2, """[{"callDate":"2030-06-15","callPrice":101}]"""),
            "StructuredCredit.v3StrayCallSchedule" => ("StructuredCredit", "write.StructuredCredit", 3, "\"x\""),
            _ => throw new ArgumentOutOfRangeException(nameof(goldenKey), goldenKey, null)
        };

        golden.Should().ContainKey(writeGoldenKey);
        var withBlock = JsonNode.Parse(golden[writeGoldenKey])!.AsObject();
        withBlock["scheduleConventions"] = JsonNode.Parse(SecurityAssetTermsSchemaRoundTripTests.ReadableTwinBlocks[assetClass][0]);
        var row = JsonNode.Parse(SecurityTermsEnvelope.StampFlat(withBlock.ToJsonString(), assetClass, stamp))!.AsObject();
        row["callSchedule"] = JsonNode.Parse(callSchedule);
        return (assetClass, row.ToJsonString());
    }

    private static string? LocateWriteGolden()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "fixtures", "security-master", "terms-golden", WriteGoldenFileName);
        if (File.Exists(beside))
        {
            return beside;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "security-master", "terms-golden", WriteGoldenFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// A structured-credit-io-po envelope carrying a populated scheduleConventions both inside
    /// profileFields and at its root, as a profile may legitimately declare.
    /// </summary>
    private static string ProfileEnvelope(string tranche)
        => $$$"""{"customProfileId":"structured-credit-io-po","profileVersion":1,"profileFields":{"tranche":"{{{tranche}}}","poolId":"POOL-1","currentFactor":0.8,"originalFace":1000000,"couponOrIndex":"SOFR","factorSchedule":"trustee-report","collateralType":"CLO","scheduleConventions":{"businessDayConvention":"ModifiedFollowing","paymentCalendars":["USNY"],"stubPolicy":"Short"}},"profileApproval":{"approvedBy":"Meridian","approvedAtUtc":"2026-05-29T00:00:00Z","approvalReference":"MERIDIAN-SEED-APPROVAL"},"scheduleConventions":{"businessDayConvention":"Following","paymentCalendars":["GBLO","USNY"],"rollConvention":"EndOfMonth"}}""";

    /// <summary>
    /// <see cref="ProfileEnvelope"/> stamped at <paramref name="stamp"/>, with a populated callSchedule
    /// and putSchedule both at its root and inside profileFields, every entry carrying the raw JSON
    /// <paramref name="exerciseFrequency"/>.
    /// </summary>
    private static string ProfileEnvelopeWithSchedules(int stamp, string exerciseFrequency)
    {
        JsonNode Calls() => JsonNode.Parse($$"""[{"callDate":"2028-06-15","exerciseStyle":"Bermudan","exerciseFrequency":{{exerciseFrequency}}}]""")!;
        JsonNode Puts() => JsonNode.Parse($$"""[{"putDate":"2029-06-15","putPrice":100,"exerciseFrequency":{{exerciseFrequency}}}]""")!;

        var envelope = JsonNode.Parse(ProfileEnvelope(tranche: "A-1"))!.AsObject();
        var profileFields = envelope["profileFields"]!.AsObject();
        profileFields["callSchedule"] = Calls();
        profileFields["putSchedule"] = Puts();
        envelope["callSchedule"] = Calls();
        envelope["putSchedule"] = Puts();
        envelope[SecurityTermsEnvelope.AssetClassTermsVersionKey] = stamp;
        return envelope.ToJsonString();
    }

    /// <summary>A create of <paramref name="envelope"/> as a StructuredCredit, with the identifiers its profile requires.</summary>
    private static CreateSecurityRequest ProfileEnvelopeCreateRequest(Guid securityId, string envelope)
        => new(
            securityId,
            "StructuredCredit",
            JsonSerializer.SerializeToElement(new { displayName = "IO strip with call and put schedules", currency = "USD" }),
            Parse(envelope),
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "88160R101", true, DateTimeOffset.UtcNow.AddDays(-30), null, null),
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "IOPO-SCHED", false, DateTimeOffset.UtcNow.AddDays(-30), null, null)
            ],
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "create");

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
