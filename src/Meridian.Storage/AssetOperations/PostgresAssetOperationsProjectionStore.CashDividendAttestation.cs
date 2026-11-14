using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.AssetOperations;

public sealed partial class PostgresAssetOperationsProjectionStore
{
    public Task<AssetAccountingEventProjectionRecord?> GetLatestAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid eventId, long eventVersion, CancellationToken ct = default)
    {
        EnsureCashDividendAssetBackend(connection, transaction);
        ValidateAssetAccountingStreamIdentity(eventId, eventVersion);
        return ReadLatestAssetAccountingEventAsync(connection, transaction, eventId, eventVersion, ct);
    }

    public async Task<AssetAccountingEventAppendResult> AppendAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, AssetAccountingEventSpineDto projection,
        long expectedSpineVersion, long expectedBookPositionVersion,
        CorporateActionCashDividendPostingLease lease, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(lease);
        EnsureCashDividendAssetBackend(connection, transaction);
        if (!lease.OwnsTransaction(connection, transaction)
            || projection.PreparerAttestation is not { } binding
            || projection.EventKind != AssetAccountingEventKindDto.CorporateAction
            || projection.EventId != lease.Projection.AccountingEventId
            || projection.EventVersion != lease.Projection.AccountingEventVersion
            || projection.Scope.LedgerBookId != lease.Projection.LedgerBookId
            || projection.Scope.PeriodId != lease.Projection.PeriodId
            || binding.EventId != lease.Projection.AccountingEventId
            || binding.EventVersion != lease.Projection.AccountingEventVersion
            || binding.LedgerBookId != lease.Projection.LedgerBookId || binding.PeriodId != lease.Projection.PeriodId
            || binding.TenantId != lease.Declaration.TenantId || binding.CompanyId != lease.Declaration.CompanyId
            || binding.DraftedCandidateFingerprint != lease.Projection.DraftedCandidateFingerprint
            || binding.PostingIntentHash != lease.Projection.PostingIntentHash
            || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(binding.Receipt), JsonSerializer.SerializeToElement(lease.Attestation)))
            throw new InvalidOperationException("The lifecycle append does not bind this active native dividend lease.");
        await lease.EnsureCurrentAsync(lease.Attestation.AttestedBy, projection.Scope.LedgerBookId,
            projection.Scope.PeriodId, projection.DraftedCandidateFingerprint ?? string.Empty,
            binding.PostingIntentHash, ct).ConfigureAwait(false);
        projection = AssetAccountingEventProjectionRules.Clone(projection);
        var nativeJournalStore = _assetAccountingJournalStore as PostgresLedgerJournalStore
            ?? throw new InvalidOperationException("Native dividend lifecycle authority requires the native PostgreSQL journal store.");
        var durable = await AssetAccountingEventProjectionRules.ValidateDurablePostedImpactAsync(projection,
            nativeJournalStore, ct, (query, queryCt) => nativeJournalStore.QueryWithinCashDividendTransactionAsync(
                connection, transaction, query, queryCt)).ConfigureAwait(false);
        return await AppendAssetAccountingWithinTransactionAsync(connection, transaction, projection,
            expectedSpineVersion, expectedBookPositionVersion, durable,
            AssetAccountingEventProjectionRules.Fingerprint(projection), ct, verifiedCashDividendPosting: true).ConfigureAwait(false);
    }

    private void EnsureCashDividendAssetBackend(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        if (!ReferenceEquals(transaction.Connection, connection))
            throw new InvalidOperationException("The native economic posting transaction has expired or changed connection.");
        var configured = new NpgsqlConnectionStringBuilder(_options.ConnectionString);
        var database = string.IsNullOrWhiteSpace(configured.Database) ? configured.Username : configured.Database;
        if (!string.Equals(configured.Host, connection.Host, StringComparison.OrdinalIgnoreCase)
            || configured.Port != connection.Port || !string.Equals(database, connection.Database, StringComparison.Ordinal))
            throw new InvalidOperationException("Economic attestation requires ledger, asset and case authority on the same PostgreSQL backend.");
    }
}
