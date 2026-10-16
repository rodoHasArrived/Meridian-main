using System.Text.Json;
using Meridian.Application.Composition;
using Meridian.Application.FundStructure;
using Meridian.Storage.Archival;
using Meridian.Storage.FundStructure;
using Meridian.Storage.FundAccounts;

namespace Meridian.Application.Commands;

/// <summary>Explicit operator preview/apply; database credentials are read only from environment.</summary>
internal sealed class FundStructureTenantBackfillCommand : ICliCommand
{
    private readonly Func<FundStructureTenantBackfillRunner> _createRunner;
    private readonly TextWriter _output;

    public FundStructureTenantBackfillCommand()
        : this(CreateRunner, Console.Out) { }

    internal FundStructureTenantBackfillCommand(Func<FundStructureTenantBackfillRunner> createRunner, TextWriter output)
    {
        _createRunner = createRunner;
        _output = output;
    }

    public IReadOnlyList<string> Triggers { get; } = ["--fund-tenant-backfill"];
    public bool CanHandle(string[] args) => CliArguments.MatchesAnyFlag(args, Triggers);

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        try
        {
            var action = CliArguments.GetValue(args, "--action");
            var outputPath = CliArguments.GetValue(args, "--output");
            if (action is not ("preview" or "apply" or "preview-resolution" or "resolve") || string.IsNullOrWhiteSpace(outputPath))
            {
                await _output.WriteLineAsync("Use --fund-tenant-backfill --action preview|apply|preview-resolution|resolve --output <evidence.json>. Apply and resolve also require --run-id, --plan-hash, --operator, and --review-reference.");
                return CliResult.Fail(2);
            }

            var timeoutText = CliArguments.GetValue(args, "--timeout-seconds") ?? "60";
            if (!int.TryParse(timeoutText, out var timeout) || timeout is < 1 or > 300)
                throw new ArgumentException("Invalid operation timeout.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(timeout));

            if (action is "preview" or "preview-resolution")
            {
                var runner = _createRunner();
                var plan = await (action == "preview" ? runner.PreviewAsync(deadline.Token) : runner.PreviewResolutionAsync(deadline.Token)).ConfigureAwait(false);
                await AtomicFileWriter.WriteAsync(outputPath, JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }), UnixFileMode.UserRead | UnixFileMode.UserWrite, deadline.Token).ConfigureAwait(false);
                await _output.WriteLineAsync($"Preview {plan.PlanHash}: {plan.Stamps.Count} proposed stamps, {plan.Exceptions.Count} exceptions, {plan.Resolutions.Count} reviewed releases, {plan.BlockingReasons.Count} apply blockers.");
                return CliResult.Ok();
            }

            if (!Guid.TryParse(CliArguments.GetValue(args, "--run-id"), out var runId) || runId == Guid.Empty)
                throw new ArgumentException("A run identity is required.");
            var hash = Required(args, "--plan-hash");
            var operatorId = Required(args, "--operator");
            var reference = Required(args, "--review-reference");
            var applyRunner = _createRunner();
            var receipt = await (action == "resolve" ? applyRunner.ResolveAsync(runId, hash, operatorId, reference, deadline.Token)
                : applyRunner.ApplyAsync(runId, hash, operatorId, reference, deadline.Token)).ConfigureAwait(false);
            await AtomicFileWriter.WriteAsync(outputPath, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }), UnixFileMode.UserRead | UnixFileMode.UserWrite, deadline.Token).ConfigureAwait(false);
            await _output.WriteLineAsync($"Receipt {receipt.RunId}: {receipt.StampedRows} stamps, {receipt.QuarantinedRows} exceptions retained.");
            return CliResult.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Connection/configuration errors can contain credentials; never print the exception,
            // its message, connection strings, or raw command arguments through the dispatcher.
            await _output.WriteLineAsync($"Fund tenant backfill refused or failed ({exception.GetType().Name}). No successful apply is claimed; retry a known run identity to recover its retained receipt.");
            return CliResult.Fail(6);
        }
    }

    private static string Required(string[] args, string flag)
        => CliArguments.GetValue(args, flag) is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Required review evidence is missing.");

    private static FundStructureTenantBackfillRunner CreateRunner()
    {
        var fund = Environment.GetEnvironmentVariable(FundStructureStartup.ConnectionStringVariable);
        var ledger = Environment.GetEnvironmentVariable(LedgerStartup.ConnectionStringVariable);
        ArgumentException.ThrowIfNullOrWhiteSpace(fund);
        ArgumentException.ThrowIfNullOrWhiteSpace(ledger);
        var accountsConnection = Environment.GetEnvironmentVariable("MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING");
        FundAccountStoreOptions? accounts = string.IsNullOrWhiteSpace(accountsConnection) ? null : new()
        {
            ConnectionString = accountsConnection,
            Schema = Environment.GetEnvironmentVariable("MERIDIAN_FUND_ACCOUNTS_SCHEMA") ?? "fund_accounts"
        };
        return new(new PostgresFundStructureTenantBackfillStore(new FundStructureStoreOptions
        {
            ConnectionString = fund,
            Schema = Environment.GetEnvironmentVariable(FundStructureStartup.SchemaVariable) ?? "fund_structure"
        }, ledger, Environment.GetEnvironmentVariable(LedgerStartup.SchemaVariable) ?? "ledger", fundAccounts: accounts));
    }
}
