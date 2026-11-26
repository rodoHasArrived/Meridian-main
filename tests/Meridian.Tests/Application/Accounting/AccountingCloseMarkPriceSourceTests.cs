using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Integrity;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Application.Accounting;

public sealed class AccountingCloseMarkPriceSourceTests
{
    private static readonly DateOnly Session = new(2026, 7, 2);
    private static readonly DateTimeOffset AsOf = new(2026, 7, 2, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GenericAdjustedHistory_DoesNotEstablishAnAccountingClose()
    {
        var provider = new ResearchOnlyProvider("adjusted-only");

        var result = await new HistoricalCloseMarkPriceSource(provider).GetMarkPriceAsync("AAPL", Session);

        result.Should().BeNull();
        provider.ResearchReads.Should().Be(0, "accounting must not ask for generic research bars");
    }

    [Theory]
    [InlineData(AccountingClosePriceBasis.Unknown)]
    [InlineData(AccountingClosePriceBasis.Adjusted)]
    [InlineData((AccountingClosePriceBasis)255)]
    public async Task AdjustedOrUnknownDeclaredBasis_IsUnavailable(AccountingClosePriceBasis basis)
    {
        var source = new HistoricalCloseMarkPriceSource(new ExplicitCloseProvider("source", Close() with { PriceBasis = basis }));

        (await source.GetMarkPriceAsync("AAPL", Session)).Should().BeNull();
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("future")]
    [InlineData("outside-window")]
    [InlineData("feed")]
    [InlineData("provider")]
    [InlineData("digest")]
    [InlineData("price")]
    public async Task IncompleteOrMismatchedRawEvidence_IsUnavailable(string defect)
    {
        var valid = Close();
        var invalid = defect switch
        {
            "symbol" => valid with { Symbol = "MSFT" },
            "future" => valid with { SessionDate = Session.AddDays(1) },
            "outside-window" => valid with { SessionDate = Session.AddDays(-8) },
            "feed" => valid with { Feed = "" },
            "provider" => valid with { Provider = "" },
            "digest" => valid with { SourceContentHash = "unretained" },
            _ => valid with { Close = 0m }
        };

        (await new HistoricalCloseMarkPriceSource(new ExplicitCloseProvider("source", invalid))
            .GetMarkPriceAsync("AAPL", Session)).Should().BeNull();
    }

    [Fact]
    public async Task Registry_FallsThroughUnsupportedAndAdjustedSources_ToExplicitRawSource()
    {
        using var registry = new ProviderRegistry();
        var unsupported = new ResearchOnlyProvider("research");
        registry.RegisterBackfill(unsupported.Name, unsupported, priority: 1);
        registry.RegisterBackfill("adjusted", new ExplicitCloseProvider("adjusted", Close() with
        {
            PriceBasis = AccountingClosePriceBasis.Adjusted
        }), priority: 2);
        registry.RegisterBackfill("raw", new ExplicitCloseProvider("raw", Close()), priority: 3);

        var quote = await new RegisteredHistoricalCloseMarkPriceSource(registry).GetMarkPriceAsync("AAPL", Session);

        quote.Should().NotBeNull();
        quote!.Price.Should().Be(100m);
        quote.Source.Should().Be("alpaca", "the serving provider is retained, not the registry route name");
        quote.EvidenceReference.Should().Be(Close().ToEvidenceReference());
        unsupported.ResearchReads.Should().Be(0);
    }

    [Fact]
    public async Task UnsupportedSource_PreCancelledRequest_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new HistoricalCloseMarkPriceSource(new ResearchOnlyProvider("research"));

        var act = () => source.GetMarkPriceAsync("AAPL", Session, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Registry_ProviderLocalCancellation_FallsThroughToExplicitRawSource()
    {
        using var registry = new ProviderRegistry();
        registry.RegisterBackfill("timed-out", new ExplicitCloseProvider("timed-out", null,
            new OperationCanceledException("Provider-local timeout")), priority: 1);
        registry.RegisterBackfill("raw", new ExplicitCloseProvider("raw", Close()), priority: 2);

        var quote = await new RegisteredHistoricalCloseMarkPriceSource(registry).GetMarkPriceAsync("AAPL", Session);

        quote.Should().NotBeNull();
        quote!.EvidenceReference.Should().Be(Close().ToEvidenceReference());
    }

    [Fact]
    public async Task Registry_CallerCancellation_PropagatesWithoutTryingFallback()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var registry = new ProviderRegistry();
        registry.RegisterBackfill("raw", new ExplicitCloseProvider("raw", Close()), priority: 1);

        var act = () => new RegisteredHistoricalCloseMarkPriceSource(registry)
            .GetMarkPriceAsync("AAPL", Session, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RawSourceEvidence_SurvivesDraftPostingSerialization_AndChangesRetryIdentity()
    {
        var close = Close();
        var first = await PrepareAsync(close);
        var changedFeed = await PrepareAsync(close with { Feed = "iex" });
        var amendedSource = await PrepareAsync(close with { SourceContentHash = Sha256Digest.ComputeUtf8("amended source record") });
        var draft = first.Approval!.Draft;
        var reference = close.ToEvidenceReference();

        reference.Should().Contain("provider=alpaca").And.Contain("feed=sip").And.Contain("symbol=AAPL")
            .And.Contain("session=2026-07-02").And.Contain("basis=raw").And.Contain("close=100")
            .And.Contain($"sha256={close.SourceContentHash}");
        first.Projection!.Lines.Single().EvidenceReference.Should().Be(reference);
        var mark = JsonSerializer.Deserialize<ValuationMarkEvidence[]>(draft.Metadata.Tags![ValuationMarkEvidenceGuard.EvidenceTag])!
            .Should().ContainSingle().Subject;
        mark.EvidenceReference.Should().Be(reference);
        draft.Metadata.EvidenceReferences.Single(item => item.Kind == "price-mark").Uri.Should().Be(reference);
        draft.Metadata.IdempotencyKey.Should().NotBe(changedFeed.Approval!.Draft.Metadata.IdempotencyKey);
        draft.Metadata.IdempotencyKey.Should().NotBe(amendedSource.Approval!.Draft.Metadata.IdempotencyKey);

        var ledger = new Meridian.Ledger.Ledger();
        var refs = draft.Metadata.EvidenceReferences.Select(item => item.Uri).ToArray();
        var approvedAt = DateTimeOffset.UtcNow;
        first.Approval.Approve("controller", approvedAt, "raw close reviewed", refs)
            .PostTo(ledger, "controller", approvedAt, "approved fixture regression", refs);
        var posted = ledger.GetJournalEntries(new LedgerQuery(ActivityType: "fair-value-mark")).Single();
        var retained = JsonSerializer.Deserialize<JournalEntry>(JsonSerializer.Serialize(posted))!;
        retained.Metadata.EvidenceReferences.Single(item => item.Kind == "price-mark").Uri.Should().Be(reference);
        retained.Metadata.Tags![ValuationMarkEvidenceGuard.EvidenceTag]
            .Should().Be(draft.Metadata.Tags[ValuationMarkEvidenceGuard.EvidenceTag]);
    }

    private static Task<DailyMarkToMarketRun> PrepareAsync(AccountingClosePrice close)
    {
        var policy = new DailyPortfolioPricingPolicy("fund-alpha", "vp-1", "Daily fair value", "raw-close",
            "controller", AsOf.AddDays(-30));
        return new DailyMarkToMarketService(new HistoricalCloseMarkPriceSource(new ExplicitCloseProvider("source", close)))
            .PrepareAsync(new DailyMarkToMarketRequest(policy, "2026-07", AsOf, "USD",
                [new MarkToMarketPosition("AAPL", 10m, 90m)], "preparer", "raw close regression"));
    }

    private static AccountingClosePrice Close() => new("AAPL", Session, 100m, "alpaca", "sip",
        AccountingClosePriceBasis.Raw, Sha256Digest.ComputeUtf8("original raw source record"));

    private sealed class ResearchOnlyProvider(string name) : IHistoricalDataProvider
    {
        public string Name => name;
        public string DisplayName => name;
        public string Description => "Research bars only";
        public HistoricalDataCapabilities Capabilities => HistoricalDataCapabilities.FullFeatured;
        public int ResearchReads { get; private set; }

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            ResearchReads++;
            return Task.FromResult<IReadOnlyList<HistoricalBar>>([new("AAPL", Session, 50m, 50m, 50m, 50m, 10, name)]);
        }
    }

    private sealed class ExplicitCloseProvider(string name, AccountingClosePrice? close, Exception? failure = null) : IHistoricalDataProvider
    {
        public string Name => name;
        public string DisplayName => name;
        public string Description => "Explicit raw-close fixture";
        public Task<AccountingClosePrice?> GetRawAccountingCloseAsync(
            string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
            => failure is null ? Task.FromResult(close) : Task.FromException<AccountingClosePrice?>(failure);
        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
            => throw new InvalidOperationException("Accounting must not use the generic research route.");
    }
}
