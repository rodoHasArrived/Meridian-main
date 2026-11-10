using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using static Meridian.Tests.Ui.SecurityMasterWorkbenchScheduleBookHarness;

namespace Meridian.Tests.Ui;

/// <summary>
/// Golden schedule summaries and schedule books of the Security Master workbench snapshot. It freezes
/// the two serialized read models for records that declare no call or put schedule the read path
/// decodes (Bond with and without scalar call terms, a Bond stamped below the arrays' terms version, a
/// Bond whose array holds only unset entries, CertificateOfDeposit and StructuredCredit), so a change
/// to how declaring bonds are read can prove every other record's output is byte-identical. It also
/// holds four declaring bonds (a call and a put with a scalar call date that agrees with the declared
/// one, the same with a scalar that contradicts it, one without conventions, and one recovered from an
/// economic document by the real conversion), whose cases change on purpose when that reading changes.
/// </summary>
/// <remarks>
/// <para>The workbench reads the system clock. Every fixture date is either before 2026 or after 2088,
/// so the frozen output does not depend on the day the test runs.</para>
/// <para>A missing or mismatched fixture writes <c>security-master-schedule-book.received.json</c>
/// beside the test binaries and fails. It never skips. Replace the committed fixture under
/// tests/fixtures/schedules/golden/ only when the change in governed output is intended.</para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SecurityMasterWorkbenchScheduleBookGoldenTests
{
    private const string FixtureFileName = "security-master-schedule-book.json";
    private const string ReceivedFileName = "security-master-schedule-book.received.json";
    private const int ExpectedCaseCount = 13;
    private static readonly DateTimeOffset RecordedAt = new(2025, 1, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public async Task ScheduleSummariesAndBooks_MatchCommittedGolden()
    {
        var received = await ComputeAsync();
        Assert.Equal(ExpectedCaseCount, received.Count);

        var fixturePath = LocateFixture();
        var failure = fixturePath is null
            ? $"The golden fixture '{FixtureFileName}' was not found beside the test binaries or under tests/fixtures/schedules/golden/."
            : Compare(ReadExpected(fixturePath), received);
        if (failure is null)
            return;

        var receivedPath = WriteReceived(received);
        Assert.Fail(
            $"{failure}{Environment.NewLine}The computed read models were written to '{receivedPath}'. " +
            "Replace tests/fixtures/schedules/golden/" + FixtureFileName + " with it only when the change in governed output is intended.");
    }

    private static async Task<List<(string Key, JsonNode Value)>> ComputeAsync()
    {
        var cases = new List<(string Key, JsonNode Value)>();
        foreach (var (key, projection, corporateActions) in Cases())
        {
            var snapshot = await CreateService(new StoredRowSecurityMasterQueryService(projection, corporateActions))
                .GetTrustSnapshotAsync(projection.SecurityId, fundProfileId: null);
            Assert.NotNull(snapshot);
            Assert.NotNull(snapshot.ScheduleSummary);
            Assert.NotNull(snapshot.ScheduleBook);
            cases.Add((key, new JsonObject
            {
                ["scheduleSummary"] = JsonSerializer.SerializeToNode(snapshot.ScheduleSummary),
                ["scheduleBook"] = JsonSerializer.SerializeToNode(snapshot.ScheduleBook)
            }));
        }
        return cases;
    }

    private static IEnumerable<(string Key, SecurityProjectionRecord Projection, IReadOnlyList<CorporateActionDto>? CorporateActions)> Cases()
    {
        var call = new Dictionary<string, object?>
        {
            ["callDate"] = "2090-01-15",
            ["callPrice"] = 101m,
            ["exerciseStyle"] = "European",
            ["priceBasis"] = "PercentOfPar",
            ["minimumNoticeDays"] = 30,
            ["maximumNoticeDays"] = 60,
            ["noticeDayType"] = "Calendar"
        };
        var put = new Dictionary<string, object?>
        {
            ["putDate"] = "2091-01-15",
            ["putPrice"] = 100m,
            ["exerciseStyle"] = "European",
            ["priceBasis"] = "PercentOfPar"
        };
        var conventions = new
        {
            accrualAdjusted = false,
            couponRateUnit = "Percent",
            couponCashBasis = "Level",
            paymentCalendars = new[] { "WEEKENDS" }
        };

        yield return ("bond/undeclared/future-scalar-call", Projection(1, "Bond", BondTerms(new() { ["callDate"] = "2090-01-15", ["isCallable"] = true })), null);
        yield return ("bond/undeclared/past-scalar-call", Projection(2, "Bond", BondTerms(new() { ["callDate"] = "2025-07-15", ["isCallable"] = true })), null);
        yield return ("bond/undeclared/no-call", Projection(3, "Bond", BondTerms([])), null);
        yield return ("bond/undeclared/stamp-2-stray-call-schedule", Projection(4, "Bond", BondTerms(new()
        {
            ["assetClassTermsVersion"] = 2,
            ["callDate"] = "2089-07-15",
            ["isCallable"] = true,
            [SecurityVersionedTerms.CallScheduleKey] = new[] { call }
        })), null);
        yield return ("bond/undeclared/all-unset-call-schedule", Projection(5, "Bond", BondTerms(new()
        {
            ["assetClassTermsVersion"] = 3,
            ["callDate"] = "2089-07-15",
            ["isCallable"] = true,
            [SecurityVersionedTerms.CallScheduleKey] = new object?[] { null },
            [SecurityVersionedTerms.ScheduleConventionsKey] = conventions
        })), null);
        yield return ("bond/undeclared/conventions-without-exercise-schedule", Projection(6, "Bond", BondTerms(new()
        {
            ["assetClassTermsVersion"] = 3,
            ["callDate"] = "2089-07-15",
            ["isCallable"] = true,
            [SecurityVersionedTerms.ScheduleConventionsKey] = conventions
        })), null);
        yield return ("bond/undeclared/scalar-call-with-corporate-actions", Projection(7, "Bond", BondTerms(new() { ["callDate"] = "2090-01-15", ["isCallable"] = true })),
            [
                CorporateAction(7, 1, new DateOnly(2025, 3, 3), new DateOnly(2025, 3, 10)),
                CorporateAction(7, 2, new DateOnly(2089, 3, 1), new DateOnly(2089, 3, 8))
            ]);
        yield return ("certificate-of-deposit/declared-call-schedule", Projection(8, "CertificateOfDeposit", new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = 3,
            ["issuerName"] = "Meridian Bank",
            ["maturity"] = "2096-01-15",
            ["couponRate"] = 4m,
            ["dayCount"] = "30/360",
            ["callableDate"] = "2090-01-15",
            [SecurityVersionedTerms.CallScheduleKey] = new[] { call }
        }), null);
        yield return ("structured-credit/declared-call-and-put-schedule", Projection(9, "StructuredCredit", new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = 4,
            ["tranche"] = "A-1",
            ["collateralType"] = "CLO",
            ["originalFace"] = 1_000_000m,
            ["couponOrIndex"] = "SOFR+250",
            ["maturity"] = "2096-01-15",
            ["currentFactor"] = 0.9m,
            [SecurityVersionedTerms.CallScheduleKey] = new[] { call },
            [SecurityVersionedTerms.PutScheduleKey] = new[] { put }
        }), null);

        // Declaring bonds. In the first the scalar callDate is the declared call date; in the others the
        // scalar (2089-07-15) is none of the declared dates.
        yield return ("bond/declaring/call-and-put-with-agreeing-scalar", Projection(13, "Bond", BondTerms(new()
        {
            ["assetClassTermsVersion"] = 3,
            ["couponType"] = "Fixed",
            ["callDate"] = "2090-01-15",
            ["isCallable"] = true,
            [SecurityVersionedTerms.CallScheduleKey] = new[] { call },
            [SecurityVersionedTerms.PutScheduleKey] = new[] { put },
            [SecurityVersionedTerms.ScheduleConventionsKey] = conventions
        })), null);
        yield return ("bond/declaring/call-and-put-with-contradicting-scalar", Projection(10, "Bond", BondTerms(new()
        {
            ["assetClassTermsVersion"] = 3,
            ["couponType"] = "Fixed",
            ["callDate"] = "2089-07-15",
            ["isCallable"] = true,
            [SecurityVersionedTerms.CallScheduleKey] = new[] { call },
            [SecurityVersionedTerms.PutScheduleKey] = new[] { put },
            [SecurityVersionedTerms.ScheduleConventionsKey] = conventions
        })), null);
        yield return ("bond/declaring/without-conventions", Projection(11, "Bond", BondTerms(new()
        {
            ["assetClassTermsVersion"] = 3,
            ["couponType"] = "Fixed",
            ["callDate"] = "2089-07-15",
            ["isCallable"] = true,
            [SecurityVersionedTerms.CallScheduleKey] = new[] { call }
        })), null);
        yield return ("bond/declaring/recovered-from-economic-terms", Projection(12, "Bond", RecoveredBondTerms()), null);
    }

    private static Dictionary<string, object?> BondTerms(Dictionary<string, object?> extra)
    {
        var terms = new Dictionary<string, object?>
        {
            ["issueDate"] = "2024-01-15",
            ["maturity"] = "2096-01-15",
            ["couponRate"] = 4m,
            ["dayCount"] = "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = "SemiAnnual"
        };
        foreach (var (key, value) in extra)
            terms[key] = value;
        return terms;
    }

    // An economic document converted by the real ADR-027 conversion: stamp 3, the scalar callDate from
    // the first call date, one recovered entry, and no schedule conventions.
    private static JsonElement RecoveredBondTerms()
    {
        const string Economic = "{\"termsFormat\":\"economic\",\"schemaVersion\":2,"
            + "\"maturity\":{\"maturityDate\":\"2096-01-15\",\"issueDate\":\"2024-01-15\"},"
            + "\"coupon\":{\"couponType\":\"Fixed\",\"couponRate\":5,\"dayCount\":\"30/360\",\"paymentFrequency\":\"SemiAnnual\"},"
            + "\"call\":{\"isCallable\":true,\"firstCallDate\":\"2090-01-15\",\"makeWholeSpreadBps\":50,"
            + "\"callSchedule\":[{\"callDt\":\"2090-01-15\",\"callPx\":101.0,\"isParCall\":false,\"callType\":\"European\"}],"
            + "\"isPuttable\":false,\"putSchedule\":[]}}";
        return SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster
            .Convert(JsonSerializer.Deserialize<JsonElement>(Economic), "Bond").Payload;
    }

    private static SecurityProjectionRecord Projection(int ordinal, string assetClass, Dictionary<string, object?> terms)
        => Projection(ordinal, assetClass, JsonSerializer.SerializeToElement(terms));

    private static SecurityProjectionRecord Projection(int ordinal, string assetClass, JsonElement terms)
        => new(
            SecurityId: Id(ordinal),
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: $"Schedule book golden {ordinal}",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: $"BOOK-{ordinal}",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = $"Schedule book golden {ordinal}", currency = "USD" }),
            AssetSpecificTerms: terms,
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "golden-edm",
                sourceRecordId = $"edm-book-{ordinal}",
                asOf = "2025-01-02T00:00:00+00:00",
                updatedBy = "steward"
            }),
            Version: 3,
            EffectiveFrom: RecordedAt,
            EffectiveTo: null,
            Identifiers: [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"BOOK-{ordinal}", true, RecordedAt)],
            Aliases: []);

    private static CorporateActionDto CorporateAction(int ordinal, int sequence, DateOnly exDate, DateOnly payDate)
        => new(
            CorpActId: Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"8b0b1c2d-0000-4000-9000-{ordinal:D6}{sequence:D6}")),
            SecurityId: Id(ordinal),
            EventType: "Dividend",
            ExDate: exDate,
            PayDate: payDate,
            DividendPerShare: 1.25m,
            Currency: "USD",
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);

    private static Guid Id(int ordinal)
        => Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"8b0b1c2d-0000-4000-8000-{ordinal:D12}"));

    private static string? LocateFixture()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "fixtures", "schedules", "golden", FixtureFileName),
            Path.Combine(FindRepositoryRoot() ?? AppContext.BaseDirectory, "tests", "fixtures", "schedules", "golden", FixtureFileName)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    private static Dictionary<string, string> ReadExpected(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        return root["cases"]!.AsObject().ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value!.ToJsonString(),
            StringComparer.Ordinal);
    }

    private static string? Compare(Dictionary<string, string> expected, List<(string Key, JsonNode Value)> received)
    {
        var failures = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in received)
        {
            seen.Add(key);
            var actual = value.ToJsonString();
            if (!expected.TryGetValue(key, out var want))
                failures.AppendLine(CultureInfo.InvariantCulture, $"Missing expected case '{key}'.");
            else if (!string.Equals(want, actual, StringComparison.Ordinal))
                failures.AppendLine(CultureInfo.InvariantCulture, $"Case '{key}' differs.{Environment.NewLine}  expected: {want}{Environment.NewLine}  actual:   {actual}");
        }
        foreach (var key in expected.Keys.Where(key => !seen.Contains(key)))
            failures.AppendLine(CultureInfo.InvariantCulture, $"Fixture case '{key}' was not computed.");
        return failures.Length == 0 ? null : failures.ToString();
    }

    private static string WriteReceived(List<(string Key, JsonNode Value)> received)
    {
        var cases = new JsonObject();
        foreach (var (key, value) in received)
            cases[key] = value;
        var root = new JsonObject
        {
            ["description"] = "Golden Security Master workbench schedule summaries and schedule books: records that declare no decoded call or put schedule, and four declaring bonds.",
            ["caseCount"] = received.Count,
            ["cases"] = cases
        };
        var path = Path.Combine(AppContext.BaseDirectory, ReceivedFileName);
        File.WriteAllText(path, root.ToJsonString(Indented) + Environment.NewLine);
        return path;
    }
}
