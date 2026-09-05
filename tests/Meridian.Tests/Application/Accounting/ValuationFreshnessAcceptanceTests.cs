using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Application.Accounting;

public sealed class ValuationFreshnessAcceptanceTests
{
    private static readonly DateOnly Date = new(2026, 7, 3);
    private static readonly DateTimeOffset AsOf = new(2026, 7, 3, 21, 0, 0, TimeSpan.Zero);
    private static readonly Guid Security = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    [Theory]
    [InlineData(null, DailyPortfolioPriceConfidence.High, "observation date is required")]
    [InlineData(-4, DailyPortfolioPriceConfidence.High, "4 days old")]
    [InlineData(1, DailyPortfolioPriceConfidence.High, "after valuation date")]
    [InlineData(0, DailyPortfolioPriceConfidence.Low, "confidence")]
    public async Task MissingStaleFutureAndLowConfidenceMarks_BlockWholeBatch_RepairRestoresReadiness(
        int? dateOffset, DailyPortfolioPriceConfidence confidence, string reason)
    {
        var source = new MutableSource(new MarkPriceQuote(160m, "official-close", "evidence:aapl",
            PriceAsOf: dateOffset is { } offset ? Date.AddDays(offset) : null, Confidence: confidence));
        var service = new DailyMarkToMarketService(source);
        var request = Request();

        var blocked = await service.PrepareAsync(request);

        blocked.IsBlocked.Should().BeTrue();
        blocked.Approvals.Should().BeEmpty();
        blocked.Projection.Should().BeNull();
        var assessment = blocked.MarkFreshness.Should().ContainSingle().Subject;
        assessment.Status.Should().Be("ReviewRequired");
        assessment.BlockReason.Should().Contain(reason);
        assessment.SecurityId.Should().Be(Security);
        assessment.FinancialAccountId.Should().Be("broker-1");
        assessment.ValuationDate.Should().Be(Date);
        assessment.PolicyVersion.Should().NotBeNullOrWhiteSpace();

        source.Quote = source.Quote! with { PriceAsOf = Date, Confidence = DailyPortfolioPriceConfidence.High };
        var restored = await service.PrepareAsync(request);
        restored.IsBlocked.Should().BeFalse();
        restored.HasDraft.Should().BeTrue();
        restored.MarkFreshness.Should().OnlyContain(mark => mark.Status == "Current" && mark.AgeDays == 0);
    }

    [Fact]
    public async Task PreviewCountsPositionsAndAffectedValuations_WithoutCreatingApprovals()
    {
        var service = new DailyMarkToMarketService(new MutableSource(new MarkPriceQuote(160m,
            "official-close", "evidence:aapl", PriceAsOf: Date.AddDays(-4))));
        var request = Request() with { Positions = [Request().Positions[0],
            new MarkToMarketPosition("AAPL", 5m, 150m, "broker-2", SecurityId: Security)] };

        var preview = await service.PreviewAsync(request);
        preview.AssessedPositionCount.Should().Be(2);
        preview.BlockedPositionCount.Should().Be(2);
        preview.AffectedValuationCount.Should().Be(1, "both positions belong to the same valuation batch");
        preview.Positions.Select(mark => mark.FinancialAccountId).Should().BeEquivalentTo("broker-1", "broker-2");
        (await service.PrepareAsync(request)).Approvals.Should().BeEmpty();
    }

