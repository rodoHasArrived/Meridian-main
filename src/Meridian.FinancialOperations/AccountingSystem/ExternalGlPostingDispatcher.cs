using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Workstation;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.AccountingSystem;

/// <summary>Advances one durable outbox document and reconciles its exact Meridian line identities.</summary>
public sealed class ExternalGlPostingDispatcher(
    PostgresLedgerJournalStore store, IEnumerable<IExternalGlPostingProvider> providers, TimeProvider time, IBookOfRecordGuard? bookOfRecord = null)
{
    private readonly IReadOnlyDictionary<string, IExternalGlPostingProvider> _providers =
        providers.ToDictionary(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase);

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var claim = await store.ClaimExternalGlDocumentAsync(ct).ConfigureAwait(false);
        if (claim is null)
            return false;
        var scope = claim.Scope;
        try
        {
            await store.RequireExternalGlBookAsync(scope.LedgerBookId, scope.TenantId, scope.CompanyId, scope.FundProfileId, ct)
                .ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            await RecordAsync(claim, Failure(ExternalGlErrorCode.CredentialsUnavailable), isLookup: false, ct).ConfigureAwait(false);
            return true;
        }

        if (!_providers.TryGetValue(claim.Destination.ProviderId, out var provider))
        {
            await RecordAsync(claim, Failure(ExternalGlErrorCode.UnsupportedConfiguration), isLookup: false, ct).ConfigureAwait(false);
            return true;
        }

        var recovery = claim.PostAttempts > 0;
        if (recovery)
        {
            var lookup = await ObserveAsync(() => provider.FindAsync(scope.TenantId, claim.Destination, claim.Document, ct), ct).ConfigureAwait(false);
            // A Pending receipt or provider document ID establishes provider knowledge. A later
            // empty lookup cannot authorize a create. Every replay uses the same retained key.
            var mayReplay = lookup.Kind == ExternalGlReceiptKind.NotFound && lookup.SafeToReplay && !claim.ProviderAcknowledged;
            if (!await RecordAsync(claim, lookup, isLookup: true, ct, keepLease: mayReplay).ConfigureAwait(false) || !mayReplay)
                return true;
        }

        try
        {
            await ExternalGlPostingService.RequireBookOfRecordAsync(bookOfRecord, scope, ct).ConfigureAwait(false);
        }
        catch (ShadowBookNotBookOfRecordException)
        {
            await RecordAsync(claim, Failure(ExternalGlErrorCode.UnsupportedConfiguration), isLookup: false, ct).ConfigureAwait(false);
            return true;
        }

        if (!await store.MarkExternalGlSubmittingAsync(claim, ct).ConfigureAwait(false))
            return true;
        var receipt = await ObserveAsync(() => provider.PostAsync(scope.TenantId, claim.Destination, claim.Document, ct), ct).ConfigureAwait(false);
        // A validation refusal on replay does not establish that the earlier request failed.
        await RecordAsync(claim, receipt, isLookup: false, ct, uncertainRejection: recovery).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> RecordAsync(ExternalGlOutboxClaim claim, ExternalGlProviderReceipt receipt, bool isLookup, CancellationToken ct,
        bool keepLease = false, bool uncertainRejection = false)
    {
        var status = Classify(claim.Document, receipt, uncertainRejection);
        if (status == ExternalGlPostingStatus.OutcomeUnknown && receipt.Kind is ExternalGlReceiptKind.Completed or ExternalGlReceiptKind.Rejected)
        {
            receipt = receipt with
            {
                ErrorCode = receipt.ErrorCode ?? ExternalGlErrorCode.ReceiptMismatch,
                Kind = ExternalGlReceiptKind.OutcomeUnknown
            };
        }
        return await store.RecordExternalGlReceiptAsync(claim, new ExternalGlReceiptObservation(time.GetUtcNow(), isLookup, receipt),
            status, keepLease, ct).ConfigureAwait(false);
    }

    private static ExternalGlPostingStatus Classify(ExternalGlPostingDocument document, ExternalGlProviderReceipt receipt, bool uncertainRejection)
    {
        if (receipt.Kind == ExternalGlReceiptKind.Pending)
            return ExternalGlPostingStatus.Pending;
        if (receipt.Kind is not (ExternalGlReceiptKind.Completed or ExternalGlReceiptKind.Rejected) ||
            receipt.Lines.Count != document.Lines.Count)
            return ExternalGlPostingStatus.OutcomeUnknown;

        var expected = document.Lines.ToDictionary(line => line.MeridianLineId);
        var accepted = 0;
        var rejected = 0;
        foreach (var result in receipt.Lines)
        {
            if (!expected.Remove(result.MeridianLineId, out var line))
                return ExternalGlPostingStatus.OutcomeUnknown;
            if (result.Status == ExternalGlLineStatus.Rejected && result.ErrorCode is not null)
                rejected++;
            if (result.Status == ExternalGlLineStatus.Accepted && result.ErrorCode is null &&
                result.ExternalAccountId == line.ExternalAccountId && result.Debit == line.Debit && result.Credit == line.Credit &&
                EqualDimensions(line.ExternalDimensions, result.ExternalDimensions))
                accepted++;
        }

        if (accepted + rejected != document.Lines.Count ||
            accepted > 0 && (receipt.Kind != ExternalGlReceiptKind.Completed || string.IsNullOrWhiteSpace(receipt.ProviderDocumentId)))
            return ExternalGlPostingStatus.OutcomeUnknown;
        if (accepted == document.Lines.Count && receipt.ErrorCode is null)
            return ExternalGlPostingStatus.FullyAccepted;
        if (accepted > 0 && rejected > 0)
            return ExternalGlPostingStatus.PartiallyAccepted;
        if (rejected == document.Lines.Count && receipt.Kind == ExternalGlReceiptKind.Rejected &&
            !uncertainRejection && receipt.ProviderDocumentId is null)
            return ExternalGlPostingStatus.Rejected;
        return ExternalGlPostingStatus.OutcomeUnknown;
    }

    private static bool EqualDimensions(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string>? returned) =>
        returned is null ? expected.Count == 0 : expected.Count == returned.Count &&
            expected.All(pair => returned.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static async Task<ExternalGlProviderReceipt> ObserveAsync(Func<Task<ExternalGlProviderReceipt>> send, CancellationToken ct)
    {
        try
        {
            return await send().ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Failure(ExternalGlErrorCode.UnrecognizedResponse);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure(ExternalGlErrorCode.ProviderUnavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return Failure(ExternalGlErrorCode.ProviderUnavailable);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(ExternalGlErrorCode.CredentialsUnavailable);
        }
    }

    private static ExternalGlProviderReceipt Failure(ExternalGlErrorCode error) =>
        new(ExternalGlReceiptKind.OutcomeUnknown, [], ErrorCode: error);
}
