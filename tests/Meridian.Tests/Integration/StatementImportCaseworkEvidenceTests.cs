using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Bai2;
using Meridian.FinancialOperations.Reconciliation.Connectors.Camt;
using Meridian.Infrastructure.Reconciliation;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Tests.Reconciliation.Connectors;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Integration;

/// <summary>
/// Month-end bank statement evidence travels through the production import, durable matcher,
/// and shared workstation case feed. These scenarios supplement the connector golden packs.
/// </summary>
public sealed class StatementImportCaseworkEvidenceTests : IDisposable
{
    private static readonly Guid FundAccountId = Guid.Parse("38804073-d0ae-4ea4-a59f-134cf0953d74");
    private static readonly Guid FundProfileId = Guid.Parse("0a60f035-afba-498f-b83d-7fbc350156bb");
    private static readonly DateOnly PeriodStart = new(2026, 5, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 5, 31);
    private static readonly DateOnly TradeDate = new(2026, 5, 27);
    private static readonly ReconciliationBreakQueueScope AccessScope = new("tenant-bank", "company-bank");
    private readonly string _root = StatementConnectorTestData.CreateTempRoot("statement-casework-evidence");

    [Theory]
    [InlineData("camt053-sample.xml", Camt053StatementConnector.ConnectorId, "DE89370400440532013000", "EUR")]
    [InlineData("bai2-sample.bai", Bai2StatementConnector.ConnectorId, "0975312468", "USD")]
    public async Task BankStatement_CommitRetainsEvidenceAndFeedsDurableScopedCases(
        string fileName, string connectorId, string externalAccountId, string currency)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var sourceBytes = StatementConnectorTestData.ReadFixture(fileName);
        var request = Request(new StatementSourceDocument(fileName, sourceBytes), connectorId, externalAccountId);
        var workflow = CreateWorkflow();
        var service = CreateImportService(workflow);

        var committed = await service.CommitAsync(request, ct);

        committed.Duplicate.Should().BeFalse();
        committed.RecordCount.Should().Be(3);
        committed.CaseCount.Should().Be(3, "an unavailable internal book must not fabricate matched bank activity");
        committed.BreakCount.Should().Be(3);
        await AssertRetainedEvidenceAsync(committed, sourceBytes, connectorId, externalAccountId, currency, ct);

        // Rebuild every file-backed reconciliation service before reading the operator feed.
        var restartedWorkflow = CreateWorkflow();
        var feed = await CreateFeedAsync(restartedWorkflow, externalAccountId, currency, ct);
        await AssertCaseFeedAsync(feed, committed, ct);
        var breaks = await feed.ListOpenStatementBreaksAsync(AccessScope, ct);
        breaks.Should().HaveCount(3);
        breaks.Select(item => item.StatementReference).Should().BeEquivalentTo(
            Enumerable.Range(1, 3).Select(row => $"{committed.RunId}:{row}"));
        breaks.Should().Contain(item =>
            item.Classification == ReconciliationBreakClassifications.InternalTransactionPopulationUnavailable,
            "missing ledger activity remains an explicit limitation in the workstation feed");

        var duplicate = await CreateImportService(restartedWorkflow).CommitAsync(request, ct);
        duplicate.Duplicate.Should().BeTrue();
        duplicate.RunId.Should().Be(committed.RunId);
        duplicate.CaseIds.Should().BeEquivalentTo(committed.CaseIds);
        duplicate.BreakIds.Should().BeEquivalentTo(committed.BreakIds);
        (await restartedWorkflow.ListImportsAsync(ct)).Should().ContainSingle();
        await AssertCaseFeedAsync(feed, committed, ct);

