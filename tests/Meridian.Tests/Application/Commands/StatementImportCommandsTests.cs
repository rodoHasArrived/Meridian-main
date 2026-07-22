using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
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
        var command = new StatementImportCommands(new StubBrokerStatementService(), new StubStatementImportCommitService(), Logger.None);

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
        var command = new StatementImportCommands(new StubBrokerStatementService(), new StubStatementImportCommitService(), Logger.None);

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
        var command = new StatementImportCommands(new StubBrokerStatementService(), new StubStatementImportCommitService(), Logger.None);

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
        var commitService = new StubStatementImportCommitService();
        var statementService = new StatementReconciliationService();
        var statementAdapter = new StatementReconciliationContextAdapter(statementService);

        var dispatcher = new CommandDispatcher(
            new StatementImportCommands(brokerStatementService, commitService, Logger.None),
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
    public async Task ExecuteAsync_StatementImport_RoutesThroughConnectorPipeline()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-statement-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var statementPath = Path.Combine(root, "statement.csv");
        await File.WriteAllTextAsync(
            statementPath,
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA1,SPY,10,500,5000,BUY,2026-01-02\n");

        var commitService = new StubStatementImportCommitService();
        var command = new StatementImportCommands(new StubBrokerStatementService(), commitService, Logger.None);
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
            commitService.Requests.Should().ContainSingle();
            commitService.Requests[0].Document.FileName.Should().Be("statement.csv", "the raw source file is handed to the connector pipeline");
            commitService.Requests[0].SourceKind.Should().Be("broker", "a non-custodian broker maps to the broker channel");
            writer.ToString().Should().Contain("imported=run-connector-1; rows=3");
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
            provider.GetRequiredService<Meridian.FinancialOperations.Reconciliation.Connectors.IStatementImportCommitService>(),
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

            // With no internal book wired (the default empty population provider), the matcher
            // reconciles each statement row against nothing and correctly surfaces all three rows
            // (position, cash, fee) as unmatched breaks — the previous self-matcher fabricated a
            // position match and only opened two.
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

    [Fact]
    public async Task ExecuteAsync_StatementImport_ImportsBai2BankFileThroughConnectorPipeline()
    {
        // A BAI2 bank file must be importable from the CLI. Before routing through the connector
        // pipeline, --statement-import rejected .bai files as invalid canonical CSV.
        var root = Path.Combine(Path.GetTempPath(), $"meridian-statement-bai2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var statementPath = Path.Combine(root, "statement.bai");
        await File.WriteAllTextAsync(statementPath, string.Join("\n",
            "01,CITIBANK,MERIDIAN,260531,0800,1,,,2/",
            "02,MERIDIAN,CITIBANK,1,260531,,USD,2/",
            "03,0975312468,USD,015,1234567,,/",
            "16,115,250000,,BANKREF01,CUSTREF01,Incoming wire/",
            "49,1234567,3/",
            "98,1234567,1,3/",
            "99,1234567,1,5/"));

        var services = new ServiceCollection();
        services.AddStatementReconciliationServices(root);
        using var provider = services.BuildServiceProvider();
        var command = new StatementImportCommands(
            provider.GetRequiredService<IBrokerStatementService>(),
            provider.GetRequiredService<IStatementImportCommitService>(),
            Logger.None);
        var breakStore = provider.GetRequiredService<IReconciliationBreakStore>();
        var originalOut = Console.Out;

        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);

            var result = await command.ExecuteAsync(
                [
                    "--statement-import",
                    "--statement-broker", "custodian",
                    "--statement-fund-account-id", "fund-account-1",
                    "--statement-external-account-id", "external-account-1",
                    "--statement-source-path", statementPath,
                    "--statement-date", "2026-05-31",
                    "--statement-period-start", "2026-05-01",
                    "--statement-period-end", "2026-05-31"
                ]);

            result.Success.Should().BeTrue("a BAI2 bank file must be importable from the CLI, not rejected as invalid CSV");
            writer.ToString().Should().Contain("imported=");
            // The balance and transaction rows reconcile against an empty book, surfacing as breaks.
            var breaks = await breakStore.ListOpenAsync();
            breaks.Should().NotBeEmpty("the BAI2 rows were parsed and reconciled through the connector pipeline");
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
            => throw new NotSupportedException("Import should flow through the connector commit pipeline.");
    }

    private sealed class StubStatementImportCommitService : IStatementImportCommitService
    {
        public List<StatementImportCommitRequest> Requests { get; } = [];

        public Task<StatementImportCommitResultDto> CommitAsync(StatementImportCommitRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new StatementImportCommitResultDto(
                RunId: "run-connector-1",
                Duplicate: false,
                RecordCount: 3,
                KindSummaries: [],
                BreakCount: 3,
                CaseCount: 3,
                RetainedSourcePath: "raw",
                RetainedCanonicalPath: "canonical",
                Status: "Committed",
                NextAction: "Review breaks"));
        }
    }
}
