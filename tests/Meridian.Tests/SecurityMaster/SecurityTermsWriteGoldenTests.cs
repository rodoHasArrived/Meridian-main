using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Byte-level goldens of what the Security Master writes and reports for every asset class,
/// captured at base d92e29f5 before the readable-before-writable terms expansion (priority 5,
/// increment 4a; ADR-024). An expand step widens reads only, so for every existing record these
/// stay identical: the terms the write path persists (Write-mode create, the F# writer and the
/// stamp), the economic terms bridged from them, the asset-class registry DTOs both workstations
/// build their forms from, the term blocks, and the validation reports.
/// <para>Extended at 1e679876 (increment 4a as merged at 2537c8f8, plus the amortization refusal for
/// declared call and put schedules) before increment 4b, with what a node that reads the
/// scheduleConventions block does with stored rows: the validation reports of stored rows carrying
/// stray <c>callSchedule</c> and <c>putSchedule</c> keys, and the terms a re-serialization (a
/// deactivation or a patch-less amend) writes for increment 4a's readable twins, with their economic
/// terms, and for the stray rows at stamps 1, 2 and 3. Increment 4b must leave every entry, old and
/// new, unchanged.</para>
/// <para>The later typed-factor-history migration intentionally extends only the current
/// StructuredCredit economic outputs with their dated observations. Flat writes and all
/// no-history goldens remain unchanged; readable-version/container manifests stay frozen.</para>
/// </summary>
/// <remarks>
/// A missing or mismatched fixture writes <c>security-terms-write-golden.received.json</c> beside the
/// test binaries and fails; it never skips. Replace the committed fixture with the received file only
/// when the change in written or reported output is intended and approved.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SecurityTermsWriteGoldenTests
{
    private const string FixtureFileName = "security-terms-write-golden.json";
    private const string ReceivedFileName = "security-terms-write-golden.received.json";

    private static readonly Guid SecurityId = Guid.Parse("4a7e5c11-0000-4000-8000-0000000000a4");
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions FixtureOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Variant payloads beyond the one fully-populated payload per class: the shapes whose written
    // bytes the expansion touches (bonds, a multi-leg swap) and the stray-key validation cases.
    private static readonly IReadOnlyDictionary<string, (string AssetClass, object Payload)> Variants =
        new Dictionary<string, (string, object)>(StringComparer.Ordinal)
        {
            ["Bond.fixed"] = ("Bond", new
            {
                maturity = "2030-01-01",
                issueDate = "2024-01-01",
                couponType = "Fixed",
                couponRate = 4.25m,
                dayCount = "30/360",
                isCallable = false,
                issuerName = "ACME Corp",
                seniority = "Senior",
                subclass = "Corporate",
                par = 1000m,
                paymentFrequency = "SemiAnnual"
            }),
            ["Bond.zeroCoupon"] = ("Bond", new
            {
                maturity = "2030-01-01",
                couponType = "ZeroCoupon",
                isCallable = false,
                subclass = "Sovereign"
            }),
            ["Bond.step"] = ("Bond", new
            {
                maturity = "2032-06-30",
                issueDate = "2026-06-30",
                couponType = "Step",
                stepSchedule = new object[]
                {
                    new { effectiveDate = "2026-06-30", rate = 3.0m },
                    new { effectiveDate = "2028-06-30", rate = 4.0m },
                    new { effectiveDate = "2030-06-30", rate = 5.0m }
                },
                dayCount = "30/360",
                isCallable = false,
                subclass = "StepRate",
                paymentFrequency = "Annual"
            }),
            ["Bond.inflationLinked"] = ("Bond", new
            {
                maturity = "2036-01-15",
                issueDate = "2026-01-15",
                couponType = "InflationLinked",
                couponRate = 1.25m,
                inflationIndex = "CPI-U",
                inflationBaseIndexValue = 305.109m,
                inflationIndexRatio = 1.0432m,
                dayCount = "ACT/ACT",
                isCallable = false,
                subclass = "InflationLinked",
                par = 1000m,
                paymentFrequency = "SemiAnnual"
            }),
            ["Swap.threeLeg"] = ("Swap", new
            {
                effectiveDate = "2026-01-15",
                maturityDate = "2031-01-15",
                legs = new object[]
                {
                    new { legId = "fixed", legType = "Fixed", currency = "USD", direction = "Pay", fixedRate = 4.25m, notional = 10_000_000m, paymentFrequency = "SemiAnnual", dayCount = "30/360", exchangesPrincipal = false },
                    new { legId = "float", legType = "Floating", currency = "USD", direction = "Receive", index = "SOFR", spreadBps = 15m, notional = 10_000_000m, paymentFrequency = "Quarterly", dayCount = "ACT/360", exchangesPrincipal = false, notionalSchedule = Array.Empty<object>(), resetSchedule = Array.Empty<object>() },
                    new { legId = "basis", legType = "Floating", currency = "USD", direction = "Receive", index = "EFFR", notional = 5_000_000m, paymentFrequency = "Quarterly", dayCount = "ACT/360", exchangesPrincipal = false }
                }
            }),
        };

    // Increment 4a's readable twins, each at the version that introduced the scheduleConventions
    // block. The versions are literal so the keys and what they pin never move.
    private static readonly (string AssetClass, int Version)[] ReadableTwinVersions =
    [
        ("Bond", 2),
        ("CertificateOfDeposit", 2),
        ("StructuredCredit", 3),
        ("Swap", 2),
    ];

    // The stored stray rows (see StoredOnlyCases) whose re-serialization is pinned: what a deactivation
    // or a patch-less amend persists for each, the stray key dropped and the stamp kept or downgraded
    // as increment 4a does.
    private static readonly IReadOnlyDictionary<string, string> StrayRowRewrites =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Bond.v1StrayCallSchedule"] = "Bond.strayCallScheduleString",
            ["Bond.v2StrayCallSchedule"] = "Bond.stampTwoStrayCallSchedule",
            ["StructuredCredit.v3StrayCallSchedule"] = "StructuredCredit.stampThreeStrayCallSchedule",
        };

    private const string StrayCallSchedule = """[{"callDate":"2030-06-15","callPrice":101}]""";
    private const string StrayPutSchedule = """[{"putDate":"2029-06-15"}]""";

    // The unset spellings of the call and put schedules, as (callSchedule, putSchedule) pairs. This node
    // does not declare the keys and drops them; a node that reads them finds nothing populated, so it
    // refuses none of them and writes at its write version, without them.
    private static readonly IReadOnlyDictionary<string, (string CallSchedule, string PutSchedule)> UnpopulatedCallAndPutSchedules =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["null"] = ("null", "null"),
            ["empty"] = ("[]", "[]"),
            ["nullEntry"] = ("[null]", "[null]"),
            ["emptyEntry"] = ("[{}]", "[{}]"),
            ["nullDate"] = ("""[{"callDate":null}]""", """[{"putDate":null}]"""),
            ["nullExerciseFrequency"] = ("""[{"exerciseFrequency":null}]""", """[{"exerciseFrequency":null}]"""),
            ["allNullEntry"] = (
                """[{"callDate":null,"callEndDate":null,"exerciseStyle":null,"exerciseFrequency":null,"callPrice":null,"priceBasis":null,"priceType":null,"makeWholeSpreadBps":null,"partialRedemption":null,"minimumNoticeDays":null,"maximumNoticeDays":null,"noticeDayType":null}]""",
                """[{"putDate":null,"putEndDate":null,"exerciseStyle":null,"exerciseFrequency":null,"putPrice":null,"priceBasis":null,"minimumNoticeDays":null,"maximumNoticeDays":null,"noticeDayType":null}]"""),
        };

    private static readonly Lazy<IReadOnlyDictionary<string, string>?> CommittedGolden = new(static () =>
        LocateFixture() is { } path
            ? JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(path))
            : null);

    [Fact]
    public void WrittenAndReportedOutput_MatchesTheCommittedGolden()
    {
        var received = Compute();

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
    public void EveryFullAndVariantPayload_IsStillAcceptedByTheWritePath()
    {
        foreach (var (assetClass, payload) in AllPayloads().Select(static entry => entry.Value))
        {
            var command = () => SecurityMasterMapping.ToCreateCommand(CreateRequest(assetClass, payload));
            command.Should().NotThrow($"the '{assetClass}' payload was writable before the expand step");
        }
    }

    public static TheoryData<string, string> UnpopulatedCallAndPutScheduleCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (name, (assetClass, _)) in AllPayloads())
        {
            if (assetClass is not ("Bond" or "CertificateOfDeposit" or "StructuredCredit"))
            {
                continue;
            }

            foreach (var spelling in UnpopulatedCallAndPutSchedules.Keys)
            {
                data.Add(name, spelling);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(UnpopulatedCallAndPutScheduleCases))]
    public void UnpopulatedCallAndPutSchedules_AreAcceptedByTheWritePath_AndWriteTheGoldenBytes(string payloadName, string spelling)
    {
        // Before increment 4b the keys are undeclared; 4b declares them above the write version. Either
        // way a payload that carries them unset is accepted and writes exactly the bytes it writes
        // without them.
        var (assetClass, payload) = AllPayloads().Single(entry => entry.Key == payloadName).Value;
        var (callSchedule, putSchedule) = UnpopulatedCallAndPutSchedules[spelling];
        var terms = JsonSerializer.SerializeToNode(payload)!.AsObject();
        terms["callSchedule"] = JsonNode.Parse(callSchedule);
        terms["putSchedule"] = JsonNode.Parse(putSchedule);

        var command = () => SecurityMasterMapping.ToCreateCommand(CreateRequest(assetClass, terms));
        command.Should().NotThrow($"the '{payloadName}' payload with unset call and put schedules ({spelling}) is writable");

        var golden = CommittedGolden.Value;
        golden.Should().NotBeNull($"the golden fixture '{FixtureFileName}' must be committed under tests/fixtures/security-master/terms-golden/");
        golden!.Should().ContainKey($"write.{payloadName}");
        CreateSnapshot(assetClass, terms).AssetSpecificTermsJson.Should().Be(
            golden[$"write.{payloadName}"],
            $"unset call and put schedules ({spelling}) mean the terms are unset, so the '{payloadName}' write is its golden write");
    }

    private static SortedDictionary<string, string> Compute()
    {
        var output = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var validation = new SecurityValidationService(Substitute.For<ISecurityMasterStore>(), AssetClassValidatorRegistry.CreateDefault());

        foreach (var (name, (assetClass, payload)) in AllPayloads())
        {
            var snapshot = CreateSnapshot(assetClass, payload);
            output[$"write.{name}"] = snapshot.AssetSpecificTermsJson;

            var projection = SecurityMasterMapping.ToProjection(snapshot);
            var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
            output[$"economic.{name}"] = economic.EconomicTerms.GetRawText();

            output[$"validation.{name}"] = DescribeIssues(validation.ValidateRecord(projection, [projection], Now));
        }

        var storedOnly = new Dictionary<string, (string AssetClass, JsonElement Terms)>(StringComparer.Ordinal);
        foreach (var (name, (assetClass, terms)) in StoredOnlyCases())
        {
            storedOnly.Add(name, (assetClass, terms));
            var projection = StoredProjection(assetClass, terms);
            output[$"validation.stored.{name}"] = DescribeIssues(validation.ValidateRecord(projection, [projection], Now));
        }

        // What a node that reads the block persists when it re-serializes a stored row: the projection
        // straight into the Read-mode mapping, then the F# writer.
        foreach (var (assetClass, version) in ReadableTwinVersions)
        {
            var projection = StoredProjection(assetClass, SecurityAssetTermsSchemaRoundTripTests.ReadableTwin(assetClass, version));
            output[$"readable.write.{assetClass}.v{version}"] = Rewrite(projection);
            output[$"readable.economic.{assetClass}.v{version}"] = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection).EconomicTerms.GetRawText();
        }

        foreach (var (key, storedName) in StrayRowRewrites)
        {
            var (assetClass, terms) = storedOnly[storedName];
            output[$"readable.write.{key}"] = Rewrite(StoredProjection(assetClass, terms));
        }

        output["registry.dtos"] = JsonSerializer.Serialize(SecurityAssetClassRegistry.All.Select(SecurityAssetClassRegistry.ToDto));
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            output[$"termBlocks.{assetClass}"] = string.Join(",", SecurityTermBlocks.For(assetClass).Select(static field => field.Key));
        }

        return output;
    }

    private static IEnumerable<KeyValuePair<string, (string AssetClass, object Payload)>> AllPayloads()
        => SecurityAssetTermsSchemaRoundTripTests.FullPayloads
            .Select(static entry => new KeyValuePair<string, (string, object)>(entry.Key, (entry.Key, entry.Value)))
            .Concat(Variants);

    // Stored rows no create would produce: stray top-level keys on bonds and a structured credit, at the
    // write version and at the readable version of increment 4a's block, and profile-backed
    // StructuredCredit envelopes carrying extra root and profile keys.
    private static IEnumerable<KeyValuePair<string, (string AssetClass, JsonElement Terms)>> StoredOnlyCases()
    {
        var bond = JsonNode.Parse(SerializeThroughDomainText("Bond", Variants["Bond.fixed"].Payload))!.AsObject();
        bond["scheduleConventions"] = "x";
        yield return new("Bond.strayScheduleConventionsString", ("Bond", JsonSerializer.SerializeToElement(bond)));

        foreach (var (name, value) in new (string, JsonNode?)[]
                 {
                     ("String", JsonValue.Create("x")),
                     ("Object", new JsonObject { ["businessDayConvention"] = "Following", ["paymentCalendars"] = new JsonArray("USNY") }),
                 })
        {
            var envelope = new JsonObject
            {
                ["customProfileId"] = "structured-credit-profile",
                ["scheduleConventions"] = value?.DeepClone(),
                ["profileFields"] = new JsonObject
                {
                    ["tranche"] = "A-1",
                    ["collateralType"] = "CLO",
                    ["scheduleConventions"] = value?.DeepClone(),
                },
            };
            yield return new($"StructuredCredit.profileEnvelopeScheduleConventions{name}", ("StructuredCredit", JsonSerializer.SerializeToElement(envelope)));
        }

        // Stray call and put schedules, which increment 4b declares above the write version.
        var bondCallString = JsonNode.Parse(SerializeThroughDomainText("Bond", Variants["Bond.fixed"].Payload))!.AsObject();
        bondCallString["callSchedule"] = "x";
        yield return new("Bond.strayCallScheduleString", ("Bond", JsonSerializer.SerializeToElement(bondCallString)));

        var bondCallAndPut = JsonNode.Parse(SerializeThroughDomainText("Bond", Variants["Bond.fixed"].Payload))!.AsObject();
        bondCallAndPut["callSchedule"] = JsonNode.Parse(StrayCallSchedule);
        bondCallAndPut["putSchedule"] = JsonNode.Parse(StrayPutSchedule);
        yield return new("Bond.strayCallAndPutArrays", ("Bond", JsonSerializer.SerializeToElement(bondCallAndPut)));

        var bondWithBlock = JsonNode.Parse(SerializeThroughDomainText("Bond", Variants["Bond.fixed"].Payload))!.AsObject();
        bondWithBlock["scheduleConventions"] = JsonNode.Parse(SecurityAssetTermsSchemaRoundTripTests.ReadableTwinBlocks["Bond"][0]);
        var bondStampTwo = JsonNode.Parse(SecurityTermsEnvelope.StampFlat(bondWithBlock.ToJsonString(), "Bond", 2))!.AsObject();
        bondStampTwo["callSchedule"] = JsonNode.Parse(StrayCallSchedule);
        yield return new("Bond.stampTwoStrayCallSchedule", ("Bond", JsonSerializer.SerializeToElement(bondStampTwo)));

        var structuredCreditWithBlock = JsonNode.Parse(SerializeThroughDomainText("StructuredCredit", SecurityAssetTermsSchemaRoundTripTests.FullPayloads["StructuredCredit"]))!.AsObject();
        structuredCreditWithBlock["scheduleConventions"] = JsonNode.Parse(SecurityAssetTermsSchemaRoundTripTests.ReadableTwinBlocks["StructuredCredit"][0]);
        var structuredCreditStampThree = JsonNode.Parse(SecurityTermsEnvelope.StampFlat(structuredCreditWithBlock.ToJsonString(), "StructuredCredit", 3))!.AsObject();
        structuredCreditStampThree["callSchedule"] = "x";
        yield return new("StructuredCredit.stampThreeStrayCallSchedule", ("StructuredCredit", JsonSerializer.SerializeToElement(structuredCreditStampThree)));

        foreach (var (name, callSchedule, putSchedule) in new (string, JsonNode?, JsonNode?)[]
                 {
                     ("String", JsonValue.Create("x"), JsonValue.Create("x")),
                     ("Array", JsonNode.Parse(StrayCallSchedule), JsonNode.Parse(StrayPutSchedule)),
                 })
        {
            var envelope = new JsonObject
            {
                ["customProfileId"] = "structured-credit-profile",
                ["callSchedule"] = callSchedule?.DeepClone(),
                ["putSchedule"] = putSchedule?.DeepClone(),
                ["profileFields"] = new JsonObject
                {
                    ["tranche"] = "A-1",
                    ["collateralType"] = "CLO",
                    ["callSchedule"] = callSchedule?.DeepClone(),
                    ["putSchedule"] = putSchedule?.DeepClone(),
                },
            };
            yield return new($"StructuredCredit.profileEnvelopeCallPut{name}", ("StructuredCredit", JsonSerializer.SerializeToElement(envelope)));
        }
    }

    private static SecurityProjectionRecord StoredProjection(string assetClass, JsonElement terms)
        => SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, terms) with { SecurityId = SecurityId };

    // The Read-mode mapping of a stored row, then the F# writer: what a deactivation or a patch-less
    // amend re-serializes.
    private static string Rewrite(SecurityProjectionRecord projection)
        => new SecurityMasterSnapshotWrapper(SecurityMasterMapping.ToRecord(projection)).AssetSpecificTermsJson;

    private static string DescribeIssues(SecurityValidationReportDto report)
        => JsonSerializer.Serialize(report.Issues.Select(static issue => new
        {
            severity = issue.Severity.ToString(),
            code = issue.Code,
            affectedFields = issue.AffectedFields,
            message = issue.Message,
        }));

    private static string SerializeThroughDomainText(string assetClass, object payload)
        => CreateSnapshot(assetClass, payload).AssetSpecificTermsJson;

    private static SecurityMasterSnapshotWrapper CreateSnapshot(string assetClass, object payload)
    {
        var command = SecurityMasterMapping.ToCreateCommand(CreateRequest(assetClass, payload));
        var result = SecurityMasterCommandFacade.Create(command);
        result.IsSuccess.Should().BeTrue(
            string.Join("; ", result.ErrorDetails.Select(error => $"[{error.Code}] {error.Message}")));
        return result.Snapshot;
    }

    private static CreateSecurityRequest CreateRequest(string assetClass, object payload)
        => new(
            SecurityId: SecurityId,
            AssetClass: assetClass,
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = $"{assetClass} write golden", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(payload),
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"GOLDEN-{assetClass}", true, EffectiveFrom)],
            EffectiveFrom: EffectiveFrom,
            SourceSystem: "terms-write-golden",
            UpdatedBy: "terms-write-golden",
            SourceRecordId: $"{assetClass}-terms-write-golden",
            Reason: "Readable-before-writable characterization");

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

        return $"The written or reported output differs from the golden. Missing: [{string.Join(", ", missing)}]; " +
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
}
