using Meridian.FinancialOperations.Reconciliation;
using Meridian.Domain.Reconciliation;
using Meridian.Application.ResultTypes;
using Serilog;

namespace Meridian.Application.Commands;

public sealed class StatementImportCommands(
    IBrokerStatementService brokerStatementService,
    IStatementRunWorkflowService statementRunWorkflowService,
    ILogger log) : ICliCommand
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

        if (args.Contains("--statement-validate", StringComparer.OrdinalIgnoreCase))
        {
            var result = await brokerStatementService.ValidateAsync(new BrokerStatementImportRequest(broker, path, statementDate), ct);
            Console.WriteLine($"valid={result.IsValid}; rows={result.RowCount}");
            foreach (var error in result.Errors) Console.WriteLine($"error={error}");
            return result.IsValid ? CliResult.Ok() : CliResult.Fail(ErrorCode.ValidationFailed);
        }

        var runRequest = await StatementRunCreateRequest.FromFileAsync(
            broker,
            Get(args, "--statement-source-institution") ?? Get(args, "--statement-custodian") ?? broker,
            Get(args, "--statement-fund-account-id") ?? "legacy-fund-account",
            Get(args, "--statement-external-account-id") ?? "legacy-external-account",
            TryGetDate(args, "--statement-period-start", out var periodStart) ? periodStart : statementDate,
            TryGetDate(args, "--statement-period-end", out var periodEnd) ? periodEnd : statementDate,
            path,
            Get(args, "--statement-mapping-profile-id") ?? "legacy-mapping-profile",
            Get(args, "--statement-tolerance-profile-id") ?? "legacy-tolerance-profile",
            Get(args, "--statement-imported-by") ?? Environment.UserName,
            ct: ct);
        var imported = await statementRunWorkflowService.CreateAsync(runRequest.ToStatementRunRequest(), ct).ConfigureAwait(false);
        Console.WriteLine($"imported={imported.Import.ImportId}; rows={imported.Import.NormalizedRowCount}");
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
        if (TryGetDate(args, "--statement-date", out statementDate))
            return true;

        if (!Has(args, "--statement-date"))
        {
            statementDate = DateOnly.FromDateTime(DateTime.UtcNow);
            return true;
        }

        return false;
    }

    private static bool TryGetDate(string[] args, string key, out DateOnly date)
    {
        var value = Get(args, key);
        return DateOnly.TryParse(value, out date);
    }
}
