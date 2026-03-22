using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityResolver : ISecurityResolver
{
    private readonly ISecurityMasterStore _store;

    public SecurityResolver(ISecurityMasterStore store)
    {
        _store = store;
    }

    public async Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default)
    {
        var record = await _store.GetByIdentifierAsync(
            request.IdentifierKind,
            request.IdentifierValue,
            request.Provider,
            request.AsOfUtc ?? DateTimeOffset.UtcNow,
            includeInactive: !request.ActiveOnly,
            ct).ConfigureAwait(false);

        return record?.SecurityId;
    }
}
