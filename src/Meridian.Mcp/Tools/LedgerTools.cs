using System.Globalization;
using System.Net;

namespace Meridian.Mcp.Tools;

/// <summary>Ledger reads use server authorization and never open journal files or post entries.</summary>
[McpServerToolType]
public sealed class LedgerTools(LedgerApiClient api)
{
    [McpServerTool(Name = "get_run_trial_balance")]
    [Description("Read a strategy run's trial balance at an inclusive as-of instant. Calls the configured Meridian workstation API and honors its issued operator session, tenant scope and run visibility. Returns an explicit unavailable/refused error when no authorized retained ledger exists.")]
    public async Task<string> GetRunTrialBalanceAsync(
        [Description("Retained strategy run identifier")] string runId,
        [Description("Inclusive as-of instant with UTC offset, for example 2026-10-31T23:59:59Z")] DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        LedgerApiClient.ValidateSegment(runId, nameof(runId));
        if (runId.Any(char.IsControl))
            throw new ArgumentException("Run identifier is invalid.", nameof(runId));
        try
        {
            return await api.ReadAsync(runId.Trim(), "trial-balance", new Dictionary<string, string?>
            {
                ["asOf"] = asOf.ToString("O", CultureInfo.InvariantCulture)
            }, ct, maxResponseCharacters: 1_048_576).ConfigureAwait(false);
        }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return "Trial balance access refused by the workstation API. Configure an authorized Meridian operator session.";
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return "Trial balance unavailable: the run is not visible or has no retained ledger.";
        }
        catch (HttpRequestException error) when (error.StatusCode.HasValue)
        {
            return $"Trial balance unavailable: workstation API returned HTTP {(int)error.StatusCode.Value}.";
        }
        catch (HttpRequestException)
        {
            return "Trial balance unavailable: cannot reach the configured workstation API.";
        }
        catch (InvalidDataException)
        {
            return "Trial balance exceeds the MCP response size limit; use the workstation export.";
        }
    }

    [McpServerTool(Name = "get_posted_ledger_journal")]
    [Description("Read authoritative posted journal entries in an accessible accounting period, including policy, rule and source-event evidence.")]
    public Task<string> GetPostedJournalAsync(Guid periodId, CancellationToken ct = default) =>
        api.ReadPeriodAsync(periodId, "journal-entries", null, ct);

    [McpServerTool(Name = "get_posted_trial_balance")]
    [Description("Read the authoritative accounting-period trial balance using server-owned book and tenant access.")]
    public Task<string> GetPostedTrialBalanceAsync(Guid periodId, CancellationToken ct = default) =>
        api.ReadPeriodAsync(periodId, "trial-balance", null, ct);

    [McpServerTool(Name = "get_posted_running_balance")]
    [Description("Read an account's posted accounting-period running balance. The server applies ledger, period and tenant authorization.")]
    public Task<string> GetPostedRunningBalanceAsync(Guid periodId, string account, CancellationToken ct = default) =>
        api.ReadPeriodAsync(periodId, "accounts/running-balance", account, ct);

    [McpServerTool(Name = "get_ledger_completeness")]
    [Description("Compare independent fill, asset and loan source populations against posted journals in an exact book/period/aggregate/account scope. Unavailable source populations remain incomplete.")]
    public Task<string> GetCompletenessAsync(Guid ledgerBookId, Guid periodId, Guid aggregateId, string financialAccountId,
        string sourceKinds = "Fill,AssetEvent,LoanEvent", string? sessionId = null, CancellationToken ct = default) =>
        api.ReadCompletenessAsync(ledgerBookId, periodId, aggregateId, financialAccountId, sourceKinds, sessionId, ct);

    [McpServerTool(Name = "get_ledger_journal")]
    [Description("Read complete journal entries and audit metadata for a retained strategy run. Results are paged; offset starts at 0 and limit is 1..10000.")]
    public Task<string> GetJournalAsync(string runId, string? from = null, string? to = null, int offset = 0, int limit = 500, CancellationToken ct = default)
    {
        if (offset < 0 || limit is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(limit));
        return api.ReadAsync(runId, "entries", new Dictionary<string, string?>
        {
            ["from"] = from,
            ["to"] = to,
            ["offset"] = offset.ToString(CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture)
        }, ct);
    }

    [McpServerTool(Name = "get_ledger_trial_balance")]
    [Description("Read the current or ISO-8601 as-of trial balance of a retained run, with debit/credit totals and financial-account identity.")]
    public Task<string> GetTrialBalanceAsync(string runId, string? asOf = null, string? financialAccountId = null, CancellationToken ct = default) =>
        api.ReadAsync(runId, asOf is null ? "accounts" : "trial-balance/as-of", new Dictionary<string, string?>
        { ["asOf"] = asOf, ["financialAccountId"] = financialAccountId }, ct);

    [McpServerTool(Name = "get_ledger_accounts")]
    [Description("Read ledger account summaries, including balances, debit/credit totals, posting counts and first/last dates.")]
    public Task<string> GetAccountsAsync(string runId, string? financialAccountId = null, CancellationToken ct = default) =>
        api.ReadAsync(runId, "accounts", new Dictionary<string, string?> { ["financialAccountId"] = financialAccountId }, ct);

    [McpServerTool(Name = "get_ledger_running_balance")]
    [Description("Read one account's running balance with source journal metadata. Qualify ambiguous account names with type, symbol and financial account; empty string selects unscoped.")]
    public Task<string> GetRunningBalanceAsync(string runId, string accountName, string? accountType = null, string? symbol = null, string? financialAccountId = null, string? from = null, string? to = null, CancellationToken ct = default) =>
        api.ReadAsync(runId, "running-balance", new Dictionary<string, string?>
        {
            ["accountName"] = accountName,
            ["accountType"] = accountType,
            ["symbol"] = symbol,
            ["financialAccountId"] = financialAccountId,
            ["from"] = from,
            ["to"] = to
        }, ct);

    [McpServerTool(Name = "get_symbol_ledger")]
    [Description("Read complete journals linked to a symbol through metadata or securities account lines, including counterpart cash legs.")]
    public Task<string> GetSymbolLedgerAsync(string runId, string symbol, string? from = null, string? to = null, int offset = 0, int limit = 500, CancellationToken ct = default)
    {
        LedgerApiClient.ValidateSegment(symbol, nameof(symbol));
        if (offset < 0 || limit is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(limit));
        return api.ReadAsync(runId, "symbols/" + Uri.EscapeDataString(symbol), new Dictionary<string, string?>
        {
            ["from"] = from,
            ["to"] = to,
            ["offset"] = offset.ToString(CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture)
        }, ct);
    }
}
