using System.Collections.Generic;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;



namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Manages structured cash flow source assignments and delegates projections to
/// <see cref="IStructuredCashFlowProvider"/> implementations.
/// Client-provided sources take precedence and remain until explicitly changed,
/// consistent with the Clearwater cash flow governance model.
/// </summary>
public sealed class SecurityMasterCashFlowService : ISecurityMasterCashFlowService
{
    private readonly ISecurityMasterCashFlowStore _store;
    private readonly IReadOnlyList<IStructuredCashFlowProvider> _providers;
    private readonly ISecurityMasterQueryService _queryService;
    private readonly ILogger<SecurityMasterCashFlowService> _logger;

    private static readonly TimeSpan StalenessThreshold = TimeSpan.FromDays(7);

    public SecurityMasterCashFlowService(
        ISecurityMasterCashFlowStore store,
        IEnumerable<IStructuredCashFlowProvider> providers,
        ISecurityMasterQueryService queryService,
        ILogger<SecurityMasterCashFlowService> logger)
    {
        _store = store;
        _providers = providers?.ToList() ?? [];
        _queryService = queryService;
        _logger = logger;
    }

    public Task<SecurityCashFlowSourceDto?> GetCashFlowSourceAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetSourceAsync(securityId, ct);

    public async Task UpsertCashFlowSourceAsync(UpsertCashFlowSourceRequest request, CancellationToken ct = default)
    {
        var existing = await _store.GetSourceAsync(request.SecurityId, ct).ConfigureAwait(false);

        // Client-provided sources remain authoritative against automated provider refreshes; an
        // operator can still clear them with an explicit Force update (e.g. when the client source
        // is stale or expired).
        if (existing is { IsClientOverride: true } && !request.IsClientOverride && !request.Force)
        {
            _logger.LogWarning(
                "Skipped cash flow source update for {SecurityId}: existing client-provided source is authoritative (use Force to override).",
                request.SecurityId);
            return;
        }

        var record = new SecurityCashFlowSourceDto(
            request.SecurityId,
            request.SourceKind,
            DateTimeOffset.UtcNow,
            request.IsClientOverride,
            request.ClientConfirmedBy,
            request.IsClientOverride ? DateTimeOffset.UtcNow : null);

        await _store.UpsertSourceAsync(record, ct).ConfigureAwait(false);
    }

    public async Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(
        Guid securityId, StructuredCashFlowScenario scenario, CancellationToken ct = default)
    {
        var assignment = await _store.GetSourceAsync(securityId, ct).ConfigureAwait(false);
        if (assignment is null)
        {
            _logger.LogDebug("No cash flow source assigned for security {SecurityId}.", securityId);
            return null;
        }

        if (assignment.LastUpdatedUtc.HasValue
            && DateTimeOffset.UtcNow - assignment.LastUpdatedUtc.Value > StalenessThreshold)
        {
            _logger.LogWarning(
                "Cash flow source for security {SecurityId} was last updated {DaysAgo} days ago and may be stale.",
                securityId,
                (int)(DateTimeOffset.UtcNow - assignment.LastUpdatedUtc.Value).TotalDays);
        }

        // Client-provided source: no external provider delegation; caller must supply projections directly.
        if (assignment.SourceKind == StructuredCashFlowSourceKind.ClientProvided)
        {
            _logger.LogDebug(
                "Security {SecurityId} uses client-provided cash flows; no provider projection available.",
                securityId);
            return null;
        }

        // Calculated types: return empty schedule — full math is handled by the ledger projector.
        if (assignment.SourceKind is StructuredCashFlowSourceKind.CalculatedBullet
            or StructuredCashFlowSourceKind.CalculatedSinker)
        {
            return new StructuredCashFlowProjectionDto(
                securityId, assignment.SourceKind, scenario, DateTimeOffset.UtcNow, []);
        }

        var security = await _queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (security is null)
        {
            _logger.LogWarning(
                "Security {SecurityId} not found when retrieving cash flow projections.", securityId);
            return null;
        }

        var isin = security.Identifiers
            .FirstOrDefault(i => i.Kind == SecurityIdentifierKind.Isin)?.Value;

        var provider = _providers.FirstOrDefault(
            p => MapSourceKindToProviderId(assignment.SourceKind) == p.ProviderId);

        if (provider is null)
        {
            _logger.LogDebug(
                "No registered provider for source kind {SourceKind} for security {SecurityId}.",
                assignment.SourceKind, securityId);
            return null;
        }

        return await provider.GetProjectedCashFlowsAsync(
            securityId, isin, DateTimeOffset.UtcNow, scenario, ct).ConfigureAwait(false);
    }

    private static string MapSourceKindToProviderId(StructuredCashFlowSourceKind kind) => kind switch
    {
        StructuredCashFlowSourceKind.MIAC => "miac",
        StructuredCashFlowSourceKind.MoodysAnalytics => "moodys-analytics",
        _ => string.Empty
    };
}
