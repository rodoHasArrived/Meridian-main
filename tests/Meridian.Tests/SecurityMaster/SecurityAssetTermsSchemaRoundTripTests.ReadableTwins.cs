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
/// version would store it, one twin per readable version (the scheduleConventions block, then the
/// callSchedule and putSchedule arrays). <see cref="FullPayloads"/> stays writable-only, so it cannot
/// carry a term the node only reads; the twins cover the read path for it instead, and never go
/// through <see cref="SecurityMasterMapping.ToCreateCommand"/>, which refuses them.
/// </summary>
public sealed partial class SecurityAssetTermsSchemaRoundTripTests
{
    private const string ScheduleConventionsKey = "scheduleConventions";
    private const string CallScheduleKey = "callSchedule";
    private const string PutScheduleKey = "putSchedule";

    // The schedule arrays a readable twin can carry, in the order the builder adds them.
    private static readonly string[] ScheduleKeys = [CallScheduleKey, PutScheduleKey];

    /// <summary>
    /// The frozen canonical member order of a written callSchedule entry (ADR-024): all twelve
    /// members, ordinal, whatever order the stored entry used.
    /// </summary>
    private static readonly string[] CanonicalCallScheduleEntryMembers =
    [
        "callDate",
        "callEndDate",
        "callPrice",
        "exerciseFrequency",
        "exerciseStyle",
        "makeWholeSpreadBps",
        "maximumNoticeDays",
        "minimumNoticeDays",
        "noticeDayType",
        "partialRedemption",
        "priceBasis",
        "priceType"
    ];

    /// <summary>
    /// The frozen canonical member order of a written putSchedule entry (ADR-024): all nine members,
    /// ordinal, whatever order the stored entry used.
    /// </summary>
    private static readonly string[] CanonicalPutScheduleEntryMembers =
    [
        "exerciseFrequency",
        "exerciseStyle",
        "maximumNoticeDays",
        "minimumNoticeDays",
        "noticeDayType",
        "priceBasis",
        "putDate",
        "putEndDate",
        "putPrice"
    ];

    // An entry that names every member and sets none: unset, like [] (ADR-024).
    private const string AllNullCallScheduleEntry =
        """{"callDate":null,"callEndDate":null,"callPrice":null,"exerciseFrequency":null,"exerciseStyle":null,"makeWholeSpreadBps":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"partialRedemption":null,"priceBasis":null,"priceType":null}""";

    private const string AllNullPutScheduleEntry =
        """{"exerciseFrequency":null,"exerciseStyle":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"priceBasis":null,"putDate":null,"putEndDate":null,"putPrice":null}""";

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

