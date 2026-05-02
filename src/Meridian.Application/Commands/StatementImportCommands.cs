using Meridian.Application.ResultTypes;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Serilog;

namespace Meridian.Application.Commands;

public sealed class StatementImportCommands(string dataRoot, ILogger log) : ICliCommand
{
    public bool CanHandle(string[] args)
        => args.Contains("--statement-validate", StringComparer.OrdinalIgnoreCase)
        || args.Contains("--statement-import", StringComparer.OrdinalIgnoreCase);

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var broker = Get(args, "--statement-broker") ?? "samplebroker";
        var path = Get(args, "--statement-source-path") ?? string.Empty;
        var statementDate = DateOnly.Parse(Get(args, "--statement-date") ?? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"));

        var store = new JsonCanonicalStatementStore(dataRoot);
        var service = new CsvBrokerStatementService(store);

        var request = new BrokerStatementImportRequest(broker, path, statementDate);
        if (args.Contains("--statement-validate", StringComparer.OrdinalIgnoreCase))
        {
            var result = await service.ValidateAsync(request, ct);
            Console.WriteLine($"valid={result.IsValid}; rows={result.RowCount}");
            foreach (var error in result.Errors) Console.WriteLine($"error={error}");
            return result.IsValid ? CliResult.Ok() : CliResult.Fail(ErrorCode.ValidationFailed);
        }

        var imported = await service.ImportAsync(request, ct);
        Console.WriteLine($"imported={imported.Import.ImportId}; rows={imported.Rows.Count}");
        log.Information("Imported broker statement {ImportId} from {SourcePath}", imported.Import.ImportId, path);
        return CliResult.Ok();
    }

    private static string? Get(string[] args, string key)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i < args.Length - 1 ? args[i + 1] : null;
    }
}
