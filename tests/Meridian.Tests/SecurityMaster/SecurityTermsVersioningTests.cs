using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Guards the terms-payload versioning contract: every payload declares its family
/// (<see cref="SecurityTermsEnvelope.FormatKey"/>) and flat payloads their asset-class terms
/// version; readers dispatch on the declaration rather than the shared <c>schemaVersion</c>
/// integer; a node reads stamps through its readable version but writes only its write version,
/// refusing on write any term it only reads (ADR-024, readable before writable); the economic → flat
/// bridge never loses data; and every security event carries a payload family and version that
/// replay checks before decoding.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityTermsVersioningTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    // ── Envelope stamping and format resolution ─────────────────────────────────────────────────

    [Fact]
    public void StampFlat_WritesFormatAndVersionAfterSchemaVersion_AndIsIdempotent()
    {
        var once = SecurityTermsEnvelope.StampFlat("""{"schemaVersion":1,"maturity":"2030-01-15"}""", "Bond");
        var twice = SecurityTermsEnvelope.StampFlat(once, "Bond");

        twice.Should().Be(once);
        Json(once).EnumerateObject().Select(static property => property.Name).Should().Equal(
            "schemaVersion",
            SecurityTermsEnvelope.FormatKey,
            SecurityTermsEnvelope.AssetClassTermsVersionKey,
            "maturity");
        Json(once).GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32()
            .Should().Be(SecurityAssetClassCatalog.GetOrDefault("Bond").TermsVersion);
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"maturity":"2030-01-15"}""")]
    [InlineData("""{"maturity":"2030-01-15","issuerName":"ACME"}""")]
    [InlineData("""{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":5,"maturity":"2030-01-15"}""")]
    [InlineData("[]")]
    public void StampFlat_ByAssetClass_IsTheExplicitVersionOverload_ByteForByte(string json)
    {
        SecurityTermsEnvelope.StampFlat(json, "Bond").Should().Be(SecurityTermsEnvelope.StampFlat(json, 1));
        SecurityTermsEnvelope.StampFlat(json, "StructuredCredit").Should().Be(SecurityTermsEnvelope.StampFlat(json, 2));
        SecurityTermsEnvelope.StampFlat(json, "Nope").Should().Be(SecurityTermsEnvelope.StampFlat(json, 1));
    }

    [Fact]
    public void StampFlat_ExplicitVersion_PlacesTheEnvelopeAfterSchemaVersionOrLast_AndRefusesVersionZero()
    {
        Json(SecurityTermsEnvelope.StampFlat("""{"maturity":"2030-01-15","schemaVersion":1,"issuerName":"ACME"}""", 3))
            .EnumerateObject().Select(static property => property.Name).Should().Equal(
                "maturity",
                "schemaVersion",
                SecurityTermsEnvelope.FormatKey,
                SecurityTermsEnvelope.AssetClassTermsVersionKey,
                "issuerName");
        Json(SecurityTermsEnvelope.StampFlat("""{"maturity":"2030-01-15"}""", 3))
            .EnumerateObject().Select(static property => property.Name).Should().Equal(
                "maturity",
                SecurityTermsEnvelope.FormatKey,
                SecurityTermsEnvelope.AssetClassTermsVersionKey);
        SecurityTermsEnvelope.StampFlat("[]", 3).Should().Be("[]");

        var zero = () => SecurityTermsEnvelope.StampFlat("{}", 0);
        zero.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ResolveFormat_TaggedFlatPayloadStampedTwo_IsFlat_NotEconomic()
    {
        // The historical trap: a flat payload stamped schemaVersion 2 that also carries a key named
        // like an economic module was indistinguishable from an economic document. A declared format
        // settles it.
        var payload = Json($$"""
            {"schemaVersion":2,"{{SecurityTermsEnvelope.FormatKey}}":"flat","maturity":"2030-01-15"}
            """);

        SecurityTermsEnvelope.ResolveFormat(payload).Should().Be(SecurityTermsFormats.Flat);
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(payload).Should().BeFalse();
        SecurityAssetSpecificTermsUpcasterChain.Normalize(payload, "Bond").Payload
            .GetProperty("maturity").GetString().Should().Be("2030-01-15", "a tagged flat payload must never be routed through the economic bridge");
    }

    [Fact]
    public void ResolveFormat_TaggedEconomicDocument_IsEconomicWhateverItsSchemaVersion()
    {
        var payload = Json($$$"""
            {"schemaVersion":7,"{{{SecurityTermsEnvelope.FormatKey}}}":"economic","maturity":{"maturityDate":"2030-01-15"}}
            """);

        SecurityTermsEnvelope.ResolveFormat(payload).Should().Be(SecurityTermsFormats.Economic);
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.IsEconomicTermsDocument(payload).Should().BeTrue();
    }

    [Fact]
    public void ResolveFormat_UntaggedPayloads_FallBackToTheHistoricalShapeRules()
    {
        SecurityTermsEnvelope.ResolveFormat(Json("""{"schemaVersion":1,"maturity":"2030-01-15"}"""))
            .Should().Be(SecurityTermsFormats.Flat);
        SecurityTermsEnvelope.ResolveFormat(Json("""{"schemaVersion":2,"coupon":{"couponRate":4.5}}"""))
            .Should().Be(SecurityTermsFormats.Economic);
        SecurityTermsEnvelope.ResolveFormat(Json("""{"schemaVersion":3,"customProfileId":"p","profileFields":{}}"""))
            .Should().Be(SecurityTermsFormats.CustomProfile);
    }

    [Fact]
    public void EnsureReadable_RefusesANewerAssetClassTermsVersion()
    {
        var supported = SecurityAssetClassCatalog.GetReadsTermsThrough("Bond");
        var payload = Json($$"""
            {"schemaVersion":1,"{{SecurityTermsEnvelope.FormatKey}}":"flat","{{SecurityTermsEnvelope.AssetClassTermsVersionKey}}":{{supported + 1}}}
            """);

        var read = () => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms("Bond", payload);

        read.Should().Throw<InvalidOperationException>().WithMessage($"*{SecurityTermsEnvelope.AssetClassTermsVersionKey}*");
    }

    [Fact]
    public void EnsureReadable_RefusesAnEconomicDocumentInTheAssetSpecificSlot()
    {
        var payload = Json($$"""{"schemaVersion":2,"{{SecurityTermsEnvelope.FormatKey}}":"economic"}""");

        var read = () => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms("Bond", payload);

        read.Should().Throw<InvalidOperationException>().WithMessage("*not an asset-specific-terms format*");
    }

    [Theory]
    [InlineData("economic")]
    [InlineData("foo")]
    public void EnsureWritable_RefusesANonFlatFormatWithWhatToSubmit(string format)
    {
        // A write's reason reaches the caller, so it must not name the server's read-side conversion.
        var payload = Json($$"""{"schemaVersion":2,"{{SecurityTermsEnvelope.FormatKey}}":"{{format}}"}""");

        var write = () => SecurityTermsEnvelope.EnsureWritableAsAssetSpecificTerms("Bond", payload);

        write.Should().Throw<InvalidOperationException>().WithMessage(
            $"Terms for asset class 'Bond' declare termsFormat '{format}', which a write does not accept. " +
            "Submit asset-specific terms: omit termsFormat, or set it to 'flat' or 'customProfile'.");
    }

    [Fact]
    public void EveryCatalogClass_HasAPositiveTermsVersion()
    {
        SecurityAssetClassCatalog.All.Should().OnlyContain(static descriptor => descriptor.TermsVersion >= 1);
    }

    // ── Readable before writable (ADR-024, expand step) ─────────────────────────────────────────

    // A flat payload: schemaVersion 1, the flat envelope when a stamp is given, then the members.
    private static JsonElement Terms(int? stamp, string members)
        => Json(
            "{\"schemaVersion\":1," +
            (stamp is { } version
                ? $"\"{SecurityTermsEnvelope.FormatKey}\":\"flat\",\"{SecurityTermsEnvelope.AssetClassTermsVersionKey}\":{version},"
                : string.Empty) +
            members.Trim() +
            "}");

    private const string Maturity = "\"maturity\":\"2030-01-15\"";

    private static string ReadableButNotWritable(string assetClass, int stamp, int reads, int writes, int? needs = null)
        => $"assetClassTermsVersion '{stamp}' for asset class '{assetClass}' is readable but not writable on this node: " +
           $"it reads versions 1 through {reads} and writes version {writes}. " +
           $"Submit the terms at version {writes}, or apply the change from a node that writes version {needs ?? stamp}.";

    private static string NotYetWritable(string assetClass, string path, int since, int writes)
        => $"'{path}' is an asset-class terms version {since} term of '{assetClass}'. This node reads it but writes " +
           $"assetClassTermsVersion {writes}, so a create, amend, import or field edit cannot set it yet. " +
           "Leave it absent, null or empty (ADR-024).";

    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    [Theory]
    [InlineData("Bond", 1, 3)]
    [InlineData("CertificateOfDeposit", 1, 3)]
    [InlineData("Swap", 1, 2)]
    [InlineData("StructuredCredit", 2, 4)]
    [InlineData("Equity", 1, 1)]
    [InlineData("Option", 1, 1)]
    public void EnsureReadable_AcceptsEveryStampThroughWhatTheClassReads_AndRefusesTheNext(string assetClass, int writes, int reads)
    {
        SecurityAssetClassCatalog.GetTermsVersion(assetClass).Should().Be(writes);
        SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass).Should().Be(reads);

        for (var version = 1; version <= reads; version++)
        {
            var payload = Terms(version, Maturity);
            var read = () => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(assetClass, payload);
            read.Should().NotThrow($"{assetClass} reads version {version}");
        }

        // Equity and Option read only what they write, so their version 2 is refused here.
        var next = () => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(assetClass, Terms(reads + 1, Maturity));
        next.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(
            $"Unsupported assetClassTermsVersion '{reads + 1}' for asset class '{assetClass}'; this node reads versions 1 through {reads}.");
    }

    [Theory]
    [InlineData("Bond", 1)]
    [InlineData("Bond", 2)]
    [InlineData("Bond", 3)]
    [InlineData("CertificateOfDeposit", 1)]
    [InlineData("CertificateOfDeposit", 2)]
    [InlineData("CertificateOfDeposit", 3)]
    [InlineData("Swap", 2)]
    [InlineData("StructuredCredit", 2)]
    [InlineData("StructuredCredit", 3)]
    [InlineData("StructuredCredit", 4)]
    [InlineData("Equity", 1)]
    public void StampFlat_AtAVersionBetweenTheWriteAndReadVersions_IsTheExplicitVersionOverload_ByteForByte(string assetClass, int version)
    {
        foreach (var json in new[]
                 {
                     """{"schemaVersion":1,"maturity":"2030-01-15"}""",
                     """{"maturity":"2030-01-15","issuerName":"ACME"}""",
                     """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":5,"maturity":"2030-01-15"}""",
                     "[]",
                 })
        {
            SecurityTermsEnvelope.StampFlat(json, assetClass, version).Should().Be(SecurityTermsEnvelope.StampFlat(json, version));
        }
    }

    [Theory]
    [InlineData("Bond", 4, 1, 3)]
    [InlineData("StructuredCredit", 1, 2, 4)]
    [InlineData("StructuredCredit", 5, 2, 4)]
    [InlineData("Equity", 2, 1, 1)]
    public void StampFlat_OutsideTheWriteToReadRange_IsRefused(string assetClass, int version, int writes, int reads)
    {
        var stamp = () => SecurityTermsEnvelope.StampFlat("""{"schemaVersion":1}""", assetClass, version);

        var refused = stamp.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage($"Asset class '{assetClass}' writes assetClassTermsVersion {writes} and reads versions 1 through {reads}.*")
            .Which;
        refused.ParamName.Should().Be("assetClassTermsVersion");
        refused.ActualValue.Should().Be(version);
    }

    [Fact]
    public void DescribeNotYetWritable_NamesThePathTheVersionsAndTheWayOut()
    {
        SecurityTermsEnvelope.DescribeNotYetWritable("Bond", "scheduleConventions", 2).Should().Be(
            "'scheduleConventions' is an asset-class terms version 2 term of 'Bond'. This node reads it but writes " +
            "assetClassTermsVersion 1, so a create, amend, import or field edit cannot set it yet. Leave it absent, null or empty (ADR-024).");
        SecurityTermsEnvelope.DescribeNotYetWritable("Swap", "legs[1].scheduleConventions", 2)
            .Should().Be(NotYetWritable("Swap", "legs[1].scheduleConventions", 2, 1));
        SecurityTermsEnvelope.DescribeNotYetWritable("StructuredCredit", "scheduleConventions", 3)
            .Should().Be(NotYetWritable("StructuredCredit", "scheduleConventions", 3, 2));
        SecurityTermsEnvelope.DescribeNotYetWritable("Bond", "callSchedule", 3).Should().Be(
            "'callSchedule' is an asset-class terms version 3 term of 'Bond'. This node reads it but writes " +
            "assetClassTermsVersion 1, so a create, amend, import or field edit cannot set it yet. Leave it absent, null or empty (ADR-024).");
        SecurityTermsEnvelope.DescribeNotYetWritable("CertificateOfDeposit", "putSchedule", 3)
            .Should().Be(NotYetWritable("CertificateOfDeposit", "putSchedule", 3, 1));
        SecurityTermsEnvelope.DescribeNotYetWritable("StructuredCredit", "callSchedule", 4)
            .Should().Be(NotYetWritable("StructuredCredit", "callSchedule", 4, 2));
    }

    private const string SwapDates = "\"effectiveDate\":\"2026-01-15\",\"maturityDate\":\"2031-01-15\"";

    // A StructuredCredit profile envelope carrying populated call and put schedules at its root and in
    // its profileFields, each entry with an exercise cadence no coupon-frequency convention accepts.
    private const string ProfileCallAndPutSchedules =
        """ "customProfileId":"structured-credit-profile","callSchedule":[{"callDate":"2030-06-15","exerciseFrequency":"Fortnightly"}],"putSchedule":[{"putDate":"2029-06-15","exerciseFrequency":3}],"profileFields":{"tranche":"A-1","callSchedule":[{"callDate":"2030-06-15","exerciseFrequency":3}],"putSchedule":[{"putDate":"2029-06-15","exerciseFrequency":"Fortnightly"}]} """;

    public static TheoryData<string, string, string> RefusedWrites()
    {
        var data = new TheoryData<string, string, string>
        {
            // Stamped above the write version: refused whatever the payload carries.
            { "Bond", Terms(2, Maturity).GetRawText(), ReadableButNotWritable("Bond", 2, 3, 1) },
            { "CertificateOfDeposit", Terms(2, Maturity).GetRawText(), ReadableButNotWritable("CertificateOfDeposit", 2, 3, 1) },
            { "Swap", Terms(2, SwapDates).GetRawText(), ReadableButNotWritable("Swap", 2, 2, 1) },
            { "StructuredCredit", Terms(3, Maturity).GetRawText(), ReadableButNotWritable("StructuredCredit", 3, 4, 2) },
            {
                "Bond",
                Terms(2, Maturity + """ ,"scheduleConventions":{"stubPolicy":"Long"} """).GetRawText(),
                ReadableButNotWritable("Bond", 2, 3, 1)
            },

            // The call and put schedule version: readable, so refused by stamp before any term is walked.
            { "Bond", Terms(3, Maturity).GetRawText(), ReadableButNotWritable("Bond", 3, 3, 1) },
            { "CertificateOfDeposit", Terms(3, Maturity).GetRawText(), ReadableButNotWritable("CertificateOfDeposit", 3, 3, 1) },
            { "StructuredCredit", Terms(4, Maturity).GetRawText(), ReadableButNotWritable("StructuredCredit", 4, 4, 2) },
            {
                "Bond",
                Terms(3, Maturity + """ ,"callSchedule":[{"callDate":"2030-06-15"}] """).GetRawText(),
                ReadableButNotWritable("Bond", 3, 3, 1)
            },

            // At or below the write version, a populated block is refused; false, 0 and "" are values.
            {
                "Bond",
                Terms(null, Maturity + """ ,"scheduleConventions":{"accrualAdjusted":false} """).GetRawText(),
                NotYetWritable("Bond", "scheduleConventions", 2, 1)
            },
            {
                "Bond",
                Terms(1, Maturity + """ ,"scheduleConventions":{"paymentLagBusinessDays":0} """).GetRawText(),
                NotYetWritable("Bond", "scheduleConventions", 2, 1)
            },
            {
                "Bond",
                Terms(1, Maturity + """ ,"scheduleConventions":{"stubPolicy":""} """).GetRawText(),
                NotYetWritable("Bond", "scheduleConventions", 2, 1)
            },
            {
                "Bond",
                Terms(null, Maturity + """ ,"scheduleConventions":{"paymentCalendars":[null,"USNY"],"rollConvention":null} """).GetRawText(),
                NotYetWritable("Bond", "scheduleConventions", 2, 1)
            },
            {
                "Bond",
                Terms(null, Maturity + """ ,"scheduleConventions":"x" """).GetRawText(),
                NotYetWritable("Bond", "scheduleConventions", 2, 1)
            },
            {
                "CertificateOfDeposit",
                Terms(null, Maturity + """ ,"scheduleConventions":{"businessDayConvention":"Following"} """).GetRawText(),
                NotYetWritable("CertificateOfDeposit", "scheduleConventions", 2, 1)
            },
            {
                "StructuredCredit",
                Terms(2, """ "tranche":"A-1","scheduleConventions":{"direction":"Backward"} """).GetRawText(),
                NotYetWritable("StructuredCredit", "scheduleConventions", 3, 2)
            },

            // A populated call or put schedule, at or below the write version.
            {
                "Bond",
                Terms(null, Maturity + """ ,"callSchedule":[{"callDate":"2030-06-15"}] """).GetRawText(),
                NotYetWritable("Bond", "callSchedule", 3, 1)
            },
            {
                "CertificateOfDeposit",
                Terms(1, Maturity + """ ,"putSchedule":[{"putDate":"2029-06-15"}] """).GetRawText(),
                NotYetWritable("CertificateOfDeposit", "putSchedule", 3, 1)
            },
            {
                "StructuredCredit",
                Terms(2, """ "tranche":"A-1","callSchedule":[{"callDate":"2030-06-15"}] """).GetRawText(),
                NotYetWritable("StructuredCredit", "callSchedule", 4, 2)
            },

            // A schedule's kind is not checked here: a string or a bare object is a populated value.
            {
                "Bond",
                Terms(null, Maturity + """ ,"callSchedule":"x" """).GetRawText(),
                NotYetWritable("Bond", "callSchedule", 3, 1)
            },
            {
                "Bond",
                Terms(1, Maturity + """ ,"callSchedule":{"callDate":"2030-06-15"} """).GetRawText(),
                NotYetWritable("Bond", "callSchedule", 3, 1)
            },

            // Both versions populated: the reason names the first readable-only term in declared order,
            // whatever order the payload spells them in.
            {
                "Bond",
                Terms(1, Maturity + """ ,"callSchedule":[{"callDate":"2030-06-15"}],"scheduleConventions":{"stubPolicy":"Long"} """).GetRawText(),
                NotYetWritable("Bond", "scheduleConventions", 2, 1)
            },

            // The key matches case-insensitively, as the readers match it; the reason names the spelling.
            {
                "Bond",
                Terms(null, Maturity + """ ,"ScheduleConventions":{"stubPolicy":"Long"} """).GetRawText(),
                NotYetWritable("Bond", "ScheduleConventions", 2, 1)
            },
            {
                "Bond",
                Terms(1, Maturity + """ ,"scheduleConventions":null,"SCHEDULECONVENTIONS":{"rollConvention":"EOM"} """).GetRawText(),
                NotYetWritable("Bond", "SCHEDULECONVENTIONS", 2, 1)
            },
            {
                "Bond",
                Terms(null, Maturity + """ ,"CallSchedule":[{"callDate":"2030-06-15"}] """).GetRawText(),
                NotYetWritable("Bond", "CallSchedule", 3, 1)
            },
            {
                "CertificateOfDeposit",
                Terms(1, Maturity + """ ,"putSchedule":[],"PUTSCHEDULE":[{"putDate":"2029-06-15"}] """).GetRawText(),
                NotYetWritable("CertificateOfDeposit", "PUTSCHEDULE", 3, 1)
            },

            // A swap carries the block per leg, under the legs container or either alias.
            {
                "Swap",
                Terms(1, SwapDates + """ ,"legs":[{"legType":"Fixed","scheduleConventions":{}},{"legType":"Floating","ScheduleConventions":{"direction":"Backward"}}] """).GetRawText(),
                NotYetWritable("Swap", "legs[1].ScheduleConventions", 2, 1)
            },

            // A Bond is not profile-capable, so a profileFields object does not shelter its root block.
            {
                "Bond",
                Terms(null, Maturity + """ ,"profileFields":{},"scheduleConventions":{"rollConvention":"EOM"} """).GetRawText(),
                NotYetWritable("Bond", "scheduleConventions", 2, 1)
            },
            {
                "Bond",
                Terms(null, Maturity + """ ,"profileFields":{},"callSchedule":[{"callDate":"2030-06-15"}] """).GetRawText(),
                NotYetWritable("Bond", "callSchedule", 3, 1)
            },

            // A profile envelope keeps the stamp check: it is the one versioned check it gets.
            {
                "StructuredCredit",
                Terms(3, """ "customProfileId":"structured-credit-profile","profileFields":{"tranche":"A-1"} """).GetRawText(),
                ReadableButNotWritable("StructuredCredit", 3, 4, 2)
            },
            { "StructuredCredit", Terms(3, ProfileCallAndPutSchedules).GetRawText(), ReadableButNotWritable("StructuredCredit", 3, 4, 2) },
            { "StructuredCredit", Terms(4, ProfileCallAndPutSchedules).GetRawText(), ReadableButNotWritable("StructuredCredit", 4, 4, 2) },

            // A flat payload that populates a term introduced above its own stamp is still refused by
            // its stamp, but only a node that writes that term's version can apply it.
            {
                "Bond",
                Terms(2, Maturity + """ ,"callSchedule":[{"callDate":"2030-06-15"}] """).GetRawText(),
                ReadableButNotWritable("Bond", 2, 3, 1, needs: 3)
            },
            {
                "CertificateOfDeposit",
                Terms(2, Maturity + """ ,"scheduleConventions":{"rollConvention":"EOM"},"PutSchedule":[{"putDate":"2029-06-15"}] """).GetRawText(),
                ReadableButNotWritable("CertificateOfDeposit", 2, 3, 1, needs: 3)
            },
            {
                "StructuredCredit",
                Terms(3, """ "putSchedule":[{"putDate":"2029-06-15"}] """).GetRawText(),
                ReadableButNotWritable("StructuredCredit", 3, 4, 2, needs: 4)
            },
            {
                "Bond",
                Terms(2, Maturity + """ ,"callSchedule":[],"putSchedule":[{}] """).GetRawText(),
                ReadableButNotWritable("Bond", 2, 3, 1)
            },
        };

        // An entry populated by any one member, without a date, is a populated schedule; false, 0 and ""
        // are values.
        foreach (var entry in new[]
                 {
                     """{"minimumNoticeDays":0}""",
                     """{"partialRedemption":"WholeOnly"}""",
                     """{"exerciseStyle":""}""",
                     """{"callPrice":101}""",
                 })
        {
            data.Add(
                "Bond",
                Terms(null, Maturity + $$""" ,"callSchedule":[{{entry}}] """).GetRawText(),
                NotYetWritable("Bond", "callSchedule", 3, 1));
        }

        foreach (var container in new[] { "legs", "swapLegs", "cashFlowLegs" })
        {
            data.Add(
                "Swap",
                Terms(null, SwapDates + $$$""" ,"{{{container}}}":[{"legType":"Fixed","currency":"USD"},{"legType":"Floating","currency":"USD","scheduleConventions":{"rollConvention":"IMM"}}] """).GetRawText(),
                NotYetWritable("Swap", $"{container}[1].scheduleConventions", 2, 1));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RefusedWrites))]
    public void TryGetWriteRefusal_RefusesWhatThisNodeReadsButCannotWrite_WithAPreciseReason(string assetClass, string payloadText, string expected)
    {
        var payload = Json(payloadText);

        SecurityTermsEnvelope.TryGetWriteRefusal(assetClass, payload, out var reason).Should().BeTrue();
        reason.Should().Be(expected);

        var read = () => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(assetClass, payload);
        read.Should().NotThrow("a payload refused on write is still readable");
        var write = () => SecurityTermsEnvelope.EnsureWritableAsAssetSpecificTerms(assetClass, payload);
        write.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(expected);
    }

    public static TheoryData<string, string> AcceptedWrites() => new()
    {
        // Absent, null, [], {} and all-null objects all mean the block is unset.
        { "Bond", Terms(null, Maturity).GetRawText() },
        { "Bond", Terms(1, Maturity).GetRawText() },
        { "Bond", Terms(1, Maturity + """ ,"scheduleConventions":null """).GetRawText() },
        { "Bond", Terms(null, Maturity + """ ,"scheduleConventions":{} """).GetRawText() },
        { "Bond", Terms(null, Maturity + """ ,"scheduleConventions":[] """).GetRawText() },
        { "Bond", Terms(1, Maturity + """ ,"scheduleConventions":{"paymentCalendars":[]} """).GetRawText() },
        { "Bond", Terms(1, Maturity + """ ,"scheduleConventions":{"paymentCalendars":[null]} """).GetRawText() },
        {
            "Bond",
            Terms(1, Maturity + """ ,"scheduleConventions":{"businessDayConvention":null,"accrualAdjusted":null,"paymentCalendars":null,"rollConvention":null,"stubPolicy":null} """).GetRawText()
        },
        { "Bond", Terms(null, Maturity + """ ,"ScheduleConventions":{"stubPolicy":null} """).GetRawText() },
        { "CertificateOfDeposit", Terms(1, Maturity + """ ,"scheduleConventions":{} """).GetRawText() },
        { "StructuredCredit", Terms(2, """ "tranche":"A-1","seniority":"Senior","scheduleConventions":{"paymentCalendars":[]} """).GetRawText() },
        { "Swap", Terms(1, SwapDates + """ ,"legs":[{"legType":"Fixed","scheduleConventions":null},{"legType":"Floating","scheduleConventions":{}}] """).GetRawText() },
        { "Swap", Terms(1, SwapDates + """ ,"swapLegs":[null,{"legType":"Fixed","scheduleConventions":{"paymentCalendars":[null]}}] """).GetRawText() },
        { "Bond", "[]" },

        // The call and put schedules: null, [], null entries, empty entries and all-null entries are unset.
        { "Bond", Terms(1, Maturity + """ ,"callSchedule":null,"putSchedule":null """).GetRawText() },
        { "Bond", Terms(null, Maturity + """ ,"callSchedule":[],"putSchedule":[] """).GetRawText() },
        { "Bond", Terms(1, Maturity + """ ,"callSchedule":[null],"putSchedule":[null,null] """).GetRawText() },
        { "Bond", Terms(null, Maturity + """ ,"callSchedule":[{}],"putSchedule":[{},null] """).GetRawText() },
        {
            "Bond",
            Terms(1, Maturity + """ ,"callSchedule":[{"callDate":null,"callEndDate":null,"exerciseStyle":null,"exerciseFrequency":null,"callPrice":null,"priceBasis":null,"priceType":null,"makeWholeSpreadBps":null,"partialRedemption":null,"minimumNoticeDays":null,"maximumNoticeDays":null,"noticeDayType":null}],"putSchedule":[{"putDate":null,"putEndDate":null,"exerciseStyle":null,"exerciseFrequency":null,"putPrice":null,"priceBasis":null,"minimumNoticeDays":null,"maximumNoticeDays":null,"noticeDayType":null}] """).GetRawText()
        },
        { "Bond", Terms(null, Maturity + """ ,"callSchedule":[{"exerciseFrequency":null}],"putSchedule":[{"exerciseFrequency":null}] """).GetRawText() },
        { "Bond", Terms(null, Maturity + """ ,"CallSchedule":[],"PUTSCHEDULE":[{"putDate":null}] """).GetRawText() },
        { "CertificateOfDeposit", Terms(1, Maturity + """ ,"callSchedule":[{}],"putSchedule":null """).GetRawText() },
        { "StructuredCredit", Terms(2, """ "tranche":"A-1","callSchedule":[],"putSchedule":[{"exerciseFrequency":null}] """).GetRawText() },

        // A StructuredCredit profile envelope has no versioned class terms: neither its root nor its
        // profileFields block is walked, so neither is refused.
        {
            "StructuredCredit",
            """{"customProfileId":"structured-credit-profile","scheduleConventions":{"businessDayConvention":"Following"},"profileFields":{"tranche":"A-1","scheduleConventions":{"businessDayConvention":"Following","paymentCalendars":["USNY"]}}}"""
        },
        {
            "StructuredCredit",
            Terms(2, """ "profileFields":{"scheduleConventions":{"stubPolicy":"Long"}},"scheduleConventions":{"stubPolicy":"Long"} """).GetRawText()
        },
        { "StructuredCredit", Terms(null, ProfileCallAndPutSchedules).GetRawText() },
        { "StructuredCredit", Terms(2, ProfileCallAndPutSchedules).GetRawText() },
    };

    [Theory]
    [MemberData(nameof(AcceptedWrites))]
    public void TryGetWriteRefusal_AcceptsEveryUnsetSpellingAndEveryProfileEnvelopeAtTheWriteVersion(string assetClass, string payloadText)
    {
        var payload = Json(payloadText);

        SecurityTermsEnvelope.TryGetWriteRefusal(assetClass, payload, out var reason).Should().BeFalse();
        reason.Should().BeNull();
        var write = () => SecurityTermsEnvelope.EnsureWritableAsAssetSpecificTerms(assetClass, payload);
        write.Should().NotThrow();
    }

    [Fact]
    public void EnsureWritable_RunsTheReadGuardFirst()
    {
        // A stamp above what the node reads gets the read refusal, not the write reason.
        var write = () => SecurityTermsEnvelope.EnsureWritableAsAssetSpecificTerms("Bond", Terms(4, Maturity));

        write.Should().Throw<InvalidOperationException>().Which.Message
            .Should().Be("Unsupported assetClassTermsVersion '4' for asset class 'Bond'; this node reads versions 1 through 3.");
    }

    public static TheoryData<string, string> StampsAboveWhatThisNodeReads() => new()
    {
        { "Bond", Terms(4, Maturity).GetRawText() },
        { "Bond", Terms(4, Maturity + """ ,"scheduleConventions":{"stubPolicy":"Long"} """).GetRawText() },
        { "Bond", Terms(4, Maturity + """ ,"callSchedule":[{"callDate":"2030-06-15"}] """).GetRawText() },
        { "Swap", Terms(3, SwapDates + """ ,"legs":[{"legType":"Fixed","scheduleConventions":{"stubPolicy":"Long"}}] """).GetRawText() },
        { "StructuredCredit", Terms(5, """ "customProfileId":"structured-credit-profile","profileFields":{"tranche":"A-1"} """).GetRawText() },
        { "StructuredCredit", Terms(5, ProfileCallAndPutSchedules).GetRawText() },
    };

    [Theory]
    [MemberData(nameof(StampsAboveWhatThisNodeReads))]
    public void TryGetWriteRefusal_LeavesAStampAboveWhatThisNodeReadsToTheReadGuard(string assetClass, string payloadText)
    {
        // The write reason says the payload is readable here, which a newer stamp is not; the read
        // guard's refusal names the newer version, so it is the only reason given.
        var payload = Json(payloadText);
        var stamp = SecurityTermsEnvelope.ResolveAssetClassTermsVersion(payload);

        SecurityTermsEnvelope.TryGetWriteRefusal(assetClass, payload, out var reason).Should().BeFalse();
        reason.Should().BeNull();
        var write = () => SecurityTermsEnvelope.EnsureWritableAsAssetSpecificTerms(assetClass, payload);
        write.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(
            $"Unsupported assetClassTermsVersion '{stamp}' for asset class '{assetClass}'; " +
            $"this node reads versions 1 through {SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass)}.");
    }

    public static TheoryData<string, string> ClassesThatReadOnlyWhatTheyWrite()
    {
        var data = new TheoryData<string, string>();
        foreach (var assetClass in new[] { "Equity", "Option" })
        {
            foreach (var text in new[]
                     {
                         Terms(null, Maturity).GetRawText(),
                         Terms(1, Maturity).GetRawText(),
                         Terms(2, Maturity).GetRawText(),
                         Terms(0, Maturity).GetRawText(),
                         Terms(null, Maturity + """ ,"scheduleConventions":{"stubPolicy":"Long"} """).GetRawText(),
                         Terms(1, """ "profileFields":{},"scheduleConventions":{"rollConvention":"EOM"} """).GetRawText(),
                         $$"""{"schemaVersion":2,"{{SecurityTermsEnvelope.FormatKey}}":"economic"}""",
                     })
            {
                data.Add(assetClass, text);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ClassesThatReadOnlyWhatTheyWrite))]
    public void EnsureWritable_IsEnsureReadable_ForAClassThatReadsOnlyWhatItWrites(string assetClass, string payloadText)
    {
        var payload = Json(payloadText);

        SecurityTermsEnvelope.TryGetWriteRefusal(assetClass, payload, out var reason).Should().BeFalse();
        reason.Should().BeNull();

        var readError = Capture(() => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms(assetClass, payload));
        var writeError = Capture(() => SecurityTermsEnvelope.EnsureWritableAsAssetSpecificTerms(assetClass, payload));
        // The same payloads fail, with the same type. Only a declared non-flat format words the write's
        // reason for the caller (EnsureWritable_RefusesANonFlatFormatWithWhatToSubmit).
        writeError?.GetType().Should().Be(readError?.GetType());
        (writeError is null).Should().Be(readError is null);
        if (SecurityTermsEnvelope.GetDeclaredFormat(payload) is null or SecurityTermsFormats.Flat or SecurityTermsFormats.CustomProfile)
        {
            writeError?.Message.Should().Be(readError?.Message);
        }
    }

    [Theory]
    [InlineData("StructuredCredit", """{"customProfileId":"structured-credit-profile"}""", true, true)]
    [InlineData("StructuredCredit", """{"tranche":"A-1","profileFields":{}}""", true, true)]
    [InlineData("StructuredCredit", """{"customProfileId":"  ","profileFields":[]}""", false, false)]
    [InlineData("StructuredCredit", """{"customProfileId":7,"profileFields":null}""", false, false)]
    [InlineData("StructuredCredit", """{"tranche":"A-1"}""", false, false)]
    [InlineData("StructuredCredit", "[]", false, false)]
    [InlineData("Bond", """{"profileFields":{}}""", true, false)]
    [InlineData("Bond", """{"customProfileId":"structured-credit-profile"}""", true, false)]
    public void ProfileGovernedPayload_NeedsAProfileCapableClassAndAnEnvelopeSignal(string assetClass, string payloadText, bool carriesEnvelope, bool profileGoverned)
    {
        var payload = Json(payloadText);

        SecurityTermsEnvelope.CarriesProfileEnvelope(payload).Should().Be(carriesEnvelope);
        SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, payload).Should().Be(profileGoverned);
    }

    // ── Write-mode element conventions (ToCreateCommand) ────────────────────────────────────────

    private static readonly DateTimeOffset CreateEffectiveFrom = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static CreateSecurityRequest CreateRequest(string assetClass, JsonElement assetSpecificTerms)
        => new(
            SecurityId: Guid.Parse("4a7e5c11-0000-4000-8000-0000000000b4"),
            AssetClass: assetClass,
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = $"{assetClass} terms versioning", currency = "USD" }),
            AssetSpecificTerms: assetSpecificTerms,
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"VERSIONING-{assetClass}", true, CreateEffectiveFrom)],
            EffectiveFrom: CreateEffectiveFrom,
            SourceSystem: "terms-versioning-tests",
            UpdatedBy: "terms-versioning-tests",
            SourceRecordId: $"{assetClass}-terms-versioning",
            Reason: "Readable-before-writable write path");

    // A class's fully-populated writable payload with one readable-only schedule added at its root.
    private static JsonElement WithSchedule(string assetClass, string key, string schedule)
    {
        var terms = JsonSerializer.SerializeToNode(SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass])!.AsObject();
        terms[key] = JsonNode.Parse(schedule);
        return Json(terms.ToJsonString());
    }

    public static TheoryData<string, string, string, string> FlatSchedulesWithAnUnschedulableExerciseCadence() => new()
    {
        {
            "Bond", "callSchedule", """[{"callDate":"2030-06-15","exerciseFrequency":"Fortnightly"}]""",
            NotYetWritable("Bond", "callSchedule", 3, 1)
        },
        {
            "Bond", "callSchedule", """[{"callDate":"2030-06-15","exerciseFrequency":3}]""",
            NotYetWritable("Bond", "callSchedule", 3, 1)
        },
        {
            "Bond", "putSchedule", """[{"putDate":"2029-06-15","exerciseFrequency":"Fortnightly"}]""",
            NotYetWritable("Bond", "putSchedule", 3, 1)
        },
        {
            "CertificateOfDeposit", "callSchedule", """[{"callDate":"2026-12-30","exerciseFrequency":"Fortnightly"}]""",
            NotYetWritable("CertificateOfDeposit", "callSchedule", 3, 1)
        },
        {
            "StructuredCredit", "putSchedule", """[{"putDate":"2029-06-15","exerciseFrequency":"Fortnightly"}]""",
            NotYetWritable("StructuredCredit", "putSchedule", 4, 2)
        },
    };

    [Theory]
    [MemberData(nameof(FlatSchedulesWithAnUnschedulableExerciseCadence))]
    public void ToCreateCommand_RefusesAReadableOnlySchedule_BeforeAnyEntryConventionIsChecked(
        string assetClass, string key, string schedule, string expected)
    {
        // The entry's exerciseFrequency carries the coupon-frequency convention, which refuses a
        // fortnightly or numeric cadence. The schedule is not writable here at all, so the write guard
        // refuses it first with the not-yet-writable reason, never the convention's.
        var create = () => SecurityMasterMapping.ToCreateCommand(CreateRequest(assetClass, WithSchedule(assetClass, key, schedule)));

        create.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(expected);
    }

    // A decodable StructuredCredit profile envelope: its profileFields carry the class's required terms,
    // and its root and its profileFields both carry a call and a put schedule whose every entry has the
    // given exercise cadence (raw JSON).
    private static JsonElement ProfileEnvelopeWithSchedules(int? stamp, string exerciseFrequency)
    {
        var calls = $$"""[{"callDate":"2030-06-15","exerciseFrequency":{{exerciseFrequency}}}]""";
        var puts = $$"""[{"putDate":"2029-06-15","exerciseFrequency":{{exerciseFrequency}}}]""";
        return Terms(
            stamp,
            $$""" "customProfileId":"structured-credit-profile","callSchedule":{{calls}},"putSchedule":{{puts}},"profileFields":{"tranche":"A-1","collateralType":"CLO","originalFace":1000000,"couponOrIndex":"SOFR","callSchedule":{{calls}},"putSchedule":{{puts}}} """);
    }

    [Theory]
    [InlineData(null, "\"Fortnightly\"")]
    [InlineData(null, "3")]
    [InlineData(1, "\"Fortnightly\"")]
    [InlineData(2, "\"Fortnightly\"")]
    [InlineData(2, "3")]
    public void ToCreateCommand_AcceptsAProfileEnvelopeWhoseSchedulesCarryAnUnschedulableExerciseCadence(int? stamp, string exerciseFrequency)
    {
        // The element-convention walk enforces only conventions this node writes, and never inside a
        // versioned container of a profile envelope, so neither schedule's coupon-frequency convention
        // is checked; and a profile envelope decodes only through the write version, so neither is read.
        var command = SecurityMasterMapping.ToCreateCommand(
            CreateRequest("StructuredCredit", ProfileEnvelopeWithSchedules(stamp, exerciseFrequency)));

        var terms = ((Meridian.FSharp.Domain.SecurityKind.StructuredCredit)command.Kind).Item;
        terms.Tranche.Should().Be("A-1", "the profileFields carry the class terms");
        terms.CallPutSchedules.Calls.IsEmpty.Should().BeTrue("a profile envelope's call schedule is never decoded");
        terms.CallPutSchedules.Puts.IsEmpty.Should().BeTrue("a profile envelope's put schedule is never decoded");
    }

    [Theory]
    [InlineData(3, "\"Fortnightly\"")]
    [InlineData(4, "3")]
    public void ToCreateCommand_RefusesTheSameProfileEnvelopeStampedAboveTheWriteVersion_ByItsStamp(int stamp, string exerciseFrequency)
    {
        var create = () => SecurityMasterMapping.ToCreateCommand(
            CreateRequest("StructuredCredit", ProfileEnvelopeWithSchedules(stamp, exerciseFrequency)));

        create.Should().Throw<InvalidOperationException>().Which.Message
            .Should().Be(ReadableButNotWritable("StructuredCredit", stamp, 4, 2));
    }

    [Theory]
    [InlineData("paymentFrequency")]
    [InlineData("frequency")]
    public void ToCreateCommand_StillRefusesAnUnschedulableSwapLegCadence_WithTheCouponFrequencyReason(string key)
    {
        // Swap legs are a writable array, so their element conventions are still enforced on write.
        var payload = Json($$"""
            {"effectiveDate":"2026-01-15","maturityDate":"2031-01-15","legs":[{"legType":"Fixed","currency":"USD","fixedRate":0.04,"exchangesPrincipal":false,"{{key}}":"Fortnightly"}]}
            """);

        var create = () => SecurityMasterMapping.ToCreateCommand(CreateRequest("Swap", payload));

        create.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(
            $"Value 'Fortnightly' is not a recognized coupon frequency (monthly or longer, or at maturity) for 'legs[].{key}' on asset class 'Swap'. " +
            "Recognized values include: Monthly, BiMonthly, Quarterly, SemiAnnual, Annual, AtMaturity.");
    }

    // ── Lossless economic → flat bridge ─────────────────────────────────────────────────────────

    [Fact]
    public void FoldRetained_RebuiltValuesWin_RetainedFillsOnlyGaps()
    {
        var rebuilt = Json("""
            {"schemaVersion":2,"call":{"isCallable":true,"firstCallDate":"2031-01-01","callSchedule":[]},"redemption":null}
            """);
        var retained = Json("""
            {"schemaVersion":2,"termsFormat":"economic",
             "call":{"isCallable":false,"firstCallDate":"2030-01-01","callPrice":101.5,"callSchedule":[{"callDt":"2030-01-01","callPx":101.5}]},
             "redemption":{"redemptionType":"Par","redemptionPrice":100}}
            """);

        var folded = SecurityTermsEnvelope.FoldRetainedEconomicTerms(rebuilt, retained);

        folded.GetProperty("call").GetProperty("isCallable").GetBoolean().Should().BeTrue("a value the flat revision expresses wins");
        folded.GetProperty("call").GetProperty("firstCallDate").GetString().Should().Be("2031-01-01");
        folded.GetProperty("call").GetProperty("callPrice").GetDecimal().Should().Be(101.5m);
        folded.GetProperty("call").GetProperty("callSchedule").GetArrayLength().Should().Be(1);
        folded.GetProperty("redemption").GetProperty("redemptionType").GetString().Should().Be("Par");
        folded.TryGetProperty("termsFormat", out _).Should().BeFalse("retained envelope keys are never folded");
    }

    [Fact]
    public void EconomicOnlyReplay_ThenAmend_KeepsEconomicDataTheFlatFamilyCannotHold()
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            "Bond",
            new
            {
                maturity = "2035-06-15",
                issueDate = "2025-06-15",
                couponType = "Fixed",
                couponRate = 4.5m,
                dayCount = "30/360",
                paymentFrequency = "SemiAnnual",
                isCallable = true,
                callDate = "2030-06-15",
                issuerName = "Meridian Capital Corp"
            });
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", original));

        // An older event: economic terms only, carrying a call schedule and redemption the flat
        // Bond keys have no slot for.
        var terms = JsonNode.Parse(economic.EconomicTerms.GetRawText())!.AsObject();
        terms["call"]!["callSchedule"] = JsonNode.Parse("""[{"callDt":"2030-06-15","callPx":102.0,"isParCall":false,"callType":"Discrete"}]""");
        terms["redemption"] = JsonNode.Parse("""{"redemptionType":"Par","redemptionPrice":100.0,"isBullet":false,"isAmortizing":true}""");
        var olderEvent = economic with
        {
            EconomicTerms = Json(terms.ToJsonString()),
            LegacyAssetSpecificTerms = null
        };

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(olderEvent);
        SecurityTermsEnvelope.TryGetRetainedEconomicTerms(rebuilt.AssetSpecificTerms, out _).Should().BeTrue();

        // The amend path re-serializes through the domain codec, which knows only flat keys.
        var amended = SecurityMasterMapping.ToProjection(new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(rebuilt)));
        SecurityTermsEnvelope.TryGetRetainedEconomicTerms(amended.AssetSpecificTerms, out _).Should().BeFalse();
        var carried = SecurityEconomicDefinitionAdapter.CarryRetainedEconomicTerms(rebuilt, amended);

        var nextEvent = SecurityEconomicDefinitionAdapter.ToEconomicRecord(carried).EconomicTerms;

        nextEvent.GetProperty("call").GetProperty("callSchedule").GetArrayLength().Should().Be(1);
        nextEvent.GetProperty("call").GetProperty("callSchedule")[0].GetProperty("callPx").GetDecimal().Should().Be(102.0m);
        nextEvent.GetProperty("redemption").GetProperty("isAmortizing").GetBoolean().Should().BeTrue();
        nextEvent.GetProperty(SecurityTermsEnvelope.FormatKey).GetString().Should().Be(SecurityTermsFormats.Economic);
    }

    // ── Event payload versions ──────────────────────────────────────────────────────────────────

    private static SecurityMasterEventEnvelope Envelope(JsonElement payload, JsonElement metadata, string? type = null, int? version = null)
        => new(null, Guid.NewGuid(), 1, "TermsAmended", DateTimeOffset.UtcNow, "tests", null, null, payload, metadata, type, version);

    [Fact]
    public void Resolve_PrefersExplicitThenMetadataThenShape()
    {
        var economicShape = Json("""{"classification":{},"economicTerms":{}}""");
        var projectionShape = Json("""{"securityId":"00000000-0000-0000-0000-000000000001"}""");

        SecurityMasterEventPayloads.Resolve(Envelope(projectionShape, Json("{}"), "Custom", 4))
            .Should().Be(new SecurityMasterEventPayloadDescriptor("Custom", 4));
        SecurityMasterEventPayloads.Resolve(Envelope(projectionShape, Json("""{"payloadType":"SecurityEconomicDefinition","schemaVersion":2}""")))
            .Should().Be(new SecurityMasterEventPayloadDescriptor(SecurityMasterEventPayloads.EconomicDefinition, 2));
        SecurityMasterEventPayloads.Resolve(Envelope(economicShape, Json("""{"sourceSystem":"x"}""")))
            .Should().Be(new SecurityMasterEventPayloadDescriptor(SecurityMasterEventPayloads.EconomicDefinition, SecurityMasterEventPayloads.EconomicDefinitionVersion));
        SecurityMasterEventPayloads.Resolve(Envelope(projectionShape, Json("""{"schemaVersion":9}""")))
            .Should().Be(new SecurityMasterEventPayloadDescriptor(SecurityMasterEventPayloads.LegacyProjection, 1),
                "a metadata version without its metadata type does not describe the inferred family");
    }

    [Fact]
    public void FromEventEnvelope_RefusesAPayloadVersionNewerThanThisNode()
    {
        var future = Envelope(
            Json("""{"classification":{},"economicTerms":{}}"""),
            Json("{}"),
            SecurityMasterEventPayloads.EconomicDefinition,
            SecurityMasterEventPayloads.EconomicDefinitionVersion + 1);

        var replay = () => SecurityMasterMapping.FromEventEnvelope(future);

        replay.Should().Throw<InvalidOperationException>().WithMessage("*payload version*");
    }

    [Fact]
    public void FromEventEnvelope_RefusesAnUnknownPayloadFamily()
    {
        var unknown = Envelope(Json("{}"), Json("{}"), "SomethingNew", 1);

        var replay = () => SecurityMasterMapping.FromEventEnvelope(unknown);

        replay.Should().Throw<InvalidOperationException>().WithMessage("*SomethingNew*");
    }

    [Fact]
    public void ToEventEnvelope_DeclaresTheEconomicDefinitionFamilyAndVersion()
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            "Bond",
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads["Bond"]);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", original));

        var envelope = SecurityMasterMapping.ToEventEnvelope(economic, "SecurityCreated", "tests", "tests", null, 1);

        envelope.PayloadType.Should().Be(SecurityMasterEventPayloads.EconomicDefinition);
        envelope.PayloadVersion.Should().Be(SecurityMasterEventPayloads.EconomicDefinitionVersion);
        SecurityMasterEventPayloads.Resolve(envelope).PayloadVersion.Should().Be(EconomicTermsSchema.Current);
    }

    // ── Stored terms_format column (migration 042, ADR-024) ─────────────────────────────────────

    private const string TermsFormatMigration = "src/Meridian.Storage/SecurityMaster/Migrations/042_security_master_terms_format_column.sql";

    [Fact]
    public void TermsFormatMigration_BackfillMirrorsResolveFormat_ForEveryFormatAndEconomicModule()
    {
        // The migration classifies untagged rows in SQL with the same rule ResolveFormat applies in
        // code. The two cannot share an implementation, so this pins the SQL to the code's vocabulary:
        // every storable format name and every economic module key the shape rule consults.
        var sql = File.ReadAllText(Path.Combine(FindRepoRoot(), TermsFormatMigration));

        foreach (var format in SecurityTermsFormats.All)
        {
            sql.Should().Contain($"'{format}'", $"the backfill and check constraint must know the '{format}' family");
        }

        foreach (var module in SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.EconomicTermsModules)
        {
            sql.Should().Contain($"'{module}'", $"an untagged v2 document carrying '{module}' is classified economic by ResolveFormat");
        }

        sql.Should().Contain($"'{SecurityTermsEnvelope.FormatKey}'");
        sql.Should().Contain($"= {EconomicTermsSchema.Current}", "only the reserved economic codec version routes untagged rows to the economic family");
    }

    [Fact]
    public void ResolveStorableTermsFormat_UsesTheDeclaredTagAndRefusesAnUnknownOne()
    {
        var tagged = Projection("Bond", Json("""{"schemaVersion":1,"termsFormat":"flat","maturity":"2030-01-15"}"""));
        var untaggedEconomic = Projection("Bond", Json("""{"schemaVersion":2,"coupon":{"couponRate":4.5}}"""));
        var unknown = Projection("Bond", Json("""{"schemaVersion":1,"termsFormat":"tabular"}"""));

        Meridian.Storage.SecurityMaster.PostgresSecurityMasterStore.ResolveStorableTermsFormat(tagged).Should().Be(SecurityTermsFormats.Flat);
        Meridian.Storage.SecurityMaster.PostgresSecurityMasterStore.ResolveStorableTermsFormat(untaggedEconomic).Should().Be(SecurityTermsFormats.Economic);
        var refuse = () => Meridian.Storage.SecurityMaster.PostgresSecurityMasterStore.ResolveStorableTermsFormat(unknown);

        refuse.Should().Throw<InvalidOperationException>().WithMessage("*'tabular'*not a storable terms format*");
    }

    private static SecurityProjectionRecord Projection(string assetClass, JsonElement assetSpecificTerms)
        => new(
            SecurityId: Guid.NewGuid(),
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: $"{assetClass} terms format",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "TF",
            CommonTerms: Json("""{"displayName":"terms format","currency":"USD"}"""),
            AssetSpecificTerms: assetSpecificTerms,
            Provenance: Json("""{"sourceSystem":"tests"}"""),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow,
            EffectiveTo: null,
            Identifiers: [],
            Aliases: []);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }
}
