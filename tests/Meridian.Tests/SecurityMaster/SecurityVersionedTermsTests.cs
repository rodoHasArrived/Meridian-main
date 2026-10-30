using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using FSharpDomain = Meridian.FSharp.Domain;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The shared decode predicate (<see cref="SecurityVersionedTermDecoding"/>), the shared block reader and
/// the declared-conventions reads (<see cref="SecurityVersionedTerms"/>), and the read guard's refusal
/// texts (<see cref="SecurityTermsEnvelope.TryGetReadRefusal"/>). Increment 5a moves the mapping and the
/// schema validator onto them with no behaviour change, so the predicate and the reader are held to
/// verbatim copies of the code they replace.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityVersionedTermsTests
{
    // The members of a flat Bond row, without braces, so tests can place envelope keys and blocks around them.
    private const string BondBody =
        "\"maturity\":\"2030-01-01\",\"issueDate\":\"2024-01-01\",\"couponType\":\"Fixed\",\"couponRate\":4.25,\"dayCount\":\"30/360\"," +
        "\"isCallable\":false,\"issuerName\":\"ACME Corp\",\"par\":1000,\"paymentFrequency\":\"SemiAnnual\"";

    private const string CanonicalBlock =
        """{"businessDayConvention":"ModifiedFollowing","accrualAdjusted":true,"paymentCalendars":["USNY","GBLO"],"paymentLagBusinessDays":2,"rollConvention":"15","endOfMonthRule":false,"firstRegularDate":"2025-09-15","lastRegularDate":"2035-03-15","direction":"Backward","stubPolicy":"Short","couponRateUnit":"Percent","couponCashBasis":"DayCount"}""";

    private const string FixedLeg =
        """{"legId":"fixed","legType":"Fixed","currency":"USD","direction":"Pay","fixedRate":4.25,"notional":10000000,"paymentFrequency":"SemiAnnual","dayCount":"30/360","exchangesPrincipal":false}""";

    private const string FloatingLegBody =
        "\"legId\":\"float\",\"legType\":\"Floating\",\"currency\":\"USD\",\"direction\":\"Receive\",\"index\":\"SOFR\",\"spreadBps\":15," +
        "\"notional\":10000000,\"paymentFrequency\":\"Quarterly\",\"dayCount\":\"ACT/360\",\"exchangesPrincipal\":false";

    private static readonly int?[] Stamps = [null, 1, 2, 3, 4, 5];

    // ── (a) Oracle parity ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Decodes_PreservesHistoricalReads_WhileWriteAuthorityFollowsTheCurrentCeiling()
    {
        var mismatches = new List<string>();
        foreach (var (assetClass, path, field) in VersionedContainers())
        {
            foreach (var stamp in Stamps)
            {
                foreach (var (envelope, payload) in Envelopes(stamp))
                {
                    foreach (var write in new[] { false, true })
                    {
                        var expected = write
                            ? !SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, payload)
                              && field.SinceTermsVersion <= SecurityAssetClassCatalog.GetTermsVersion(assetClass)
                            : field.SinceTermsVersion <= OldDecodeThrough(assetClass, payload, write: false);
                        var actual = SecurityVersionedTerms.Decoding(assetClass, payload, write).Decodes(field);
                        if (actual != expected)
                        {
                            mismatches.Add($"{assetClass}.{path} stamp {stamp?.ToString(CultureInfo.InvariantCulture) ?? "absent"} {envelope} {(write ? "Write" : "Read")}: {actual}, was {expected}");
                        }
                    }
                }
            }
        }

        mismatches.Should().BeEmpty("S2 preserves historical own-stamp reads and raises only explicit write authority");
    }

    [Fact]
    public void TheVersionedContainers_AreTheScheduleBlocksCallPutArraysAndCalculationCompanions()
    {
        VersionedContainers().Select(static container => $"{container.AssetClass}.{container.Path}").Should().BeEquivalentTo(new[]
        {
            "Bond.scheduleConventions", "Bond.callSchedule", "Bond.putSchedule", "Bond.cashFlowContract",
            "CertificateOfDeposit.scheduleConventions", "CertificateOfDeposit.callSchedule", "CertificateOfDeposit.putSchedule", "CertificateOfDeposit.cashFlowContract",
            "DirectLoan.cashFlowContract",
            "StructuredCredit.scheduleConventions", "StructuredCredit.callSchedule", "StructuredCredit.putSchedule", "StructuredCredit.cashFlowContract",
            "Swap.legs[].scheduleConventions", "Swap.cashFlowContract",
        });

        // Containers that predate per-class versioning, and later scalar terms, are not versioned containers.
        SecurityVersionedTerms.IsVersionedContainer(SecurityAssetTermsSchema.Field("Swap", "legs")!).Should().BeFalse();
        SecurityVersionedTerms.IsVersionedContainer(SecurityAssetTermsSchema.Field("StructuredCredit", "factorScheduleEntries")!).Should().BeFalse();
        SecurityVersionedTerms.IsVersionedContainer(SecurityAssetTermsSchema.Field("Bond", "principalSchedule")!).Should().BeFalse();
        SecurityAssetTermsSchema.Fields("StructuredCredit").Where(static field => field.SinceTermsVersion == 2)
            .Should().NotBeEmpty().And.OnlyContain(static field => !SecurityVersionedTerms.IsVersionedContainer(field));
    }

    // ── (b) Option B: a read decodes by the row's own stamp ─────────────────────────────────────

    [Fact]
    public void Decoding_ReadsByTheRowsOwnStamp_AndWritesByTheWriteVersion()
    {
        // The replaced ceiling read a StructuredCredit stamp-1 row through its write version 2.
        SecurityVersionedTerms.Decoding("StructuredCredit", Terms(1, "\"tranche\":\"A-1\""), writeMode: false)
            .Should().Be(new SecurityVersionedTermDecoding(ProfileGoverned: false, Through: 1));
        SecurityVersionedTerms.Decoding("Bond", Json("{" + BondBody + "}"), writeMode: false).Through.Should().Be(1);
        SecurityVersionedTerms.Decoding("Bond", Terms(3, BondBody), writeMode: false).Through.Should().Be(3);
        SecurityVersionedTerms.Decoding("Bond", Terms(3, BondBody), writeMode: true).Through.Should().Be(3);
        SecurityVersionedTerms.Decoding("StructuredCredit", Terms(1, "\"tranche\":\"A-1\""), writeMode: true).Through.Should().Be(4);

        SecurityVersionedTerms.Decoding("StructuredCredit", Json("""{"customProfileId":"structured-credit-v1","profileFields":{}}"""), writeMode: false)
            .ProfileGoverned.Should().BeTrue();
        SecurityVersionedTerms.Decoding("Bond", Json("""{"profileFields":{"issuerName":"ACME Corp"}}"""), writeMode: false)
            .ProfileGoverned.Should().BeFalse("a Bond does not support profile-backed terms");
    }

    // ── (c) Decodes on hand-built values ────────────────────────────────────────────────────────

    [Fact]
    public void Decodes_GatesOnlyVersionedContainers()
    {
        var flatThroughThree = new SecurityVersionedTermDecoding(ProfileGoverned: false, Through: 3);
        var envelopeThroughFour = new SecurityVersionedTermDecoding(ProfileGoverned: true, Through: 4);

        foreach (var (assetClass, path, field) in VersionedContainers())
        {
            flatThroughThree.Decodes(field).Should().Be(field.SinceTermsVersion <= 3, $"{assetClass}.{path} is Since {field.SinceTermsVersion}");
            envelopeThroughFour.Decodes(field).Should().BeFalse($"a profile envelope decodes no versioned container ({assetClass}.{path})");
        }

        foreach (var field in AllFields().Where(static field => !SecurityVersionedTerms.IsVersionedContainer(field)))
        {
            flatThroughThree.Decodes(field).Should().BeTrue();
            envelopeThroughFour.Decodes(field).Should().BeTrue();
        }
    }

    // ── (d) Reader parity ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("de-DE")]
    public void ReadScheduleConventions_MatchesTheMappingReaderItReplaces(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        try
        {
            foreach (var (name, key, raw) in BlockCorpus())
            {
                var payload = Json($$"""{"termsFormat":"flat","assetClassTermsVersion":2,{{BondBody}},{{JsonSerializer.Serialize(key)}}:{{raw}}}""");

                SecurityVersionedTerms.ReadScheduleConventions(payload, SecurityVersionedTerms.ScheduleConventionsKey)
                    .Should().BeEquivalentTo(
                        OracleRaw(payload, SecurityVersionedTerms.ScheduleConventionsKey),
                        options => options.ComparingByMembers<ScheduleConventionTerms>().WithStrictOrdering(),
                        $"'{name}' reads as the mapping's getters read it, blanks and untrimmed calendars kept");

                var decoded = (FSharpDomain.SecurityKind.Bond)SecurityMasterMapping.ToRecord(
                    SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", payload)).Kind;
                decoded.Item.ScheduleConventions.Should().Be(
                    OracleMapping(payload, SecurityVersionedTerms.ScheduleConventionsKey),
                    $"the mapping decodes '{name}' exactly as before increment 5a");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ReadScheduleConventions_ReadsANonObjectContainerAsEmpty()
    {
        SecurityVersionedTerms.ReadScheduleConventions(Json("[]"), SecurityVersionedTerms.ScheduleConventionsKey).IsEmpty.Should().BeTrue();
        SecurityVersionedTerms.ReadScheduleConventions(Json("null"), SecurityVersionedTerms.ScheduleConventionsKey).IsEmpty.Should().BeTrue();
        ScheduleConventionTerms.Empty.IsEmpty.Should().BeTrue();
        (ScheduleConventionTerms.Empty with { PaymentCalendars = ["USNY"] }).IsEmpty.Should().BeFalse();
        (ScheduleConventionTerms.Empty with { StubPolicy = "" }).IsEmpty.Should().BeFalse("the shared reader keeps a blank spelling");
    }

    // ── (e) Neutrality precondition ─────────────────────────────────────────────────────────────

    [Fact]
    public void EveryVersionedContainer_IsAboveItsClassesPreservedDocumentFloor()
    {
        // Why increment 5a is neutral: the replaced Read ceiling max(write, stamp) and the predicate's
        // stamp differ only for a container at or below the write version, and none is. The raise in
        // increment 5b-2 makes containers writable, and this precondition is retargeted there.
        foreach (var (assetClass, path, field) in VersionedContainers())
        {
            field.SinceTermsVersion.Should().BeGreaterThan(
                SecurityAssetClassCatalog.GetStampFloor(assetClass), $"{assetClass}.{path} remains above its document floor");
        }
    }

    // ── (f) ReadDeclaredScheduleConventions ─────────────────────────────────────────────────────

    [Fact]
    public void ReadDeclaredScheduleConventions_DeclaresNothing_WhereTheMappingDecodesNothing()
    {
        var none = new (string Name, string AssetClass, JsonElement Terms)[]
        {
            ("array payload", "Bond", Json("[]")),
            ("string payload", "Bond", Json("\"x\"")),
            ("null payload", "Bond", Json("null")),
            ("class without a root block", "Swap", Terms(2, $"\"effectiveDate\":\"2026-01-15\",\"maturityDate\":\"2031-01-15\",\"legs\":[],\"scheduleConventions\":{CanonicalBlock}")),
            ("class without any container", "Equity", Terms(2, $"\"scheduleConventions\":{CanonicalBlock}")),
            ("profile envelope at stamp 4", "StructuredCredit", Json($$"""{"schemaVersion":3,"termsFormat":"customProfile","assetClassTermsVersion":4,"customProfileId":"structured-credit-v1","profileVersion":1,"profileFields":{"tranche":"A-1"},"scheduleConventions":{{CanonicalBlock}}}""")),
            ("Bond stamp 1 with a block", "Bond", Terms(1, $"{BondBody},\"scheduleConventions\":{CanonicalBlock}")),
            ("unstamped Bond with a block", "Bond", Terms(null, $"{BondBody},\"scheduleConventions\":{CanonicalBlock}")),
            ("null block", "Bond", Terms(2, $"{BondBody},\"scheduleConventions\":null")),
            ("all-null block", "Bond", Terms(2, $"{BondBody},\"scheduleConventions\":{{\"stubPolicy\":null,\"paymentCalendars\":[\" \"]}}")),
            ("case-variant container key", "Bond", Terms(2, $"{BondBody},\"ScheduleConventions\":{CanonicalBlock}")),
            ("economic-format payload", "Bond", Json($$"""{"schemaVersion":2,"termsFormat":"economic","assetClassTermsVersion":9,"scheduleConventions":{{CanonicalBlock}}}""")),
        };

        foreach (var (name, assetClass, terms) in none)
        {
            var read = SecurityVersionedTerms.ReadDeclaredScheduleConventions(assetClass, terms);
            read.Status.Should().Be(SecurityDeclaredConventionsStatus.None, name);
            read.Block.IsEmpty.Should().BeTrue(name);
            read.UnreadableReason.Should().BeNull(name);
        }
    }

    [Fact]
    public void ReadDeclaredScheduleConventions_IsUnreadable_WithTheReadGuardsText()
    {
        var terms = Terms(5, $"{BondBody},\"scheduleConventions\":{CanonicalBlock}");

        var read = SecurityVersionedTerms.ReadDeclaredScheduleConventions("Bond", terms);

        read.Status.Should().Be(SecurityDeclaredConventionsStatus.Unreadable);
        read.Block.IsEmpty.Should().BeTrue();
        read.UnreadableReason.Should().Be("Unsupported assetClassTermsVersion '5' for asset class 'Bond'; this node reads versions 1 through 4.")
            .And.Be(ReadGuardMessage("Bond", terms));
    }

    [Fact]
    public void ReadDeclaredScheduleConventions_DeclaresTheBlock_TheMappingDecodes()
    {
        var declared = new (string Name, string AssetClass, JsonElement Terms)[]
        {
            ("Bond stamp 2", "Bond", Terms(2, $"{BondBody},\"scheduleConventions\":{CanonicalBlock}")),
            ("Bond stamp 3", "Bond", Terms(3, $"{BondBody},\"scheduleConventions\":{CanonicalBlock}")),
            // A Bond is never profile-governed, so an untagged customProfileId and profileFields
            // envelope (its format resolves to customProfile) decodes its root block by its own stamp.
            ("untagged Bond envelope stamp 2", "Bond", Terms(2, $"\"customProfileId\":\"bond-profile-v1\",\"profileFields\":{{\"issuerName\":\"ACME Corp\"}},{BondBody},\"scheduleConventions\":{CanonicalBlock}", format: null)),
            ("CertificateOfDeposit stamp 2", "CertificateOfDeposit", Terms(2, $"\"issuerName\":\"First Meridian Bank\",\"maturity\":\"2027-06-30\",\"scheduleConventions\":{CanonicalBlock}")),
            ("flat StructuredCredit stamp 3", "StructuredCredit", Terms(3, $"\"tranche\":\"A-1\",\"scheduleConventions\":{CanonicalBlock}")),
        };

        foreach (var (name, assetClass, terms) in declared)
        {
            var read = SecurityVersionedTerms.ReadDeclaredScheduleConventions(assetClass, terms);
            read.Status.Should().Be(SecurityDeclaredConventionsStatus.Declared, name);
            read.Block.Should().BeEquivalentTo(
                SecurityVersionedTerms.ReadScheduleConventions(terms, SecurityVersionedTerms.ScheduleConventionsKey),
                options => options.ComparingByMembers<ScheduleConventionTerms>().WithStrictOrdering(),
                name);
            read.Block.StubPolicy.Should().Be("Short", name);
            read.UnreadableReason.Should().BeNull(name);
            read.ElementBlocks.Should().BeEmpty(name);
        }

        // A blank spelling is declared: the shared reader keeps it so the engine can refuse it.
        SecurityVersionedTerms.ReadDeclaredScheduleConventions("Bond", Terms(2, $"{BondBody},\"scheduleConventions\":{{\"stubPolicy\":\"\"}}"))
            .Status.Should().Be(SecurityDeclaredConventionsStatus.Declared);
    }

    // ── (g) The leg reader ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReadDeclaredLegScheduleConventions_SelectsTheLegsTheMappingReads()
    {
        var shortLeg = FloatingLeg("""{"stubPolicy":"Short"}""");
        var longLeg = FloatingLeg("""{"stubPolicy":"Long"}""");

        var besideCashFlowLegs = SecurityVersionedTerms.ReadDeclaredLegScheduleConventions(
            "Swap", Swap(2, $"\"swapLegs\":[],\"cashFlowLegs\":[{FixedLeg},{shortLeg}]"));
        besideCashFlowLegs.Status.Should().Be(SecurityDeclaredConventionsStatus.Declared);
        besideCashFlowLegs.ElementBlocks.Select(static block => block.StubPolicy).Should().Equal(new string?[] { "Short" });
        besideCashFlowLegs.Block.IsEmpty.Should().BeTrue();

        var legsFirst = Swap(2, $"\"legs\":[{FixedLeg},{longLeg}],\"swapLegs\":[{shortLeg}]");
        SecurityVersionedTerms.ReadDeclaredLegScheduleConventions("Swap", legsFirst)
            .ElementBlocks.Select(static block => block.StubPolicy).Should().Equal(new string?[] { "Long" });
        var mapped = (FSharpDomain.SecurityKind.Swap)SecurityMasterMapping.ToRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Swap", legsFirst)).Kind;
        mapped.Item.Legs.Select(static leg => leg.ScheduleConventions.StubPolicy?.Value).Should().Equal(new string?[] { null, "Long" });

        // The mapping refuses a legs object, so the row declares nothing.
        SecurityVersionedTerms.ReadDeclaredLegScheduleConventions("Swap", Swap(2, $"\"legs\":{longLeg}"))
            .Status.Should().Be(SecurityDeclaredConventionsStatus.None);
        SecurityVersionedTerms.ReadDeclaredLegScheduleConventions("Swap", Swap(2, $"\"swapLegs\":[],\"cashFlowLegs\":null"))
            .Status.Should().Be(SecurityDeclaredConventionsStatus.None);

        // The refusal stops at the malformed key: a declaring alias after it is not read instead.
        var legsObjectFirst = Swap(2, $"\"legs\":{longLeg},\"swapLegs\":[{FixedLeg},{shortLeg}]");
        SecurityVersionedTerms.ReadDeclaredLegScheduleConventions("Swap", legsObjectFirst)
            .Status.Should().Be(SecurityDeclaredConventionsStatus.None);
        FluentActions.Invoking(() => SecurityMasterMapping.ToRecord(
                SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Swap", legsObjectFirst)))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Swap leg container 'legs' must be a JSON array.");

        // The container is found case-insensitively, as the mapping finds it.
        var caseVariantContainer = Swap(2, $"\"Legs\":[{FixedLeg},{longLeg}]");
        var caseVariant = SecurityVersionedTerms.ReadDeclaredLegScheduleConventions("Swap", caseVariantContainer);
        caseVariant.Status.Should().Be(SecurityDeclaredConventionsStatus.Declared);
        caseVariant.ElementBlocks.Select(static block => block.StubPolicy).Should().Equal(new string?[] { "Long" });
        var caseVariantMapped = (FSharpDomain.SecurityKind.Swap)SecurityMasterMapping.ToRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Swap", caseVariantContainer)).Kind;
        caseVariantMapped.Item.Legs.Select(static leg => leg.ScheduleConventions.StubPolicy?.Value).Should().Equal(new string?[] { null, "Long" });

        // Below the block's version, and in a leg that declares nothing, nothing is declared.
        SecurityVersionedTerms.ReadDeclaredLegScheduleConventions("Swap", Swap(1, $"\"legs\":[{FixedLeg},{longLeg}]"))
            .Status.Should().Be(SecurityDeclaredConventionsStatus.None);
        SecurityVersionedTerms.ReadDeclaredLegScheduleConventions("Swap", Swap(2, $"\"legs\":[{FixedLeg},{FloatingLeg("{}")}]"))
            .Status.Should().Be(SecurityDeclaredConventionsStatus.None);
        SecurityVersionedTerms.ReadDeclaredLegScheduleConventions("Bond", Terms(2, $"{BondBody},\"scheduleConventions\":{CanonicalBlock}"))
            .Status.Should().Be(SecurityDeclaredConventionsStatus.None, "a Bond has no array term with a leg block");

        var unreadable = Swap(4, $"\"legs\":[{FixedLeg},{longLeg}]");
        var read = SecurityVersionedTerms.ReadDeclaredLegScheduleConventions("Swap", unreadable);
        read.Status.Should().Be(SecurityDeclaredConventionsStatus.Unreadable);
        read.UnreadableReason.Should().Be(ReadGuardMessage("Swap", unreadable));
        read.ElementBlocks.Should().BeEmpty();
    }

    // ── (h) TryGetReadRefusal ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TryGetReadRefusal_GivesTheReadGuardsExactTexts()
    {
        var refused = new (string AssetClass, JsonElement Terms, string Reason)[]
        {
            ("Bond", Json("""{"schemaVersion":2,"termsFormat":"economic","assetClassTermsVersion":9}"""),
                "Terms payload for asset class 'Bond' declares termsFormat 'economic', which is not an asset-specific-terms format; " +
                "route it through SecurityAssetSpecificTermsUpcasterChain before reading."),
            ("Bond", Terms(5, BondBody), "Unsupported assetClassTermsVersion '5' for asset class 'Bond'; this node reads versions 1 through 4."),
            ("StructuredCredit", Terms(0, "\"tranche\":\"A-1\""), "Unsupported assetClassTermsVersion '0' for asset class 'StructuredCredit'; this node reads versions 1 through 5."),
            ("Swap", Terms(4, "\"legs\":[]"), "Unsupported assetClassTermsVersion '4' for asset class 'Swap'; this node reads versions 1 through 3."),
        };

        foreach (var (assetClass, terms, expected) in refused)
        {
            SecurityTermsEnvelope.TryGetReadRefusal(assetClass, terms, out var reason).Should().BeTrue();
            reason.Should().Be(expected).And.Be(ReadGuardMessage(assetClass, terms));
        }

        foreach (var (assetClass, terms) in new[] { ("Bond", Terms(3, BondBody)), ("Bond", Json("{" + BondBody + "}")), ("Bond", Json("[]")) })
        {
            SecurityTermsEnvelope.TryGetReadRefusal(assetClass, terms, out var reason).Should().BeFalse();
            reason.Should().BeNull();
            FluentActions.Invoking(() => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(assetClass, terms)).Should().NotThrow();
        }
    }

    // ── Oracles: verbatim copies of the code increment 5a replaces (f79ee152) ───────────────────

    // SecurityMasterMapping.ToSecurityKind's decode ceiling, which SchemaTermsAssetClassValidator.Validate
    // repeated for reads: a term was decoded when its Since was at or below it.
    private static int OldDecodeThrough(string assetClass, JsonElement json, bool write)
        => write || SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, json)
            ? SecurityAssetClassCatalog.GetStampFloor(assetClass)
            : Math.Max(SecurityAssetClassCatalog.GetStampFloor(assetClass), SecurityTermsEnvelope.ResolveAssetClassTermsVersion(json));

    // SecurityMasterMapping.ReadScheduleConventions (less its version gate, which is the caller's).
    private static FSharpDomain.ScheduleConventions OracleMapping(JsonElement container, string key)
    {
        if (GetOptionalObject(container, key) is not { } block)
        {
            return FSharpDomain.ScheduleConventionsModule.Empty;
        }

        return new FSharpDomain.ScheduleConventions(
            ToOption(GetOptionalString(block, "businessDayConvention")),
            ToOption(GetOptionalBoolean(block, "accrualAdjusted")),
            ListModule.OfSeq(GetOptionalStringArray(block, "paymentCalendars").Where(static calendar => !string.IsNullOrWhiteSpace(calendar))),
            ToOption(GetOptionalInt(block, "paymentLagBusinessDays")),
            ToOption(GetOptionalString(block, "rollConvention")),
            ToOption(GetOptionalBoolean(block, "endOfMonthRule")),
            ToOption(GetOptionalDateOnly(block, "firstRegularDate")),
            ToOption(GetOptionalDateOnly(block, "lastRegularDate")),
            ToOption(GetOptionalString(block, "direction")),
            ToOption(GetOptionalString(block, "stubPolicy")),
            ToOption(GetOptionalString(block, "couponRateUnit")),
            ToOption(GetOptionalString(block, "couponCashBasis")));
    }

    // The same reader before its conversion to the F# record: what each getter returned.
    private static ScheduleConventionTerms OracleRaw(JsonElement container, string key)
    {
        if (GetOptionalObject(container, key) is not { } block)
        {
            return ScheduleConventionTerms.Empty;
        }

        return new ScheduleConventionTerms(
            GetOptionalString(block, "businessDayConvention"),
            GetOptionalBoolean(block, "accrualAdjusted"),
            GetOptionalStringArray(block, "paymentCalendars").Where(static calendar => !string.IsNullOrWhiteSpace(calendar)).ToArray(),
            GetOptionalInt(block, "paymentLagBusinessDays"),
            GetOptionalString(block, "rollConvention"),
            GetOptionalBoolean(block, "endOfMonthRule"),
            GetOptionalDateOnly(block, "firstRegularDate"),
            GetOptionalDateOnly(block, "lastRegularDate"),
            GetOptionalString(block, "direction"),
            GetOptionalString(block, "stubPolicy"),
            GetOptionalString(block, "couponRateUnit"),
            GetOptionalString(block, "couponCashBasis"));
    }

    private static string? GetOptionalString(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetOptionalInt(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var intValue)
            ? intValue
            : null;

    private static bool? GetOptionalBoolean(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? value.GetBoolean()
            : null;

    private static DateOnly? GetOptionalDateOnly(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), out var date)
            ? date
            : null;

    private static IEnumerable<string> GetOptionalStringArray(JsonElement json, string propertyName)
    {
        if (json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    yield return item.GetString()!;
            }
        }
    }

    private static JsonElement? GetOptionalObject(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static FSharpOption<string> ToOption(string? value)
        => string.IsNullOrWhiteSpace(value) ? FSharpOption<string>.None : FSharpOption<string>.Some(value);

    private static FSharpOption<int> ToOption(int? value)
        => value.HasValue ? FSharpOption<int>.Some(value.Value) : FSharpOption<int>.None;

    private static FSharpOption<bool> ToOption(bool? value)
        => value.HasValue ? FSharpOption<bool>.Some(value.Value) : FSharpOption<bool>.None;

    private static FSharpOption<DateOnly> ToOption(DateOnly? value)
        => value.HasValue ? FSharpOption<DateOnly>.Some(value.Value) : FSharpOption<DateOnly>.None;

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────

    // Every versioned container: top-level fields, and fields inside a declared array element.
    private static IEnumerable<(string AssetClass, string Path, SecurityAssetTermField Field)> VersionedContainers()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses.OrderBy(static assetClass => assetClass, StringComparer.Ordinal))
        {
            foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
            {
                if (SecurityVersionedTerms.IsVersionedContainer(field))
                {
                    yield return (assetClass, field.Key, field);
                }

                if (field.Type == SecurityAssetTermFieldType.Array)
                {
                    foreach (var element in SecurityAssetTermsSchema.ElementFields(assetClass, field.Key)
                                 .Where(SecurityVersionedTerms.IsVersionedContainer))
                    {
                        yield return (assetClass, $"{field.Key}[].{element.Key}", element);
                    }
                }
            }
        }
    }

    private static IEnumerable<SecurityAssetTermField> AllFields()
        => SecurityAssetTermsSchema.AssetClasses.SelectMany(static assetClass => SecurityAssetTermsSchema.Fields(assetClass)
            .SelectMany(field => SecurityAssetTermsSchema.ElementFields(assetClass, field.Key).Prepend(field)));

    // The payload shapes that decide the decoding: a flat payload, the two profile envelope signals, and
    // the untagged customProfileId plus profileFields envelope (profile-governed only for a class with
    // profile-backed terms).
    private static IEnumerable<(string Name, JsonElement Payload)> Envelopes(int? stamp)
    {
        var version = stamp is { } value ? $",\"assetClassTermsVersion\":{value}" : string.Empty;
        yield return ("flat", Json($$"""{"termsFormat":"flat"{{version}}}"""));
        yield return ("customProfileId envelope", Json($$"""{"termsFormat":"customProfile","customProfileId":"profile-v1"{{version}}}"""));
        yield return ("profileFields envelope", Json($$"""{"profileFields":{"field":1}{{version}}}"""));
        yield return ("untagged envelope", Json($$"""{"customProfileId":"profile-v1","profileFields":{}{{version}}}"""));
    }

    // Block values (raw JSON under a container key), including spellings a JsonObject cannot hold.
    private static IEnumerable<(string Name, string Key, string Raw)> BlockCorpus()
    {
        const string key = SecurityVersionedTerms.ScheduleConventionsKey;
        yield return ("canonical", key, CanonicalBlock);
        yield return ("pascalCaseMembers", key, """{"BusinessDayConvention":"Following","PaymentCalendars":["USNY"],"StubPolicy":"Long"}""");
        yield return ("caseVariantMembersBesideExact", key, """{"businessDayConvention":"Following","BusinessDayConvention":"Preceding","PAYMENTCALENDARS":["GBLO"],"paymentCalendars":["USNY"]}""");
        yield return ("duplicateMember", key, """{"businessDayConvention":"Following","businessDayConvention":"Preceding"}""");
        yield return ("wrongKinds", key, """{"businessDayConvention":1,"accrualAdjusted":"true","paymentCalendars":"USNY","paymentLagBusinessDays":"2","rollConvention":15,"endOfMonthRule":0,"firstRegularDate":20250915,"lastRegularDate":["2035-03-15"],"direction":{"value":"Backward"},"stubPolicy":false,"couponRateUnit":["Percent"],"couponCashBasis":true}""");
        yield return ("lagFractional", key, """{"paymentLagBusinessDays":2.5}""");
        yield return ("lagBeyondInt32", key, """{"paymentLagBusinessDays":4294967296}""");
        yield return ("lagNegative", key, """{"paymentLagBusinessDays":-1}""");
        yield return ("blankStrings", key, """{"businessDayConvention":"","rollConvention":"","direction":"","stubPolicy":"","couponRateUnit":"","couponCashBasis":"","firstRegularDate":""}""");
        yield return ("whitespaceStrings", key, """{"businessDayConvention":"  \t","stubPolicy":" ","couponCashBasis":"\n"}""");
        yield return ("calendarsWhitespaceOnly", key, """{"paymentCalendars":[""," ","\t","\n"]}""");
        yield return ("calendarsNonString", key, """{"paymentCalendars":[1,true,null,{"id":"USNY"},["USNY"]]}""");
        yield return ("calendarsMixed", key, """{"paymentCalendars":["USNY"," ",7,null,"GBLO"]}""");
        yield return ("calendarsUntrimmed", key, """{"paymentCalendars":[" USNY ","GBLO\t"]}""");
        yield return ("calendarsDuplicatesAndCase", key, """{"paymentCalendars":["USNY","usny","USNY"]}""");
        yield return ("calendarsNull", key, """{"paymentCalendars":null}""");
        foreach (var (name, first, last) in new[]
                 {
                     ("iso", "2025-09-15", "2035-03-15"),
                     ("timestampUtc", "2025-09-15T00:00:00Z", "2035-03-15T00:00:00+00:00"),
                     ("monthFirstSlashes", "09/15/2025", "03/15/2035"),
                     ("dayFirstSlashes", "15/09/2025", "15/03/2035"),
                     ("dayFirstDots", "15.09.2025", "15.03.2035"),
                     ("ambiguousSlashes", "03/04/2030", "04/03/2031"),
                     ("surroundingWhitespace", " 2025-09-15 ", "2035-03-15\t"),
                 })
        {
            yield return ($"dates.{name}", key, $"{{\"firstRegularDate\":{JsonSerializer.Serialize(first)},\"lastRegularDate\":{JsonSerializer.Serialize(last)}}}");
        }

        yield return ("emptyObject", key, "{}");
        yield return ("null", key, "null");
        yield return ("string", key, "\"x\"");
        yield return ("emptyArray", key, "[]");
        yield return ("number", key, "7");
        yield return ("arrayOfBlock", key, "[" + CanonicalBlock + "]");
        yield return ("allNull", key, """{"businessDayConvention":null,"accrualAdjusted":null,"paymentCalendars":null,"stubPolicy":null}""");
        yield return ("unknownMembersOnly", key, """{"calendar":"USNY","bdc":"Following"}""");
        yield return ("caseVariantContainer", "ScheduleConventions", CanonicalBlock);
        yield return ("upperCaseContainer", "SCHEDULECONVENTIONS", CanonicalBlock);
    }

    private static string FloatingLeg(string block)
        => $"{{{FloatingLegBody},\"scheduleConventions\":{block}}}";

    private static JsonElement Swap(int stamp, string legContainers)
        => Terms(stamp, $"\"effectiveDate\":\"2026-01-15\",\"maturityDate\":\"2031-01-15\",{legContainers}");

    // Stored terms with the flat envelope first, as a writer stamps them; null leaves a key out.
    private static JsonElement Terms(int? stamp, string body, string? format = SecurityTermsFormats.Flat)
    {
        var members = new List<string>();
        if (format is not null)
        {
            members.Add($"\"termsFormat\":{JsonSerializer.Serialize(format)}");
        }

        if (stamp is { } version)
        {
            members.Add($"\"assetClassTermsVersion\":{version}");
        }

        members.Add(body);
        return Json("{" + string.Join(",", members) + "}");
    }

    private static string ReadGuardMessage(string assetClass, JsonElement terms)
        => FluentActions.Invoking(() => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(assetClass, terms))
            .Should().Throw<InvalidOperationException>().Which.Message;

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
