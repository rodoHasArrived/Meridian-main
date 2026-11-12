using System.Globalization;

namespace Meridian.Mcp.Tools;

/// <summary>Ledger reads use server authorization and never open journal files or post entries.</summary>
[McpServerToolType]
public sealed class LedgerTools(LedgerApiClient api)
{
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
