using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Application.ResultTypes;
using Serilog;

namespace Meridian.Application.Commands;

public sealed class StatementImportCommands(string dataRoot, ILogger log) : ICliCommand
{
    public bool CanHandle(string[] args)
        => (args.Contains("--statement-validate", StringComparer.OrdinalIgnoreCase)
            || args.Contains("--statement-import", StringComparer.OrdinalIgnoreCase))
           && (Has(args, "--statement-broker")
               || Has(args, "--statement-date"));

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var broker = Get(args, "--statement-broker") ?? "samplebroker";
        var path = Get(args, "--statement-source-path");
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine("Missing required option: --statement-source-path <path>");
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        }

        if (!TryGetStatementDate(args, out var statementDate))
        {
            Console.Error.WriteLine("Invalid --statement-date. Use yyyy-MM-dd.");
            return CliResult.Fail(ErrorCode.ValidationFailed);
        }

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

    private static bool Has(string[] args, string key)
        => args.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static string? Get(string[] args, string key)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i < args.Length - 1 ? args[i + 1] : null;
    }

    private static bool TryGetStatementDate(string[] args, out DateOnly statementDate)
    {
        var value = Get(args, "--statement-date");
        if (string.IsNullOrWhiteSpace(value))
        {
            statementDate = DateOnly.FromDateTime(DateTime.UtcNow);
            return true;
        }

        return DateOnly.TryParse(value, out statementDate);
    }
}
