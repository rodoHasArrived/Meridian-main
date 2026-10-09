using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Reconciliation;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Tests.Reconciliation.Connectors;
using Meridian.Tests.Storage.Reporting;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Integration;

/// <summary>
/// W9-INGEST-009 / #2634: retained bank evidence crosses the PostgreSQL accounting and
/// reporting authorities and the durable statement matcher and operator casework stores.
/// Month-end bank closes guard against lost split-settlement membership, cash balances
/// falsely marked complete, and changed journal populations replacing retained decisions on restart.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class StatementLedgerReconciliationPostgresTests
{
    [ReportingDatabaseFact]
    public Task Bai2Statement_JournalMatchesAndCaseworkSurviveFreshServicesAndRetries()
        => AssertJournalMatchesAndCaseworkSurviveFreshServicesAndRetriesAsync(Bai2Scenario);

    /// <summary>May EUR close: a split wire and signed debit retain booking and settlement dates through restart.</summary>
    [ReportingDatabaseFact]
    public Task Camt053Statement_JournalMatchesAndCaseworkSurviveFreshServicesAndRetries()
        => AssertJournalMatchesAndCaseworkSurviveFreshServicesAndRetriesAsync(Camt053Scenario);

    private static async Task AssertJournalMatchesAndCaseworkSurviveFreshServicesAndRetriesAsync(
        BankStatementScenario scenario)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await ScenarioDatabase.CreateAsync(ct, scenario);
        var services = database.CreateServices();
        var splitA = database.Journal(1_500m, tradeDate: scenario.SplitTradeDate,
            settlementDate: scenario.SplitSettlementDate);
        var splitB = database.Journal(1_000m, tradeDate: scenario.SplitTradeDate,
            settlementDate: scenario.SplitSettlementDate);
        var pair = database.Journal(-154.33m, scenario.PairExternalId,
            tradeDate: scenario.PairTradeDate, settlementDate: scenario.PairSettlementDate);
        var otherBook = database.Journal(2_500m, scenario.SplitExternalId, otherBook: true,
            tradeDate: scenario.SplitTradeDate, settlementDate: scenario.SplitSettlementDate);
        using var postingTarget = new DurableLedgerPostingTarget(services.Journals);
        foreach (var journal in new[] { splitA, splitB, pair, otherBook })
        {
            var posted = await postingTarget.PostAsync(journal, ct);
            posted.WasAppended.Should().BeTrue();
            // The production posting target owns idempotent handoff; the journal store's
            // lower-level append intentionally rejects duplicate immutable identities.
            using var restartedTarget = new DurableLedgerPostingTarget(database.CreateServices().Journals);
            var replay = await restartedTarget.PostAsync(journal, ct);
            replay.WasAppended.Should().BeFalse();
            replay.JournalEntryId.Should().Be(posted.JournalEntryId);
        }

        var source = new LedgerJournalInternalTransactionSource(database.CreateServices().Journals);
        // The retained book must override the caller's USD fallback for the EUR statement.
        var transactions = await source.GetTransactionsAsync(new InternalLedgerTransactionQuery(
            scenario.ExternalAccountId, [database.AccountId.ToString("D"), scenario.ExternalAccountId],
            PeriodStart, PeriodEnd, "USD")
        {
            AccountingScope = database.Scope
        }, ct);
        transactions.Select(item => item.EvidenceReference).Should().BeEquivalentTo(
            new[] { splitA, splitB, pair }.Select(JournalEvidence),
            "the same bank account's parallel book must not satisfy the primary book statement");
        transactions.Should().OnlyContain(item => item.Currency == scenario.Currency,
            "currency-blind cash legs inherit the retained book's denomination");
        var splitTransaction = transactions.Single(item => item.EvidenceReference == JournalEvidence(splitA));
        splitTransaction.TradeDate.Should().Be(scenario.SplitTradeDate);
        splitTransaction.SettlementDate.Should().Be(scenario.SplitSettlementDate);
        var pairTransaction = transactions.Single(item => item.EvidenceReference == JournalEvidence(pair));
        pairTransaction.TradeDate.Should().Be(scenario.PairTradeDate);
        pairTransaction.SettlementDate.Should().Be(scenario.PairSettlementDate);
        pairTransaction.ExternalTransactionId.Should().Be(scenario.PairExternalId);

        var command = database.Command();
        var first = await services.Coordinator.StartAsync(command, ct);
        first.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation,
            first.Workflow.FailureReason);
        first.Workflow.AccountingScope.Should().BeEquivalentTo(database.Scope);
        first.Workflow.OperationsWorkflowId.Should().NotBeNull();
        first.ImportResult.Should().NotBeNull();
        var committed = first.ImportResult!;
        committed.Duplicate.Should().BeFalse();
        committed.RecordCount.Should().Be(3);
        committed.BreakIds.Should().ContainSingle();
        committed.CaseIds.Should().ContainSingle();
        committed.BreakCount.Should().Be(1, "the unmatched closing cash balance needs operator review");
        committed.CaseCount.Should().Be(1);
        var run = (await services.Runs.GetAsync(committed.RunId, ct))!;
        run.Import.AccountingScope.Should().Be(database.Scope);
        var match = (await services.Artifacts.GetAsync(committed.RunId, ct))!;
        match.MatchCount.Should().Be(2);
        var split = match.MatchGroups!.Should().ContainSingle(group =>
            group.RuleIds.Contains("statement-transaction-split-v1")).Subject;
        split.StatementEvidenceReferences.Should().Equal($"{committed.RunId}:3");
        split.InternalEvidenceReferences.Should().BeEquivalentTo(new[] { JournalEvidence(splitA), JournalEvidence(splitB) });
        var exactPair = match.MatchGroups!.Should().ContainSingle(group =>
            group.InternalEvidenceReferences.Contains(JournalEvidence(pair))).Subject;
        exactPair.StatementEvidenceReferences.Should().Equal($"{committed.RunId}:4");
        exactPair.InternalEvidenceReferences.Should().Equal(JournalEvidence(pair));
        match.MatchGroups!.SelectMany(group => group.InternalEvidenceReferences)
            .Should().OnlyHaveUniqueItems("one journal leg cannot be consumed by two match groups");

        await AssertCaseworkAsync(services, database, committed, ct);
        var authorityScope = new StatementReconciliationReportAuthorityScope(TenantId, CompanyId, first.Workflow.WorkflowId);
        var retained = await ReadAuthorityAsync(services.Authority, authorityScope, ct);
        var sourceArtifact = committed.EvidenceVaultIdentity!.Artifacts.Single(item => item.Kind == "statement-source");
        retained[sourceArtifact.RelativePath].Content.Should().Equal(StatementConnectorTestData.ReadFixture(scenario.FixtureName));
        var canonicalArtifact = committed.EvidenceVaultIdentity.Artifacts.Single(item => item.Kind == "statement-canonical");
        var canonicalBytes = await File.ReadAllBytesAsync(Path.Combine(database.Root, committed.RetainedCanonicalPath), ct);
        retained[canonicalArtifact.RelativePath].Content.Should().Equal(canonicalBytes);
        run.Import.SourceFileHash.Should().Be(sourceArtifact.ContentHashSha256);
        run.Import.CanonicalArtifactHash.Should().Be(Sha256Digest.Compute(canonicalBytes));
        var runEvidence = committed.EvidenceVaultIdentity.Artifacts.Single(item => item.Kind == "statement-run-evidence");
        var requiredEvidenceKeys = new[] { sourceArtifact.RelativePath, canonicalArtifact.RelativePath, runEvidence.RelativePath };
        foreach (var key in requiredEvidenceKeys)
        {
            retained[key].Document.IsImmutable.Should().BeTrue("published statement evidence is append-only authority");
            retained[key].Document.Version.Should().Be(1);
        }
        using (var document = JsonDocument.Parse(retained[runEvidence.RelativePath].Content))
        {
            document.RootElement.GetProperty("breakIds").EnumerateArray().Select(item => item.GetString())
                .Should().Equal(committed.BreakIds);
            document.RootElement.GetProperty("caseIds").EnumerateArray().Select(item => item.GetString())
                .Should().Equal(committed.CaseIds);
        }
        var operationId = first.Workflow.OperationsWorkflowId!.Value;
        var timeline = await services.Operations.GetTimelineAsync(operationId, ct);
        var queueBefore = await services.Queue.GetAllAsync(AccessScope, ct: ct);
        var checkpoint = await new FileStatementRunRecoveryRepository(database.Root)
            .GetAsync(committed.RunId, ct);
        checkpoint.Should().NotBeNull();
        checkpoint!.Stage.Should().Be(StatementRunRecoveryStage.Completed);
        checkpoint.Status.Should().Be(StatementRunRecoveryStatus.Completed);

        // Today's book now offers an exact pair that would win ahead of the retained split.
        // Resuming yesterday's completed matching decision must not consume this new posting.
        var laterPair = database.Journal(2_500m, scenario.SplitExternalId,
            tradeDate: scenario.SplitTradeDate, settlementDate: scenario.SplitSettlementDate);
        await database.CreateServices().Journals.AppendAsync(laterPair, ct);

        // Only the reporting coordinator's node-local workspace is discarded. The supported
        // production composition retains statement match and casework authority on durable files.
        var workspace = Path.Combine(database.Root, "runtime", "statement-reconciliation-authority-workspace");
        Directory.Exists(workspace).Should().BeTrue();
        Directory.Delete(workspace, recursive: true);
        var restarted = database.CreateServices();
        // Import duplicate handling reads the existing run without entering matching recovery.
        // Exercise the run's resume path directly against the changed journal population too.
        var imported = run.Import;
        var resumedRun = await restarted.Runs.CreateAsync(new StatementRunRequest(
            imported.Broker, imported.SourceInstitution, imported.FundAccountId, imported.ExternalAccountId,
            imported.StatementPeriodStart, imported.StatementPeriodEnd,
            imported.SourcePath, imported.OriginalFileName,
            imported.MappingProfileId, imported.ToleranceProfileId, imported.ImportedBy, imported.SourceFileHash)
        {
            CanonicalSourcePath = Path.GetFullPath(Path.Combine(database.Root, committed.RetainedCanonicalPath)),
            CanonicalArtifactHash = imported.CanonicalArtifactHash,
            AccountingScope = imported.AccountingScope
        }, ct);
        resumedRun.Should().BeEquivalentTo(run, "resume must preserve the retained split despite a new exact-match journal");
        (await restarted.Coordinator.GetAsync(first.Workflow.WorkflowId, TenantId, CompanyId, ct))
            .Should().BeEquivalentTo(first.Workflow);
        var retry = await restarted.Coordinator.StartAsync(command, ct);
        retry.Workflow.WorkflowId.Should().Be(first.Workflow.WorkflowId);
        retry.Workflow.OperationsWorkflowId.Should().Be(operationId);
        retry.Workflow.Status.Should().Be(StatementReconciliationReportWorkflowStatusDto.AwaitingReconciliation);
        retry.ImportResult!.BreakIds.Should().Equal(committed.BreakIds);
        retry.ImportResult.CaseIds.Should().Equal(committed.CaseIds);
        retry.ImportResult.EvidenceVaultIdentity.Should().BeEquivalentTo(committed.EvidenceVaultIdentity);
        var duplicate = await restarted.Imports.CommitAsync(command.Import with { AccountingScope = database.Scope }, ct);
        duplicate.Duplicate.Should().BeTrue();
        duplicate.RunId.Should().Be(committed.RunId);
        duplicate.BreakIds.Should().Equal(committed.BreakIds);
        duplicate.CaseIds.Should().Equal(committed.CaseIds);
        (await restarted.Runs.ListImportsAsync(ct)).Should().ContainSingle();
        (await restarted.Artifacts.GetAsync(committed.RunId, ct)).Should().BeEquivalentTo(match);
        (await restarted.Operations.GetTimelineAsync(operationId, ct)).Should().BeEquivalentTo(timeline);
        (await restarted.Operations.ListAsync(database.AccountId, database.Period.PeriodId.ToString("D"),
            status: null, ct: ct, ledgerBookId: database.Book.LedgerBookId)).Should().ContainSingle();
        (await restarted.Queue.GetAllAsync(AccessScope, ct: ct)).Should().BeEquivalentTo(queueBefore);
        (await new FileStatementRunRecoveryRepository(database.Root)
            .GetAsync(committed.RunId, ct)).Should().BeEquivalentTo(checkpoint);
        (await restarted.Journals.QueryAsync(new LedgerJournalEntryQuery(EffectiveFrom: PeriodStart, EffectiveTo: PeriodEnd), ct)).Should().HaveCount(5)
            .And.OnlyContain(item => item.Entry.IsBalanced);
        await AssertCaseworkAsync(restarted, database, committed, ct);
        var retainedAfter = await ReadAuthorityAsync(restarted.Authority, authorityScope, ct);
        retainedAfter.Keys.Should().BeEquivalentTo(retained.Keys);
        foreach (var key in requiredEvidenceKeys)
        {
            retainedAfter[key].Should().BeEquivalentTo(retained[key], "retry cannot replace retained evidence");
            retainedAfter[key].Document.Version.Should().Be(1);
        }
    }

    [ReportingDatabaseFact]
    public async Task StatementIntake_RejectsMismatchedAuthorityBeforeImportOrCaseworkEffects()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await ScenarioDatabase.CreateAsync(ct);
        var command = database.Command();
        var scope = database.Scope;
        var mismatches = new (StatementReconciliationReportStartCommand Command, string Code)[]
        {
            (command with { Import = command.Import with { AccountingScope = scope with { FundProfileId = Guid.NewGuid().ToString("D") } } }, "STATEMENT_FUND_SCOPE_MISMATCH"),
            (command with { Import = command.Import with { AccountingScope = scope with { LedgerBookId = database.OtherBook.LedgerBookId } } }, "STATEMENT_LEDGER_BOOK_REQUIRED"),
            (command with { Import = command.Import with { AccountingScope = scope with { AccountingPeriodId = database.OtherPeriod.PeriodId } } }, "STATEMENT_ACCOUNTING_PERIOD_REQUIRED"),
            (command with { Import = command.Import with { AccountingScope = scope with { AsOfDate = PeriodEnd.AddDays(-1) } } }, "STATEMENT_AS_OF_SCOPE_MISMATCH"),
            (command with { TenantId = "other-tenant" }, "STATEMENT_FUND_NOT_AUTHORIZED"),
            (command with { CompanyId = "other-company" }, "STATEMENT_FUND_NOT_AUTHORIZED"),
            (command with { Import = command.Import with { ExternalAccountId = "other-bank-account" } }, "STATEMENT_ACCOUNT_NOT_AUTHORIZED"),
            (command with { Import = command.Import with { PeriodStart = PeriodStart.AddDays(1) } }, "STATEMENT_ACCOUNTING_PERIOD_REQUIRED")
        };
        foreach (var (invalid, code) in mismatches)
        {
            var services = database.CreateServices();
            var failure = await FluentActions.Awaiting(() => services.Coordinator.StartAsync(invalid, ct))
                .Should().ThrowAsync<StatementReconciliationIntakeAuthorityException>();
            failure.Which.Code.Should().Be(code);
            (await services.Runs.ListImportsAsync(ct)).Should().BeEmpty();
            (await services.Feed.ListOpenCasesAsync(AccessScope, ct)).Should().BeEmpty();
            (await services.Queue.GetAllAsync(AccessScope, ct: ct)).Should().BeEmpty();
            (await services.Operations.ListAsync(database.AccountId, database.Period.PeriodId.ToString("D"),
                status: null, ct: ct, ledgerBookId: database.Book.LedgerBookId)).Should().BeEmpty();
            (await services.Journals.QueryAsync(new LedgerJournalEntryQuery(EffectiveFrom: PeriodStart, EffectiveTo: PeriodEnd), ct)).Should().BeEmpty();
            (await database.ReportingAuthorityRowCountAsync(ct)).Should().Be(0);
            Directory.Exists(Path.Combine(database.Root, "reconciliation", "statement-connector-imports"))
                .Should().BeFalse();
        }
    }

    private static string JournalEvidence(LedgerJournalEntryWrite write) => $"internal:journal:{write.Entry.JournalEntryId:D}";

    private static async Task AssertCaseworkAsync(Services services, ScenarioDatabase database,
        StatementImportCommitResultDto committed, CancellationToken ct)
    {
        var cases = await services.Feed.ListOpenCasesAsync(AccessScope, ct);
        cases.Should().ContainSingle().Which.CaseId.Should().Be(committed.CaseIds.Single());
        var breaks = await services.Feed.ListOpenStatementBreaksAsync(AccessScope, ct);
        breaks.Should().ContainSingle().Which.StatementReference.Should().Be($"{committed.RunId}:2");
        var item = (await services.Queue.GetAllAsync(AccessScope, ct: ct)).Should().ContainSingle().Subject;
        item.SourceImportId.Should().Be(committed.RunId);
        item.FundProfileId.Should().Be(database.FundId.ToString("D"));
        item.LedgerBookId.Should().Be(database.Book.LedgerBookId);
        item.AccountingPeriodId.Should().Be(database.Period.PeriodId.ToString("D"));
        item.AsOfDate.Should().Be(PeriodEnd);
        foreach (var other in new[] { new ReconciliationBreakQueueScope("other", CompanyId), new ReconciliationBreakQueueScope(TenantId, "other") })
        {
            (await services.Feed.ListOpenCasesAsync(other, ct)).Should().BeEmpty();
            (await services.Queue.GetAllAsync(other, ct: ct)).Should().BeEmpty();
        }
    }

    private static async Task<Dictionary<string, (StatementReconciliationReportAuthorityDocument Document, byte[] Content)>> ReadAuthorityAsync(
        IStatementReconciliationReportAuthorityStore authority, StatementReconciliationReportAuthorityScope scope, CancellationToken ct)
    {
        var result = new Dictionary<string, (StatementReconciliationReportAuthorityDocument, byte[])>(StringComparer.Ordinal);
        foreach (var key in await authority.ListDocumentKeysAsync(scope, string.Empty, ct))
        {
            var document = (await authority.GetDocumentAsync(scope, key, ct))!;
            var content = (await authority.TryReadDocumentAsync(scope, key, ct))!;
            Sha256Digest.Compute(content).Should().Be(document.Identity.ContentHashSha256);
            result.Add(key, (document, content));
        }
        return result;
    }
}
