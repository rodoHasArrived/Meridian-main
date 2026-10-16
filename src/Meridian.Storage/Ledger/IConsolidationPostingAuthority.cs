using Meridian.Contracts.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Revalidates authoritative ownership and policy while retaining their write exclusion through commit.</summary>
public interface IConsolidationPostingAuthority
{
    Task<IAsyncDisposable> AcquireValidatedLeaseAsync(ConsolidationEvidenceDto evidence, CancellationToken ct = default);
}
