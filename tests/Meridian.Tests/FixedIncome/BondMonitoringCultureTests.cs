using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed class BondMonitoringCultureTests
{
    private static readonly Guid Security = Guid.Parse("a1edb19b-8417-4269-af03-a596445aa321");
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly AccountCashForecastScopeDto Scope = new("synthetic-tenant", "synthetic-company", "account", "book", "USD");

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void CurrentRendererRetainsTheSameAmountsPercentagesAndGregorianIdentitiesInEveryCulture(string culture)
    {
        var request = Request();
        BondMonitoringSnapshotDto baseline;
        using (new CultureScope(CultureInfo.InvariantCulture))
            baseline = BondMonitoringEngine.Build(request);
        BondMonitoringSnapshotDto actual;
        using (new CultureScope(CultureInfo.GetCultureInfo(culture)))
            actual = BondMonitoringEngine.Build(request);

        actual.EngineVersion.Should().Be("bond-monitoring-v3");
        actual.Issues.Should().BeEmpty();
        Json(actual).Should().Be(Json(baseline));
        actual.Watch.Single(row => row.WatchId == "due:coupon").Summary.Should().Be("Coupon: 100.5 USD (forecast)");
        var reset = actual.Watch.Single(row => row.Kind == "ObservedReset");
        reset.WatchId.Should().Be($"reset:{Security:D}:2026-11-10:2026-11-11");
        reset.Summary.Should().Be("Observed index 1.234567 %; accrual 2026-11-10; prior index -0.432100 %");
        var coupon = actual.Watch.Single(row => row.Kind == "CouponRevision");
        coupon.WatchId.Should().Be($"coupon-estimate:{Security:D}:2026-11-13");
        coupon.Summary.Should().Contain("Whole coupon 100.5 USD on 2026-11-13; prior 98.25; total change 2.25.");
        var exercise = actual.Watch.Single(row => row.Kind == "ExerciseEligibility");
        exercise.WatchId.Should().Be($"exercise:{Security:D}:IssuerCall:2026-11-15");
        exercise.Summary.Should().Be("IssuerCall eligibility 2026-11-15 at 101.75; notice Open. Eligibility is not an observed exercise notice.");
        actual.Watch.Single(row => row.Kind == "UnallocatedCash").Summary.Should().Be("Unallocated observed cash 7.25 USD");
        actual.Watch.Single(row => row.Kind == "Rating").Summary.Should().Be("A+ (retained source text 10,5)");
    }

    [Theory]
    [InlineData(BondMonitoringEngine.LegacyEngineVersion, "en-US", "fr-FR")]
    [InlineData(BondMonitoringEngine.LegacyEngineVersion, "fr-FR", "en-US")]
    [InlineData(BondMonitoringEngine.LegacyEngineVersion, "th-TH", "fr-FR")]
    [InlineData(BondMonitoringEngine.LegacyEngineVersion, "fa-IR", "de-DE")]
    [InlineData(BondMonitoringEngine.PreviousEngineVersion, "en-US", "fr-FR")]
    [InlineData(BondMonitoringEngine.PreviousEngineVersion, "fr-FR", "en-US")]
    [InlineData(BondMonitoringEngine.PreviousEngineVersion, "th-TH", "fr-FR")]
    [InlineData(BondMonitoringEngine.PreviousEngineVersion, "fa-IR", "de-DE")]
    public async Task LegacyLocalizedHistoryAndExactRetriesSurviveRestartWithoutRewritingReceiptBytes(
        string version, string capturedCulture, string reopenedCulture)
    {
        using var directory = new HistoryDirectory();
        var request = Request();
        BondMonitoringSnapshotDto original;
        using (new CultureScope(CultureInfo.GetCultureInfo(capturedCulture)))
            original = BondMonitoringEngine.Build(request, version);
        original.Issues.Should().BeEmpty();
        await new BondMonitoringFileStore(directory.Path).AppendAsync(original);
        var retainedBytes = await File.ReadAllBytesAsync(directory.HistoryPath);
        var retainedJson = Json(original);

        using (new CultureScope(CultureInfo.GetCultureInfo(reopenedCulture)))
        {
            var restarted = new BondMonitoringService(new BondMonitoringFileStore(directory.Path));
            Json((await restarted.GetHistoryAsync(Scope)).Single()).Should().Be(retainedJson);
            Json(await restarted.CaptureAsync(request)).Should().Be(retainedJson);
            (await File.ReadAllBytesAsync(directory.HistoryPath)).Should().Equal(retainedBytes);
            var refreshed = await restarted.CaptureAsync(request with
            {
                RunId = "invariant-refresh", RecordedAtUtc = Now.AddDays(1),
                PreviousSnapshotId = original.SnapshotId, CorrectionRationale = "Explicit synthetic locale-independent refresh."
            });
            refreshed.EngineVersion.Should().Be("bond-monitoring-v3");
            var history = await new BondMonitoringService(new BondMonitoringFileStore(directory.Path)).GetHistoryAsync(Scope);
            history.Should().HaveCount(2);
            Json(history[0]).Should().Be(retainedJson);
            Json(history[1]).Should().Be(Json(refreshed));
        }
    }

    [Theory]
    [InlineData(BondMonitoringEngine.LegacyEngineVersion)]
    [InlineData(BondMonitoringEngine.PreviousEngineVersion)]
    [InlineData(BondMonitoringEngine.EngineVersion)]
    public async Task CultureCompatibilityStillRequiresEveryRetainedFinancialAndDisplayFieldToReplay(string version)
    {
        BondMonitoringSnapshotDto original;
        using (new CultureScope(CultureInfo.GetCultureInfo("fr-FR")))
            original = BondMonitoringEngine.Build(Request(), version);
        var reset = original.Watch.Single(row => row.Kind == "ObservedReset");
        var source = original.Watch.Single(row => row.Kind == "Rating");
        var changedInputs = original.Inputs with
        {
            Obligations = [original.Inputs.Obligations.Single() with { Amount = 100.51m }]
        };
        var changedInputHash = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(changedInputs,
            BondMonitoringJsonContext.Default.BondMonitoringCaptureRequest));
        var tampered = new[]
        {
            original with { Watch = original.Watch.Select(row => row == reset ? row with { Summary = row.Summary + " altered" } : row).ToArray() },
            original with { Watch = original.Watch.Select(row => row == reset ? row with { WatchId = row.WatchId + "-altered" } : row).ToArray() },
            original with { Watch = original.Watch.Select(row => row == source ? row with { Summary = "invented rating" } : row).ToArray() },
            original with { Collections = [original.Collections.Single() with { RemainingAmount = 100.51m }] },
            original with { Inputs = changedInputs, SnapshotId = Sha256Digest.ComputeUtf8($"{version}|{changedInputHash}") },
            original with { Inputs = original.Inputs with { Scope = Scope with { BookId = "another-book" } } }
        };
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        foreach (var snapshot in tampered)
        {
            var store = Substitute.For<IBondMonitoringStore>();
            store.GetHistoryAsync(Scope, Arg.Any<CancellationToken>()).Returns(new[] { snapshot });
            var read = () => new BondMonitoringService(store).GetHistoryAsync(Scope);
            await read.Should().ThrowAsync<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData(BondMonitoringEngine.LegacyEngineVersion)]
    [InlineData(BondMonitoringEngine.PreviousEngineVersion)]
    public async Task UnsupportedCustomLegacyRenderingFailsClosedUntilTheExactSettingsAreAvailable(string version)
    {
        using var directory = new HistoryDirectory();
        var custom = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        custom.NumberFormat.NumberDecimalSeparator = ":";
        custom.NumberFormat.PercentDecimalSeparator = ":";
        custom.NumberFormat.PercentSymbol = "[custom-percent]";
        BondMonitoringSnapshotDto original;
        using (new CultureScope(custom))
        {
            original = BondMonitoringEngine.Build(Request(), version);
            BondMonitoringEngine.Build(Request()).Watch.Single(row => row.Kind == "ObservedReset").Summary
                .Should().Be("Observed index 1.234567 %; accrual 2026-11-10; prior index -0.432100 %");
        }
        original.Watch.Single(row => row.Kind == "ObservedReset").Summary.Should().Contain("[custom-percent]");
        await new BondMonitoringFileStore(directory.Path).AppendAsync(original);
        var bytes = await File.ReadAllBytesAsync(directory.HistoryPath);
        using (new CultureScope(CultureInfo.GetCultureInfo("fr-FR")))
        {
            var read = () => new BondMonitoringService(new BondMonitoringFileStore(directory.Path)).GetHistoryAsync(Scope);
            await read.Should().ThrowAsync<InvalidDataException>().WithMessage("*unsupported custom output*");
            (await File.ReadAllBytesAsync(directory.HistoryPath)).Should().Equal(bytes);
        }
        using (new CultureScope(custom))
            Json((await new BondMonitoringService(new BondMonitoringFileStore(directory.Path)).GetHistoryAsync(Scope)).Single())
                .Should().Be(Json(original));
        (await File.ReadAllBytesAsync(directory.HistoryPath)).Should().Equal(bytes);
    }

    private static BondMonitoringCaptureRequest Request()
    {
        var input = new BondCalculationInput("Terms", "synthetic-terms", RecordedAt: Now.AddHours(-3), ContentSha256: Sha256Digest.ComputeUtf8("synthetic-terms"));
        var obligation = new BondMonitoringObligationDto("coupon", Security, "Coupon", Today.AddDays(1), 100.5m, "USD", "synthetic-model", [input], IsForecast: true);
        var payment = new AccountCashFlowDto("receipt", "receipt-v1", Scope.AccountId, Scope.BookId, AccountCashFlowStatus.Actual,
            "Coupon", 7.25m, "USD", Today, Today, Today, Evidence("receipt"), Security);
        return new("culture-receipt", Scope, Now, [obligation], [payment], [], [],
            [new("rating", Security, "Rating", Today, "A+ (retained source text 10,5)", "Fitch", Evidence("rating"))])
        {
            Resets = [new(Security, Today.AddDays(-2), Today.AddDays(-1), .01234567m, true, "synthetic-fixing", [input])
            { PreviousRate = -.004321m, CouponPaymentDate = Today.AddDays(1), CouponAmount = 100.5m, PreviousCouponAmount = 98.25m }],
            ExerciseWindows = [new(Security, 1, Today, 30,
                [new("IssuerCall", Today.AddDays(3), 101.75m, Today.AddDays(-1), Today.AddDays(1), BondExerciseNoticeStatus.Open, 1)], [], [])]
        };
    }

    private static AccountCashProvenanceDto Evidence(string id)
    {
        var hash = Sha256Digest.ComputeUtf8(id);
        return new("SyntheticCustodian", id, hash, AccountCashEvidenceRole.Observation,
            new(id, $"https://evidence.example.test/{id}", hash, "SyntheticCustodian", id, "Accepted", "reviewer",
                Now.AddHours(-2), Today, 1, Now.AddHours(-1), "retainer", "AccountCash", Scope.AccountId));
    }

    private static string Json(BondMonitoringSnapshotDto snapshot)
        => JsonSerializer.Serialize(snapshot, BondMonitoringJsonContext.Default.BondMonitoringSnapshotDto);

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _original = CultureInfo.CurrentCulture;
        private readonly CultureInfo _originalUi = CultureInfo.CurrentUICulture;
        public CultureScope(CultureInfo culture) { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = culture; }
        public void Dispose() { CultureInfo.CurrentCulture = _original; CultureInfo.CurrentUICulture = _originalUi; }
    }

    private sealed class HistoryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bond-monitoring-culture-" + Guid.NewGuid().ToString("N"));
        public string HistoryPath => System.IO.Path.Combine(Path, "fixed-income", "bond-monitoring", "history.json");
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
