using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterService
{
    public Task<SecurityAliasDto> UpsertAliasAsync(UpsertSecurityAliasRequest request, CancellationToken ct = default)
    {
        var alias = new SecurityAliasDto(
            request.AliasId,
            request.SecurityId,
            request.AliasKind,
            request.AliasValue,
            request.Provider,
            request.Scope,
            request.Reason,
            request.CreatedBy,
            // Proposed creation stamp. It is applied only to a new alias. For an existing alias ID the
            // store retains the original creation facts; a material correction appends an alias revision.
            DateTimeOffset.UtcNow,
            request.ValidFrom,
            request.ValidTo,
            true);

        return UpsertAliasAsyncCore(alias, ct);
    }

    private async Task<SecurityAliasDto> UpsertAliasAsyncCore(SecurityAliasDto alias, CancellationToken ct)
    {
        // The store retains the original created_at/created_by for an existing alias ID, appends an
        // alias revision for a material correction, and rejects re-pointing an alias ID at another
        // security. A null result cannot be treated as success because that would tell the caller a
        // correction landed when no revision was recorded.
        var persisted = await _store.UpsertAliasAsync(alias, ct).ConfigureAwait(false)
            ?? throw new SecurityAliasHistoryConflictException(alias.AliasId);

        // Aliases are part of the cached projection and seed the canonical registry, so a new or
        // corrected alias refreshes both here; other nodes refresh from the store's notification.
        TryReseedRegistryInBackground();
        if (_projectionCache is not null
            && !(await TryRefreshProjectionCacheAsync(alias.SecurityId).ConfigureAwait(false)).Succeeded)
        {
            RetryProjectionCacheRefreshInBackground(alias.SecurityId);
        }

        return persisted;
    }
}
