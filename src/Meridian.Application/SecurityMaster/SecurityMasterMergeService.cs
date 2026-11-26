using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>Request to merge a duplicate security into a survivor (review C1).</summary>
public sealed record MergeSecuritiesRequest(
    Guid RetiredSecurityId,
    Guid SurvivorSecurityId,
    long ExpectedRetiredVersion,
    string Actor,
    string Reason);

/// <summary>Outcome of a merge.</summary>
public sealed record MergeSecuritiesResult(
    Guid RetiredSecurityId,
    Guid SurvivorSecurityId,
    bool RetiredWasDeactivated,
    DateTimeOffset MergedAt);

/// <summary>
/// Review C1 survivorship (owner decision 2026-11-25: redirect). Merging retires the duplicate's stream
/// through the governed deactivation path with a <c>MergedInto</c> reason and records a redirect that
/// identifier resolution follows. Lots, positions and journals keep the retired SecurityId and their
/// history; nothing is re-keyed. The operation is idempotent: retrying after a partial failure skips the
/// deactivation and records the redirect.
/// </summary>
public sealed class SecurityMasterMergeService
{
    /// <summary>Source system stamped on the merge's deactivation event.</summary>
    public const string MergeSourceSystem = "SecurityMasterMerge";

    private readonly ISecurityMasterService _service;
    private readonly ISecurityMasterStore _store;
    private readonly ISecurityMasterRedirectStore _redirects;
    private readonly ILogger<SecurityMasterMergeService> _logger;

    public SecurityMasterMergeService(
        ISecurityMasterService service,
        ISecurityMasterStore store,
        ISecurityMasterRedirectStore redirects,
        ILogger<SecurityMasterMergeService> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _redirects = redirects ?? throw new ArgumentNullException(nameof(redirects));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MergeSecuritiesResult> MergeAsync(MergeSecuritiesRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Actor);
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("A merge requires a reason.", nameof(request));
        if (request.RetiredSecurityId == request.SurvivorSecurityId)
            throw new ArgumentException("A security cannot be merged into itself.", nameof(request));

        var retired = await _store.GetProjectionAsync(request.RetiredSecurityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Security '{request.RetiredSecurityId:D}' was not found.");
        var survivor = await _store.GetProjectionAsync(request.SurvivorSecurityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Survivor security '{request.SurvivorSecurityId:D}' was not found.");
        if (survivor.Status != SecurityStatusDto.Active)
            throw new SecurityMasterWriteRefusedException($"Survivor security '{survivor.SecurityId:D}' is not active; merge into an active security.");
        if (await _redirects.ResolveSurvivorAsync(survivor.SecurityId, ct).ConfigureAwait(false) != survivor.SecurityId)
            throw new SecurityMasterWriteRefusedException($"Survivor security '{survivor.SecurityId:D}' has itself been merged; merge into its survivor instead.");
        if (await _redirects.ResolveSurvivorAsync(survivor.SecurityId, ct).ConfigureAwait(false) == retired.SecurityId)
            throw new SecurityMasterWriteRefusedException("The merge would create a redirect cycle.");

        var mergedAt = DateTimeOffset.UtcNow;
        var deactivated = false;
        if (retired.Status == SecurityStatusDto.Active)
        {
            await _service.DeactivateAsync(
                new DeactivateSecurityRequest(
                    retired.SecurityId,
                    request.ExpectedRetiredVersion,
                    mergedAt,
                    MergeSourceSystem,
                    request.Actor,
                    $"merge:{survivor.SecurityId:D}",
                    $"MergedInto {survivor.SecurityId:D}: {request.Reason.Trim()}"),
                ct).ConfigureAwait(false);
            deactivated = true;
        }

        await _redirects.AddAsync(
            new SecurityMasterRedirect(retired.SecurityId, survivor.SecurityId, mergedAt, request.Actor, request.Reason.Trim()),
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Merged security {RetiredSecurityId} into {SurvivorSecurityId} by {Actor}",
            retired.SecurityId, survivor.SecurityId, request.Actor);
        return new MergeSecuritiesResult(retired.SecurityId, survivor.SecurityId, deactivated, mergedAt);
    }
}
