using System.Text;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Alpaca;
using Meridian.FinancialOperations.Reconciliation.Connectors.IbFlex;
using Meridian.FinancialOperations.Reconciliation.Connectors.Ofx;
using Meridian.Infrastructure.Reconciliation;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

/// <summary>
/// End-to-end coverage for the statement connector import pipeline: preview (mapping
/// confidence, per-kind breakdown, suggestions, drift), commit through the real statement-run
/// workflow into breaks and reconciliation cases, duplicate idempotency, deterministic
/// canonical artifacts, and scheduled fetch execution.
/// </summary>
public sealed class StatementImportServiceTests : IDisposable
{
    private readonly string _root = StatementConnectorTestData.CreateTempRoot("mdc_stmt_import_svc");
    private readonly StatementMappingProfileCatalog _catalog;
    private readonly StatementConnectorRegistry _registry;
    private readonly StatementImportService _service;
    private readonly IStatementRunWorkflowService _workflow;

    public StatementImportServiceTests()
    {
        _catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        _registry = new StatementConnectorRegistry(
        [
            new CsvStatementConnector(_catalog),
            new OfxStatementConnector(_catalog),
            new IbFlexStatementConnector(_catalog),
            new AlpacaActivityStatementConnector(_catalog, [], []),
            new FakeFetchingConnector()
        ]);

        var statementStore = new JsonCanonicalStatementStore(_root);
        _workflow = new StatementRunWorkflowService(
            statementStore,
            new JsonReconciliationCaseStore(_root),
            new JsonReconciliationBreakStore(_root),
            new CsvBrokerStatementService(statementStore),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()));
        _service = new StatementImportService(_registry, _catalog, _workflow, _root);
    }

    private static StatementSourceDocument FixtureDocument(string fileName, string? mappingProfileId = null)
        => new(fileName, StatementConnectorTestData.ReadFixture(fileName), mappingProfileId);

    private StatementImportCommitRequest CommitRequest(
        StatementSourceDocument document,
        string fundAccountId = "FUND-A",
        string? connectorId = null)
        => new(
            document,
            connectorId,
            SourceKind: "broker",
            SourceInstitution: "Sample Broker",
            FundAccountId: fundAccountId,
            ExternalAccountId: "EXT-001",
            PeriodStart: new DateOnly(2026, 6, 1),
            PeriodEnd: new DateOnly(2026, 6, 30),
            ToleranceProfileId: null,
            ImportedBy: "test-operator");

    [Fact]
    public async Task Preview_MixedCsv_ReportsMappingsKindBreakdownAndSuggestions()
    {
        var preview = await _service.PreviewAsync(FixtureDocument("csv-mixed-kinds.csv"), connectorId: null);

        preview.ConnectorId.Should().Be(CsvStatementConnector.ConnectorId, "auto-detection picks the CSV catch-all");
        preview.Status.Should().Be("ReadyToImport");
        preview.RecordCount.Should().Be(6);
        preview.KindSummaries.Should().HaveCount(5);
        preview.KindSummaries.Select(summary => summary.Kind).Should().BeEquivalentTo(
            "Position", "CashBalance", "Transaction", "Fee", "Dividend");
        preview.KindSummaries.Single(summary => summary.Kind == "Transaction").RecordCount.Should().Be(2);
        preview.KindSummaries.Single(summary => summary.Kind == "Transaction").SampleRecords.Should().NotBeEmpty();
        preview.ColumnMappings.Should().OnlyContain(mapping => mapping.Confidence == StatementColumnConfidenceDto.Exact);
        preview.ProfileSuggestions.Should().NotBeEmpty();
        preview.ProfileSuggestions[0].ProfileId.Should().Be(StatementMappingProfileRegistry.CanonicalCsvV1ProfileId);
        preview.ProfileSuggestions[0].Score.Should().Be(1.0m);
    }

    [Fact]
    public async Task Preview_FlexXml_AutoDetectsIbFlexConnector()
    {
        var preview = await _service.PreviewAsync(FixtureDocument("ib-flex-sample.xml"), connectorId: null);

        preview.ConnectorId.Should().Be(IbFlexStatementConnector.ConnectorId);
        preview.RecordCount.Should().Be(7);
    }

    [Fact]
    public async Task Preview_UnknownConnectorId_ReportsConnectorNotFound()
    {
        var preview = await _service.PreviewAsync(FixtureDocument("csv-mixed-kinds.csv"), connectorId: "nope");

        preview.Status.Should().Be("NeedsAttention");
        preview.Issues.Should().Contain(issue => issue.Code == "CONNECTOR_NOT_FOUND");
    }

    [Fact]
    public async Task Preview_DriftedLayout_SurfacesFormatDriftWarning()
    {
        var canonical = StatementBuiltInProfiles.All.Single(profile =>
            profile.ProfileId == StatementMappingProfileRegistry.CanonicalCsvV1ProfileId);
        await _catalog.UpsertAsync(canonical with { ProfileId = "acme-clone-v1", IsBuiltIn = false });

        var first = await _service.CommitAsync(CommitRequest(FixtureDocument("csv-mixed-kinds.csv", "acme-clone-v1")));
        first.Duplicate.Should().BeFalse();

        var preview = await _service.PreviewAsync(FixtureDocument("csv-drifted-headers.csv", "acme-clone-v1"), connectorId: null);

        preview.Issues.Should().Contain(issue =>
            issue.Code == "FORMAT_DRIFT" && issue.Severity == StatementParseIssue.WarningSeverity);
    }

    [Fact]
    public async Task Commit_MixedCsv_CreatesRunBreaksAndReconciliationQueueCases()
    {
        var result = await _service.CommitAsync(CommitRequest(FixtureDocument("csv-mixed-kinds.csv")));

        result.Duplicate.Should().BeFalse();
        result.RunId.Should().NotBeNullOrWhiteSpace();
        result.RecordCount.Should().Be(6);
        result.KindSummaries.Should().HaveCount(5);
        // Legacy matcher semantics: two trades and one cash balance breach tolerance; the
        // position, fee, and dividend rows match. Each break becomes a queue case.
        result.BreakCount.Should().Be(3);
        result.CaseCount.Should().Be(3);
        result.BreakIds.Should().HaveCount(3);
        result.CaseIds.Should().HaveCount(3);
        result.CaseIds.Should().OnlyContain(caseId => caseId.StartsWith("case:", StringComparison.OrdinalIgnoreCase));
        result.ReconciliationCaseRoutes.Should().HaveCount(3);
        result.ReconciliationCaseRoutes.Should().OnlyContain(route =>
            route.StartsWith($"/accounting/reconciliation/match?runId={Uri.EscapeDataString(result.RunId)}&caseId=", StringComparison.OrdinalIgnoreCase) &&
            route.Contains("&breakId=", StringComparison.OrdinalIgnoreCase));
        result.ReconciliationCaseLinks.Should().HaveCount(3);
        result.CaseIds.Should().Equal(result.ReconciliationCaseLinks.Select(link => link.CaseId));
        result.ReconciliationCaseRoutes.Should().Equal(result.ReconciliationCaseLinks.Select(link => link.Route));
        result.ReconciliationCaseLinks.Should().OnlyContain(link =>
            link.CaseId.StartsWith("case:", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(link.BreakId) &&
            link.Route.Contains($"caseId={Uri.EscapeDataString(link.CaseId)}", StringComparison.OrdinalIgnoreCase) &&
            link.Route.Contains($"breakId={Uri.EscapeDataString(link.BreakId!)}", StringComparison.OrdinalIgnoreCase) &&
            link.Status == "Open" &&
            !string.IsNullOrWhiteSpace(link.Priority) &&
            !string.IsNullOrWhiteSpace(link.Reason) &&
            !string.IsNullOrWhiteSpace(link.SuggestedNextAction));
        result.Status.Should().Be("Imported");

        var run = await _workflow.GetAsync(result.RunId);
        run.Should().NotBeNull();
        run!.Import.NormalizedRowCount.Should().Be(6);
        run.Cases.Should().HaveCount(3);
        run.Cases.Should().OnlyContain(reconciliationCase => reconciliationCase.Status == "Open");

        File.Exists(Path.Combine(_root, result.RetainedSourcePath)).Should().BeTrue();
        File.Exists(Path.Combine(_root, result.RetainedCanonicalPath)).Should().BeTrue();
        result.RetainedCanonicalEvidencePath.Should().NotBeNull();
        File.Exists(Path.Combine(_root, result.RetainedCanonicalEvidencePath!)).Should().BeTrue();
    }

    [Fact]
    public async Task Commit_RendersDeterministicCanonicalArtifact()
    {
        var result = await _service.CommitAsync(CommitRequest(FixtureDocument("csv-mixed-kinds.csv")));

        var artifact = await File.ReadAllTextAsync(Path.Combine(_root, result.RetainedCanonicalPath));
        artifact.Should().Be(
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency,feesCommission,externalTransactionId,activityCategory,activitySubtype,providerActivityCode,relatedTransactionId,orderId,description\n" +
            "FUND-A,AAPL,100,187.25,-18726.05,trade,2026-06-02,2026-06-04,USD,1.05,T-1001,,,,,,\n" +
            "FUND-A,MSFT,-50,412.10,20603.98,trade,2026-06-15,2026-06-17,USD,1.02,T-1002,,,,,,\n" +
            "FUND-A,AAPL,100,190.10,19010.00,position,2026-06-30,,USD,,,,,,,,\n" +
            "FUND-A,,0,0,31247.93,cash,2026-06-30,,USD,,,,,,,,\n" +
            "FUND-A,,0,0,-25.00,fee,2026-06-28,,USD,,F-9001,,,,,,\n" +
            "FUND-A,AAPL,0,0,24.00,dividend,2026-06-10,,USD,,D-7001,,,,,,\n");
    }

    [Fact]
    public async Task Commit_SameStatementTwice_IsIdempotentDuplicate()
    {
        var first = await _service.CommitAsync(CommitRequest(FixtureDocument("csv-mixed-kinds.csv")));
        var second = await _service.CommitAsync(CommitRequest(FixtureDocument("csv-mixed-kinds.csv")));

        second.Duplicate.Should().BeTrue();
        second.RunId.Should().Be(first.RunId, "the duplicate key is the import id");
        second.Status.Should().Be("Duplicate");
    }

    [Fact]
    public async Task Commit_OfxAndFlexStatements_LandInTheSameQueue()
    {
        var ofxResult = await _service.CommitAsync(CommitRequest(FixtureDocument("ofx-102-bank.ofx"), "FUND-OFX"));
        var flexResult = await _service.CommitAsync(CommitRequest(FixtureDocument("ib-flex-sample.xml"), "FUND-FLEX"));

        ofxResult.RecordCount.Should().Be(4);
        flexResult.RecordCount.Should().Be(7);

        var imports = await _workflow.ListImportsAsync();
        imports.Select(import => import.ImportId).Should().Contain([ofxResult.RunId, flexResult.RunId]);
    }

    [Fact]
    public async Task Commit_BlockingParseErrors_Throw()
    {
        var content = "symbol,quantity,price,cashAmount,activityType,tradeDate\nAAPL,1,10,-10,trade,2026-06-01\n";
        var document = new StatementSourceDocument("missing-account.csv", Encoding.UTF8.GetBytes(content));

        var act = () => _service.CommitAsync(CommitRequest(document));

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*Account*");
    }

    [Fact]
    public async Task Commit_UnsupportedSourceKind_Throws()
    {
        var request = CommitRequest(FixtureDocument("csv-mixed-kinds.csv")) with { SourceKind = "local" };

        var act = () => _service.CommitAsync(request);

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*broker*custodian*");
    }

    [Fact]
    public async Task FetchDocument_NonFetchingConnector_Throws()
    {
        var act = () => _service.FetchDocumentAsync(new StatementFetchRequest(CsvStatementConnector.ConnectorId, "EXT-001"));

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ScheduleStore_LegacySnapshot_DefaultsSourceKindToBroker()
    {
        var schedulePath = Path.Combine(_root, "reconciliation", "statement-fetch-schedules.json");
        Directory.CreateDirectory(Path.GetDirectoryName(schedulePath)!);
        await File.WriteAllTextAsync(
            schedulePath,
            """
            {
              "version": 1,
              "schedules": [
                {
                  "scheduleId": "legacy-schedule",
                  "connectorId": "fake-fetch",
                  "externalAccountId": "EXT-LEGACY",
                  "fundAccountId": "FUND-LEGACY",
                  "sourceInstitution": "Legacy Broker",
                  "mappingProfileId": null,
                  "toleranceProfileId": "statement-default",
                  "cadenceHours": 24,
                  "enabled": true,
                  "lastRunAtUtc": null,
                  "lastRunStatus": null
                }
              ]
            }
            """);

        var schedule = (await new FileStatementFetchScheduleStore(_root).ListAsync()).Single();

        schedule.SourceKind.Should().Be("broker");
    }

    [Fact]
    public async Task ScheduleRunner_RunsDueSchedules_AndRecordsOutcome()
    {
        var scheduleStore = new FileStatementFetchScheduleStore(_root);
        var runner = new StatementFetchScheduleRunner(scheduleStore, _service);
        var schedule = await scheduleStore.UpsertAsync(new StatementFetchSchedule(
            ScheduleId: string.Empty,
            ConnectorId: FakeFetchingConnector.FakeConnectorId,
            ExternalAccountId: "EXT-001",
            FundAccountId: "FUND-SCHED",
            SourceInstitution: "Fake Custodian",
            MappingProfileId: null,
            ToleranceProfileId: "statement-default",
            CadenceHours: 24,
            Enabled: true,
            SourceKind: "custodian"));

        var now = new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);
        var ran = await runner.RunDueSchedulesAsync(now);

        ran.Should().Be(1);
        var updated = (await scheduleStore.ListAsync()).Single();
        updated.LastRunAtUtc.Should().Be(now);
        updated.LastRunStatus.Should().StartWith("Imported");
        (await _workflow.ListImportsAsync()).Single().Broker.Should().Be("custodian");

        // Just ran: not due again until the cadence elapses.
        (await runner.RunDueSchedulesAsync(now.AddHours(1))).Should().Be(0);
        (await runner.RunDueSchedulesAsync(now.AddHours(25))).Should().Be(1);
    }

    [Fact]
    public async Task ScheduleRunner_FetchFailure_IsRecordedNotThrown()
    {
        var scheduleStore = new FileStatementFetchScheduleStore(_root);
        var runner = new StatementFetchScheduleRunner(scheduleStore, _service);
        await scheduleStore.UpsertAsync(new StatementFetchSchedule(
            ScheduleId: "sched-bad",
            ConnectorId: CsvStatementConnector.ConnectorId,
            ExternalAccountId: "EXT-001",
            FundAccountId: "FUND-SCHED",
            SourceInstitution: "Fake Custodian",
            MappingProfileId: null,
            ToleranceProfileId: "statement-default",
            CadenceHours: 24,
            Enabled: true));
        var lastSuccessfulRun = new DateTimeOffset(2026, 7, 17, 6, 0, 0, TimeSpan.Zero);
        await scheduleStore.RecordRunAsync("sched-bad", lastSuccessfulRun, "Imported prior run");

        var ran = await runner.RunDueSchedulesAsync(lastSuccessfulRun.AddHours(25));

        ran.Should().Be(1);
        var updated = (await scheduleStore.ListAsync()).Single();
        updated.LastRunAtUtc.Should().Be(
            lastSuccessfulRun,
            "a transient fetch failure must preserve the last successful schedule watermark");
        updated.LastRunStatus.Should().Be("Failed: NotSupportedException");
    }

    /// <summary>A fetch-capable connector returning a canonical CSV document, for scheduler tests.</summary>
    private sealed class FakeFetchingConnector : IFetchingStatementConnector
    {
        public const string FakeConnectorId = "fake-fetch";

        public StatementConnectorDescriptor Descriptor { get; } = new(
            FakeConnectorId,
            "Fake fetching connector",
            [".fake"],
            SupportsFileImport: false,
            SupportsRemoteFetch: true,
            RequiresMappingProfile: false,
            DefaultProfileId: null);

        public bool CanHandle(StatementSourceDocument document)
            => Path.GetExtension(document.FileName) == ".fake";

        public Task<StatementSourceDocument> FetchAsync(StatementFetchRequest request, CancellationToken ct = default)
        {
            const string content =
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n" +
                "EXT-001,AAPL,5,100,-500,trade,2026-06-20\n";
            return Task.FromResult(new StatementSourceDocument("fetched.fake", Encoding.UTF8.GetBytes(content)));
        }

        public async Task<StatementParseResult> ParseAsync(StatementSourceDocument document, CancellationToken ct = default)
        {
            // Delegate to the CSV pipeline shape by parsing canonical CSV inline.
            var catalog = new StatementMappingProfileCatalog(new NullProfileStore());
            return await new CsvStatementConnector(catalog).ParseAsync(document, ct).ConfigureAwait(false);
        }

        private sealed class NullProfileStore : IStatementMappingProfileStore
        {
            public Task<IReadOnlyList<StatementMappingProfileDocument>> ListAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<StatementMappingProfileDocument>>([]);

            public Task<StatementMappingProfileDocument> UpsertAsync(StatementMappingProfileDocument document, CancellationToken ct = default)
                => Task.FromResult(document);

            public Task<bool> DeleteAsync(string profileId, CancellationToken ct = default)
                => Task.FromResult(false);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
