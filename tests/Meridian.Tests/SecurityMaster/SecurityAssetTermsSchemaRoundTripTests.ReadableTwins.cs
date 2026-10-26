using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The readable twins: for every class that reads a terms version above the one it writes
/// (ADR-024, readable before writable), the fully-populated payload as a node that WRITES the newer
/// version would store it. <see cref="FullPayloads"/> stays writable-only, so it cannot carry a term
/// the node only reads; the twins cover the read path for it instead, and never go through
/// <see cref="SecurityMasterMapping.ToCreateCommand"/>, which refuses them.
/// </summary>
public sealed partial class SecurityAssetTermsSchemaRoundTripTests
{
    private const string ScheduleConventionsKey = "scheduleConventions";

    /// <summary>
    /// The frozen canonical member order of a written scheduleConventions block (ADR-024): all twelve
    /// members, alphabetical, whatever order the stored row used.
    /// </summary>
    private static readonly string[] CanonicalScheduleConventionMembers =
    [
        "accrualAdjusted",
        "businessDayConvention",
        "couponCashBasis",
        "couponRateUnit",
        "direction",
        "endOfMonthRule",
        "firstRegularDate",
        "lastRegularDate",
        "paymentCalendars",
        "paymentLagBusinessDays",
        "rollConvention",
        "stubPolicy"
    ];

