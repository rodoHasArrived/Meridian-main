using Meridian.Application.Reconciliation;
using Meridian.Application.ResultTypes;

namespace Meridian.Application.Commands;

internal sealed class StatementCommands(
    IStatementReconciliationValidationService validationService,
    IDataIntegrationIngestionService ingestionService,
    IReconciliationCaseIntakeService intakeService,
    IStatementReconciliationCheckpointStore checkpointStore) : ICliCommand
{
    public bool CanHandle(string[] args)
        => CliArguments.HasFlag(args, "--statement-import")
           || CliArguments.HasFlag(args, "--statement-validate")
           || CliArguments.HasFlag(args, "--statement-reconcile")
           || CliArguments.HasFlag(args, "--statement-orchestrate");

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var sourceKind = CliArguments.RequireValue(args, "--statement-source-kind", "--statement-source-kind <local|s3|sftp>");
        var sourcePath = CliArguments.RequireValue(args, "--statement-source-path", "--statement-source-path <path>");
        if (sourceKind is null || sourcePath is null)
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);

        var accountIdRaw = CliArguments.GetValue(args, "--statement-account-id");
        var resume = CliArguments.HasFlag(args, "--statement-resume");
        try
        {
            if (CliArguments.HasFlag(args, "--statement-validate"))
            {
                var result = await validationService.ValidateAsync(
                    new StatementReconciliationValidationRequest(sourceKind, sourcePath, MappingProfileId: null),
                    ct).ConfigureAwait(false);
                Console.WriteLine(result);
                return CliResult.Ok();
            }

            if (CliArguments.HasFlag(args, "--statement-import"))
            {
                var result = await ingestionService.IngestAsync(
                    new DataIntegrationIngestionRequest(sourceKind, sourcePath, MappingProfileId: null),
                    ct).ConfigureAwait(false);
                Console.WriteLine($"Imported statement batch {result.ImportId} with {result.ImportedRowCount} row(s).");
                return CliResult.Ok();
            }

            if (CliArguments.HasFlag(args, "--statement-orchestrate"))
            {
                if (!Guid.TryParse(accountIdRaw, out var accountId))
                {
                    Console.WriteLine("--statement-account-id <guid> is required for --statement-orchestrate.");
                    return CliResult.Fail(ErrorCode.ValidationFailed);
                }

                var orchestrator = new StatementReconciliationOrchestrator(
                    validationService,
                    ingestionService,
                    intakeService,
                    checkpointStore);
                var result = await orchestrator.RunAsync(accountId, sourceKind, sourcePath, resume, ct).ConfigureAwait(false);
                Console.WriteLine($"Orchestration status for {result.AccountId}: {result.Status}; stage={result.CurrentStage}; unresolved={result.UnresolvedCount}.");
                return CliResult.Ok();
            }

            var reconcileResult = await intakeService.IntakeAsync(
                new ReconciliationCaseIntakeRequest(sourceKind, sourcePath, MappingProfileId: null),
                ct).ConfigureAwait(false);
            Console.WriteLine($"Reconciled batch {reconcileResult.ImportId}: matches={reconcileResult.MatchCount}, unresolved={reconcileResult.Cases.Count}.");
            return CliResult.Ok();
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine(ex.Message);
            return CliResult.Fail(ErrorCode.FileNotFound);
        }
        catch (NotSupportedException ex)
        {
            Console.WriteLine(ex.Message);
            return CliResult.Fail(ErrorCode.NotSupported);
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine(ex.Message);
            return CliResult.Fail(ErrorCode.ValidationFailed);
        }
    }
}