        var checkpoint = await new FileStatementRunRecoveryRepository(_root).GetAsync(committed.RunId, ct);
        checkpoint.Should().NotBeNull();
        checkpoint!.Stage.Should().Be(StatementRunRecoveryStage.Completed);
        checkpoint.Status.Should().Be(StatementRunRecoveryStatus.Completed);
    }

    [Theory]
    [InlineData("camt053-sample.xml", Camt053StatementConnector.ConnectorId)]
    [InlineData("bai2-sample.bai", Bai2StatementConnector.ConnectorId)]
    public async Task BankStatement_WrongAccountCannotPublishEvidenceOrCases(string fileName, string connectorId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var workflow = CreateWorkflow();
        var request = Request(
            new StatementSourceDocument(fileName, StatementConnectorTestData.ReadFixture(fileName)),
            connectorId,
            "UNRELATED-BANK-ACCOUNT");

        await FluentActions.Awaiting(() => CreateImportService(workflow).CommitAsync(request, ct))
            .Should().ThrowAsync<InvalidDataException>();

        Directory.Exists(Path.Combine(_root, "reconciliation", "statement-connector-imports")).Should().BeFalse();
        (await workflow.ListImportsAsync(ct)).Should().BeEmpty();
        (await workflow.ListCasesAsync(ct)).Should().BeEmpty();
        var feed = await CreateFeedAsync(workflow, "UNRELATED-BANK-ACCOUNT", "USD", ct);
        (await feed.ListOpenCasesAsync(AccessScope, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task SplitSettlement_RetainedMatchGroupFeedsOnlyTheUnmatchedCaseAfterRestart()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        const string externalAccountId = "CUSTODY-MAY";
        var populations = new InternalReconciliationPopulations([], [],
        [
            new InternalLedgerTransaction("ledger:spy:a", null, externalAccountId, "SPY", "USD",
                TradeDate, TradeDate, "trade", 6m, -3_000m, "internal:journal:leg-a"),
            new InternalLedgerTransaction("ledger:spy:b", null, externalAccountId, "SPY", "USD",
                TradeDate, TradeDate, "trade", 4m, -2_000m, "internal:journal:leg-b"),
            new InternalLedgerTransaction("ledger:msft", null, externalAccountId, "MSFT", "USD",
                TradeDate, TradeDate, "trade", 5m, -1_000m, "internal:journal:pair")
        ]);
        var sourceBytes = Encoding.UTF8.GetBytes(
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId\n" +
            "CUSTODY-MAY,SPY,10,500,-5000,trade,2026-05-27,2026-05-27,USD,0,SPY-MAY\n" +
            "CUSTODY-MAY,MSFT,5,200,-1000,trade,2026-05-27,2026-05-27,USD,0,MSFT-MAY\n" +
            "CUSTODY-MAY,XYZ,1,42,-42,trade,2026-05-27,2026-05-27,USD,0,XYZ-MAY\n");
        var request = Request(new StatementSourceDocument("may-settlements.csv", sourceBytes),
            CsvStatementConnector.ConnectorId, externalAccountId);
        var committed = await CreateImportService(CreateWorkflow(populations)).CommitAsync(request, ct);

        committed.CaseCount.Should().Be(1);
        committed.BreakCount.Should().Be(1);
        var artifacts = new FileStatementRunMatchArtifactStore(_root);
        var retainedMatch = await artifacts.GetAsync(committed.RunId, ct);
        retainedMatch.Should().NotBeNull();
        retainedMatch!.MatchCount.Should().Be(2);
        retainedMatch.MatchGroups.Should().HaveCount(2);
        var split = retainedMatch.MatchGroups!.Should()
            .ContainSingle(group => group.RuleIds.Contains("statement-transaction-split-v1")).Subject;
        split.StatementEvidenceReferences.Should().Equal($"{committed.RunId}:1");
        split.InternalEvidenceReferences.Should().Equal("internal:journal:leg-a", "internal:journal:leg-b");

        // A restarted read/replay must use the retained match, even when the current book is unavailable.
        var restarted = CreateWorkflow();
        var feed = await CreateFeedAsync(restarted, externalAccountId, "USD", ct);
        await AssertCaseFeedAsync(feed, committed, ct);
        (await feed.ListOpenStatementBreaksAsync(AccessScope, ct)).Should().ContainSingle()
            .Which.StatementReference.Should().Be($"{committed.RunId}:3");
        var duplicate = await CreateImportService(restarted).CommitAsync(request, ct);
        duplicate.Duplicate.Should().BeTrue();
        duplicate.CaseIds.Should().Equal(committed.CaseIds);
        var replayedMatch = await artifacts.GetAsync(committed.RunId, ct);
        replayedMatch.Should().BeEquivalentTo(retainedMatch);
        await AssertCaseFeedAsync(feed, committed, ct);
    }

    private async Task AssertRetainedEvidenceAsync(StatementImportCommitResultDto committed, byte[] sourceBytes,
        string connectorId, string externalAccountId, string currency, CancellationToken ct)
    {
        var raw = await File.ReadAllBytesAsync(Path.Combine(_root, committed.RetainedSourcePath), ct);
        raw.Should().Equal(sourceBytes);
        var canonical = await File.ReadAllBytesAsync(Path.Combine(_root, committed.RetainedCanonicalPath), ct);
        var run = await CreateWorkflow().GetAsync(committed.RunId, ct);
        run.Should().NotBeNull();
        run!.Import.SourceFileHash.Should().Be(Sha256Digest.Compute(raw));
        run.Import.CanonicalArtifactHash.Should().Be(Sha256Digest.Compute(canonical));
        run.Import.StatementPeriodStart.Should().Be(PeriodStart);
        run.Import.StatementPeriodEnd.Should().Be(PeriodEnd);
        run.Import.ExternalAccountId.Should().Be(externalAccountId);
        committed.RetainedCanonicalEvidencePath.Should().NotBeNull();
        var sidecar = await File.ReadAllBytesAsync(Path.Combine(_root, committed.RetainedCanonicalEvidencePath!), ct);
        var evidence = JsonSerializer.Deserialize<StatementCanonicalEvidenceArtifact>(sidecar,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } });
        evidence.Should().NotBeNull();
        evidence!.ConnectorId.Should().Be(connectorId);
        evidence.Records.Should().HaveCount(3);
        evidence.Records.Should().OnlyContain(record => record.Account == externalAccountId && record.Currency == currency);
        evidence.Records.Select(record => record.CashAmount).Should().BeEquivalentTo(new[] { 12_345.67m, 2_500m, -154.33m });
    }

    private static async Task AssertCaseFeedAsync(ReconciliationApiService feed,
        StatementImportCommitResultDto committed, CancellationToken ct)
    {
        var cases = await feed.ListOpenCasesAsync(AccessScope, ct);
        cases.Select(item => item.CaseId).Should().BeEquivalentTo(committed.CaseIds);
        cases.Should().OnlyContain(item => item.ImportId == committed.RunId && item.Status == "Open");
        committed.ReconciliationCaseLinks.Select(link => link.CaseId).Should().BeEquivalentTo(committed.CaseIds);
        committed.ReconciliationCaseLinks.Should().OnlyContain(link =>
            link.Route.Contains($"runId={Uri.EscapeDataString(committed.RunId)}", StringComparison.Ordinal) &&
            link.Route.Contains($"caseId={Uri.EscapeDataString(link.CaseId)}", StringComparison.Ordinal));
        foreach (var unauthorized in new[]
        {
            new ReconciliationBreakQueueScope("other-tenant", AccessScope.CompanyId),
            new ReconciliationBreakQueueScope(AccessScope.TenantId, "other-company")
        })
        {
            (await feed.ListOpenCasesAsync(unauthorized, ct)).Should().BeEmpty();
            (await feed.ListOpenStatementBreaksAsync(unauthorized, ct)).Should().BeEmpty();
        }
    }

    private async Task<ReconciliationApiService> CreateFeedAsync(StatementRunWorkflowService workflow,
        string externalAccountId, string currency, CancellationToken ct)
    {
        var accountPath = Path.Combine(_root, "fund-accounts.json");
        var accounts = new InMemoryFundAccountService(accountPath);
        if (await accounts.GetAccountAsync(FundAccountId, ct) is null)
        {
            await accounts.CreateAccountAsync(new CreateAccountRequest(FundAccountId, AccountTypeDto.Custody,
                externalAccountId, "May custody reconciliation", currency,
                new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), "bank-operations", FundId: FundProfileId), ct);
        }
        var tenancy = new FileFundProfileTenancyRegistry(Path.Combine(_root, "tenancy.json"));
        await tenancy.BindAsync(FundProfileId.ToString("D"), AccessScope.TenantId, AccessScope.CompanyId, ct);
        return new ReconciliationApiService(workflow, accounts, tenancy);
    }

    private StatementRunWorkflowService CreateWorkflow(InternalReconciliationPopulations? populations = null)
    {
        var imports = new JsonCanonicalStatementStore(_root);
        return new StatementRunWorkflowService(imports, new JsonReconciliationCaseStore(_root),
            new JsonReconciliationBreakStore(_root), new CsvBrokerStatementService(imports),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()),
            new RetainedBookPopulationProvider(populations ?? InternalReconciliationPopulations.Empty),
            IdentityReconciliationFxRateProvider.Instance, new InMemoryStatementToleranceProfileProvider(),
            new FileStatementRunRecoveryRepository(_root), new FileStatementRunMatchArtifactStore(_root),
            caseworkCommitStore: new FileStatementCaseworkCommitStore(_root));
    }

    private StatementImportService CreateImportService(StatementRunWorkflowService workflow)
    {
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        return new StatementImportService(new StatementConnectorRegistry(
            [new CsvStatementConnector(catalog), new Camt053StatementConnector(), new Bai2StatementConnector()]),
            catalog, workflow, _root);
    }

    private static StatementImportCommitRequest Request(StatementSourceDocument document, string connectorId,
        string externalAccountId) => new(document, connectorId, "custodian", "May Bank",
        FundAccountId.ToString("D"), externalAccountId, PeriodStart, PeriodEnd, null, "bank-operations");

    private sealed class RetainedBookPopulationProvider(InternalReconciliationPopulations populations)
        : IInternalReconciliationPopulationProvider
    {
        public Task<InternalReconciliationPopulations> GetPopulationsAsync(
            InternalReconciliationPopulationContext context, CancellationToken ct = default)
            => Task.FromResult(populations);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
