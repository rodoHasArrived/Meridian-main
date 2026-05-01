using Meridian.Application.Reconciliation;
using Meridian.Application.ResultTypes;

namespace Meridian.Application.Commands;

internal sealed class StatementCommands : ICliCommand
{
    public bool CanHandle(string[] args)
        => CliArguments.HasFlag(args, "--statement-import")
           || CliArguments.HasFlag(args, "--statement-validate")
           || CliArguments.HasFlag(args, "--statement-reconcile");

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var sourceKind = CliArguments.RequireValue(args, "--statement-source-kind", "--statement-source-kind <local|s3|sftp>");
        var sourcePath = CliArguments.RequireValue(args, "--statement-source-path", "--statement-source-path <path>");
        if (sourceKind is null || sourcePath is null)
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);

        var svc = new StatementReconciliationService();
        if (CliArguments.HasFlag(args, "--statement-validate"))
        {
            var result = await svc.ValidateAsync(sourceKind, sourcePath, ct).ConfigureAwait(false);
            Console.WriteLine(result);
            return CliResult.Ok();
        }

        if (CliArguments.HasFlag(args, "--statement-import"))
        {
            var result = await svc.ImportAsync(sourceKind, sourcePath, ct).ConfigureAwait(false);
            Console.WriteLine($"Imported statement batch {result.ImportId} with {result.RowCount} row(s).");
            return CliResult.Ok();
        }

        var reconcileResult = await svc.ReconcileAsync(sourceKind, sourcePath, ct).ConfigureAwait(false);
        Console.WriteLine($"Reconciled batch {reconcileResult.ImportId}: matches={reconcileResult.MatchCount}, unresolved={reconcileResult.UnresolvedCount}.");
        return CliResult.Ok();
    }
}