    [Theory]
    [InlineData(StalePriceHandling.Allow)]
    [InlineData(StalePriceHandling.Flag)]
    [InlineData(StalePriceHandling.Block)]
    public async Task CompatibilityModesAndPartialCoverage_CannotBypassAdmission(StalePriceHandling handling)
    {
        var request = Request() with
        {
            Policy = new DailyPortfolioPricingPolicy("fund-alpha", "policy-1", "Close", "official-close",
                "controller", AsOf.AddDays(-20), stalePricePolicy: StalePricePolicy.Of(3, handling)),
            QualityPolicy = new MarkPriceQualityPolicy(TimeSpan.FromDays(100), DailyPortfolioPriceConfidence.Low,
                RequireCompleteCoverage: false, RequireObservedDate: false)
        };
        var service = new DailyMarkToMarketService(new MutableSource(new MarkPriceQuote(160m,
            "official-close", "evidence:aapl", PriceAsOf: Date.AddDays(-4))));
        (await service.PrepareAsync(request)).IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task RetainedMarkTampering_BlocksApprovalAndPosting_OverrideTagsDoNotAuthorizeIt()
    {
        var run = await new DailyMarkToMarketService(new MutableSource(new MarkPriceQuote(160m,
            "official-close", "evidence:aapl", PriceAsOf: Date))).PrepareAsync(Request());
        var submitted = run.Approval!;
        var approved = submitted.Approve("controller", AsOf.AddDays(90), "Reviewed historical close", ["review:1"]);
        approved.ToJournalEntry().Should().NotBeNull("historical marks are assessed against valuation date, not review time");
        var tags = (IDictionary<string, string>)submitted.Draft.Metadata.Tags!;
        var validJson = tags[ValuationMarkEvidenceGuard.EvidenceTag];
        var evidence = JsonSerializer.Deserialize<ValuationMarkEvidence[]>(validJson)!;
        tags["valuation.override.approvedBy"] = "controller";
        tags["valuation.override.expiresAtUtc"] = AsOf.AddDays(-1).ToString("O");
        tags[ValuationMarkEvidenceGuard.EvidenceTag] = JsonSerializer.Serialize(
            evidence.Select(mark => mark with { ObservedOn = Date.AddDays(-4) }).ToArray());

        var approve = () => submitted.Approve("controller", AsOf, "Override", ["review:2"]);
        approve.Should().Throw<InvalidOperationException>().WithMessage("*differs from the retained server assessment*");
        var post = () => approved.ToJournalEntry();
        post.Should().Throw<InvalidOperationException>().WithMessage("*differs from the retained server assessment*");
        tags[ValuationMarkEvidenceGuard.EvidenceTag] = validJson;
        approved.ToJournalEntry().Should().NotBeNull();
    }

    [Theory]
    [InlineData("other-fund", "broker-1", 0)]
    [InlineData("fund-alpha", "other-account", 0)]
    [InlineData("fund-alpha", "broker-1", 1)]
    public async Task RetainedEvidenceCannotSupportAnotherSubject(string fund, string account, int dateOffset)
    {
        var run = await new DailyMarkToMarketService(new MutableSource(new MarkPriceQuote(160m,
            "official-close", "evidence:aapl", PriceAsOf: Date))).PrepareAsync(Request());
        var json = run.Approval!.Draft.Metadata.Tags![ValuationMarkEvidenceGuard.EvidenceTag];
        ValuationMarkEvidenceGuard.Validate(json, fund, Date.AddDays(dateOffset), Security, account, "AAPL",
                run.Approval.Draft.Metadata.Tags![ValuationMarkEvidenceGuard.DigestTag])
            .Should().Contain("does not match");
    }

    [Fact]
    public async Task PolicyTamperingCannotWidenRetainedMarkAcceptance()
    {
        var run = await new DailyMarkToMarketService(new MutableSource(new MarkPriceQuote(160m,
            "official-close", "evidence:aapl", PriceAsOf: Date))).PrepareAsync(Request());
        var tags = run.Approval!.Draft.Metadata.Tags!;
        var retained = JsonSerializer.Deserialize<ValuationMarkEvidence[]>(tags[ValuationMarkEvidenceGuard.EvidenceTag])!;
        var manipulated = JsonSerializer.Serialize(retained.Select(mark => mark with
        {
            ObservedOn = Date.AddDays(-90), MaximumAgeDays = 999999,
            MinimumConfidence = DailyPortfolioPriceConfidence.Low, PolicyVersion = "invented-policy"
        }).ToArray());
        ValuationMarkEvidenceGuard.Validate(manipulated, "fund-alpha", Date, Security, "broker-1", "AAPL",
                tags[ValuationMarkEvidenceGuard.DigestTag])
            .Should().Contain("differs from the retained server assessment");
    }

    private static DailyMarkToMarketRequest Request() => new(
        new DailyPortfolioPricingPolicy("fund-alpha", "policy-1", "Close", "official-close", "controller", AsOf.AddDays(-20)),
        "2026-07", AsOf, "USD", [new MarkToMarketPosition("AAPL", 10m, 150m, "broker-1", SecurityId: Security)],
        "preparer", "Daily close");

    private sealed class MutableSource(MarkPriceQuote? quote) : IMarkPriceSource
    {
        public MarkPriceQuote? Quote { get; set; } = quote;
        public Task<MarkPriceQuote?> GetMarkPriceAsync(string symbol, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(Quote);
    }
}
