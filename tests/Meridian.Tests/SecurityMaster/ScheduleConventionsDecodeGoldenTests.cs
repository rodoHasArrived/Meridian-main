using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Core;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The golden of what the Read-mode mapping decodes from a stored scheduleConventions block, captured
/// at f79ee152 (main 838a74d5 plus increment 5a-0) before increment 5a moves the decode onto one shared
/// predicate and one shared reader. Every value of a block corpus is placed in every container a row
/// can carry it in, at the stamps around each class's versions, and mapped through
/// <see cref="SecurityMasterMapping.ToRecord"/>. Each entry records the decoded F#
/// <see cref="ScheduleConventions"/> as invariant JSON, <c>empty</c> when nothing is declared, or the
/// mapping's exception. Increment 5a must leave every entry unchanged: the untrimmed calendars, the
/// untagged Bond envelope (its format resolves to customProfile, but a Bond row is not
/// profile-governed) and the swap leg containers included.
/// </summary>
/// <remarks>
/// <para>The reader parses dates with the current culture, so the golden is computed under the
/// invariant culture, and the date corpus is recorded again under de-DE at the Bond stamp-2
/// placement.</para>
/// <para>A missing or mismatched fixture writes <c>schedule-conventions-decode-golden.received.json</c>
/// beside the test binaries and fails; it never skips. Replace the committed fixture with the received
/// file only when the change in decoded output is intended and approved.</para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ScheduleConventionsDecodeGoldenTests
{
    private const string FixtureFileName = "schedule-conventions-decode-golden.json";
    private const string ReceivedFileName = "schedule-conventions-decode-golden.received.json";
    private const string BlockKey = "scheduleConventions";
    private const string Empty = "empty";

    private static readonly JsonSerializerOptions FixtureOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions EntryOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // The canonical block: all twelve members, populated, in the frozen written order.
    private static readonly (string Member, string Value)[] CanonicalMembers =
    [
        ("accrualAdjusted", "true"),
        ("businessDayConvention", "\"ModifiedFollowing\""),
        ("couponCashBasis", "\"DayCount\""),
        ("couponRateUnit", "\"Percent\""),
        ("direction", "\"Backward\""),
        ("endOfMonthRule", "false"),
        ("firstRegularDate", "\"2025-09-15\""),
        ("lastRegularDate", "\"2035-03-15\""),
        ("paymentCalendars", "[\"USNY\",\"GBLO\"]"),
        ("paymentLagBusinessDays", "2"),
        ("rollConvention", "\"15\""),
        ("stubPolicy", "\"Short\""),
    ];

    // Each member of the canonical block replaced by a value of the wrong JSON kind (or, for the
    // business-day lag, a number that is not an Int32), the rest left canonical.
    private static readonly (string Name, string Member, string Value)[] WrongKinds =
    [
        ("accrualAdjusted", "accrualAdjusted", "\"true\""),
        ("businessDayConvention", "businessDayConvention", "1"),
        ("couponCashBasis", "couponCashBasis", "true"),
        ("couponRateUnit", "couponRateUnit", "[\"Percent\"]"),
        ("direction", "direction", "{\"value\":\"Backward\"}"),
        ("endOfMonthRule", "endOfMonthRule", "0"),
        ("firstRegularDate", "firstRegularDate", "20250915"),
        ("lastRegularDate", "lastRegularDate", "[\"2035-03-15\"]"),
        ("paymentCalendars", "paymentCalendars", "\"USNY\""),
        ("paymentLagBusinessDays", "paymentLagBusinessDays", "\"2\""),
        ("paymentLagBusinessDays.fractional", "paymentLagBusinessDays", "2.5"),
        ("paymentLagBusinessDays.beyondInt32", "paymentLagBusinessDays", "4294967296"),
        ("paymentLagBusinessDays.negative", "paymentLagBusinessDays", "-1"),
        ("rollConvention", "rollConvention", "15"),
        ("stubPolicy", "stubPolicy", "false"),
    ];

    // Blocks carrying only the two regular dates, spelled as timestamps and in culture formats.
    private static readonly (string Name, string First, string Last)[] DateSpellings =
    [
        ("timestampUtc", "2025-09-15T00:00:00Z", "2035-03-15T00:00:00+00:00"),
        ("timestampLocal", "2025-09-15T12:30:00", "2035-03-15 00:00"),
        ("monthFirstSlashes", "09/15/2025", "03/15/2035"),
        ("dayFirstSlashes", "15/09/2025", "15/03/2035"),
        ("dayFirstDots", "15.09.2025", "15.03.2035"),
        ("ambiguousSlashes", "03/04/2030", "04/03/2031"),
        ("unpaddedIso", "2025-9-5", "2035-3-15"),
        ("compact", "20250915", "2035 03 15"),
        ("surroundingWhitespace", " 2025-09-15 ", "2035-03-15\t"),
    ];

    private static readonly Lazy<SortedDictionary<string, string>> Computed = new(Compute);

    [Fact]
    public void DecodedScheduleConventions_MatchTheCommittedGolden()
    {
        var received = Computed.Value;

        var fixturePath = LocateFixture();
        string? failure;
        if (fixturePath is null)
        {
            failure = $"The golden fixture '{FixtureFileName}' was not found beside the test binaries or under tests/fixtures/security-master/terms-golden/.";
        }
        else
        {
            var expected = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(fixturePath))!;
            failure = Compare(expected, received);
        }

        if (failure is null)
        {
            return;
        }

        var receivedPath = Path.Combine(AppContext.BaseDirectory, ReceivedFileName);
        File.WriteAllText(receivedPath, JsonSerializer.Serialize(received, FixtureOptions) + "\n", new UTF8Encoding(false));
        Assert.Fail(
            $"{failure}{Environment.NewLine}The computed output was written to '{receivedPath}'. Replace " +
            "tests/fixtures/security-master/terms-golden/" + FixtureFileName + " with it only when the change is intended and approved.");
    }

    [Fact]
    public void TheCasesIncrementFiveAMustKeep_DecodeAsRecorded()
    {
        var decoded = Computed.Value;

        // The reader filters blank calendar ids but never trims the rest.
        JsonNode.Parse(decoded["Bond.stamp2.calendars.untrimmed"])!["paymentCalendars"]!.AsArray()
            .Select(static calendar => calendar!.GetValue<string>())
            .Should().Equal(" USNY ", "GBLO\t");

        // A Bond is never profile-governed, so its untagged customProfileId and profileFields envelope
        // decodes the root block by its own stamp, as a flat Bond row does.
        decoded["Bond.untaggedProfileEnvelope.stamp2.canonical"].Should().NotBe(Empty)
            .And.Be(decoded["Bond.stamp2.canonical"]);

        // The leg container resolves past an empty swapLegs to cashFlowLegs, and a legs object is
        // unreadable whatever the block holds.
        decoded["Swap.swapLegsEmptyBesideCashFlowLegs.stamp2.canonical"].Should().Be(decoded["Swap.legs.stamp2.canonical"]);
        decoded["Swap.legsObject.stamp2.canonical"].Should().Be(
            "exception InvalidOperationException: Swap leg container 'legs' must be a JSON array.");

        // Below the block's version, and in a profile envelope at any stamp, nothing is decoded.
        foreach (var prefix in new[]
                 {
                     "Bond.unstamped.", "Bond.stamp1.", "CertificateOfDeposit.stamp1.", "StructuredCredit.flat.stamp2.",
                     "StructuredCredit.profileEnvelope.", "Swap.legs.stamp1.", "Swap.swapLegsEmptyBesideCashFlowLegs.stamp1.",
                 })
        {
            decoded.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Should().NotBeEmpty().And.OnlyContain(entry => entry.Value == Empty || entry.Value == "[\"empty\",\"empty\"]",
                    $"no '{prefix}' row decodes a block");
        }
    }

    private static SortedDictionary<string, string> Compute()
    {
        var output = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var corpus = Corpus().ToArray();
        WithCulture(CultureInfo.InvariantCulture, () =>
        {
            foreach (var placement in Placements())
            {
                foreach (var (name, members) in corpus)
                {
                    output.Add($"{placement.Name}.{name}", Decode(placement, members));
                }
            }
        });

        // The same date corpus under a day-first culture, at one placement that decodes the block.
        var bondStampTwo = Placements().Single(static placement => placement.Name == "Bond.stamp2");
        WithCulture(CultureInfo.GetCultureInfo("de-DE"), () =>
        {
            foreach (var (name, members) in corpus.Where(static entry => entry.Name.StartsWith("dates.", StringComparison.Ordinal)))
            {
                output.Add($"culture.de-DE.{bondStampTwo.Name}.{name}", Decode(bondStampTwo, members));
            }
        });

        return output;
    }

    // The block-value corpus: each entry is the members (container key and raw JSON value) added to
    // the placement's container. Raw text keeps duplicate member names, which a JsonObject cannot hold.
    private static IEnumerable<(string Name, (string Key, string Value)[] Members)> Corpus()
    {
        yield return ("canonical", [(BlockKey, Block(CanonicalMembers))]);

        foreach (var (name, member, value) in WrongKinds)
        {
            yield return ($"wrongKind.{name}", [(BlockKey, Block(CanonicalMembers.Select(entry => entry.Member == member ? (member, value) : entry)))]);
        }

        foreach (var (name, first, last) in DateSpellings)
        {
            yield return ($"dates.{name}", [(BlockKey, Block([("firstRegularDate", Text(first)), ("lastRegularDate", Text(last))]))]);
        }

        string[] textMembers = ["businessDayConvention", "couponCashBasis", "couponRateUnit", "direction", "firstRegularDate", "lastRegularDate", "rollConvention", "stubPolicy"];
        yield return ("blankStrings", [(BlockKey, Block(textMembers.Select(static member => (member, "\"\""))))]);
        yield return ("whitespaceStrings", [(BlockKey, Block(textMembers.Select(static member => (member, "\"  \\t\""))))]);

        foreach (var (name, calendars) in new (string, string)[]
                 {
                     ("whitespaceOnly", "[\"\",\" \",\"\\t\",\"\\n\"]"),
                     ("nonString", "[1,true,null,{\"id\":\"USNY\"},[\"USNY\"]]"),
                     ("mixed", "[\"USNY\",\" \",7,null,\"GBLO\"]"),
                     ("untrimmed", "[\" USNY \",\"GBLO\\t\"]"),
                     ("duplicatesAndCase", "[\"USNY\",\"usny\",\"USNY\"]"),
                     ("empty", "[]"),
                     ("null", "null"),
                 })
        {
            yield return ($"calendars.{name}", [(BlockKey, Block([("paymentCalendars", calendars)]))]);
        }

        var pascalCase = CanonicalMembers.Select(static entry => (char.ToUpperInvariant(entry.Member[0]) + entry.Member[1..], entry.Value));
        yield return ("caseVariantMembers", [(BlockKey, Block(pascalCase))]);
        yield return ("caseVariantMembersBesideExact", [(BlockKey, Block(
        [
            ("businessDayConvention", "\"Following\""),
            ("BusinessDayConvention", "\"Preceding\""),
            ("PAYMENTCALENDARS", "[\"GBLO\"]"),
            ("paymentCalendars", "[\"USNY\"]"),
        ]))]);
        yield return ("duplicateMember", [(BlockKey, Block([("businessDayConvention", "\"Following\""), ("businessDayConvention", "\"Preceding\"")]))]);
        yield return ("caseVariantContainer", [("ScheduleConventions", Block(CanonicalMembers))]);
        yield return ("upperCaseContainer", [("SCHEDULECONVENTIONS", Block(CanonicalMembers))]);
        yield return ("caseVariantContainerBesideExact",
        [
            (BlockKey, Block([("stubPolicy", "\"Long\"")])),
            ("ScheduleConventions", Block(CanonicalMembers)),
        ]);

        yield return ("emptyObject", [(BlockKey, "{}")]);
        yield return ("null", [(BlockKey, "null")]);
        yield return ("string", [(BlockKey, "\"x\"")]);
        yield return ("emptyArray", [(BlockKey, "[]")]);
        yield return ("number", [(BlockKey, "7")]);
        yield return ("arrayOfBlock", [(BlockKey, "[" + Block(CanonicalMembers) + "]")]);
        yield return ("allNull", [(BlockKey, Block(CanonicalMembers.Select(static entry => (entry.Member, "null"))))]);
        yield return ("unknownMembersOnly", [(BlockKey, Block([("calendar", "\"USNY\""), ("bdc", "\"Following\"")]))]);
    }

    // Where a row can carry the block: the class, the stored terms around it and the object the
    // corpus members are added to.
    private static IEnumerable<Placement> Placements()
    {
        const string bond = """{"maturity":"2030-01-01","issueDate":"2024-01-01","couponType":"Fixed","couponRate":4.25,"dayCount":"30/360","isCallable":false,"issuerName":"ACME Corp","par":1000,"paymentFrequency":"SemiAnnual"}""";
        const string certificateOfDeposit = """{"issuerName":"First Meridian Bank","maturity":"2027-06-30","couponRate":4.1,"dayCount":"ACT/360"}""";
        const string structuredCredit = """{"tranche":"A-1","collateralType":"CLO","originalFace":10000000,"couponOrIndex":"SOFR+250","maturity":"2031-06-15"}""";
        const string fixedLeg = """{"legId":"fixed","legType":"Fixed","currency":"USD","direction":"Pay","fixedRate":4.25,"notional":10000000,"paymentFrequency":"SemiAnnual","dayCount":"30/360","exchangesPrincipal":false}""";
        const string floatingLeg = """{"legId":"float","legType":"Floating","currency":"USD","direction":"Receive","index":"SOFR","spreadBps":15,"notional":10000000,"paymentFrequency":"Quarterly","dayCount":"ACT/360","exchangesPrincipal":false}""";

        yield return new("Bond.unstamped", "Bond", () => Parse(bond), static terms => terms);
        foreach (var stamp in new[] { 1, 2, 3 })
        {
            yield return new($"Bond.stamp{stamp}", "Bond", () => StampedFlat(bond, stamp), static terms => terms);
        }

        foreach (var stamp in new[] { 1, 2 })
        {
            yield return new($"CertificateOfDeposit.stamp{stamp}", "CertificateOfDeposit", () => StampedFlat(certificateOfDeposit, stamp), static terms => terms);
        }

        foreach (var stamp in new[] { 2, 3, 4 })
        {
            yield return new($"StructuredCredit.flat.stamp{stamp}", "StructuredCredit", () => StampedFlat(structuredCredit, stamp), static terms => terms);
        }

        foreach (var stamp in new[] { 1, 3, 4 })
        {
            JsonObject Envelope() => new()
            {
                ["schemaVersion"] = 3,
                ["termsFormat"] = "customProfile",
                ["assetClassTermsVersion"] = stamp,
                ["customProfileId"] = "structured-credit-v1",
                ["profileVersion"] = 1,
                ["profileFields"] = Parse(structuredCredit),
            };

            yield return new($"StructuredCredit.profileEnvelope.root.stamp{stamp}", "StructuredCredit", Envelope, static terms => terms);
            yield return new($"StructuredCredit.profileEnvelope.profileFields.stamp{stamp}", "StructuredCredit", Envelope, static terms => terms["profileFields"]!.AsObject());
        }

        // No termsFormat, so the payload's format resolves to customProfile; the block sits at the root.
        yield return new("Bond.untaggedProfileEnvelope.stamp2", "Bond", () =>
        {
            var terms = new JsonObject
            {
                ["assetClassTermsVersion"] = 2,
                ["customProfileId"] = "bond-profile-v1",
                ["profileFields"] = new JsonObject { ["issuerName"] = "ACME Corp" },
            };
            foreach (var (key, value) in Parse(bond))
            {
                terms[key] = value?.DeepClone();
            }

            return terms;
        }, static terms => terms);

        // The block goes on the second leg; the first carries none.
        foreach (var stamp in new[] { 1, 2 })
        {
            yield return new($"Swap.legs.stamp{stamp}", "Swap", () => Swap(stamp, ("legs", new JsonArray(Parse(fixedLeg), Parse(floatingLeg)))),
                static terms => terms["legs"]![1]!.AsObject());
            yield return new($"Swap.swapLegsEmptyBesideCashFlowLegs.stamp{stamp}", "Swap",
                () => Swap(stamp, ("swapLegs", new JsonArray()), ("cashFlowLegs", new JsonArray(Parse(fixedLeg), Parse(floatingLeg)))),
                static terms => terms["cashFlowLegs"]![1]!.AsObject());
            yield return new($"Swap.legsObject.stamp{stamp}", "Swap", () => Swap(stamp, ("legs", Parse(floatingLeg))),
                static terms => terms["legs"]!.AsObject());
        }
    }

    private static string Decode(Placement placement, (string Key, string Value)[] members)
    {
        var terms = placement.Terms();
        var container = placement.Container(terms);
        for (var index = 0; index < members.Length; index++)
        {
            container[members[index].Key] = Sentinel(index);
        }

        var text = terms.ToJsonString();
        for (var index = 0; index < members.Length; index++)
        {
            text = text.Replace(JsonSerializer.Serialize(Sentinel(index)), members[index].Value, StringComparison.Ordinal);
        }

        var payload = JsonDocument.Parse(text).RootElement.Clone();
        SecurityKind kind;
        try
        {
            kind = SecurityMasterMapping.ToRecord(SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(placement.AssetClass, payload)).Kind;
        }
        catch (Exception exception)
        {
            return $"exception {exception.GetType().Name}: {exception.Message}";
        }

        return kind switch
        {
            SecurityKind.Bond bondKind => DescribeText(bondKind.Item.ScheduleConventions),
            SecurityKind.CertificateOfDeposit certificateKind => DescribeText(certificateKind.Item.ScheduleConventions),
            SecurityKind.StructuredCredit structuredCreditKind => DescribeText(structuredCreditKind.Item.ScheduleConventions),
            // One entry per leg, in leg order.
            SecurityKind.Swap swapKind => new JsonArray(swapKind.Item.Legs.Select(static leg => (JsonNode?)Describe(leg.ScheduleConventions)).ToArray()).ToJsonString(EntryOptions),
            _ => throw new InvalidOperationException($"'{placement.AssetClass}' decoded to an unexpected kind."),
        };
    }

    private static string DescribeText(ScheduleConventions conventions)
        => Describe(conventions) is JsonObject block ? block.ToJsonString(EntryOptions) : Empty;

    // The decoded conventions as invariant JSON in the frozen written member order, or "empty".
    private static JsonNode Describe(ScheduleConventions conventions)
        => ScheduleConventionsModule.IsEmpty(conventions)
            ? JsonValue.Create(Empty)!
            : new JsonObject
            {
                ["accrualAdjusted"] = Value(conventions.AccrualAdjusted),
                ["businessDayConvention"] = Value(conventions.BusinessDayConvention),
                ["couponCashBasis"] = Value(conventions.CouponCashBasis),
                ["couponRateUnit"] = Value(conventions.CouponRateUnit),
                ["direction"] = Value(conventions.Direction),
                ["endOfMonthRule"] = Value(conventions.EndOfMonthRule),
                ["firstRegularDate"] = Date(conventions.FirstRegularDate),
                ["lastRegularDate"] = Date(conventions.LastRegularDate),
                ["paymentCalendars"] = new JsonArray(conventions.PaymentCalendars.Select(static calendar => (JsonNode?)JsonValue.Create(calendar)).ToArray()),
                ["paymentLagBusinessDays"] = Value(conventions.PaymentLagBusinessDays),
                ["rollConvention"] = Value(conventions.RollConvention),
                ["stubPolicy"] = Value(conventions.StubPolicy),
            };

    private static JsonNode? Value(FSharpOption<string>? option)
        => option is null ? null : JsonValue.Create(option.Value);

    private static JsonNode? Value(FSharpOption<bool>? option)
        => option is null ? null : JsonValue.Create(option.Value);

    private static JsonNode? Value(FSharpOption<int>? option)
        => option is null ? null : JsonValue.Create(option.Value);

    private static JsonNode? Date(FSharpOption<DateOnly>? option)
        => option is null ? null : JsonValue.Create(option.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    private static string Block(IEnumerable<(string Member, string Value)> members)
        => "{" + string.Join(",", members.Select(static member => $"{JsonSerializer.Serialize(member.Member)}:{member.Value}")) + "}";

    private static string Text(string value) => JsonSerializer.Serialize(value);

    private static string Sentinel(int index) => $"@@schedule-conventions-corpus-{index}@@";

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    // A flat payload stamped as a writer stamps it: the envelope keys first, then the terms.
    private static JsonObject StampedFlat(string json, int stamp)
    {
        var terms = new JsonObject { ["termsFormat"] = "flat", ["assetClassTermsVersion"] = stamp };
        foreach (var (key, value) in Parse(json))
        {
            terms[key] = value?.DeepClone();
        }

        return terms;
    }

    private static JsonObject Swap(int stamp, params (string Key, JsonNode Value)[] legContainers)
    {
        var terms = StampedFlat("""{"effectiveDate":"2026-01-15","maturityDate":"2031-01-15"}""", stamp);
        foreach (var (key, value) in legContainers)
        {
            terms[key] = value;
        }

        return terms;
    }

    private static void WithCulture(CultureInfo culture, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static string? Compare(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> received)
    {
        var missing = expected.Keys.Except(received.Keys, StringComparer.Ordinal).ToArray();
        var added = received.Keys.Except(expected.Keys, StringComparer.Ordinal).ToArray();
        var changed = expected.Keys.Intersect(received.Keys, StringComparer.Ordinal)
            .Where(key => !string.Equals(expected[key], received[key], StringComparison.Ordinal))
            .ToArray();
        if (missing.Length == 0 && added.Length == 0 && changed.Length == 0)
        {
            return null;
        }

        return $"The decoded scheduleConventions differ from the golden. Missing: [{string.Join(", ", missing)}]; " +
               $"added: [{string.Join(", ", added)}]; changed: [{string.Join(", ", changed)}].";
    }

    private static string? LocateFixture()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "fixtures", "security-master", "terms-golden", FixtureFileName);
        if (File.Exists(beside))
        {
            return beside;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "security-master", "terms-golden", FixtureFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed record Placement(string Name, string AssetClass, Func<JsonObject> Terms, Func<JsonObject, JsonObject> Container);
}
