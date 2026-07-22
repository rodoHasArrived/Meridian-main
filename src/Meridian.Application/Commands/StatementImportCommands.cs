using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Domain.Reconciliation;
using Meridian.Platform.Results;
using Serilog;

namespace Meridian.Application.Commands;

public sealed class StatementImportCommands(
    IStatementImportCommitService importCommitService,
    ILogger log) : ICliCommand
{
    public IReadOnlyList<string> Triggers { get; } = ["--statement-validate", "--statement-import"];

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

        // Bound the file before buffering it into memory: the connectors are invoked only after the
        // whole file is read, and a very large camt.053/BAI2 file could otherwise exhaust the CLI
        // process. Apply the same 20 MiB limit the workstation upload route enforces.
        var fileInfo = new FileInfo(path);
        if (fileInfo.Exists && fileInfo.Length > StatementConnectorLimits.MaxFileBytes)
        {
            Console.Error.WriteLine(
                $"Statement file exceeds the {StatementConnectorLimits.MaxFileBytes / (1024 * 1024)} MiB import limit.");
            return CliResult.Fail(ErrorCode.ValidationFailed);
        }

        // Read the raw source once and route both validate and import through the connector pipeline
        // (CSV, OFX, IB Flex, Alpaca, camt.053, BAI2) rather than the CSV/IB-Flex-only broker router,
        // so the bank formats registered for the workstation are equally usable from the CLI. The
        // connector resolves from file content unless an explicit connector id is supplied.
        var content = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var document = new StatementSourceDocument(
            Path.GetFileName(path),
            content,
            MappingProfileId: Get(args, "--statement-mapping-profile-id"),
            ExternalAccountId: Get(args, "--statement-external-account-id"));

        if (args.Contains("--statement-validate", StringComparer.OrdinalIgnoreCase))
        {
            var result = await importCommitService
                .ValidateAsync(document, Get(args, "--statement-connector-id"), ct)
                .ConfigureAwait(false);
            Console.WriteLine($"valid={result.IsValid}; rows={result.RecordCount}");
            foreach (var error in result.Errors)
                Console.WriteLine($"error={error}");
            return result.IsValid ? CliResult.Ok() : CliResult.Fail(ErrorCode.ValidationFailed);
        }

        // The connector pipeline distinguishes the source channel (broker vs custodian), not a broker
        // name; derive it from the request and carry the operator's broker/custodian label as the
        // institution.
        var sourceKind = Get(args, "--statement-source-kind")
            ?? (string.Equals(broker, "custodian", StringComparison.OrdinalIgnoreCase) ? "custodian" : "broker");

        var commitRequest = new StatementImportCommitRequest(
            document,
            ConnectorId: Get(args, "--statement-connector-id"),
            SourceKind: sourceKind,
            SourceInstitution: Get(args, "--statement-source-institution") ?? Get(args, "--statement-custodian") ?? broker,
            FundAccountId: Get(args, "--statement-fund-account-id") ?? "legacy-fund-account",
            ExternalAccountId: Get(args, "--statement-external-account-id") ?? "legacy-external-account",
            PeriodStart: TryGetDate(args, "--statement-period-start", out var periodStart) ? periodStart : statementDate,
            PeriodEnd: TryGetDate(args, "--statement-period-end", out var periodEnd) ? periodEnd : statementDate,
            ToleranceProfileId: Get(args, "--statement-tolerance-profile-id") ?? StatementToleranceProfile.DefaultProfileId,
            ImportedBy: Get(args, "--statement-imported-by") ?? Environment.UserName);

        var imported = await importCommitService.CommitAsync(commitRequest, ct).ConfigureAwait(false);
        Console.WriteLine($"imported={imported.RunId}; rows={imported.RecordCount}; breaks={imported.BreakCount}; cases={imported.CaseCount}");
        log.Information("Imported statement {RunId} from {SourcePath} through the connector pipeline", imported.RunId, path);
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