    /// <summary>
    /// A fully-populated scheduleConventions block per readable-ahead class: one for a class that
    /// declares it at the top level, one per leg for a swap. Every member is set, so a member either
    /// codec side drops fails the round trip. The certificate of deposit's block is deliberately out
    /// of canonical order.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ReadableTwinBlocks =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["Bond"] =
            [
                """{"accrualAdjusted":true,"businessDayConvention":"ModifiedFollowing","couponCashBasis":"DayCount","couponRateUnit":"Percent","direction":"Backward","endOfMonthRule":false,"firstRegularDate":"2025-09-15","lastRegularDate":"2035-03-15","paymentCalendars":["USNY","GBLO"],"paymentLagBusinessDays":2,"rollConvention":"15","stubPolicy":"Short"}"""
            ],
            ["CertificateOfDeposit"] =
            [
                """{"stubPolicy":"Long","rollConvention":"EndOfMonth","paymentLagBusinessDays":0,"paymentCalendars":["USNY"],"lastRegularDate":"2027-06-30","firstRegularDate":"2026-03-31","endOfMonthRule":true,"direction":"Forward","couponRateUnit":"Percent","couponCashBasis":"Level","businessDayConvention":"Following","accrualAdjusted":false}"""
            ],
            ["StructuredCredit"] =
            [
                """{"accrualAdjusted":true,"businessDayConvention":"ModifiedFollowing","couponCashBasis":"DayCount","couponRateUnit":"Fraction","direction":"Backward","endOfMonthRule":false,"firstRegularDate":"2026-07-15","lastRegularDate":"2031-04-15","paymentCalendars":["USNY","GBLO"],"paymentLagBusinessDays":2,"rollConvention":"15","stubPolicy":"Short"}"""
            ],
            ["Swap"] =
            [
                """{"accrualAdjusted":true,"businessDayConvention":"ModifiedFollowing","couponCashBasis":"DayCount","couponRateUnit":"Percent","direction":"Backward","endOfMonthRule":false,"firstRegularDate":"2026-07-15","lastRegularDate":"2030-07-15","paymentCalendars":["USNY"],"paymentLagBusinessDays":2,"rollConvention":"15","stubPolicy":"Short"}""",
                """{"accrualAdjusted":false,"businessDayConvention":"Following","couponCashBasis":"Level","couponRateUnit":"Fraction","direction":"Forward","endOfMonthRule":true,"firstRegularDate":"2026-04-15","lastRegularDate":"2030-10-15","paymentCalendars":["GBLO","USNY"],"paymentLagBusinessDays":1,"rollConvention":"Imm","stubPolicy":"Long"}"""
            ]
        };

    // The spellings of an unset block a write accepts and ignores (ADR-024): absent is covered by
    // FullPayloads itself.
    private static readonly string[] UnpopulatedScheduleConventionSpellings =
    [
        "null",
        "{}",
        "[]",
        """{"paymentCalendars":[]}""",
        """{"businessDayConvention":null}""",
        """{"accrualAdjusted":null,"businessDayConvention":null,"couponCashBasis":null,"couponRateUnit":null,"direction":null,"endOfMonthRule":null,"firstRegularDate":null,"lastRegularDate":null,"paymentCalendars":null,"paymentLagBusinessDays":null,"rollConvention":null,"stubPolicy":null}"""
    ];

    public static TheoryData<string> ReadableAheadAssetClasses()
    {
        var data = new TheoryData<string>();
        foreach (var assetClass in ReadableTwinBlocks.Keys)
        {
            data.Add(assetClass);
        }

        return data;
    }

    public static TheoryData<string, string> UnpopulatedScheduleConventionCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var assetClass in ReadableTwinBlocks.Keys)
        {
            foreach (var spelling in UnpopulatedScheduleConventionSpellings)
            {
                data.Add(assetClass, spelling);
            }
        }

        return data;
    }

    // Every stamp at or below the write version, and an unstamped row (null).
    public static TheoryData<string, int?> WritableStampCases()
    {
        var data = new TheoryData<string, int?>();
        foreach (var assetClass in ReadableTwinBlocks.Keys)
        {
            data.Add(assetClass, null);
            for (var version = 1; version <= SecurityAssetClassCatalog.GetTermsVersion(assetClass); version++)
            {
                data.Add(assetClass, version);
            }
        }

        return data;
    }

    // ── (a) Coverage ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReadableTwins_CoverExactlyTheClassesThatReadAheadOfWhatTheyWrite()
    {
        var readAhead = SecurityAssetClassCatalog.AssetClasses
            .Where(static assetClass => SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass) > SecurityAssetClassCatalog.GetTermsVersion(assetClass));

        ReadableTwinBlocks.Keys.Should().BeEquivalentTo(
            readAhead,
            "every class that reads a terms version it does not write needs a readable twin, and a twin for a class " +
            "that reads only what it writes would be refused on read");
    }

    [Theory]
    [MemberData(nameof(ReadableAheadAssetClasses))]
    public void ReadableTwin_PopulatesEveryTermAboveTheWriteVersion(string assetClass)
    {
        // A later expand step that adds another term to the class has to grow the twin with it. The
        // check is by path, so a swap twin must populate the block on every leg, not just one.
        var writes = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
        var twin = ReadableTwin(assetClass);
        var expected = new List<string>();
        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
        {
            if (field.SinceTermsVersion > writes)
            {
                expected.Add(field.Key);
                continue;
            }

            var inner = SecurityAssetTermsSchema.ElementFields(assetClass, field.Key).Where(inner => inner.SinceTermsVersion > writes).ToArray();
            if (inner.Length == 0)
            {
                continue;
            }

            if (field.Type == SecurityAssetTermFieldType.Array)
            {
                for (var index = 0; index < twin.GetProperty(field.Key).GetArrayLength(); index++)
                {
                    expected.AddRange(inner.Select(innerField => $"{field.Key}[{index}].{innerField.Key}"));
                }
            }
            else
            {
                expected.AddRange(inner.Select(innerField => $"{field.Key}.{innerField.Key}"));
            }
        }

        expected.Should().NotBeEmpty();
        if (SecurityAssetTermsSchema.Field(assetClass, ScheduleConventionsKey) is null)
        {
            expected.Should().HaveCount(ReadableTwinBlocks[assetClass].Count, "a swap twin declares the block once per leg");
        }

        SecurityAssetTermsSchema.PopulatedTermsAbove(assetClass, twin, writes)
            .Select(static term => term.Path)
            .Should().BeEquivalentTo(expected, $"the '{assetClass}' readable twin must populate every term the node reads but does not write, on every container");
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(twin).Should().Be(SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass));
    }

    [Theory]
    [MemberData(nameof(ReadableAheadAssetClasses))]
    public void ReadableTwinBlocks_SetEveryMemberToAPopulatedValue(string assetClass)
    {
        // The round trip (b) compares order-insensitively and the writer emits an unset member as null
        // (the calendars as []), so a twin member left null or empty would hide a member either codec
        // side drops. Every block therefore sets all twelve members to a populated value.
        var blocks = ReadableTwinBlocks[assetClass];
        blocks.Should().NotBeEmpty();
        for (var index = 0; index < blocks.Count; index++)
        {
            var members = JsonNode.Parse(blocks[index])!.AsObject();
            var label = $"the '{assetClass}' twin block {index}";

            members.Select(static member => member.Key)
                .Should().BeEquivalentTo(CanonicalScheduleConventionMembers, $"{label} must name every declared member exactly once");
            foreach (var (name, value) in members)
            {
                value.Should().NotBeNull($"{label} must set '{name}'");
                SecurityAssetTermsSchema.IsPopulatedTermValue(JsonSerializer.SerializeToElement(value))
                    .Should().BeTrue($"{label} must populate '{name}'");
                if (value!.GetValueKind() == JsonValueKind.String)
                {
                    value.GetValue<string>().Should().NotBeNullOrWhiteSpace($"{label} must set '{name}' to a non-blank value");
                }
            }

            var calendars = members["paymentCalendars"].Should().BeOfType<JsonArray>().Subject;
            calendars.Should().NotBeEmpty($"{label} must carry at least one payment calendar");
            calendars.Should().OnlyContain(
                calendar => calendar != null
                    && calendar.GetValueKind() == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(calendar.GetValue<string>()),
                $"{label} must list its payment calendars as non-blank ids");
        }
    }

    // ── (b) Read-path round trip ────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(ReadableAheadAssetClasses))]
    public void ReadableTwin_RoundTripsByteStableThroughTheReadPath_AtItsReadableVersion(string assetClass)
    {
        var twin = ReadableTwin(assetClass);

        var firstPass = ReadAndRewrite(assetClass, twin);
        var secondPass = ReadAndRewrite(assetClass, Parse(firstPass));

        secondPass.Should().Be(firstPass, $"the '{assetClass}' read path (read → write → read → write) must be lossless");

        using var written = JsonDocument.Parse(firstPass);
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(written.RootElement)
            .Should().Be(SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass), "the block keeps the stamp it was read at");
        JsonNode.DeepEquals(JsonNode.Parse(firstPass), JsonNode.Parse(twin.GetRawText())).Should().BeTrue(
            $"the rewritten '{assetClass}' terms must carry every value of the stored twin.{Environment.NewLine}" +
            $"stored:    {twin.GetRawText()}{Environment.NewLine}rewritten: {firstPass}");

        var expectedBlocks = ReadableTwinBlocks[assetClass];
        var writtenBlocks = ScheduleConventionBlocks(assetClass, written.RootElement);
        writtenBlocks.Should().HaveCount(expectedBlocks.Count);
        for (var index = 0; index < expectedBlocks.Count; index++)
        {
            var block = writtenBlocks[index];
            block.Value.Should().NotBeNull($"{block.Path} must survive the read path");
            JsonNode.DeepEquals(JsonNode.Parse(block.Value!.Value.GetRawText()), JsonNode.Parse(expectedBlocks[index])).Should().BeTrue(
                $"{block.Path} must come back with the stored members.{Environment.NewLine}" +
                $"stored:    {expectedBlocks[index]}{Environment.NewLine}rewritten: {block.Value.Value.GetRawText()}");
        }
    }

    // ── (c) Written field set and canonical block form ──────────────────────────────────────────

    [Theory]
    [MemberData(nameof(ReadableAheadAssetClasses))]
    public void ReadableTwin_WritesExactlyTheFieldsItsVersionDeclares_AndTheCanonicalBlock(string assetClass)
    {
        var reads = SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
        var writes = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
        using var written = JsonDocument.Parse(ReadAndRewrite(assetClass, ReadableTwin(assetClass)));

        var writtenKeys = ElementKeys(written.RootElement);
        writtenKeys.RemoveWhere(SecurityTermsEnvelope.IsReservedKey);
        writtenKeys.Should().BeEquivalentTo(
            SecurityAssetTermsSchema.Fields(assetClass).Where(field => field.SinceTermsVersion <= reads).Select(static field => field.Key),
            $"a '{assetClass}' row read at version {reads} is rewritten with exactly the fields version {reads} declares");

        foreach (var container in SecurityAssetTermsSchema.Fields(assetClass).Where(static field => field.Type == SecurityAssetTermFieldType.Array))
        {
            var elementFields = SecurityAssetTermsSchema.ElementFields(assetClass, container.Key);
            if (!elementFields.Any(field => field.SinceTermsVersion > writes))
            {
                continue;
            }

            var declared = elementFields.Where(field => field.SinceTermsVersion <= reads).Select(static field => field.Key).ToArray();
            var index = 0;
            foreach (var element in written.RootElement.GetProperty(container.Key).EnumerateArray())
            {
                ElementKeys(element).Should().BeEquivalentTo(
                    declared,
                    $"every '{assetClass}.{container.Key}[{index}]' element carries the version-{reads} element fields, the block included");
                index++;
            }
        }

        var blockPath = SecurityAssetTermsSchema.Field(assetClass, ScheduleConventionsKey) is null
            ? $"legs.{ScheduleConventionsKey}"
            : ScheduleConventionsKey;
        SecurityAssetTermsSchema.ElementFields(assetClass, blockPath).Select(static field => field.Key).Order(StringComparer.Ordinal)
            .Should().Equal(CanonicalScheduleConventionMembers, "the frozen block form lists exactly the declared members");

        var blocks = ScheduleConventionBlocks(assetClass, written.RootElement);
        blocks.Should().NotBeEmpty();
        foreach (var (path, block) in blocks)
        {
            block.Should().NotBeNull($"{path} is declared");
            block!.Value.EnumerateObject().Select(static member => member.Name)
                .Should().Equal(CanonicalScheduleConventionMembers, $"{path} is written in the frozen canonical form: all twelve members, alphabetical");
        }
    }

    [Fact]
    public void ReadableSwap_WithOneDeclaringLeg_WritesTheKeyOnEveryLeg_NullOnThePlainLeg()
    {
        var stored = Parse(
            """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":2,"effectiveDate":"2026-01-15","maturityDate":"2031-01-15","legs":[{"legType":"Fixed","currency":"USD","fixedRate":0.04,"exchangesPrincipal":false},{"legType":"Floating","currency":"USD","index":"SOFR","exchangesPrincipal":false,"scheduleConventions":""" +
            ReadableTwinBlocks["Swap"][1] + "}]}");

        var written = ReadAndRewrite("Swap", stored);

        written.Should().Be(
            """{"effectiveDate":"2026-01-15","legs":[{"currency":"USD","currentIndexRate":null,"dayCount":null,"direction":null,"exchangesPrincipal":false,"fixedRate":0.04,"index":null,"legId":null,"legType":"Fixed","notional":null,"notionalSchedule":[],"paymentFrequency":null,"resetSchedule":[],"scheduleConventions":null,"spreadBps":null},{"currency":"USD","currentIndexRate":null,"dayCount":null,"direction":null,"exchangesPrincipal":false,"fixedRate":null,"index":"SOFR","legId":null,"legType":"Floating","notional":null,"notionalSchedule":[],"paymentFrequency":null,"resetSchedule":[],"scheduleConventions":{"accrualAdjusted":false,"businessDayConvention":"Following","couponCashBasis":"Level","couponRateUnit":"Fraction","direction":"Forward","endOfMonthRule":true,"firstRegularDate":"2026-04-15","lastRegularDate":"2030-10-15","paymentCalendars":["GBLO","USNY"],"paymentLagBusinessDays":1,"rollConvention":"Imm","stubPolicy":"Long"},"spreadBps":null}],"maturityDate":"2031-01-15","schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":2}""",
            "from the block's version upward every leg carries the key, null on a leg that declares nothing (ADR-024's frozen form)");
        ReadAndRewrite("Swap", Parse(written)).Should().Be(written);
    }

    // ── (d) The write path refuses the twins ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Bond", 1, 2)]
    [InlineData("CertificateOfDeposit", 1, 2)]
    [InlineData("StructuredCredit", 2, 3)]
    [InlineData("Swap", 1, 2)]
    public void ReadableTwin_IsRefusedByCreateAndAmend_WithTheWriteVersionReason(string assetClass, int writes, int reads)
    {
        var twin = ReadableTwin(assetClass);
        var expected =
            $"assetClassTermsVersion '{reads}' for asset class '{assetClass}' is readable but not writable on this node: " +
            $"it reads versions 1 through {reads} and writes version {writes}. " +
            $"Submit the terms at version {writes}, or apply the change from a node that writes version {reads}.";

        FluentActions.Invoking(() => SecurityMasterMapping.ToCreateCommand(CreateRequest(assetClass, twin)))
            .Should().Throw<InvalidOperationException>().WithMessage(expected);
        FluentActions.Invoking(() => SecurityMasterMapping.ToAmendCommand(AmendRequest(twin), CurrentProjection(assetClass)))
            .Should().Throw<InvalidOperationException>().WithMessage(expected);
    }

    [Theory]
    [InlineData("Bond", 1, 2, "scheduleConventions")]
    [InlineData("CertificateOfDeposit", 1, 2, "scheduleConventions")]
    [InlineData("StructuredCredit", 2, 3, "scheduleConventions")]
    [InlineData("Swap", 1, 2, "legs[0].scheduleConventions")]
    public void ReadableTwin_RestampedAtTheWriteVersion_IsRefusedByCreateAndAmend_WithTheNotYetWritableReason(
        string assetClass,
        int writes,
        int since,
        string path)
    {
        // Dropping the stamp to the write version does not make the block writable: a write that
        // populates it is refused on the term itself, at the first populated path.
        var restamped = Parse(SecurityTermsEnvelope.StampFlat(ReadableTwin(assetClass).GetRawText(), assetClass));
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(restamped).Should().Be(writes);
        var expected =
            $"'{path}' is an asset-class terms version {since} term of '{assetClass}'. This node reads it but writes " +
            $"assetClassTermsVersion {writes}, so a create, amend, import or field edit cannot set it yet. " +
            "Leave it absent, null or empty (ADR-024).";

        FluentActions.Invoking(() => SecurityMasterMapping.ToCreateCommand(CreateRequest(assetClass, restamped)))
            .Should().Throw<InvalidOperationException>().WithMessage(expected);
        FluentActions.Invoking(() => SecurityMasterMapping.ToAmendCommand(AmendRequest(restamped), CurrentProjection(assetClass)))
            .Should().Throw<InvalidOperationException>().WithMessage(expected);
    }

    // ── (e) Unpopulated spellings are accepted and write the golden bytes ───────────────────────

    [Theory]
    [MemberData(nameof(UnpopulatedScheduleConventionCases))]
    public void UnpopulatedScheduleConventions_AreAcceptedOnWrite_AndWriteTheGoldenBytes(string assetClass, string spelling)
    {
        var golden = SerializeThroughDomain(assetClass, FullPayloads[assetClass]).GetRawText();
        var payload = JsonNode.Parse(JsonSerializer.Serialize(FullPayloads[assetClass]))!.AsObject();
        foreach (var container in BlockContainers(assetClass, payload))
        {
            container[ScheduleConventionsKey] = JsonNode.Parse(spelling);
        }

        var snapshot = CreateSnapshot(assetClass, JsonSerializer.SerializeToElement(payload));

        snapshot.AssetSpecificTermsJson.Should().Be(
            golden,
            $"an unset block ({spelling}) means the term is unset, so the '{assetClass}' write is byte-identical to the one without it");
    }

    // ── (f) A payload stamped at or below the write version never decodes the block ─────────────

    [Theory]
    [MemberData(nameof(WritableStampCases))]
    public void StrayBlock_OnAPayloadStampedAtOrBelowTheWriteVersion_IsNotDecoded(string assetClass, int? stamp)
    {
        // The block is decoded only from a payload stamped at or above the version that declares it;
        // a stray one on an older or unstamped row is ignored, so the row reads and rewrites as before.
        var golden = SerializeThroughDomain(assetClass, FullPayloads[assetClass]).GetRawText();
        var stored = JsonNode.Parse(ReadableTwin(assetClass).GetRawText())!.AsObject();
        if (stamp is { } version)
        {
            stored[SecurityTermsEnvelope.AssetClassTermsVersionKey] = version;
        }
        else
        {
            stored.Remove(SecurityTermsEnvelope.FormatKey);
            stored.Remove(SecurityTermsEnvelope.AssetClassTermsVersionKey);
        }

        var record = SecurityMasterMapping.ToRecord(LegacyProjection(assetClass, JsonSerializer.SerializeToElement(stored)));

        DecodedScheduleConventions(record.Kind).Should().NotBeEmpty()
            .And.OnlyContain(conventions => ScheduleConventionsModule.IsEmpty(conventions), "a block above the payload's stamp is not decoded");
        new SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson.Should().Be(
            golden,
            $"the '{assetClass}' row stamped {stamp?.ToString() ?? "nothing"} rewrites exactly as a write of the same terms without the block");
    }

    // ── Foreign-shaped stored rows: exactly what this node persists, stamp included ─────────────

    // Rows a node that writes the newer version may leave in the store, each with the bytes this node
    // persists for it.
    private const string ForeignPartialBondBlockAtStampTwo =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":2,"maturity":"2030-01-01","couponType":"ZeroCoupon","isCallable":false,"subclass":"Sovereign","scheduleConventions":{"stubPolicy":"Long"}}""";

    private const string ForeignPartialBondBlockPersisted =
        """{"callDate":null,"capRate":null,"couponRate":null,"couponType":"ZeroCoupon","dayCount":null,"floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":null,"issuerName":null,"legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":null,"paymentFrequency":null,"preRefundDate":null,"principalSchedule":[],"scheduleConventions":{"accrualAdjusted":null,"businessDayConvention":null,"couponCashBasis":null,"couponRateUnit":null,"direction":null,"endOfMonthRule":null,"firstRegularDate":null,"lastRegularDate":null,"paymentCalendars":[],"paymentLagBusinessDays":null,"rollConvention":null,"stubPolicy":"Long"},"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":2,"seniority":null,"spreadBps":null,"stepSchedule":[],"subclass":"Sovereign"}""";

    private const string ForeignNullBondBlockAtStampTwo =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":2,"maturity":"2030-01-01","couponType":"ZeroCoupon","isCallable":false,"subclass":"Sovereign","scheduleConventions":null}""";

    private const string ForeignNullBondBlockPersisted =
        """{"callDate":null,"capRate":null,"couponRate":null,"couponType":"ZeroCoupon","dayCount":null,"floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":null,"issuerName":null,"legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":null,"paymentFrequency":null,"preRefundDate":null,"principalSchedule":[],"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1,"seniority":null,"spreadBps":null,"stepSchedule":[],"subclass":"Sovereign"}""";

    private const string ForeignSwapWithoutLegBlocksAtStampTwo =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":2,"effectiveDate":"2026-01-15","maturityDate":"2031-01-15","legs":[{"legType":"Fixed","currency":"USD","fixedRate":0.04,"exchangesPrincipal":false},{"legType":"Floating","currency":"USD","index":"SOFR","exchangesPrincipal":false}]}""";

    private const string ForeignSwapWithoutLegBlocksPersisted =
        """{"effectiveDate":"2026-01-15","legs":[{"currency":"USD","currentIndexRate":null,"dayCount":null,"direction":null,"exchangesPrincipal":false,"fixedRate":0.04,"index":null,"legId":null,"legType":"Fixed","notional":null,"notionalSchedule":[],"paymentFrequency":null,"resetSchedule":[],"spreadBps":null},{"currency":"USD","currentIndexRate":null,"dayCount":null,"direction":null,"exchangesPrincipal":false,"fixedRate":null,"index":"SOFR","legId":null,"legType":"Floating","notional":null,"notionalSchedule":[],"paymentFrequency":null,"resetSchedule":[],"spreadBps":null}],"maturityDate":"2031-01-15","schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":1}""";

    private const string Deactivate = "deactivate";
    private const string AmendWithoutTermsPatch = "amend-without-terms-patch";

    private static readonly IReadOnlyDictionary<string, (string AssetClass, string Stored, string Persisted)> ForeignRows =
        new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
        {
            ["partialBondBlock"] = ("Bond", ForeignPartialBondBlockAtStampTwo, ForeignPartialBondBlockPersisted),
            ["nullBondBlock"] = ("Bond", ForeignNullBondBlockAtStampTwo, ForeignNullBondBlockPersisted),
            ["swapWithoutLegBlocks"] = ("Swap", ForeignSwapWithoutLegBlocksAtStampTwo, ForeignSwapWithoutLegBlocksPersisted)
        };

    public static TheoryData<string, string> ForeignRowsAndPersistOperations()
    {
        var data = new TheoryData<string, string>();
        foreach (var row in ForeignRows.Keys)
        {
            data.Add(row, Deactivate);
            data.Add(row, AmendWithoutTermsPatch);
        }

        return data;
    }

    [Fact]
    public void ForeignPartialBlock_AtStampTwo_IsWrittenWithEveryMember_AndKeepsTheStamp()
    {
        var written = ReadAndRewrite("Bond", Parse(ForeignPartialBondBlockAtStampTwo));

        written.Should().Be(
            ForeignPartialBondBlockPersisted,
            "a partial block is written in the frozen canonical form: the eleven unset members as null (the calendars as []), at the stamp it was read at");
        ReadAndRewrite("Bond", Parse(written)).Should().Be(written);
    }

    [Fact]
    public void ForeignNullBlock_AtStampTwo_IsWrittenAtTheWriteVersion_WithoutTheKey()
    {
        ReadAndRewrite("Bond", Parse(ForeignNullBondBlockAtStampTwo)).Should().Be(
            ForeignNullBondBlockPersisted,
            "a row that declares nothing is rewritten at the write version; this lossless downgrade is intended (ADR-024)");
    }

    [Fact]
    public void ForeignSwap_AtStampTwo_WithoutLegBlocks_IsWrittenAtTheWriteVersion_WithoutTheKeyOnAnyLeg()
    {
        ReadAndRewrite("Swap", Parse(ForeignSwapWithoutLegBlocksAtStampTwo)).Should().Be(
            ForeignSwapWithoutLegBlocksPersisted,
            "a swap whose legs declare nothing is rewritten at the write version, with no block key on any leg");
    }

    [Theory]
    [MemberData(nameof(ForeignRowsAndPersistOperations))]
    public async Task ForeignRow_IsPersistedByDeactivateAndPatchlessAmend_WithExactlyThePinnedBytes(string row, string operation)
    {
        // The read path above is what a deactivation or a patch-less amend re-serializes, but the
        // service persists it only when its ahead-of-write round-trip guard finds every versioned term
        // intact: the partial block must compare equal to its twelve-member rewrite (eleven missing
        // members against ten nulls and []), and a block-less row must downgrade cleanly.
        var (assetClass, stored, persisted) = ForeignRows[row];
        var harness = new PersistPathHarness(assetClass, stored);

        await harness.RunAsync(operation);

        harness.Upserted.Should().ContainSingle().Which.AssetSpecificTerms.GetRawText().Should().Be(
            persisted,
            $"the '{row}' row persists exactly the bytes its read path writes, stamp included");
        var economic = SecurityMasterMapping.FromEventEnvelope(harness.Appended.Should().ContainSingle().Subject);
        economic.LegacyAssetSpecificTerms.Should().NotBeNull();
        JsonNode.DeepEquals(JsonNode.Parse(economic.LegacyAssetSpecificTerms!.Value.GetRawText()), JsonNode.Parse(persisted))
            .Should().BeTrue("the appended event retains the flat terms the projection persists");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The readable twin of <paramref name="assetClass"/>: its <see cref="FullPayloads"/> entry written
    /// through the domain, with the fully-populated block of <see cref="ReadableTwinBlocks"/> added and
    /// the payload stamped at <see cref="SecurityAssetClassCatalog.GetReadsTermsThrough"/>.
    /// </summary>
    internal static JsonElement ReadableTwin(string assetClass)
        => ReadableTwin(assetClass, FullPayloads[assetClass], ReadableTwinBlocks[assetClass]);

    /// <summary>
    /// A readable twin of <paramref name="payload"/> carrying <paramref name="blocks"/>: one block for a
    /// class that declares it at the top level, one per leg (in order) for a swap.
    /// </summary>
    internal static JsonElement ReadableTwin(string assetClass, object payload, IReadOnlyList<string> blocks)
    {
        var terms = JsonNode.Parse(SerializeThroughDomain(assetClass, payload).GetRawText())!.AsObject();
        var containers = BlockContainers(assetClass, terms);
        containers.Should().HaveCount(blocks.Count, $"the '{assetClass}' twin needs one block per block container");
        for (var index = 0; index < blocks.Count; index++)
        {
            containers[index][ScheduleConventionsKey] = JsonNode.Parse(blocks[index]);
        }

        return Parse(SecurityTermsEnvelope.StampFlat(
            terms.ToJsonString(),
            assetClass,
            SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass)));
    }

    // The objects that carry the block: the terms themselves, or each swap leg.
    private static IReadOnlyList<JsonObject> BlockContainers(string assetClass, JsonObject terms)
        => SecurityAssetTermsSchema.Field(assetClass, ScheduleConventionsKey) is null
            ? terms["legs"]!.AsArray().Select(static leg => leg!.AsObject()).ToArray()
            : [terms];

    private static IReadOnlyList<(string Path, JsonElement? Value)> ScheduleConventionBlocks(string assetClass, JsonElement terms)
    {
        if (SecurityAssetTermsSchema.Field(assetClass, ScheduleConventionsKey) is not null)
        {
            return [(ScheduleConventionsKey, terms.TryGetProperty(ScheduleConventionsKey, out var block) ? block : (JsonElement?)null)];
        }

        return terms.GetProperty("legs").EnumerateArray()
            .Select((leg, index) => ($"legs[{index}].{ScheduleConventionsKey}", leg.TryGetProperty(ScheduleConventionsKey, out var block) ? block : (JsonElement?)null))
            .ToArray();
    }

    private static IReadOnlyList<ScheduleConventions> DecodedScheduleConventions(SecurityKind kind)
        => kind switch
        {
            SecurityKind.Bond bond => [bond.Item.ScheduleConventions],
            SecurityKind.CertificateOfDeposit certificate => [certificate.Item.ScheduleConventions],
            SecurityKind.StructuredCredit structuredCredit => [structuredCredit.Item.ScheduleConventions],
            SecurityKind.Swap swap => swap.Item.Legs.Select(static leg => leg.ScheduleConventions).ToArray(),
            _ => []
        };

    /// <summary>
    /// The READ path a node takes for a stored row: the projection straight into Read-mode
    /// <see cref="SecurityMasterMapping.ToRecord"/>, then the F# writer, as a deactivation or a
    /// patch-less amend re-serializes it.
    /// </summary>
    private static string ReadAndRewrite(string assetClass, JsonElement storedTerms)
        => new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(LegacyProjection(assetClass, storedTerms))).AssetSpecificTermsJson;

    private static SecurityProjectionRecord CurrentProjection(string assetClass)
        => LegacyProjection(assetClass, SerializeThroughDomain(assetClass, FullPayloads[assetClass]));

    private static AmendSecurityTermsRequest AmendRequest(JsonElement assetSpecificTermsPatch)
        => new(
            SecurityId: Guid.NewGuid(),
            ExpectedVersion: 1,
            CommonTerms: null,
            AssetSpecificTermsPatch: assetSpecificTermsPatch,
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            SourceSystem: "schema-round-trip-tests",
            UpdatedBy: "schema-round-trip-tests",
            SourceRecordId: null,
            Reason: "SecurityAssetTermsSchema readable-twin guard");

    private static JsonElement Parse(string json)
        => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>
    /// The service persist path for one stored row: a <see cref="SecurityMasterService"/> over
    /// substitute stores whose projection holds exactly the stored terms. With no events and no
    /// snapshot the rebuilder falls back to that projection, so the stored document reaches the
    /// round-trip guards verbatim. Every appended event and upserted projection is recorded.
    /// </summary>
    private sealed class PersistPathHarness
    {
        private static readonly DateTimeOffset EffectiveAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly SecurityMasterService _service;
        private readonly SecurityProjectionRecord _seeded;

        public PersistPathHarness(string assetClass, string storedTerms)
        {
            _seeded = LegacyProjection(assetClass, Parse(storedTerms));

            var eventStore = Substitute.For<ISecurityMasterEventStore>();
            eventStore
                .AppendAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Appended.AddRange(call.ArgAt<IReadOnlyList<SecurityMasterEventEnvelope>>(2));
                    return Task.CompletedTask;
                });

            var store = Substitute.For<ISecurityMasterStore>();
            store.GetProjectionAsync(_seeded.SecurityId, Arg.Any<CancellationToken>()).Returns(_seeded);
            store.UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var persisted = call.Arg<SecurityProjectionRecord>();
                    Upserted.Add(persisted);
                    store.GetProjectionAsync(persisted.SecurityId, Arg.Any<CancellationToken>()).Returns(persisted);
                    return Task.CompletedTask;
                });

            var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
            _service = new SecurityMasterService(
                eventStore,
                snapshotStore,
                store,
                new SecurityMasterAggregateRebuilder(eventStore, snapshotStore),
                new SecurityMasterOptions
                {
                    SnapshotIntervalVersions = 50,
                    ResolveInactiveByDefault = true
                },
                NullLogger<SecurityMasterService>.Instance);
        }

        public List<SecurityMasterEventEnvelope> Appended { get; } = [];

        public List<SecurityProjectionRecord> Upserted { get; } = [];

        public Task RunAsync(string operation)
            => operation switch
            {
                Deactivate => _service.DeactivateAsync(new DeactivateSecurityRequest(
                    _seeded.SecurityId,
                    _seeded.Version,
                    EffectiveAt,
                    "schema-round-trip-tests",
                    "schema-round-trip-tests",
                    null,
                    "deactivate a foreign-shaped row")),
                AmendWithoutTermsPatch => _service.AmendTermsAsync(new AmendSecurityTermsRequest(
                    SecurityId: _seeded.SecurityId,
                    ExpectedVersion: _seeded.Version,
                    CommonTerms: null,
                    AssetSpecificTermsPatch: null,
                    IdentifiersToAdd:
                    [
                        new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "FOREIGN-ROW", false, EffectiveAt)
                    ],
                    IdentifiersToExpire: [],
                    EffectiveFrom: EffectiveAt,
                    SourceSystem: "schema-round-trip-tests",
                    UpdatedBy: "schema-round-trip-tests",
                    SourceRecordId: null,
                    Reason: "add an identifier to a foreign-shaped row")),
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };
    }
}
