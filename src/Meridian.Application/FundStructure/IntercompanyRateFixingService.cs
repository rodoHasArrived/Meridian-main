using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Tenant-scoped authority for reviewed index fixings. It records what an operator observed and a second
/// person approved; it never fetches, interpolates or carries a rate forward.
/// </summary>
public sealed class IntercompanyRateFixingService(IIntercompanyRateFixingStore store, TimeProvider? clock = null)
{
    /// <summary>A fixing range read never spans more than this many days.</summary>
    public const int MaximumRangeDays = 3_700;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<IntercompanyRateFixingDto?> GetAsync(string tenantId, Guid fixingId, CancellationToken ct = default)
        => store.GetAsync(Tenant(tenantId), fixingId, ct);

    public Task<IReadOnlyList<IntercompanyRateFixingDto>> ListAsync(string tenantId, string indexReference, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > MaximumRangeDays)
            throw new ArgumentException($"Select an inclusive fixing-date range of at most {MaximumRangeDays} days.");
        return store.ListAsync(Tenant(tenantId), IntercompanyRateFixingRules.IndexKey(indexReference), from, to, ct);
    }

    /// <summary>Approved values only, as the accrual calculator receives them.</summary>
    public async Task<IReadOnlyList<IntercompanyApprovedRateFixingDto>> ApprovedAsync(string tenantId, string indexReference,
        DateOnly from, DateOnly to, CancellationToken ct = default)
        => (await ListAsync(tenantId, indexReference, from, to, ct).ConfigureAwait(false))
            .Select(IntercompanyRateFixingRules.Approved).OfType<IntercompanyApprovedRateFixingDto>()
            .OrderBy(item => item.FixingDate).ToArray();

    public async Task<IntercompanyRateFixingDto> ProposeAsync(string tenantId, string actor, ProposeIntercompanyRateFixingRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = Tenant(tenantId);
        var id = IntercompanyRateFixingRules.FixingId(tenant, request.IndexReference, request.FixingDate);
        var current = await store.GetAsync(tenant, id, ct).ConfigureAwait(false);
        if (IntercompanyRateFixingRules.IsReplay(current, request.RequestId, IntercompanyRateFixingActionDto.Propose))
            return current!;
        var next = IntercompanyRateFixingRules.Propose(current, tenant, Actor(actor), request, _clock.GetUtcNow());
        return await store.AppendAsync(next, current?.Version ?? 0, ct).ConfigureAwait(false);
    }

    public async Task<IntercompanyRateFixingDto> DecideAsync(string tenantId, string actor, Guid fixingId,
        DecideIntercompanyRateFixingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = Tenant(tenantId);
        var current = await store.GetAsync(tenant, fixingId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The fixing was not found in this tenant.");
        if (IntercompanyRateFixingRules.IsReplay(current, request.RequestId, request.Action))
            return current;
        var next = IntercompanyRateFixingRules.Decide(current, Actor(actor), request, _clock.GetUtcNow());
        return await store.AppendAsync(next, current.Version, ct).ConfigureAwait(false);
    }

    private static string Tenant(string tenantId) => string.IsNullOrWhiteSpace(tenantId)
        ? throw new UnauthorizedAccessException("A tenant scope is required.")
        : tenantId.Trim().ToLowerInvariant();

    private static string Actor(string actor) => string.IsNullOrWhiteSpace(actor)
        ? throw new UnauthorizedAccessException("An authenticated operator is required.")
        : actor.Trim();
}
