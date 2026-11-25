using Meridian.Contracts.Integrity;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Opens and verifies durable journal logs by scope (a run, session or book id).</summary>
public interface ILedgerJournalLogFactory
{
    /// <summary>Opens the journal log for <paramref name="scopeId"/>, creating it when absent.</summary>
    ILedgerJournalLog Open(string scopeId, LedgerPolicy? policy);

    /// <summary>
    /// Re-verifies the journal for <paramref name="scopeId"/>. Returns <c>null</c> when no journal exists.
    /// </summary>
    Task<LedgerJournalChainVerification?> VerifyAsync(string scopeId, CancellationToken ct = default);

    /// <summary>
    /// Loads the verified ledger for <paramref name="scopeId"/>, or <c>null</c> when no journal exists.
    /// </summary>
    /// <exception cref="InvalidDataException">The journal fails verification.</exception>
    Task<IReadOnlyLedger?> LoadAsync(string scopeId, CancellationToken ct = default);
}

/// <summary>
/// Stores each scope's journal in its own directory under <c>{root}/{sha256(scope)}</c>, so any scope
/// id is a safe directory name and two scopes can never share a journal.
/// </summary>
public sealed class FileLedgerJournalLogFactory : ILedgerJournalLogFactory
{
    private readonly string _root;
    private readonly IChartOfAccounts _chart;

    /// <param name="root">Directory that holds one sub-directory per journal.</param>
    /// <param name="chart">Account registry used to verify journals created by a bound ledger.</param>
    public FileLedgerJournalLogFactory(string root, IChartOfAccounts? chart = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _chart = chart ?? StandardChartOfAccounts.Instance;
    }

    /// <summary>Default location under a storage root: <c>{storageRoot}/ledger/journals</c>.</summary>
    public static string DefaultRoot(string storageRoot) => Path.Combine(storageRoot, "ledger", "journals");

    /// <inheritdoc />
    public ILedgerJournalLog Open(string scopeId, LedgerPolicy? policy)
        => new FileLedgerJournalLog(DirectoryFor(scopeId), scopeId, policy);

    /// <inheritdoc />
    public Task<LedgerJournalChainVerification?> VerifyAsync(string scopeId, CancellationToken ct = default)
        => FileLedgerJournalLog.VerifyDirectoryAsync(DirectoryFor(scopeId), scopeId, _chart, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyLedger?> LoadAsync(string scopeId, CancellationToken ct = default)
    {
        // Read-only: a run that is still writing its journal keeps sole ownership of the WAL.
        await using var log = FileLedgerJournalLog.OpenReadOnly(DirectoryFor(scopeId), scopeId, _chart);
        return log is null ? null : await log.LoadLedgerAsync(ct).ConfigureAwait(false);
    }

    private string DirectoryFor(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        return Path.Combine(_root, Sha256Digest.ComputeUtf8(scopeId));
    }
}
