namespace Meridian.Ledger;

/// <summary>
/// Customizable hierarchical chart of accounts for ledger postings and reporting rollups.
/// Account paths use colon-delimited segments such as Assets:Cash:Brokerage.
/// </summary>
public sealed class ChartOfAccounts
{
    private readonly Dictionary<string, ChartOfAccountsNode> _nodes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All registered accounts, sorted by account type and path.</summary>
    public IReadOnlyList<ChartOfAccountsNode> Accounts =>
        _nodes.Values
            .OrderBy(node => node.Account.AccountType)
            .ThenBy(node => node.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Registers an account path and returns the ledger account to use in journal entries.
    /// Missing parent paths are created as summary accounts with the same account type.
    /// </summary>
    public LedgerAccount Register(
        string path,
        LedgerAccountType accountType,
        string? symbol = null,
        string? financialAccountId = null)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedSymbol = NormalizeOptional(symbol, uppercase: true);
        var normalizedFinancialAccountId = NormalizeOptional(financialAccountId, uppercase: false);
        var parentPath = GetParentPath(normalizedPath);

        if (parentPath is not null)
            EnsureParents(normalizedPath, accountType);

        if (_nodes.TryGetValue(normalizedPath, out var existing))
        {
            if (existing.Account.AccountType != accountType)
            {
                throw new ArgumentException(
                    $"Chart account '{normalizedPath}' is already registered as {existing.Account.AccountType}.",
                    nameof(accountType));
            }

            if (!string.Equals(existing.Account.Symbol, normalizedSymbol, StringComparison.Ordinal)
                || !string.Equals(existing.Account.FinancialAccountId, normalizedFinancialAccountId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Chart account '{normalizedPath}' is already registered with a different account scope.",
                    nameof(path));
            }

            return existing.Account;
        }

        var account = new LedgerAccount(
            normalizedPath,
            accountType,
            normalizedSymbol,
            normalizedFinancialAccountId);
        _nodes[normalizedPath] = new ChartOfAccountsNode(account, normalizedPath, parentPath);
        return account;
    }

    /// <summary>Returns the registered node for <paramref name="path"/>, or null when absent.</summary>
    public ChartOfAccountsNode? Find(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _nodes.TryGetValue(normalizedPath, out var node) ? node : null;
    }

    /// <summary>Returns direct child accounts for the supplied account path.</summary>
    public IReadOnlyList<ChartOfAccountsNode> GetChildren(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _nodes.Values
            .Where(node => string.Equals(node.ParentPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Returns descendant accounts for the supplied account path.</summary>
    public IReadOnlyList<ChartOfAccountsNode> GetDescendants(string path)
    {
        var normalizedPath = NormalizePath(path);
        var prefix = normalizedPath + ":";
        return _nodes.Values
            .Where(node => node.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Aggregates a flat trial balance up the chart hierarchy.
    /// Direct balances come from exact account-path matches; aggregate balances include descendants.
    /// </summary>
    public IReadOnlyList<LedgerChartBalance> AggregateBalances(
        IReadOnlyDictionary<LedgerAccount, decimal> trialBalance,
        string? rootPath = null)
    {
        ArgumentNullException.ThrowIfNull(trialBalance);

        var root = string.IsNullOrWhiteSpace(rootPath) ? null : NormalizePath(rootPath);
        var balancesByPath = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var (account, balance) in trialBalance)
        {
            var accountPath = NormalizePath(account.Name);
            balancesByPath.TryGetValue(accountPath, out var current);
            balancesByPath[accountPath] = current + balance;
        }

        var nodes = _nodes.Values
            .Where(node => root is null
                           || string.Equals(node.Path, root, StringComparison.OrdinalIgnoreCase)
                           || node.Path.StartsWith(root + ":", StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return nodes
            .Select(node =>
            {
                balancesByPath.TryGetValue(node.Path, out var directBalance);
                var aggregateBalance = directBalance;
                var prefix = node.Path + ":";

                foreach (var (path, balance) in balancesByPath)
                {
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        aggregateBalance += balance;
                }

                return new LedgerChartBalance(node.Account, node.Path, node.ParentPath, directBalance, aggregateBalance);
            })
            .ToList();
    }

    private void EnsureParents(string path, LedgerAccountType accountType)
    {
        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = string.Empty;

        for (var index = 0; index < segments.Length - 1; index++)
        {
            current = index == 0 ? segments[index] : current + ":" + segments[index];
            if (_nodes.TryGetValue(current, out var existing))
            {
                if (existing.Account.AccountType != accountType)
                {
                    throw new ArgumentException(
                        $"Chart account parent '{current}' is registered as {existing.Account.AccountType}, not {accountType}.",
                        nameof(accountType));
                }

                continue;
            }

            var parentPath = GetParentPath(current);
            _nodes[current] = new ChartOfAccountsNode(
                new LedgerAccount(current, accountType),
                current,
                parentPath);
        }
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var segments = path
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (segments.Length == 0)
            throw new ArgumentException("Chart account path must contain at least one segment.", nameof(path));

        return string.Join(':', segments);
    }

    private static string? GetParentPath(string path)
    {
        var index = path.LastIndexOf(':');
        return index < 0 ? null : path[..index];
    }

    private static string? NormalizeOptional(string? value, bool uppercase)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return uppercase ? normalized.ToUpperInvariant() : normalized;
    }
}
