using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Platform.Results;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Xunit;

namespace Meridian.Tests.Application.Commands;

[Trait("Category", "Unit")]
[Collection("Sequential")]
public sealed class StatementImportCommandsTests
{
    [Fact]
    public void CanHandle_BrokerSpecificValidate_ReturnsTrue()
    {
        var command = new StatementImportCommands(new StubBrokerStatementService(), new StubStatementRunWorkflowService(), Logger.None);

        command.CanHandle(
            [
                "--statement-validate",
                "--statement-broker", "samplebroker",
                "--statement-source-path", "statement.csv"
            ]).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_GenericLocalValidate_ReturnsFalse()
    {
        var command = new StatementImportCommands(new StubBrokerStatementService(), new StubStatementRunWorkflowService(), Logger.None);

        command.CanHandle(
            [
                "--statement-validate",
                "--statement-source-kind", "local",
                "--statement-source-path", "statement.csv"
            ]).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidStatementDate_ReturnsValidationFailure()
    {
        var command = new StatementImportCommands(new StubBrokerStatementService(), new StubStatementRunWorkflowService(), Logger.None);

        var result = await CommandTestConsole.CaptureErrorAsync(
            () => command.ExecuteAsync(
                [
                    "--statement-validate",
                    "--statement-broker", "samplebroker",
                    "--statement-source-path", "statement.csv",
                    "--statement-date", "not-a-date"
                ]));

        result.Error.Should().Be(ErrorCode.ValidationFailed);
    }

    [Fact]
    public async Task Dispatcher_PrefersBrokerImportOnlyForBrokerSpecificStatementArgs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-statement-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var statementPath = Path.Combine(root, "statement.csv");
        await File.WriteAllTextAsync(
            statementPath,
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA1,SPY,10,500,5000,BUY,2026-01-02\n");
        var brokerStatementService = new CsvBrokerStatementService(new JsonCanonicalStatementStore(root));
        var workflowService = new StubStatementRunWorkflowService();
        var statementService = new StatementReconciliationService();
        var statementAdapter = new StatementReconciliationContextAdapter(statementService);

        var dispatcher = new CommandDispatcher(
            new StatementImportCommands(brokerStatementService, workflowService, Logger.None),
            new StatementCommands(
                statementAdapter,
                statementAdapter,
                statementAdapter,
                new Meridian.FinancialOperations.Reconciliation.InMemoryStatementReconciliationCheckpointStore()));

        var originalOut = Console.Out;
        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);

            var (handled, result) = await dispatcher.TryDispatchAsync(
                [
                    "--statement-validate",
                    "--statement-broker", "samplebroker",
                    "--statement-source-path", statementPath,
                    "--statement-date", "2026-01-31"
                ]);

            handled.Should().BeTrue();
            result.Success.Should().BeTrue();
            writer.ToString().Should().Contain("valid=True; rows=1");
        }
        finally
        {
            Console.SetOut(originalOut);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_StatementImport_UsesWorkflowService()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-statement-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var statementPath = Path.Combine(root, "statement.csv");
        await File.WriteAllTextAsync(
            statementPath,
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA1,SPY,10,500,5000,BUY,2026-01-02\n");

        var workflowService = new StubStatementRunWorkflowService();
        var command = new StatementImportCommands(new StubBrokerStatementService(), workflowService, Logger.None);
        var originalOut = Console.Out;

        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);

            var result = await command.ExecuteAsync(
                [
                    "--statement-import",
                    "--statement-broker", "samplebroker",
                    "--statement-source-path", statementPath,
                    "--statement-date", "2026-01-31"
                ]);

            result.Success.Should().BeTrue();
            workflowService.CreateRequests.Should().ContainSingle();
            workflowService.CreateRequests[0].SourcePath.Should().Be(statementPath);
            writer.ToString().Should().Contain("imported=workflow-import-id; rows=3");
        }
        finally
        {
            Console.SetOut(originalOut);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_StatementImport_PersistsImportBreaksAndCasesThroughWorkflow()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-statement-import-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var statementPath = Path.Combine(root, "statement.csv");
        await File.WriteAllLinesAsync(statementPath,
        [
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
            "FUND-1,SPY,10,500,0,position,2026-05-28",
            "FUND-1,,0,0,2500.25,cash,2026-05-28",
            "FUND-1,MSFT,1,15.75,0,fee,2026-05-28"
        ]);

        var services = new ServiceCollection();
        services.AddStatementReconciliationServices(root);
        using var provider = services.BuildServiceProvider();
        var command = new StatementImportCommands(
            provider.GetRequiredService<IBrokerStatementService>(),
            provider.GetRequiredService<IStatementRunWorkflowService>(),
            Logger.None);
        var importStore = provider.GetRequiredService<ICanonicalStatementStore>();
        var breakStore = provider.GetRequiredService<IReconciliationBreakStore>();
        var caseStore = provider.GetRequiredService<IReconciliationCaseStore>();
        var originalOut = Console.Out;

        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);

            var result = await command.ExecuteAsync(
                [
                    "--statement-import",
                    "--statement-broker", "custodian",
                    "--statement-source-institution", "Sample Custodian",
                    "--statement-fund-account-id", "fund-account-1",
                    "--statement-external-account-id", "external-account-1",
                    "--statement-mapping-profile-id", "canonical-csv-v1",
                    "--statement-tolerance-profile-id", "statement-default",
                    "--statement-imported-by", "ops-user",
                    "--statement-source-path", statementPath,
                    "--statement-date", "2026-05-31",
                    "--statement-period-start", "2026-05-01",
                    "--statement-period-end", "2026-05-31"
                ]);

            result.Success.Should().BeTrue();
            writer.ToString().Should().Contain("imported=");

            var imports = await importStore.ListImportsAsync();
            imports.Should().ContainSingle();
            imports[0].Broker.Should().Be("custodian");
            imports[0].SourceInstitution.Should().Be("Sample Custodian");

            // Sided reconciliation against an unprovisioned (empty) internal book: the position,
            // cash, and fee rows each lack an internal counterpart, so all three are honest
            // unmatched breaks (the retired shim fabricated a position self-match).
            var breaks = await breakStore.ListOpenAsync();
            breaks.Should().HaveCount(3);
            breaks.Should().OnlyContain(item => item.ImportId == imports[0].ImportId);

            var cases = await caseStore.ListAsync();
            cases.Should().HaveCount(3);
            cases.Should().OnlyContain(item => item.ImportId == imports[0].ImportId && item.Attachments.Count > 0);
            cases.Should().OnlyContain(item => item.BreakExplanation != null);
        }
        finally
        {
            Console.SetOut(originalOut);
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubBrokerStatementService : IBrokerStatementService
    {
        public Task<BrokerStatementValidationResult> ValidateAsync(BrokerStatementImportRequest request, CancellationToken ct = default)
            => Task.FromResult(new BrokerStatementValidationResult(true, [], 1));

        public Task<BrokerStatementImportResult> ImportAsync(BrokerStatementImportRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("Import should flow through IStatementRunWorkflowService.");
    }

    private sealed class StubStatementRunWorkflowService : IStatementRunWorkflowService
    {
        public List<StatementRunRequest> CreateRequests { get; } = [];

        public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CanonicalStatementImport>>([]);

        public Task<StatementRunWorkflowResult> CreateAsync(StatementRunRequest request, CancellationToken cancellationToken = default)
        {
            CreateRequests.Add(request);
            var import = new CanonicalStatementImport(
                "workflow-import-id",
                request.Broker,
                request.StatementPeriodEnd,
                DateTimeOffset.UtcNow,
                request.SourcePath,
                request.SourceFileHash,
                3,
                3);
            return Task.FromResult(new StatementRunWorkflowResult(import, [], []));
        }

        public Task<StatementRunWorkflowResult?> GetAsync(string runId, CancellationToken cancellationToken = default)
            => Task.FromResult<StatementRunWorkflowResult?>(null);

        public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenBreaksAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>([]);

        public Task<IReadOnlyList<ReconciliationCase>> ListCasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReconciliationCase>>([]);
    }
}
