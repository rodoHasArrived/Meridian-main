using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>
/// Maker-checker lifecycle of intercompany confirmation close policies (ADR-032). A book has at most one proposed or
/// active policy; book ownership is checked by the calling route before any command reaches this service.
/// </summary>
public sealed class IntercompanyConfirmationClosePolicyService(IIntercompanyConfirmationClosePolicyStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<IReadOnlyList<IntercompanyConfirmationClosePolicyDto>> ListAsync(string tenantId, Guid ledgerBookId, CancellationToken ct = default)
        => store.ListAsync(Tenant(tenantId), ledgerBookId, ct);

    public async Task<IntercompanyConfirmationClosePolicyDto> ProposeAsync(string tenantId, string? companyId, string actor,
        ProposeIntercompanyConfirmationClosePolicyRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = Tenant(tenantId);
        var existing = await store.ListAsync(tenant, request.LedgerBookId, ct).ConfigureAwait(false);
        // A retried proposal returns the policy it created; the identity reused for another step conflicts.
        if (existing.FirstOrDefault(item => item.Events.Any(step => step.RequestId == request.RequestId)) is { } replay)
        {
            IntercompanyConfirmationClosePolicyRules.IsReplay(replay, request.RequestId, IntercompanyConfirmationClosePolicyActionDto.Propose);
            return replay;
        }
        if (existing.Any(item => item.Status is IntercompanyConfirmationClosePolicyStatusDto.Proposed or IntercompanyConfirmationClosePolicyStatusDto.Active))
            throw new InvalidOperationException("This book already has a proposed or active confirmation close policy; disable or reject it first.");
        var next = IntercompanyConfirmationClosePolicyRules.Propose(tenant, companyId, Actor(actor), request, _clock.GetUtcNow());
        return await store.AppendAsync(next, 0, ct).ConfigureAwait(false);
    }

    public async Task<IntercompanyConfirmationClosePolicyDto> DecideAsync(string tenantId, string? companyId, string actor, Guid policyId,
        DecideIntercompanyConfirmationClosePolicyRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = Tenant(tenantId);
        var current = await store.GetAsync(tenant, policyId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The close policy was not found in this tenant.");
        if (current.CompanyId is { } owner && !string.Equals(owner, companyId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The close policy belongs to another company.");
        if (IntercompanyConfirmationClosePolicyRules.IsReplay(current, request.RequestId, request.Action))
            return current;
        var next = IntercompanyConfirmationClosePolicyRules.Decide(current, Actor(actor), request, _clock.GetUtcNow());
        return await store.AppendAsync(next, current.Version, ct).ConfigureAwait(false);
    }

    private static string Tenant(string tenantId) => string.IsNullOrWhiteSpace(tenantId)
        ? throw new UnauthorizedAccessException("A tenant scope is required.") : tenantId.Trim().ToLowerInvariant();
    private static string Actor(string actor) => string.IsNullOrWhiteSpace(actor)
        ? throw new UnauthorizedAccessException("An authenticated operator is required.") : actor.Trim();
}
