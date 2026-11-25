using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using Xunit;
using ContractSecurityResolver = Meridian.Contracts.SecurityMaster.ISecurityResolver;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Security Master architecture review F2 (owner decision 2026-11-25): per-security ledger accounts
/// are keyed by SecurityId, with the ticker kept as a display attribute.
/// </summary>
public sealed class LedgerSecurityKeyTests
{
    private static readonly Guid MetaSecurityId = Guid.Parse("f2f2f2f2-0000-4000-8000-000000000001");
    private static readonly Guid OtherSecurityId = Guid.Parse("f2f2f2f2-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset Opened = new(2026, 6, 1, 14, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Renamed = new(2026, 6, 9, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void TickerRename_KeepsOneSecuritiesAccountAndOneBasisHistory()
    {
        var ledger = new Meridian.Ledger.Ledger();

        // Bought as FB, then the issuer renamed the ticker to META; the Security Master identity is unchanged.
        ledger.PostLines(Opened, "Buy 10 FB", SecurityTradePostingProjector.Project(
            new SecurityTradePosting(SecurityTradePostingKind.OpenLong, 10m, 3_000m),
            LedgerSecurityKey.Resolved(MetaSecurityId, "FB"), "broker-1"));
        ledger.PostLines(Renamed, "Buy 5 META", SecurityTradePostingProjector.Project(
            new SecurityTradePosting(SecurityTradePostingKind.OpenLong, 5m, 1_600m),
            LedgerSecurityKey.Resolved(MetaSecurityId, "META"), "broker-1"));
        ledger.PostLines(Renamed.AddDays(1), "Sell 15 META", SecurityTradePostingProjector.Project(
            new SecurityTradePosting(SecurityTradePostingKind.CloseLong, 15m, 4_800m, CarryingValue: 4_600m),
            LedgerSecurityKey.Resolved(MetaSecurityId, "META"), "broker-1"));

        var securitiesAccounts = ledger.Accounts.Where(a => a.Name == LedgerAccounts.SecuritiesAccountName).ToArray();
        securitiesAccounts.Should().ContainSingle("a ticker rename must not split the holding's account history")
            .Which.SecurityId.Should().Be(MetaSecurityId);
        ledger.GetBalance(LedgerAccounts.Securities(LedgerSecurityKey.Resolved(MetaSecurityId, "FB"), "broker-1"))
            .Should().Be(0m, "the full 4,600 basis bought under both tickers is relieved from the one account");
        ledger.GetBalance(LedgerAccounts.Securities(LedgerSecurityKey.Resolved(MetaSecurityId), "broker-1"))
            .Should().Be(0m, "the ticker is display-only");
    }

    [Fact]
    public void TickerRename_ThroughStandardChart_KeepsOneShortPayableAndDividendReceivable()
    {
        var chart = StandardChartOfAccounts.Instance;
        var beforeRename = LedgerSecurityKey.Resolved(MetaSecurityId, "FB");
        var afterRename = LedgerSecurityKey.Resolved(MetaSecurityId, "META");

        chart.Resolve(LedgerAccountRole.ShortSecuritiesPayable, "broker-1", beforeRename)
            .Should().Be(chart.Resolve(LedgerAccountRole.ShortSecuritiesPayable, "broker-1", afterRename));
        LedgerAccounts.DividendReceivable(beforeRename, "broker-1")
            .Should().Be(LedgerAccounts.DividendReceivable(afterRename, "broker-1"));
        LedgerAccounts.AccruedInterestReceivable(beforeRename)
            .Should().Be(LedgerAccounts.AccruedInterestReceivable(afterRename));
    }

    [Fact]
    public void SecurityKeyedAccount_KeepsTickerForDisplay_AndNeverEqualsLegacyTickerAccount()
    {
        var keyed = LedgerAccounts.Securities(LedgerSecurityKey.Resolved(MetaSecurityId, "meta "), "broker-1");
        var legacy = LedgerAccounts.Securities(LedgerSecurityKey.Unresolved("META"), "broker-1");

        keyed.Symbol.Should().Be("META");
        keyed.SecurityId.Should().Be(MetaSecurityId);
        keyed.ToString().Should().Contain("META").And.Contain(MetaSecurityId.ToString("D"));
        keyed.Should().NotBe(legacy, "moving legacy balances is a governed reclass, not an implicit merge");
        keyed.Should().NotBe(LedgerAccounts.Securities(LedgerSecurityKey.Resolved(OtherSecurityId, "META"), "broker-1"),
            "two securities that share a ticker are different holdings");
        keyed.GetHashCode().Should().Be(
            LedgerAccounts.Securities(LedgerSecurityKey.Resolved(MetaSecurityId, "FB"), "BROKER-1").GetHashCode());
    }

    [Fact]
    public void StandardChart_RefusesTickerOnlyResolutionOfPerSecurityRoles()
    {
        var resolve = () => StandardChartOfAccounts.Instance.Resolve(LedgerAccountRole.Securities, "broker-1", symbol: "META");

        resolve.Should().Throw<ArgumentException>().WithMessage("*LedgerSecurityKey*");
        StandardChartOfAccounts.Instance.TryGetRegistered(
                LedgerAccounts.Securities(LedgerSecurityKey.Resolved(MetaSecurityId, "META")), out var role)
            .Should().BeTrue();
        role.Should().Be(LedgerAccountRole.Securities);
    }

    [Fact]
    public void RetainedEquivalence_AcceptsLegacyTickerKeyForRebuiltSecurityKeyedLine_Only()
    {
        var rebuilt = LedgerAccounts.Securities(LedgerSecurityKey.Resolved(MetaSecurityId, "META"), "broker-1");

        LedgerSecurityKeyMigration.IsRetainedEquivalent(
            rebuilt, LedgerAccounts.Securities(LedgerSecurityKey.Unresolved("META"), "broker-1")).Should().BeTrue();
        LedgerSecurityKeyMigration.IsRetainedEquivalent(
            rebuilt, LedgerAccounts.Securities(LedgerSecurityKey.Unresolved("FB"), "broker-1")).Should().BeFalse();
        LedgerSecurityKeyMigration.IsRetainedEquivalent(
            rebuilt, LedgerAccounts.Securities(LedgerSecurityKey.Resolved(OtherSecurityId, "META"), "broker-1")).Should().BeFalse();
        LedgerSecurityKeyMigration.IsRetainedEquivalent(
            LedgerAccounts.Securities(LedgerSecurityKey.Unresolved("META"), "broker-1"), rebuilt).Should().BeFalse(
            "a ticker-keyed rebuild must not accept a SecurityId-keyed retained line");
    }

    [Fact]
    public async Task MappingReport_MapsOldTickerKeysAndProposesBalancedReclass()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var legacyFb = LedgerAccounts.Securities(LedgerSecurityKey.Unresolved("FB"), "broker-1");
        var legacyAmbiguous = LedgerAccounts.Securities(LedgerSecurityKey.Unresolved("ABC"), "broker-1");
        var legacyUnknown = LedgerAccounts.ShortSecuritiesPayable(LedgerSecurityKey.Unresolved("ZZZ"), "broker-1");
        ledger.PostLines(Opened, "Legacy FB buy", [(legacyFb, 3_000m, 0m), (LedgerAccounts.CashAccount("broker-1"), 0m, 3_000m)]);
        ledger.PostLines(Opened, "Legacy ABC buy", [(legacyAmbiguous, 100m, 0m), (LedgerAccounts.CashAccount("broker-1"), 0m, 100m)]);
        ledger.PostLines(Opened, "Legacy ZZZ short", [(LedgerAccounts.CashAccount("broker-1"), 50m, 0m), (legacyUnknown, 0m, 50m)]);

        var resolver = new TableResolver(new()
        {
            ["FB"] = [MetaSecurityId],
            ["ABC"] = [MetaSecurityId, OtherSecurityId]
        });

        var report = await LedgerSecurityKeyMigration.BuildReportAsync(ledger, resolver, Renamed);

        report.Rows.Should().HaveCount(3);
        var fb = report.Rows.Single(row => row.LegacyAccount == legacyFb);
        fb.Resolution.Status.Should().Be(CanonicalSecurityResolutionStatus.Resolved);
        fb.TargetAccount.Should().Be(LedgerAccounts.Securities(LedgerSecurityKey.Resolved(MetaSecurityId, "FB"), "broker-1"));
        report.Rows.Single(row => row.LegacyAccount == legacyAmbiguous).Resolution.Status
            .Should().Be(CanonicalSecurityResolutionStatus.Ambiguous);
        report.Rows.Single(row => row.LegacyAccount == legacyUnknown).Resolution.Status
            .Should().Be(CanonicalSecurityResolutionStatus.NotFound);
        report.Unmapped.Should().HaveCount(2);

        var proposal = LedgerSecurityKeyMigration.ProposeReclass(report);
        proposal.Should().NotBeNull();
        proposal!.Lines.Sum(line => line.Debit).Should().Be(proposal.Lines.Sum(line => line.Credit));
        ledger.PostLines(Renamed, proposal.Description, proposal.Lines);
        ledger.GetBalance(legacyFb).Should().Be(0m);
        ledger.GetBalance(fb.TargetAccount!).Should().Be(3_000m);
        ledger.GetBalance(legacyAmbiguous).Should().Be(100m, "ambiguous tickers are never reclassed automatically");
    }

