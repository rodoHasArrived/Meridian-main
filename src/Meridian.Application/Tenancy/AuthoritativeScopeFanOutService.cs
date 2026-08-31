using Meridian.Contracts.Tenancy;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Tenancy;

/// <summary>
/// Composes the registered <see cref="IScopeAssignmentProvider"/> authorities into one fan-out
/// answer.
///
/// <para>The composition rule is unanimity, not union: the result is authoritative only when every
/// registered provider reported that it saw the whole of its own slice. A union over "whichever
/// authorities happened to answer" is precisely the failure this service exists to prevent — it
/// yields a confident-looking affected set that silently omits the scopes the unavailable authority
/// owned, and a caller acting on it would apply a decision to some affected tenants and not
/// others.</para>
///
/// <para>Composing zero providers is likewise not an empty affected set. It is an unanswerable
/// question, and is reported as such.</para>
/// </summary>
public sealed class AuthoritativeScopeFanOutService : IAuthoritativeScopeFanOutService
{
    internal const string NoProvidersBlocker =
        "No scope-assignment authority is composed, so the set of affected tenant/company scopes cannot be enumerated.";

    internal const string MissingIdentifiersBlocker =
        "The security carries no resolvable identifiers, so holdings cannot be attributed to any scope.";

    private readonly IReadOnlyList<IScopeAssignmentProvider> _providers;
    private readonly ILogger<AuthoritativeScopeFanOutService> _logger;

    public AuthoritativeScopeFanOutService(
        IEnumerable<IScopeAssignmentProvider> providers,
        ILogger<AuthoritativeScopeFanOutService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ScopeFanOutResult> ResolveAffectedScopesAsync(
        ScopeFanOutRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (_providers.Count == 0)
        {
            return ScopeFanOutResult.NotAuthoritative(NoProvidersBlocker);
        }

        if (request.Identifiers.Count == 0)
        {
            return ScopeFanOutResult.NotAuthoritative(MissingIdentifiersBlocker);
        }

        var assignments = new List<AuthoritativeScopeAssignment>();
        var blockers = new List<string>();
        var authoritative = true;

        foreach (var provider in _providers)
        {
            ct.ThrowIfCancellationRequested();
            ScopeAssignmentProviderResult contribution;
            try
            {
                contribution = await provider.ResolveAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // An authority that faults has not proven its slice is empty. Degrade the whole
                // resolution rather than letting the remaining authorities imply completeness.
                _logger.LogError(
                    exception,
                    "Scope-assignment authority {AuthorityId} failed resolving security {SecurityId}",
                    provider.AuthorityId,
                    request.SecurityId);
                authoritative = false;
                blockers.Add(
                    $"Scope-assignment authority '{provider.AuthorityId}' failed and its affected scopes are unknown.");
                continue;
            }

            if (!contribution.IsAuthoritative)
            {
                authoritative = false;
            }

            blockers.AddRange(contribution.Blockers);
            assignments.AddRange(contribution.Scopes);
        }

        var distinct = DistinctOrdered(assignments);
        return authoritative
            ? ScopeFanOutResult.Authoritative(distinct)
            : new ScopeFanOutResult(false, distinct, DistinctBlockers(blockers));
    }

    /// <inheritdoc />
    public async Task<ScopeFanOutResult> ResolveOwnedScopesAsync(
        string tenantId,
        string companyId,
        ScopeFanOutRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
        {
            return ScopeFanOutResult.NotAuthoritative(
                "A resolved tenant and company are required before owned scopes can be enumerated.");
        }

        var full = await ResolveAffectedScopesAsync(request, ct).ConfigureAwait(false);
        if (!full.IsAuthoritative)
        {
            // Deliberately not narrowed to the caller's tenancy: a filtered view of an incomplete
            // resolution would read as a complete answer for this tenant, which it is not.
            return full;
        }

        return ScopeFanOutResult.Authoritative(
            full.Scopes.Where(scope => scope.IsOwnedBy(tenantId, companyId)).ToArray());
    }

    private static IReadOnlyList<AuthoritativeScopeAssignment> DistinctOrdered(
        IReadOnlyList<AuthoritativeScopeAssignment> assignments)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return assignments
            .Where(assignment => seen.Add(assignment.ScopeKey))
            .OrderBy(static assignment => assignment.ScopeKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> DistinctBlockers(IReadOnlyList<string> blockers)
        => blockers
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
