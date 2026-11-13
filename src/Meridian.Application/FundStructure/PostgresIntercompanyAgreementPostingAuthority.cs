using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;

namespace Meridian.Application.FundStructure;

/// <summary>Validates exact approved provenance while retaining structure locks through ledger commit.</summary>
public sealed class PostgresIntercompanyAgreementPostingAuthority(IIntercompanyAgreementStore store)
    : IIntercompanyAgreementBatchPostingAuthority
{
    public Task<IAsyncDisposable> AcquireValidatedLeaseAsync(IntercompanyAgreementPostingRequest request,
        CancellationToken ct = default)
        => AcquireValidatedBatchLeaseAsync([request], ct);

    public async Task<IAsyncDisposable> AcquireValidatedBatchLeaseAsync(IReadOnlyList<IntercompanyAgreementPostingRequest> requests,
        CancellationToken ct = default)
    {
        if (requests.Count == 0 || requests.Any(request => string.IsNullOrWhiteSpace(request.TenantId)
            || request.BookEntityId == Guid.Empty || request.Lines.Count == 0))
            throw new LedgerValidationException("Authoritative agreement posting scope is required.");
        var tenant = requests[0].TenantId;
        if (requests.Any(request => !string.Equals(request.TenantId.Trim(), tenant.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new LedgerValidationException("One agreement posting transaction cannot span multiple authoritative tenants.");
        var ids = requests.SelectMany(request => request.Lines).Select(line => line.AgreementId).Distinct().Order().ToArray();
        var proof = requests.Select(request => request.StructureAuthorityProof).FirstOrDefault(item => item is not null);
        var lease = proof is not null
            ? await store.AcquirePostingLeaseAsync(tenant, ids, proof, ct).ConfigureAwait(false)
            : await store.AcquirePostingLeaseAsync(tenant, ids, ct).ConfigureAwait(false);
        try
        {
            foreach (var request in requests)
            foreach (var line in request.Lines)
            {
                var state = lease.Agreements.SingleOrDefault(agreement => agreement.AgreementId == line.AgreementId);
                var revision = state?.Revisions.SingleOrDefault(item => item.AgreementVersion == line.AgreementVersion);
                if (revision is null || revision.Action == IntercompanyAgreementActionDto.Terminate)
                    throw new LedgerValidationException("The referenced approved agreement revision is unavailable.");
                var terms = revision.Terms;
                if (line.PostingEntityId != request.BookEntityId || line.PostingEntityId == line.CounterpartyId
                    || !((terms.PartyOneId == line.PostingEntityId && terms.PartyTwoId == line.CounterpartyId)
                         || (terms.PartyTwoId == line.PostingEntityId && terms.PartyOneId == line.CounterpartyId)))
                    throw new LedgerValidationException("Agreement parties do not match the posting entity and counterparty.");
                if (!string.Equals(terms.Currency, line.TransactionCurrency, StringComparison.OrdinalIgnoreCase))
                    throw new LedgerValidationException("Agreement currency does not match the transaction currency.");
                var first = await lease.GetLegalEntityAsync(terms.PartyOneId, ct).ConfigureAwait(false);
                var second = await lease.GetLegalEntityAsync(terms.PartyTwoId, ct).ConfigureAwait(false);
                if (first is null || second is null)
                    throw new LedgerValidationException("Agreement legal-entity authority is unavailable.");
                if (!request.IsVerifiedSourceReversal)
                {
                    var selection = IntercompanyAgreementTimeline.Resolve(state!.Revisions, request.AccountingDate);
                    if (selection.Status != IntercompanyAgreementStatusDto.Active
                        || selection.Revision?.AgreementVersion != line.AgreementVersion)
                        throw new LedgerValidationException("Agreement authority changed or is not effective on the accounting date; renew journal review.");
                    var date = new DateTimeOffset(request.AccountingDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                    if (date < first.EffectiveFrom || first.EffectiveTo is { } firstEnd && date >= firstEnd
                        || date < second.EffectiveFrom || second.EffectiveTo is { } secondEnd && date >= secondEnd)
                        throw new LedgerValidationException("Agreement parties do not exist on the accounting date.");
                }
            }
            return lease;
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
