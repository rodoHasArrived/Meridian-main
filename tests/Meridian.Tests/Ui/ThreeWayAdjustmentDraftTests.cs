using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class ThreeWayAdjustmentDraftTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-three-way-adjustment-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Cutoff = new(2026, 11, 13, 23, 59, 59, TimeSpan.Zero);
    private static readonly ThreeWayReconciliationScopeDto Scope = new("tenant", "company", "profile", Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "USD", "synthetic", "external-1");

    [Fact]
    public void FeeProposal_RequiresExactIndependentFeeAndCompletePostingIdentityCoverage()
    {
        var sources = Sources();
        var ready = Proposals(sources).Single(proposal => proposal.Status == "Ready");
        ready.Amount.Should().Be(1m);
        ready.EvidenceIds.Should().Equal("fee-123");
        ready.EvidenceHash.Should().HaveLength(64);
        foreach (var broker in new[]
        {
            sources[2] with { CashActivities = [] },
            sources[2] with { CashActivities = [sources[2].CashActivities[0] with { Amount = -2m }] },
            sources[2] with { CashActivities = [sources[2].CashActivities[0] with { Currency = "EUR" }] },
            sources[2] with { CashActivities = [sources[2].CashActivities[0], sources[2].CashActivities[0]] }
        })
            Proposals([sources[0], sources[1], broker]).Should().OnlyContain(proposal => proposal.Status == "InsufficientEvidence");
        Proposals([sources[0] with { TransactionIdentityCoverageComplete = false }, sources[1], sources[2]])
            .Should().OnlyContain(proposal => proposal.Status == "InsufficientEvidence");
        Proposals([sources[0] with { PostedExternalTransactionIds = ["fee-123"] }, sources[1], sources[2]])
            .Should().OnlyContain(proposal => proposal.Status == "InsufficientEvidence");
    }

    [Fact]
    public async Task Prepare_UsesFrozenEvidenceAndStableDraftIdentity_WithoutApprovalPostingOrSourceClearing()
    {
        var fixture = await FixtureAsync();
        var result = await fixture.Drafts.PrepareAsync(Scope.FinancialAccountId, Access(), fixture.Request, "operator");
        var retry = await fixture.Drafts.PrepareAsync(Scope.FinancialAccountId, Access(), fixture.Request, "operator");

        result.JournalEntryId.Should().Be(retry.JournalEntryId);
        result.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        result.TotalDebits.Should().Be(0m, "the workbench performs its normal validation; preparation does not certify a journal");
        result.Lines.Should().HaveCount(2);
        result.Lines.Single(line => line.Side == AccountingTemplateLineSideDto.Debit).Amount.Should().Be(1m);
        result.Lines.Single(line => line.Side == AccountingTemplateLineSideDto.Credit).Amount.Should().Be(1m);
        result.Lines.Should().OnlyContain(line => line.LedgerAccountFinancialAccountId == Scope.FinancialAccountId.ToString("D"));
        result.EvidenceLinks.Should().Contain("urn:sha256:" + fixture.Request.EvidenceHash);
        result.TreasuryContext!.SettlementReference.Should().Be("three-way-fee:fee-123");
        result.DataProvenanceToken.Should().Be("simulated");
        await fixture.Workbench.DidNotReceive().SubmitApprovalAsync(Arg.Any<SubmitManualJournalEntryApprovalRequest>(), Arg.Any<CancellationToken>());
        (await fixture.Queue.GetAllAsync(Access())).Should().OnlyContain(item => item.Status == ReconciliationBreakQueueStatus.Open);
    }

    [Fact]
    public async Task TwoCompletedRunsForSameFee_PrepareOneEconomicDraftIdentity()
    {
        var fixture = await FixtureAsync();
        var first = await fixture.Drafts.PrepareAsync(Scope.FinancialAccountId, Access(), fixture.Request, "operator");
        var secondRun = await fixture.Comparison.RunAsync(Scope.FinancialAccountId, Access(),
            new(Scope.LedgerBookId, Scope.AccountingPeriodId, "run-2", Cutoff));
        var proposal = secondRun.AdjustmentProposals.Single(candidate => candidate.Status == "Ready");
        var second = await fixture.Drafts.PrepareAsync(Scope.FinancialAccountId, Access(),
            fixture.Request with { OperationId = "run-2", EvidenceHash = proposal.EvidenceHash }, "operator");

        second.JournalEntryId.Should().Be(first.JournalEntryId);
        second.TreasuryContext!.IdempotencyKey.Should().Be(first.TreasuryContext!.IdempotencyKey);
        second.ThreeWayFeeCorrection!.EconomicKey.Should().Be(first.ThreeWayFeeCorrection!.EconomicKey);
        first.IsAdjustingEntry.Should().BeTrue();
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("fee-id")]
    public async Task Prepare_CorruptedRetainedProposalCannotReuseAnUnchangedEvidenceHash(string corruption)
    {
        var fixture = await FixtureAsync();
        var store = new FileThreeWayReconciliationEvidenceStore(new() { RootDirectory = _root });
        var receipt = (await store.ReadRunAsync(Scope, fixture.Request.OperationId, CancellationToken.None))!;
        var altered = receipt.Result.AdjustmentProposals.Select(proposal => proposal.CheckId != fixture.Request.CheckId
            ? proposal : corruption switch
            {
                "amount" => proposal with { Amount = 20m },
                "currency" => proposal with { Currency = "EUR" },
                _ => proposal with { EvidenceIds = ["not-the-retained-broker-fee"] }
            }).ToArray();
        await store.SaveRunAsync(receipt with { Result = receipt.Result with { AdjustmentProposals = altered } }, CancellationToken.None);

        var action = () => fixture.Drafts.PrepareAsync(Scope.FinancialAccountId, Access(), fixture.Request, "operator");

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Current source operands*");
        await fixture.Workbench.DidNotReceive().SaveDraftAsync(Arg.Any<SaveManualJournalEntryDraftRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Prepare_InterveningJournalOrChangedPeriodBlocksNewDraft()
    {
        var fixture = await FixtureAsync();
        var changed = Sources();
        changed[0] = changed[0] with { EvidenceReference = "urn:sha256:new-intervening-journal-set" };
        fixture.Reader.ReadAsync(Scope, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(changed);
        var action = () => fixture.Drafts.PrepareAsync(Scope.FinancialAccountId, Access(), fixture.Request, "operator");
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Current source operands*");
        await fixture.Workbench.DidNotReceive().SaveDraftAsync(Arg.Any<SaveManualJournalEntryDraftRequest>(), Arg.Any<CancellationToken>());
        fixture.Reader.ReadAsync(Scope, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Sources());
        fixture.Ledger.GetPeriodAsync(Scope.AccountingPeriodId, Arg.Any<CancellationToken>()).Returns(Period() with { Version = 5 });
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*current open accounting period*");
    }

    [Fact]
    public void RegisteredConfigurationFallback_LoadsOnlyExplicitEnabledScopes()
    {
        var data = new Dictionary<string, string?>
        {
            ["Reconciliation:ThreeWay:Schedules:0:TenantId"] = "tenant",
            ["Reconciliation:ThreeWay:Schedules:0:CompanyId"] = "company",
            ["Reconciliation:ThreeWay:Schedules:0:FinancialAccountId"] = Scope.FinancialAccountId.ToString(),
            ["Reconciliation:ThreeWay:Schedules:0:LedgerBookId"] = Scope.LedgerBookId.ToString(),
            ["Reconciliation:ThreeWay:Schedules:0:AccountingPeriodId"] = Scope.AccountingPeriodId.ToString(),
            ["Reconciliation:ThreeWay:Schedules:0:Enabled"] = "true"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddThreeWayReconciliationServices(null, _ => _root);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<ThreeWayReconciliationOptions>();
        options.Schedules.Should().ContainSingle().Which.FinancialAccountId.Should().Be(Scope.FinancialAccountId);
        options.Schedules[0].Enabled.Should().BeTrue();
    }

    private async Task<Fixture> FixtureAsync()
    {
        var options = new ThreeWayReconciliationOptions { RootDirectory = _root, CashAccountPath = "Assets:Cash", FeeExpenseAccountPath = "Expenses:Fees" };
        var store = new FileThreeWayReconciliationEvidenceStore(options);
        var reader = Substitute.For<IThreeWayReconciliationSourceReader>();
        reader.AuthorizeAsync(Scope.FinancialAccountId, Arg.Any<ReconciliationBreakQueueScope>(), Scope.LedgerBookId,
            Scope.AccountingPeriodId, Arg.Any<CancellationToken>()).Returns(Scope);
        reader.ReadAsync(Scope, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Sources());
        var queue = new FileReconciliationBreakQueueRepository(_root, NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        var comparison = new ThreeWayReconciliationService(reader, store, queue, options, new Clock());
        var run = await comparison.RunAsync(Scope.FinancialAccountId, Access(), new(Scope.LedgerBookId, Scope.AccountingPeriodId, "run-1", Cutoff));
        var ready = run.AdjustmentProposals.Single(proposal => proposal.Status == "Ready");
        var ledger = Substitute.For<ILedgerJournalStore>();
        ledger.GetPeriodAsync(Scope.AccountingPeriodId, Arg.Any<CancellationToken>()).Returns(Period());
        var configuration = Substitute.For<IAccountingConfigurationService>();
        configuration.GetWorkspaceAsync(Scope.FundProfileId, Scope.LedgerBookId, Arg.Any<CancellationToken>(), Scope.TenantId, Scope.CompanyId)
            .Returns(new AccountingConfigurationWorkspaceDto(Scope.FundProfileId, Scope.LedgerBookId, AccountingConfigurationStatusDto.Active,
                "v1", Cutoff, [], [new("cash", "Assets:Cash", "Cash", "Asset"), new("fees", "Expenses:Fees", "Fee Expense", "Expense")],
                [], [], [], [], TenantId: Scope.TenantId, CompanyId: Scope.CompanyId));
        var workbench = Substitute.For<IManualJournalEntryWorkbenchService>();
        workbench.SaveDraftAsync(Arg.Any<SaveManualJournalEntryDraftRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SaveManualJournalEntryDraftRequest>().Draft);
        var drafts = new ThreeWayAdjustmentDraftService(reader, store, options, ledger, configuration, workbench, timeProvider: new Clock());
        return new(reader, ledger, workbench, queue, drafts, comparison,
            new(Scope.LedgerBookId, Scope.AccountingPeriodId, "run-1", ready.CheckId, 4, ready.EvidenceHash));
    }

    private static ThreeWaySourceSnapshotDto[] Sources()
        => [new("ledger", Cutoff, "urn:sha256:ledger-snapshot", 1_000m, [], ThreeWayReconciliationOptions.SupportedBasisConvention, [], "simulated")
            { AccountingPeriodVersion = 4, TransactionIdentityCoverageComplete = true },
            new("oms", Cutoff, "urn:synthetic:oms", 1_000m, [], ThreeWayReconciliationOptions.SupportedBasisConvention, [], "simulated"),
            new("broker", Cutoff, "urn:synthetic:broker-snapshot", 999m, [], ThreeWayReconciliationOptions.SupportedBasisConvention, [], "simulated")
            { CashActivities = [new("fee-123", "FEE", -1m, "USD", Cutoff.AddMinutes(-1), "urn:synthetic:fee-packet")] }];
    private static IReadOnlyList<ThreeWayAdjustmentProposalDto> Proposals(IReadOnlyList<ThreeWaySourceSnapshotDto> sources)
        => ThreeWayAdjustmentProposalBuilder.Build(sources, ThreeWayReconciliationComparer.Compare(sources, Cutoff,
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5)), Cutoff);
    private static ReconciliationBreakQueueScope Access() => new(Scope.TenantId, Scope.CompanyId);
    private static LedgerAccountingPeriod Period() => new(Scope.AccountingPeriodId, Scope.LedgerBookId, 2026, 11, "November",
        new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Open", Cutoff.AddDays(-1), null, 4);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Cutoff.AddSeconds(1); }
    private sealed record Fixture(IThreeWayReconciliationSourceReader Reader, ILedgerJournalStore Ledger,
        IManualJournalEntryWorkbenchService Workbench, FileReconciliationBreakQueueRepository Queue,
        ThreeWayAdjustmentDraftService Drafts, ThreeWayReconciliationService Comparison, PrepareThreeWayAdjustmentRequestDto Request);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