    /// <summary>
    /// The declared call and put schedules per class that reads them ahead of what it writes, keyed by
    /// array. An entry names only the members it sets, and across the twins every member of each
    /// entry shape is set at least once, so a member either codec side drops fails the round trip.
    /// The bond's calls and the structured credit's puts are stored out of date order and the
    /// certificate of deposit's call out of canonical member order, so the round trip also proves the
    /// writer keeps the stored entry order of both arrays and canonicalizes the members.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ReadableTwinSchedules =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            // A Bermudan window at 102 of accreted value, a make-whole American window (the 100 price
            // is the floor) and an American par window with pro-rata partial calls and calendar-day
            // notice, stored out of date order; one American put window with business-day notice.
            ["Bond"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CallScheduleKey] =
                    """[{"callDate":"2032-06-15","callEndDate":"2034-06-15","exerciseStyle":"Bermudan","exerciseFrequency":"Quarterly","callPrice":102,"priceBasis":"PercentOfAccretedValue","priceType":"Fixed"},{"callDate":"2027-06-15","callEndDate":"2030-06-14","exerciseStyle":"American","callPrice":100,"priceBasis":"PercentOfPar","priceType":"MakeWhole","makeWholeSpreadBps":25},{"callDate":"2030-06-15","callEndDate":"2032-06-14","exerciseStyle":"American","callPrice":100,"priceBasis":"PercentOfPar","priceType":"Fixed","partialRedemption":"ProRata","minimumNoticeDays":10,"maximumNoticeDays":60,"noticeDayType":"Calendar"}]""",
                [PutScheduleKey] =
                    """[{"putDate":"2029-06-15","putEndDate":"2029-07-15","exerciseStyle":"American","putPrice":100,"priceBasis":"PercentOfPar","minimumNoticeDays":5,"maximumNoticeDays":15,"noticeDayType":"Business"}]"""
            },
            // A Bermudan par call whose members are stored out of canonical order, and a European put.
            ["CertificateOfDeposit"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CallScheduleKey] =
                    """[{"priceType":"Fixed","priceBasis":"PercentOfPar","partialRedemption":"WholeOnly","noticeDayType":"Business","minimumNoticeDays":30,"maximumNoticeDays":45,"exerciseStyle":"Bermudan","exerciseFrequency":"Quarterly","callPrice":100,"callEndDate":"2027-06-30","callDate":"2026-09-30"}]""",
                [PutScheduleKey] =
                    """[{"putDate":"2027-03-31","exerciseStyle":"European","putPrice":100,"priceBasis":"PercentOfPar","minimumNoticeDays":5,"maximumNoticeDays":20,"noticeDayType":"Business"}]"""
            },
            // A Bermudan par call after the non-call period with pro-rata partial calls, and two puts
            // stored out of date order: a European put, then an earlier Bermudan put window, the twin
            // put that sets exerciseFrequency, which the deferred write rules allow only on a Bermudan
            // entry.
            ["StructuredCredit"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CallScheduleKey] =
                    """[{"callDate":"2027-07-15","callEndDate":"2031-04-15","exerciseStyle":"Bermudan","exerciseFrequency":"Quarterly","callPrice":100,"priceBasis":"PercentOfPar","priceType":"Fixed","partialRedemption":"ProRata","minimumNoticeDays":10,"maximumNoticeDays":30,"noticeDayType":"Business"}]""",
                [PutScheduleKey] =
                    """[{"putDate":"2030-07-15","exerciseStyle":"European","putPrice":100,"priceBasis":"PercentOfPar","minimumNoticeDays":15,"maximumNoticeDays":30,"noticeDayType":"Calendar"},{"putDate":"2029-07-15","putEndDate":"2030-01-15","exerciseStyle":"Bermudan","exerciseFrequency":"SemiAnnual","putPrice":100,"priceBasis":"PercentOfPar","minimumNoticeDays":15,"maximumNoticeDays":30,"noticeDayType":"Calendar"}]"""
            }
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

    // The spellings of an unset call or put schedule a write accepts and ignores (ADR-024), beside
    // an entry whose date alone is null and the all-null entry of each shape.
    private static readonly string[] UnpopulatedScheduleSpellings =
    [
        "null",
        "[]",
        "[null]",
        "[{}]",
        """[{"exerciseFrequency":null}]"""
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

    // Every readable twin: each class at each version its twin data introduces a term at.
    public static TheoryData<string, int> ReadableTwinCases()
    {
        var data = new TheoryData<string, int>();
        foreach (var assetClass in ReadableTwinBlocks.Keys)
        {
            foreach (var version in ReadableTwinVersionsOf(assetClass))
            {
                data.Add(assetClass, version);
            }
        }

        return data;
    }

    // Every unset spelling of every versioned term a readable-ahead class declares at the top level or
    // on its legs: the block, and the call and put schedules.
    public static TheoryData<string, string, string> UnpopulatedScheduleConventionCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var assetClass in ReadableTwinBlocks.Keys)
        {
            foreach (var spelling in UnpopulatedScheduleConventionSpellings)
            {
                data.Add(assetClass, ScheduleConventionsKey, spelling);
            }

            foreach (var key in ScheduleKeys.Where(schedule => SecurityAssetTermsSchema.Field(assetClass, schedule) is not null))
            {
                foreach (var spelling in UnpopulatedScheduleSpellings)
                {
                    data.Add(assetClass, key, spelling);
                }

                data.Add(assetClass, key, $$"""[{"{{ScheduleDateKey(key)}}":null}]""");
                data.Add(assetClass, key, $"[{AllNullScheduleEntry(key)}]");
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
            for (var version = 1; version < BlockSince(assetClass); version++)
            {
                data.Add(assetClass, version);
            }
        }

        return data;
    }

    // ── (a) Coverage ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReadableTwins_CoverExactlyTheHistoricalVersionsAboveTheDocumentFloor()
    {
        var readAhead = SecurityAssetClassCatalog.AssetClasses
            .Where(static assetClass => SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass) > SecurityAssetClassCatalog.GetStampFloor(assetClass))
            .ToArray();

        ReadableTwinBlocks.Keys.Should().BeEquivalentTo(
            readAhead,
            "every class that reads a terms version it does not write needs a readable twin, and a twin for a class " +
            "that reads only what it writes would be refused on read");

        var readableVersions = readAhead.SelectMany(assetClass => Enumerable
            .Range(
                SecurityAssetClassCatalog.GetStampFloor(assetClass) + 1,
                SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass) - SecurityAssetClassCatalog.GetStampFloor(assetClass))
            .Select(version => $"{assetClass} v{version}"));
        ReadableTwinBlocks.Keys
            .SelectMany(assetClass => ReadableTwinVersionsOf(assetClass).Select(version => $"{assetClass} v{version}"))
            .Should().BeEquivalentTo(
                readableVersions,
                "every version a class reads but does not write needs a twin stamped at it, built from the data that introduces " +
                "that version's terms, and a twin above what the class reads would be refused on read");
    }

    [Theory]
    [MemberData(nameof(ReadableTwinCases))]
    public void ReadableTwin_PopulatesEveryTermAboveTheDocumentFloor(string assetClass, int version)
    {
        // A later expand step that adds another term to the class has to grow the twin with it. The
        // check is by path, so a swap twin must populate the block on every leg, not just one. A twin
        // populates exactly the terms above the write version up to its own version, and none above.
        var writes = SecurityAssetClassCatalog.GetStampFloor(assetClass);
        var twin = ReadableTwin(assetClass, version);
        var expected = new List<string>();
        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
        {
            if (field.SinceTermsVersion > version)
            {
                continue;
            }

            if (field.SinceTermsVersion > writes)
            {
                expected.Add(field.Key);
                continue;
            }

            var inner = SecurityAssetTermsSchema.ElementFields(assetClass, field.Key)
                .Where(inner => inner.SinceTermsVersion > writes && inner.SinceTermsVersion <= version)
                .ToArray();
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
            .Should().BeEquivalentTo(
                expected,
                $"the '{assetClass}' readable twin at version {version} must populate every term the node reads but does not write " +
                "up to that version, on every container, and nothing above it");
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(twin).Should().Be(version);
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

    [Fact]
    public void ReadableTwinSchedules_SetEveryMemberToAPopulatedValue()
    {
        // The round trip (b) compares each rewritten entry with the stored one, an unset member as
        // null, so a member no twin entry sets would hide a member either codec side drops. Across the
        // twins every member of each entry shape is therefore set at least once, and every member an
        // entry names is set to a populated value.
        foreach (var key in ScheduleKeys)
        {
            var members = CanonicalScheduleEntryMembers(key);
            var dateKey = ScheduleDateKey(key);
            var named = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (assetClass, schedules) in ReadableTwinSchedules)
            {
                schedules.Keys.Should().BeEquivalentTo(ScheduleKeys, $"the '{assetClass}' twin declares both schedules");
                SecurityAssetTermsSchema.ElementFields(assetClass, key).Select(static field => field.Key).Order(StringComparer.Ordinal)
                    .Should().Equal(members, $"'{assetClass}.{key}' declares exactly the frozen entry members");

                var entries = JsonNode.Parse(schedules[key])!.AsArray();
                entries.Should().NotBeEmpty($"the '{assetClass}' twin must declare at least one '{key}' entry");
                for (var index = 0; index < entries.Count; index++)
                {
                    var label = $"the '{assetClass}' twin {key}[{index}]";
                    var entry = entries[index].Should().BeOfType<JsonObject>($"{label} must be an object").Subject;

                    entry.Select(static member => member.Key)
                        .Should().BeSubsetOf(members, $"{label} names only declared members")
                        .And.Contain(dateKey, $"{label} must carry its date, or the reader drops it");
                    foreach (var (name, value) in entry)
                    {
                        value.Should().NotBeNull($"{label} must set '{name}'");
                        SecurityAssetTermsSchema.IsPopulatedTermValue(JsonSerializer.SerializeToElement(value))
                            .Should().BeTrue($"{label} must populate '{name}'");
                        if (value!.GetValueKind() == JsonValueKind.String)
                        {
                            value.GetValue<string>().Should().NotBeNullOrWhiteSpace($"{label} must set '{name}' to a non-blank value");
                        }

                        named.Add(name);
                    }
                }
            }

            named.Should().BeEquivalentTo(members, $"across the twins every '{key}' entry member must be populated in at least one entry");
        }

        ReadableTwinSchedules.Values.Should().Contain(
            schedules => IsStoredOutOfDateOrder(schedules[CallScheduleKey], "callDate"),
            "a twin that stores its calls out of date order proves the read path keeps the stored order");
        ReadableTwinSchedules.Values.Should().Contain(
            schedules => IsStoredOutOfDateOrder(schedules[PutScheduleKey], "putDate"),
            "a twin that stores its puts out of date order proves the read path keeps the stored put order");
        ReadableTwinSchedules.Values.Should().Contain(
            schedules => IsStoredOutOfCanonicalMemberOrder(schedules[CallScheduleKey], CanonicalCallScheduleEntryMembers),
            "a twin that stores an entry's members out of canonical order proves the writer canonicalizes them");
    }

    // ── (b) Read-path round trip ────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(ReadableTwinCases))]
    public void ReadableTwin_RoundTripsByteStableThroughTheReadPath_AtItsReadableVersion(string assetClass, int version)
    {
        var twin = ReadableTwin(assetClass, version);

        var firstPass = ReadAndRewrite(assetClass, twin);
        var secondPass = ReadAndRewrite(assetClass, Parse(firstPass));

        secondPass.Should().Be(firstPass, $"the '{assetClass}' read path (read → write → read → write) must be lossless at version {version}");

        using var written = JsonDocument.Parse(firstPass);
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(written.RootElement)
            .Should().Be(version, "the twin keeps the stamp it was read at");
        JsonNode.DeepEquals(JsonNode.Parse(firstPass), WithCanonicalScheduleEntries(twin)).Should().BeTrue(
            $"the rewritten '{assetClass}' terms must carry every value of the stored twin, an entry member it leaves unset as null.{Environment.NewLine}" +
            $"stored:    {twin.GetRawText()}{Environment.NewLine}rewritten: {firstPass}");

        foreach (var key in ScheduleKeys)
        {
            if (!twin.TryGetProperty(key, out var storedEntries))
            {
                written.RootElement.TryGetProperty(key, out _).Should().BeFalse($"the '{assetClass}' twin at version {version} declares no '{key}'");
                continue;
            }

            var dateKey = ScheduleDateKey(key);
            var writtenEntries = written.RootElement.GetProperty(key).EnumerateArray().ToArray();
            writtenEntries.Select(entry => entry.GetProperty(dateKey).GetString())
                .Should().Equal(
                    storedEntries.EnumerateArray().Select(entry => entry.GetProperty(dateKey).GetString()),
                    $"'{assetClass}.{key}' keeps the stored entry order, whatever the dates");
            for (var index = 0; index < writtenEntries.Length; index++)
            {
                writtenEntries[index].EnumerateObject().Select(static member => member.Name)
                    .Should().Equal(CanonicalScheduleEntryMembers(key), $"{key}[{index}] is written with every member, in ordinal order");
            }
        }

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
    [MemberData(nameof(ReadableTwinCases))]
    public void ReadableTwin_WritesExactlyTheFieldsItsVersionDeclares_AndTheCanonicalBlock(string assetClass, int version)
    {
        var writes = SecurityAssetClassCatalog.GetStampFloor(assetClass);
        using var written = JsonDocument.Parse(ReadAndRewrite(assetClass, ReadableTwin(assetClass, version)));

        var writtenKeys = ElementKeys(written.RootElement);
        writtenKeys.RemoveWhere(SecurityTermsEnvelope.IsReservedKey);
        writtenKeys.Should().BeEquivalentTo(
            SecurityAssetTermsSchema.Fields(assetClass).Where(field => field.SinceTermsVersion <= version).Select(static field => field.Key),
            $"a '{assetClass}' row read at version {version} is rewritten with exactly the fields version {version} declares");

        foreach (var container in SecurityAssetTermsSchema.Fields(assetClass).Where(static field => field.Type == SecurityAssetTermFieldType.Array))
        {
            var elementFields = SecurityAssetTermsSchema.ElementFields(assetClass, container.Key);
            if (!elementFields.Any(field => field.SinceTermsVersion > writes))
            {
                continue;
            }

            var declared = elementFields.Where(field => field.SinceTermsVersion <= version).Select(static field => field.Key).ToArray();
            var index = 0;
            foreach (var element in written.RootElement.GetProperty(container.Key).EnumerateArray())
            {
                ElementKeys(element).Should().BeEquivalentTo(
                    declared,
                    $"every '{assetClass}.{container.Key}[{index}]' element carries the version-{version} element fields, the block included");
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

        foreach (var key in ScheduleKeys)
        {
            if (SecurityAssetTermsSchema.Field(assetClass, key) is not { } field || field.SinceTermsVersion > version)
            {
                continue;
            }

            var members = CanonicalScheduleEntryMembers(key);
            SecurityAssetTermsSchema.ElementFields(assetClass, key).Select(static inner => inner.Key).Order(StringComparer.Ordinal)
                .Should().Equal(members, "the frozen entry form lists exactly the declared members");

            var entries = written.RootElement.GetProperty(key);
            entries.ValueKind.Should().Be(JsonValueKind.Array, $"'{assetClass}.{key}' is declared at version {version}");
            entries.GetArrayLength().Should().BePositive();
            var index = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                entry.EnumerateObject().Select(static member => member.Name)
                    .Should().Equal(members, $"{key}[{index}] is written in the frozen canonical form: every member, ordinal, null when unset");
                index++;
            }
        }
    }

    [Fact]
    public void ReadableCertificateOfDeposit_AtVersionThree_WritesItsSchedulesInTheFrozenForm_BeforeCallableDate()
    {
        var written = ReadAndRewrite("CertificateOfDeposit", ReadableTwin("CertificateOfDeposit", 3));

        written.Should().Be(
            """{"callSchedule":[{"callDate":"2026-09-30","callEndDate":"2027-06-30","callPrice":100,"exerciseFrequency":"Quarterly","exerciseStyle":"Bermudan","makeWholeSpreadBps":null,"maximumNoticeDays":45,"minimumNoticeDays":30,"noticeDayType":"Business","partialRedemption":"WholeOnly","priceBasis":"PercentOfPar","priceType":"Fixed"}],"callableDate":"2026-09-30","couponRate":4.4,"dayCount":"ACT/365","issuerName":"First Meridian Bank","maturity":"2027-09-30","putSchedule":[{"exerciseFrequency":null,"exerciseStyle":"European","maximumNoticeDays":20,"minimumNoticeDays":5,"noticeDayType":"Business","priceBasis":"PercentOfPar","putDate":"2027-03-31","putEndDate":null,"putPrice":100}],"scheduleConventions":{"accrualAdjusted":false,"businessDayConvention":"Following","couponCashBasis":"Level","couponRateUnit":"Percent","direction":"Forward","endOfMonthRule":true,"firstRegularDate":"2026-03-31","lastRegularDate":"2027-06-30","paymentCalendars":["USNY"],"paymentLagBusinessDays":0,"rollConvention":"EndOfMonth","stubPolicy":"Long"},"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3}""",
            "keys sort ordinally, so 'callSchedule' precedes 'callableDate' ('S' sorts before 'a'), and each entry carries every " +
            "member in ordinal order with null for an unset one (ADR-024's frozen form)");
        ReadAndRewrite("CertificateOfDeposit", Parse(written)).Should().Be(written);
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
    [InlineData("Bond", 2, 1, 3)]
    [InlineData("Bond", 3, 1, 3)]
    [InlineData("CertificateOfDeposit", 2, 1, 3)]
    [InlineData("CertificateOfDeposit", 3, 1, 3)]
    [InlineData("StructuredCredit", 3, 2, 4)]
    [InlineData("StructuredCredit", 4, 2, 4)]
    [InlineData("Swap", 2, 1, 2)]
    public void HistoricalReadableTwin_WriteUsesCurrentAuthorityAndSemanticRules(string assetClass, int version, int writes, int reads)
    {
        var twin = ReadableTwin(assetClass, version);
        var expected =
            $"assetClassTermsVersion '{version}' for asset class '{assetClass}' is readable but not writable on this node: " +
            $"it reads versions 1 through {reads} and writes version {writes}. " +
            $"Submit the terms at version {writes}, or apply the change from a node that writes version {version}.";

        AssertCurrentWriteBoundary(assetClass, twin, expected);
    }

    [Theory]
    [InlineData("Bond", 2, 1, 2, "scheduleConventions", true)]
    [InlineData("Bond", 3, 1, 2, "scheduleConventions", true)]
    [InlineData("Bond", 3, 1, 3, "callSchedule", false)]
    [InlineData("CertificateOfDeposit", 2, 1, 2, "scheduleConventions", true)]
    [InlineData("CertificateOfDeposit", 3, 1, 2, "scheduleConventions", true)]
    [InlineData("CertificateOfDeposit", 3, 1, 3, "callSchedule", false)]
    [InlineData("StructuredCredit", 3, 2, 3, "scheduleConventions", true)]
    [InlineData("StructuredCredit", 4, 2, 3, "scheduleConventions", true)]
    [InlineData("StructuredCredit", 4, 2, 4, "callSchedule", false)]
    [InlineData("Swap", 2, 1, 2, "legs[0].scheduleConventions", true)]
    public void HistoricalReadableTwin_RestampedAtTheFloor_StillUsesCurrentWriteRules(
        string assetClass,
        int version,
        int writes,
        int since,
        string path,
        bool withBlock)
    {
        // Dropping the stamp to the write version does not make the twin's terms writable: a write that
        // populates one is refused on the term itself, at the first populated path in schema order. The
        // block comes before the schedules, so only a twin without it is refused on its call schedule.
        var twin = JsonNode.Parse(ReadableTwin(assetClass, version).GetRawText())!.AsObject();
        if (!withBlock)
        {
            twin.Remove(ScheduleConventionsKey).Should().BeTrue($"the '{assetClass}' twin at version {version} carries the block");
        }

        var restamped = Parse(SecurityTermsEnvelope.StampFlat(twin.ToJsonString(), assetClass));
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(restamped).Should().Be(writes);
        var expected =
            $"'{path}' is an asset-class terms version {since} term of '{assetClass}'. This node reads it but writes " +
            $"assetClassTermsVersion {writes}, so a create, amend, import or field edit cannot set it yet. " +
            "Leave it absent, null or empty (ADR-024).";

        AssertCurrentWriteBoundary(assetClass, restamped, expected);
    }

    private static void AssertCurrentWriteBoundary(string assetClass, JsonElement terms, string oldReadOnlyReason)
    {
        var create = () => SecurityMasterMapping.ToCreateCommand(CreateRequest(assetClass, terms));
        var amend = () => SecurityMasterMapping.ToAmendCommand(AmendRequest(terms), CurrentProjection(assetClass));
        if (SecurityAssetClassCatalog.GetOrDefault(assetClass).TermsVersion < SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass))
        {
            create.Should().Throw<InvalidOperationException>().WithMessage(oldReadOnlyReason);
            amend.Should().Throw<InvalidOperationException>().WithMessage(oldReadOnlyReason);
            return;
        }
        // The frozen twins prove historical read behavior, not that their old declared terms meet
        // today's scheduling semantics. A newly writable payload must obey those exact pure rules.
        InvalidOperationException? semantic = null;
        try
        { ScheduleTermsWriteRules.Validate(assetClass, terms); }
        catch (InvalidOperationException exception) { semantic = exception; }
        if (semantic is null)
        {
            create.Should().NotThrow();
            amend.Should().NotThrow();
        }
        else
        {
            create.Should().Throw<InvalidOperationException>().WithMessage(semantic.Message);
            amend.Should().Throw<InvalidOperationException>().WithMessage(semantic.Message);
        }
    }

    // ── (e) Unpopulated spellings are accepted and write the golden bytes ───────────────────────

    [Theory]
    [MemberData(nameof(UnpopulatedScheduleConventionCases))]
    public void UnpopulatedScheduleConventions_AreAcceptedOnWrite_AndWriteTheGoldenBytes(string assetClass, string key, string spelling)
    {
        var golden = SerializeThroughDomain(assetClass, FullPayloads[assetClass]).GetRawText();
        var payload = JsonNode.Parse(JsonSerializer.Serialize(FullPayloads[assetClass]))!.AsObject();
        IReadOnlyList<JsonObject> containers = key == ScheduleConventionsKey ? BlockContainers(assetClass, payload) : [payload];
        foreach (var container in containers)
        {
            container[key] = JsonNode.Parse(spelling);
        }

        var snapshot = CreateSnapshot(assetClass, JsonSerializer.SerializeToElement(payload));
        snapshot.AssetSpecificTermsJson.Should().Be(golden,
            $"an unset '{key}' ({spelling}) preserves the '{assetClass}' document's floor bytes regardless of its writable ceiling");
    }

    // ── (f) A payload stamped at or below the write version never decodes the block ─────────────

    [Theory]
    [MemberData(nameof(WritableStampCases))]
    public void StrayBlock_OnAPayloadStampedAtOrBelowTheWriteVersion_IsNotDecoded(string assetClass, int? stamp)
    {
        // The block is decoded only from a payload stamped at or above the version that declares it,
        // and so are the call and put schedules; stray ones on an older or unstamped row are ignored,
        // so the row reads and rewrites as before.
        var golden = SerializeThroughDomain(assetClass, FullPayloads[assetClass]).GetRawText();
        var stored = JsonNode.Parse(ReadableTwin(assetClass, ReadableTwinVersionsOf(assetClass)[^1]).GetRawText())!.AsObject();
        if (ReadableTwinSchedules.ContainsKey(assetClass))
        {
            (stored.ContainsKey(CallScheduleKey) && stored.ContainsKey(PutScheduleKey))
                .Should().BeTrue($"the stray '{assetClass}' row carries populated schedules too");
        }

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
        DecodedCallPutSchedules(record.Kind).Where(static schedules => !CallPutSchedulesModule.IsEmpty(schedules))
            .Should().BeEmpty("call and put schedules above the payload's stamp are not decoded");
        new SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson.Should().Be(
            golden,
            $"the '{assetClass}' row stamped {stamp?.ToString() ?? "nothing"} rewrites exactly as a write of the same terms without the block or the schedules");
    }

    [Theory]
    [InlineData("Bond", 2)]
    [InlineData("CertificateOfDeposit", 2)]
    [InlineData("StructuredCredit", 3)]
    public void StrayCallPutSchedules_OnAPayloadStampedAtTheBlockVersion_AreNotDecoded(string assetClass, int stamp)
    {
        // At the block's own version the block is a declared term and is decoded, but the schedules
        // belong to the next version: on this row they are undeclared keys, so it reads and rewrites
        // exactly as increment 4a's block-only twin, stamp included.
        BlockSince(assetClass).Should().Be(stamp);
        var stored = JsonNode.Parse(ReadableTwin(assetClass, ReadableTwinVersionsOf(assetClass)[^1]).GetRawText())!.AsObject();
        (stored.ContainsKey(CallScheduleKey) && stored.ContainsKey(PutScheduleKey))
            .Should().BeTrue($"the stray '{assetClass}' row carries populated schedules");
        stored[SecurityTermsEnvelope.AssetClassTermsVersionKey] = stamp;

        var record = SecurityMasterMapping.ToRecord(LegacyProjection(assetClass, JsonSerializer.SerializeToElement(stored)));

        ScheduleConventionsModule.IsEmpty(DecodedScheduleConventions(record.Kind).Single())
            .Should().BeFalse("the block is declared at the payload's stamp");
        CallPutSchedulesModule.IsEmpty(DecodedCallPutSchedules(record.Kind).Single())
            .Should().BeTrue("call and put schedules above the payload's stamp are not decoded");
        new SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson.Should().Be(
            ReadAndRewrite(assetClass, ReadableTwin(assetClass)),
            $"the '{assetClass}' row stamped {stamp} rewrites exactly as the twin without the schedules");
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

    // Rows stamped at the call and put schedules' version, which this node reads but does not write.
    private const string ForeignPartialCallEntryAtStampThree =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"maturity":"2030-01-01","couponType":"ZeroCoupon","isCallable":false,"subclass":"Sovereign","callSchedule":[{"callDate":"2031-06-15","callPrice":101}]}""";

    private const string ForeignPartialCallEntryPersisted =
        """{"callDate":null,"callSchedule":[{"callDate":"2031-06-15","callEndDate":null,"callPrice":101,"exerciseFrequency":null,"exerciseStyle":null,"makeWholeSpreadBps":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"partialRedemption":null,"priceBasis":null,"priceType":null}],"capRate":null,"couponRate":null,"couponType":"ZeroCoupon","dayCount":null,"floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":null,"issuerName":null,"legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":null,"paymentFrequency":null,"preRefundDate":null,"principalSchedule":[],"putSchedule":[],"scheduleConventions":null,"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"seniority":null,"spreadBps":null,"stepSchedule":[],"subclass":"Sovereign"}""";

    private const string ForeignDatelessCallEntryAtStampThree =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"maturity":"2030-01-01","couponType":"ZeroCoupon","isCallable":false,"subclass":"Sovereign","callSchedule":[{"callDate":"2031-06-15"},{"callDate":null}]}""";

    private const string ForeignDatelessCallEntryPersisted =
        """{"callDate":null,"callSchedule":[{"callDate":"2031-06-15","callEndDate":null,"callPrice":null,"exerciseFrequency":null,"exerciseStyle":null,"makeWholeSpreadBps":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"partialRedemption":null,"priceBasis":null,"priceType":null}],"capRate":null,"couponRate":null,"couponType":"ZeroCoupon","dayCount":null,"floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":null,"issuerName":null,"legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":null,"paymentFrequency":null,"preRefundDate":null,"principalSchedule":[],"putSchedule":[],"scheduleConventions":null,"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"seniority":null,"spreadBps":null,"stepSchedule":[],"subclass":"Sovereign"}""";

    private const string ForeignAllNullCallEntryWithoutBlockAtStampThree =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"maturity":"2030-01-01","couponType":"ZeroCoupon","isCallable":false,"subclass":"Sovereign","callSchedule":[""" + AllNullCallScheduleEntry + "]}";

    private const string ForeignPartialBondBlockAtStampThree =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"maturity":"2030-01-01","couponType":"ZeroCoupon","isCallable":false,"subclass":"Sovereign","scheduleConventions":{"stubPolicy":"Long"}}""";

    private const string ForeignCallAndPutWithoutBlockAtStampThree =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"maturity":"2030-01-01","couponType":"ZeroCoupon","isCallable":false,"subclass":"Sovereign","callSchedule":[{"callDate":"2031-06-15"}],"putSchedule":[{"putDate":"2029-06-15"}]}""";

    private const string ForeignCallAndPutWithoutBlockPersisted =
        """{"callDate":null,"callSchedule":[{"callDate":"2031-06-15","callEndDate":null,"callPrice":null,"exerciseFrequency":null,"exerciseStyle":null,"makeWholeSpreadBps":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"partialRedemption":null,"priceBasis":null,"priceType":null}],"capRate":null,"couponRate":null,"couponType":"ZeroCoupon","dayCount":null,"floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":null,"issuerName":null,"legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":null,"paymentFrequency":null,"preRefundDate":null,"principalSchedule":[],"putSchedule":[{"exerciseFrequency":null,"exerciseStyle":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"priceBasis":null,"putDate":"2029-06-15","putEndDate":null,"putPrice":null}],"scheduleConventions":null,"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"seniority":null,"spreadBps":null,"stepSchedule":[],"subclass":"Sovereign"}""";

    private const string ForeignNullPutScheduleBesideCallsAtStampThree =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"maturity":"2030-01-01","couponType":"ZeroCoupon","isCallable":false,"subclass":"Sovereign","callSchedule":[{"callDate":"2031-06-15"}],"putSchedule":null}""";

    private const string ForeignNullPutScheduleBesideCallsPersisted =
        """{"callDate":null,"callSchedule":[{"callDate":"2031-06-15","callEndDate":null,"callPrice":null,"exerciseFrequency":null,"exerciseStyle":null,"makeWholeSpreadBps":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"partialRedemption":null,"priceBasis":null,"priceType":null}],"capRate":null,"couponRate":null,"couponType":"ZeroCoupon","dayCount":null,"floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":null,"issuerName":null,"legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":null,"paymentFrequency":null,"preRefundDate":null,"principalSchedule":[],"putSchedule":[],"scheduleConventions":null,"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"seniority":null,"spreadBps":null,"stepSchedule":[],"subclass":"Sovereign"}""";

    private const string ForeignUnsortedCallEntriesAtStampThree =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"maturity":"2030-01-01","couponType":"ZeroCoupon","isCallable":false,"subclass":"Sovereign","callSchedule":[{"callDate":"2032-06-15"},{"callDate":"2030-06-15"}]}""";

    private const string ForeignUnsortedCallEntriesPersisted =
        """{"callDate":null,"callSchedule":[{"callDate":"2032-06-15","callEndDate":null,"callPrice":null,"exerciseFrequency":null,"exerciseStyle":null,"makeWholeSpreadBps":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"partialRedemption":null,"priceBasis":null,"priceType":null},{"callDate":"2030-06-15","callEndDate":null,"callPrice":null,"exerciseFrequency":null,"exerciseStyle":null,"makeWholeSpreadBps":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"partialRedemption":null,"priceBasis":null,"priceType":null}],"capRate":null,"couponRate":null,"couponType":"ZeroCoupon","dayCount":null,"floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":null,"issuerName":null,"legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":null,"paymentFrequency":null,"preRefundDate":null,"principalSchedule":[],"putSchedule":[],"scheduleConventions":null,"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"seniority":null,"spreadBps":null,"stepSchedule":[],"subclass":"Sovereign"}""";

    private const string ForeignUnsortedPutEntriesAtStampThree =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"maturity":"2030-01-01","couponType":"ZeroCoupon","isCallable":false,"subclass":"Sovereign","putSchedule":[{"putDate":"2030-06-15"},{"putDate":"2029-06-15"}]}""";

    private const string ForeignUnsortedPutEntriesPersisted =
        """{"callDate":null,"callSchedule":[],"capRate":null,"couponRate":null,"couponType":"ZeroCoupon","dayCount":null,"floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":null,"issuerName":null,"legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":null,"paymentFrequency":null,"preRefundDate":null,"principalSchedule":[],"putSchedule":[{"exerciseFrequency":null,"exerciseStyle":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"priceBasis":null,"putDate":"2030-06-15","putEndDate":null,"putPrice":null},{"exerciseFrequency":null,"exerciseStyle":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"priceBasis":null,"putDate":"2029-06-15","putEndDate":null,"putPrice":null}],"scheduleConventions":null,"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"seniority":null,"spreadBps":null,"stepSchedule":[],"subclass":"Sovereign"}""";

    private const string ForeignLowercaseExerciseStyleAtStampThree =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"maturity":"2030-01-01","couponType":"ZeroCoupon","isCallable":false,"subclass":"Sovereign","callSchedule":[{"callDate":"2031-06-15","exerciseStyle":"american"}]}""";

    private const string ForeignLowercaseExerciseStylePersisted =
        """{"callDate":null,"callSchedule":[{"callDate":"2031-06-15","callEndDate":null,"callPrice":null,"exerciseFrequency":null,"exerciseStyle":"american","makeWholeSpreadBps":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"partialRedemption":null,"priceBasis":null,"priceType":null}],"capRate":null,"couponRate":null,"couponType":"ZeroCoupon","dayCount":null,"floatingIndex":null,"floorRate":null,"inflationBaseIndexValue":null,"inflationIndex":null,"inflationIndexRatio":null,"isCallable":false,"issueDate":null,"issuerName":null,"legalFinalMaturity":null,"mandatoryPutDate":null,"maturity":"2030-01-01","par":null,"paymentFrequency":null,"preRefundDate":null,"principalSchedule":[],"putSchedule":[],"scheduleConventions":null,"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":3,"seniority":null,"spreadBps":null,"stepSchedule":[],"subclass":"Sovereign"}""";

    private const string ForeignStructuredCreditPutsOnlyAtStampFour =
        """{"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":4,"tranche":"A","collateralType":"CLO","originalFace":5000000,"couponOrIndex":"SOFR","maturity":"2031-06-15","putSchedule":[{"putDate":"2029-07-15","putPrice":100}]}""";

    private const string ForeignStructuredCreditPutsOnlyPersisted =
        """{"attachmentPoint":null,"callSchedule":[],"collateralType":"CLO","couponOrIndex":"SOFR","currentFactor":null,"detachmentPoint":null,"factorSchedule":null,"factorScheduleEntries":[],"maturity":"2031-06-15","nonCallPeriodEnd":null,"originalFace":5000000,"poolId":null,"putSchedule":[{"exerciseFrequency":null,"exerciseStyle":null,"maximumNoticeDays":null,"minimumNoticeDays":null,"noticeDayType":null,"priceBasis":null,"putDate":"2029-07-15","putEndDate":null,"putPrice":100}],"reinvestmentPeriodEnd":null,"scheduleConventions":null,"schemaVersion":1,"termsFormat":"flat","assetClassTermsVersion":4,"seniority":null,"structuredCreditKind":null,"tranche":"A","weightedAverageCoupon":null,"weightedAverageLoanAgeMonths":null,"weightedAverageMaturityMonths":null}""";

    private const string Deactivate = "deactivate";
    private const string AmendWithoutTermsPatch = "amend-without-terms-patch";

    private static readonly IReadOnlyDictionary<string, (string AssetClass, string Stored, string Persisted)> ForeignRows =
        new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
        {
            ["partialBondBlock"] = ("Bond", ForeignPartialBondBlockAtStampTwo, ForeignPartialBondBlockPersisted),
            ["nullBondBlock"] = ("Bond", ForeignNullBondBlockAtStampTwo, ForeignNullBondBlockPersisted),
            ["swapWithoutLegBlocks"] = ("Swap", ForeignSwapWithoutLegBlocksAtStampTwo, ForeignSwapWithoutLegBlocksPersisted),
            // A partial entry is written with its ten unset members as null, at the stamp it was read at.
            ["partialCallEntry"] = ("Bond", ForeignPartialCallEntryAtStampThree, ForeignPartialCallEntryPersisted),
            // An entry without a date is not a term: the list is compacted to the dated entry.
            ["datelessCallEntry"] = ("Bond", ForeignDatelessCallEntryAtStampThree, ForeignDatelessCallEntryPersisted),
            // A row whose only versioned term is an all-null entry declares nothing: written at the
            // write version, with no versioned key.
            ["allNullCallEntryWithoutBlock"] = ("Bond", ForeignAllNullCallEntryWithoutBlockAtStampThree, ForeignNullBondBlockPersisted),
            // Conventions alone need only the block's version.
            ["bondBlockOnlyAtStampThree"] = ("Bond", ForeignPartialBondBlockAtStampThree, ForeignPartialBondBlockPersisted),
            // Schedules without a block keep their version and write the block key as null.
            ["callAndPutWithoutBlock"] = ("Bond", ForeignCallAndPutWithoutBlockAtStampThree, ForeignCallAndPutWithoutBlockPersisted),
            // An explicitly null putSchedule beside declared calls is written as [].
            ["nullPutScheduleBesideCalls"] = ("Bond", ForeignNullPutScheduleBesideCallsAtStampThree, ForeignNullPutScheduleBesideCallsPersisted),
            // Entries keep their stored order, which is not date order.
            ["unsortedCallEntries"] = ("Bond", ForeignUnsortedCallEntriesAtStampThree, ForeignUnsortedCallEntriesPersisted),
            ["unsortedPutEntries"] = ("Bond", ForeignUnsortedPutEntriesAtStampThree, ForeignUnsortedPutEntriesPersisted),
            // An undeclared vocabulary spelling is carried verbatim.
            ["lowercaseExerciseStyle"] = ("Bond", ForeignLowercaseExerciseStyleAtStampThree, ForeignLowercaseExerciseStylePersisted),
            // A structured credit that declares only puts writes its calls as [], at stamp 4.
            ["structuredCreditPutsOnly"] = ("StructuredCredit", ForeignStructuredCreditPutsOnlyAtStampFour, ForeignStructuredCreditPutsOnlyPersisted)
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
    public async Task ForeignRow_DeactivationPinsBytes_WhilePatchlessAmendmentRequiresValidDeclarations(string row, string operation)
    {
        // Deactivation preserves losslessly decodable historical terms. An amendment also checks
        // the complete canonical payload against today's semantic rules before any write.
        var (assetClass, stored, persisted) = ForeignRows[row];
        var harness = new PersistPathHarness(assetClass, stored);

        var amendmentRefusal = row switch
        {
            "partialBondBlock" or "bondBlockOnlyAtStampThree" => "*record carries no issueDate*",
            "unsortedPutEntries" or "structuredCreditPutsOnly" => "*exerciseStyle must be explicitly stated*",
            "partialCallEntry" or "datelessCallEntry" or "callAndPutWithoutBlock" or
                "nullPutScheduleBesideCalls" or "unsortedCallEntries" or "lowercaseExerciseStyle"
                => "*exercise window is inverted or extends beyond maturity*",
            _ => null
        };
        if (operation == AmendWithoutTermsPatch && amendmentRefusal is not null)
        {
            await FluentActions.Invoking(() => harness.RunAsync(operation))
                .Should().ThrowExactlyAsync<SecurityMasterWriteRefusedException>()
                .WithMessage(amendmentRefusal);
            harness.Appended.Should().BeEmpty();
            harness.Upserted.Should().BeEmpty();
            harness.Saved.Should().BeEmpty();
            return;
        }

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
    /// The increment-4a readable twin of <paramref name="assetClass"/>:
    /// <see cref="ReadableTwin(string, int)"/> at the version that introduced the block
    /// (<see cref="BlockSince"/>). It stays pinned there however far the class later reads, so what it
    /// means never moves.
    /// </summary>
    internal static JsonElement ReadableTwin(string assetClass)
        => ReadableTwin(assetClass, BlockSince(assetClass));

    /// <summary>
    /// The readable twin of <paramref name="assetClass"/> at <paramref name="version"/>: its
    /// <see cref="FullPayloads"/> entry written through the domain, with the fully-populated block of
    /// <see cref="ReadableTwinBlocks"/> added when <paramref name="version"/> is at or above the block's
    /// <see cref="BlockSince"/>, each schedule of <see cref="ReadableTwinSchedules"/> added when it is at
    /// or above that array's version, and the payload stamped <paramref name="version"/>.
    /// </summary>
    internal static JsonElement ReadableTwin(string assetClass, int version)
    {
        var terms = JsonNode.Parse(SerializeThroughDomain(assetClass, FullPayloads[assetClass]).GetRawText())!.AsObject();
        if (version >= BlockSince(assetClass))
        {
            AddBlocks(assetClass, terms, ReadableTwinBlocks[assetClass]);
        }

        if (ReadableTwinSchedules.TryGetValue(assetClass, out var schedules))
        {
            foreach (var key in ScheduleKeys)
            {
                if (schedules.TryGetValue(key, out var entries) && version >= ScheduleSince(assetClass, key))
                {
                    terms[key] = JsonNode.Parse(entries);
                }
            }
        }

        return Parse(SecurityTermsEnvelope.StampFlat(terms.ToJsonString(), assetClass, version));
    }

    /// <summary>
    /// The versions a readable twin of <paramref name="assetClass"/> is built at, ascending: each
    /// version that introduces a term the twin data populates (the block's <see cref="BlockSince"/>,
    /// and each <see cref="ReadableTwinSchedules"/> array's).
    /// </summary>
    private static IReadOnlyList<int> ReadableTwinVersionsOf(string assetClass)
    {
        var versions = new SortedSet<int> { BlockSince(assetClass) };
        if (ReadableTwinSchedules.TryGetValue(assetClass, out var schedules))
        {
            foreach (var key in schedules.Keys)
            {
                versions.Add(ScheduleSince(assetClass, key));
            }
        }

        return versions.ToArray();
    }

    // The terms version that introduced the callSchedule or putSchedule array of assetClass.
    private static int ScheduleSince(string assetClass, string key)
        => (SecurityAssetTermsSchema.Field(assetClass, key)
            ?? throw new InvalidOperationException($"'{assetClass}' declares no '{key}' term."))
            .SinceTermsVersion;

    private static IReadOnlyList<string> CanonicalScheduleEntryMembers(string key)
        => key == CallScheduleKey ? CanonicalCallScheduleEntryMembers : CanonicalPutScheduleEntryMembers;

    // The member without which a schedule entry is not a term (the reader drops it).
    private static string ScheduleDateKey(string key)
        => key == CallScheduleKey ? "callDate" : "putDate";

    private static string AllNullScheduleEntry(string key)
        => key == CallScheduleKey ? AllNullCallScheduleEntry : AllNullPutScheduleEntry;

    /// <summary>
    /// <paramref name="terms"/> with every schedule entry in the written form: all the entry shape's
    /// members, a member the stored entry leaves unset as null, in stored entry order.
    /// </summary>
    private static JsonObject WithCanonicalScheduleEntries(JsonElement terms)
    {
        var expected = JsonNode.Parse(terms.GetRawText())!.AsObject();
        foreach (var key in ScheduleKeys)
        {
            if (expected[key] is not JsonArray entries)
            {
                continue;
            }

            var canonical = new JsonArray();
            foreach (var entry in entries)
            {
                var stored = entry!.AsObject();
                var written = new JsonObject();
                foreach (var member in CanonicalScheduleEntryMembers(key))
                {
                    written[member] = stored[member]?.DeepClone();
                }

                canonical.Add(written);
            }

            expected[key] = canonical;
        }

        return expected;
    }

    private static bool IsStoredOutOfDateOrder(string entries, string dateKey)
    {
        var dates = JsonNode.Parse(entries)!.AsArray().Select(entry => entry![dateKey]!.GetValue<string>()).ToArray();
        return !dates.SequenceEqual(dates.Order(StringComparer.Ordinal));
    }

    private static bool IsStoredOutOfCanonicalMemberOrder(string entries, IReadOnlyList<string> canonicalMembers)
        => JsonNode.Parse(entries)!.AsArray().Any(entry =>
        {
            var names = entry!.AsObject().Select(static member => member.Key).ToArray();
            var named = new HashSet<string>(names, StringComparer.Ordinal);
            return !names.SequenceEqual(canonicalMembers.Where(named.Contains));
        });

    /// <summary>
    /// A readable twin of <paramref name="payload"/> carrying <paramref name="blocks"/>: one block for a
    /// class that declares it at the top level, one per leg (in order) for a swap. The payload is
    /// stamped <paramref name="version"/>, by default the block's <see cref="BlockSince"/>.
    /// </summary>
    internal static JsonElement ReadableTwin(string assetClass, object payload, IReadOnlyList<string> blocks, int? version = null)
    {
        var terms = JsonNode.Parse(SerializeThroughDomain(assetClass, payload).GetRawText())!.AsObject();
        AddBlocks(assetClass, terms, blocks);

        return Parse(SecurityTermsEnvelope.StampFlat(terms.ToJsonString(), assetClass, version ?? BlockSince(assetClass)));
    }

    private static void AddBlocks(string assetClass, JsonObject terms, IReadOnlyList<string> blocks)
    {
        var containers = BlockContainers(assetClass, terms);
        containers.Should().HaveCount(blocks.Count, $"the '{assetClass}' twin needs one block per block container");
        for (var index = 0; index < blocks.Count; index++)
        {
            containers[index][ScheduleConventionsKey] = JsonNode.Parse(blocks[index]);
        }
    }

    /// <summary>
    /// The terms version that introduced the scheduleConventions block of <paramref name="assetClass"/>:
    /// the top-level field's <see cref="SecurityAssetTermField.SinceTermsVersion"/>, or for a swap the
    /// leg element field's.
    /// </summary>
    private static int BlockSince(string assetClass)
        => (SecurityAssetTermsSchema.Field(assetClass, ScheduleConventionsKey)
            ?? SecurityAssetTermsSchema.ElementFields(assetClass, "legs").Single(static field => field.Key == ScheduleConventionsKey))
            .SinceTermsVersion;

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

    private static IReadOnlyList<CallPutSchedules> DecodedCallPutSchedules(SecurityKind kind)
        => kind switch
        {
            SecurityKind.Bond bond => [bond.Item.CallPutSchedules],
            SecurityKind.CertificateOfDeposit certificate => [certificate.Item.CallPutSchedules],
            SecurityKind.StructuredCredit structuredCredit => [structuredCredit.Item.CallPutSchedules],
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
            snapshotStore.SaveAsync(Arg.Any<SecuritySnapshotRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Saved.Add(call.Arg<SecuritySnapshotRecord>());
                    return Task.CompletedTask;
                });
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

        public List<SecuritySnapshotRecord> Saved { get; } = [];

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
