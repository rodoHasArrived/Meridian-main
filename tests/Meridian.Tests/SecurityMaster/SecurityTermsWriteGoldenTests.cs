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

        foreach (var (name, (assetClass, terms)) in StoredOnlyCases())
        {
            var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, terms) with { SecurityId = SecurityId };
            output[$"validation.stored.{name}"] = DescribeIssues(validation.ValidateRecord(projection, [projection], Now));
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

    // Stored rows no create would produce: a stray top-level key on a version-1 bond, and a
    // profile-backed StructuredCredit envelope carrying extra root and profile keys.
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
    }

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
