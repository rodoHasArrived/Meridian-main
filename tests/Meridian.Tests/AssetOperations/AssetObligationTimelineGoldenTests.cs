using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// Golden asset-operations details, obligation timeline included, for records that declare no call or
/// put schedule the read path decodes. It is not a defect pin: it freezes the whole serialized detail so
/// that a change which adds declared exercise events to the timeline can prove every other record's
/// output is byte-identical.
/// </summary>
/// <remarks>
/// <para>The cases are bonds read with and without a bond reference (scalar call, mandatory put and
/// pre-refund dates in every combination the scalar probe distinguishes), a Bond stamped 2 that carries
/// a stray call schedule, a Bond stamped 3 that declares conventions and no exercise schedule, and a
/// CertificateOfDeposit and a StructuredCredit that declare a call schedule.</para>
/// <para>A missing or mismatched fixture writes <c>asset-obligation-timeline-undeclared.received.json</c>
/// beside the test binaries and fails. It never skips. Replace the committed fixture under
/// tests/fixtures/schedules/golden/ only when the change in governed output is intended.</para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class AssetObligationTimelineGoldenTests
{
    private const string FixtureFileName = "asset-obligation-timeline-undeclared.json";
    private const string ReceivedFileName = "asset-obligation-timeline-undeclared.received.json";
    private const int ExpectedCaseCount = 10;
    private static readonly DateTimeOffset AsOf = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedAt = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public async Task UndeclaredRecords_MatchCommittedGoldenDetails()
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
            $"{failure}{Environment.NewLine}The computed details were written to '{receivedPath}'. " +
            "Replace tests/fixtures/schedules/golden/" + FixtureFileName + " with it only when the change in governed output is intended.");
    }

    private static async Task<List<(string Key, JsonNode Value)>> ComputeAsync()
    {
        var cases = new List<(string Key, JsonNode Value)>();
        foreach (var (key, security, reference) in Cases())
        {
            var query = Substitute.For<ISecurityMasterQueryService>();
            query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
            var bonds = Substitute.For<IBondReferenceService>();
            bonds.GetReferenceAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(reference);
            var service = new AssetOperationsReadService(securityMasterQueryService: query, bondReferenceService: bonds);
            var detail = await service.GetOperationsAsOfAsync(security.SecurityId, new BondCalculationContext(AsOf));
            Assert.NotNull(detail);
            Assert.NotNull(detail.TermsObligationsTimeline);
            cases.Add((key, JsonSerializer.SerializeToNode(detail)!));
        }
        return cases;
    }

    private static IEnumerable<(string Key, SecurityDetailDto Security, BondReferenceDto? Reference)> Cases()
    {
        var call = new Dictionary<string, object?>
        {
            ["callDate"] = "2027-01-15",
            ["callPrice"] = 101m,
            ["exerciseStyle"] = "European",
            ["priceBasis"] = "PercentOfPar",
            ["minimumNoticeDays"] = 30,
            ["maximumNoticeDays"] = 60,
            ["noticeDayType"] = "Calendar"
        };

        yield return ("synthesized/scalar-call", Security(1, "Bond", BondTerms(new() { ["callDate"] = "2027-01-15" })), null);
        yield return ("synthesized/mandatory-put-only", Security(2, "Bond", BondTerms(new() { ["mandatoryPutDate"] = "2027-03-15" })), null);
        yield return ("synthesized/pre-refund-only", Security(3, "Bond", BondTerms(new() { ["preRefundDate"] = "2027-05-15" })), null);
        yield return ("synthesized/call-put-and-pre-refund", Security(4, "Bond", BondTerms(new()
        {
            ["preRefundDate"] = "2027-05-15",
            ["mandatoryPutDate"] = "2027-03-15",
            ["callDate"] = "2027-07-15"
        })), null);
        yield return ("synthesized/stamp-2-stray-call-schedule", Security(5, "Bond", BondTerms(new()
        {
            ["assetClassTermsVersion"] = 2,
            ["callDate"] = "2027-07-15",
            [SecurityVersionedTerms.CallScheduleKey] = new[] { call }
        })), null);
        yield return ("synthesized/stamp-3-conventions-without-exercise-schedule", Security(6, "Bond", BondTerms(new()
        {
            ["assetClassTermsVersion"] = 3,
            ["couponType"] = "Fixed",
            ["callDate"] = "2027-07-15",
            [SecurityVersionedTerms.ScheduleConventionsKey] = new
            {
                accrualAdjusted = false,
                couponRateUnit = "Percent",
                couponCashBasis = "Level",
                paymentCalendars = new[] { "WEEKENDS" }
            }
        })), null);
        yield return ("bond-reference/scalar-call", Security(7, "Bond", BondTerms(new() { ["callDate"] = "2027-01-15" })),
            Reference(7, callDate: new DateOnly(2027, 1, 15)));
        yield return ("bond-reference/mandatory-put-and-pre-refund", Security(8, "Bond", BondTerms(new()
        {
            ["mandatoryPutDate"] = "2027-03-15",
            ["preRefundDate"] = "2027-05-15"
        })), Reference(8, mandatoryPutDate: new DateOnly(2027, 3, 15), preRefundDate: new DateOnly(2027, 5, 15)));
        yield return ("certificate-of-deposit/declared-call-schedule", Security(9, "CertificateOfDeposit", new()
        {
            ["assetClassTermsVersion"] = 3,
            ["issuerName"] = "Meridian Bank",
            ["maturity"] = "2028-01-15",
            ["couponRate"] = 4m,
            ["dayCount"] = "30/360",
            ["callableDate"] = "2027-01-15",
            [SecurityVersionedTerms.CallScheduleKey] = new[] { call }
        }), null);
        yield return ("structured-credit/declared-call-schedule", Security(10, "StructuredCredit", new()
        {
            ["assetClassTermsVersion"] = 4,
            ["tranche"] = "A-1",
            ["collateralType"] = "CLO",
            ["originalFace"] = 1_000_000m,
            ["couponOrIndex"] = "SOFR+250",
            ["maturity"] = "2028-01-15",
            ["currentFactor"] = 0.9m,
            [SecurityVersionedTerms.CallScheduleKey] = new[] { call }
        }), null);
    }

    private static Dictionary<string, object?> BondTerms(Dictionary<string, object?> extra)
    {
        var terms = new Dictionary<string, object?>
        {
            ["issueDate"] = "2026-01-15",
            ["maturity"] = "2028-01-15",
            ["couponRate"] = 4m,
            ["dayCount"] = "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = "SemiAnnual"
        };
        foreach (var (key, value) in extra)
            terms[key] = value;
        return terms;
    }

    private static SecurityDetailDto Security(int ordinal, string assetClass, Dictionary<string, object?> terms)
        => new(Id(ordinal), assetClass, SecurityStatusDto.Active, $"Timeline golden {ordinal}", "USD",
            JsonSerializer.SerializeToElement(new { displayName = $"Timeline golden {ordinal}", currency = "USD" }),
            JsonSerializer.SerializeToElement(terms),
            [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"TIMELINE-{ordinal}", true, RecordedAt)],
            [], 2, RecordedAt, null);

    private static BondReferenceDto Reference(int ordinal, DateOnly? callDate = null, DateOnly? mandatoryPutDate = null,
        DateOnly? preRefundDate = null)
        => new(Id(ordinal), $"Timeline golden {ordinal}", "USD", "Meridian Funding LLC", "Senior", $"InternalCode:TIMELINE-{ordinal}",
            new BondLifecycleDto(Id(ordinal), BondLifecycleStat.Active, new DateOnly(2026, 1, 15), callDate,
                new DateOnly(2028, 1, 15), callDate is not null, 2, Par: 100m, PaymentFrequency: "SemiAnnual",
                PreRefundDate: preRefundDate, MandatoryPutDate: mandatoryPutDate),
            new BondAccrualConventionDto(Id(ordinal), "30/360", 2, "US", "Fixed", 4m, null, null, 2), 2);

    private static Guid Id(int ordinal)
        => Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"8a0b1c2d-0000-4000-8000-{ordinal:D12}"));

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
            ["description"] = "Golden asset-operations details (obligation timeline included) for records that declare no decoded call or put schedule, captured before declared exercise events joined the timeline.",
            ["caseCount"] = received.Count,
            ["cases"] = cases
        };
        var path = Path.Combine(AppContext.BaseDirectory, ReceivedFileName);
        File.WriteAllText(path, root.ToJsonString(Indented) + Environment.NewLine);
        return path;
    }
}
