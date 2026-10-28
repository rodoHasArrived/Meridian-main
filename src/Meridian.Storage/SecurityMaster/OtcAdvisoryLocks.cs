using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// The transaction-scoped advisory locks the OTC stores share, and the single order they are taken
/// in. A transaction takes only the scopes it needs, always in this order and always before any row
/// lock, so two transactions cannot wait on each other's advisory locks:
/// <list type="number">
/// <item><see cref="ExposureSnapshot"/> of one netting set: an append, an issue.</item>
/// <item><see cref="MarginCall"/> of one netting set: an issue, a transition.</item>
/// <item><see cref="NettingSetContracts"/> of each netting set a write touches, in id order: a
/// contract insert, amendment or move (a move touches two); an issue, for its own netting set.</item>
/// <item><see cref="CollateralBalances"/> of one collateral agreement: a balance write; an issue,
/// for its snapshot's agreement.</item>
/// </list>
/// Every writer of a contract or balance row holds that row's scope until it commits, so an issue
/// holding the scope never waits on a contract or balance row and no new one can appear under it.
/// Price and pricing hierarchy writers take none of these scopes: an issue checks the market data
/// recorded since its snapshot's knowledge time as committed when it reads it, and does not hold
/// it until the call commits.
/// </summary>
internal static class OtcAdvisoryLocks
{
    internal static string ExposureSnapshot(Guid nettingSetId) => "otc-exposure-snapshot:" + nettingSetId.ToString("D");

    internal static string MarginCall(Guid nettingSetId) => "otc-margin-call:" + nettingSetId.ToString("D");

    internal static string NettingSetContracts(Guid nettingSetId) => "otc-netting-set-contracts:" + nettingSetId.ToString("D");

    internal static string CollateralBalances(Guid collateralAgreementId) => "otc-collateral-balances:" + collateralAgreementId.ToString("D");

    /// <summary>Takes one scope until the transaction ends.</summary>
    internal static async Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string scope, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(hashtextextended(@lock_scope, 0));";
        command.Parameters.AddWithValue("lock_scope", scope);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Takes the contracts scope of each distinct netting set in id order, the one order every caller uses.</summary>
    internal static async Task LockNettingSetContractsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<Guid> nettingSetIds, CancellationToken ct)
    {
        foreach (var nettingSetId in nettingSetIds.Distinct().Order())
            await LockAsync(connection, transaction, NettingSetContracts(nettingSetId), ct).ConfigureAwait(false);
    }
}