    [Fact]
    public async Task CanonicalResolver_ReportsAmbiguityInsteadOfPickingACandidate()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.FindSecurityIdsByIdentifierAsync(SecurityIdentifierKind.Ticker, "ABC", Arg.Any<DateTimeOffset>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([OtherSecurityId, MetaSecurityId]));
        store.FindSecurityIdsByIdentifierAsync(SecurityIdentifierKind.Ticker, "META", Arg.Any<DateTimeOffset>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([MetaSecurityId]));
        store.FindSecurityIdsByIdentifierAsync(SecurityIdentifierKind.Ticker, "NONE", Arg.Any<DateTimeOffset>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([]));
        ContractSecurityResolver resolver = new SecurityResolver(store);

        var ambiguous = await resolver.ResolveCanonicalAsync(new(SecurityIdentifierKind.Ticker, "ABC", Renamed));
        var resolved = await resolver.ResolveCanonicalAsync(new(SecurityIdentifierKind.Ticker, "META", Renamed));
        var missing = await resolver.ResolveCanonicalAsync(new(SecurityIdentifierKind.Ticker, "NONE", Renamed));

        ambiguous.Status.Should().Be(CanonicalSecurityResolutionStatus.Ambiguous);
        ambiguous.SecurityId.Should().BeNull();
        ambiguous.Candidates.Should().BeEquivalentTo([MetaSecurityId, OtherSecurityId]);
        resolved.IsResolved.Should().BeTrue();
        resolved.SecurityId.Should().Be(MetaSecurityId);
        missing.Status.Should().Be(CanonicalSecurityResolutionStatus.NotFound);
        new CanonicalSecurityResolutionException(ambiguous).Message.Should().Contain("refusing to pick one");
    }

    [Fact]
    public void PaperFill_CarryingSecurityId_PostsToSecurityKeyedAccount()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);

        portfolio.ApplyFill(new ExecutionReport
        {
            OrderId = "ord-1",
            ReportType = ExecutionReportType.Fill,
            Symbol = "META",
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 10m,
            FilledQuantity = 10m,
            FillPrice = 300m,
            Timestamp = Opened,
            SecurityId = MetaSecurityId
        });

        var ledger = portfolio.Ledger!;
        ledger.Accounts.Where(a => a.Name == LedgerAccounts.SecuritiesAccountName)
            .Should().ContainSingle().Which.SecurityId.Should().Be(MetaSecurityId);
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
    }

    private sealed class TableResolver(Dictionary<string, Guid[]> table) : ContractSecurityResolver
    {
        public Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);

        public Task<CanonicalSecurityResolution> ResolveCanonicalAsync(ResolveCanonicalSecurityRequest request, CancellationToken ct = default)
            => Task.FromResult(CanonicalSecurityResolution.FromCandidates(
                request, request.AsOfUtc ?? DateTimeOffset.UtcNow, table.GetValueOrDefault(request.IdentifierValue) ?? []));
    }
}
