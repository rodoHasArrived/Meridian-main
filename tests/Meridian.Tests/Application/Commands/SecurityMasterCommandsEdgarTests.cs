using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Serilog.Core;
using Xunit;

namespace Meridian.Tests.Application.Commands;

[Trait("Category", "Unit")]
public sealed class SecurityMasterCommandsEdgarTests
{
    [Fact]
    public async Task ExecuteAsync_EdgarProvider_InvokesOrchestratorWithoutFileImportService()
    {
        var orchestrator = new FakeEdgarIngestOrchestrator();
        var command = new SecurityMasterCommands(
            importService: null,
            log: Logger.None,
            securityMasterService: null,
            edgarIngestOrchestrator: orchestrator);

        var originalOut = Console.Out;
        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);

            var result = await command.ExecuteAsync(
                [
                    "--security-master-ingest",
                    "--provider", "edgar",
                    "--scope", "all-filers",
                    "--include-xbrl",
                    "--include-filing-documents",
                    "--cik", "789019",
                    "--max-filers", "5",
                    "--dry-run"
                ],
                CancellationToken.None);

            result.Success.Should().BeTrue();
            orchestrator.LastRequest.Should().NotBeNull();
            orchestrator.LastRequest!.Scope.Should().Be("all-filers");
            orchestrator.LastRequest.IncludeXbrl.Should().BeTrue();
            orchestrator.LastRequest.IncludeFilingDocuments.Should().BeTrue();
            orchestrator.LastRequest.Cik.Should().Be("789019");
            orchestrator.LastRequest.MaxFilers.Should().Be(5);
            orchestrator.LastRequest.DryRun.Should().BeTrue();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private sealed class FakeEdgarIngestOrchestrator : IEdgarIngestOrchestrator
    {
        public EdgarIngestRequest? LastRequest { get; private set; }

        public Task<EdgarIngestResult> IngestAsync(EdgarIngestRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new EdgarIngestResult(
                FilersProcessed: 0,
                TickerAssociationsStored: 0,
                FactsStored: 0,
                SecurityDataStored: 0,
                SecuritiesCreated: 0,
                SecuritiesAmended: 0,
                SecuritiesSkipped: 0,
                ConflictsDetected: 0,
                DryRun: request.DryRun,
                Errors: []));
        }
    }
}
