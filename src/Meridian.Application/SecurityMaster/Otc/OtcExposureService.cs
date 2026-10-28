using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.Otc;

/// <summary>
/// Computes and records a netting set's exposure: reads the netting set with its agreements and
/// contracts, the collateral balances under its collateral agreement and the netting sets that
/// reference that agreement, values each contract that enters the exposure through the registered
/// <see cref="IOtcContractValuationSource"/> at one knowledge time (the request's, else the
/// computation instant the snapshot is stamped with), applies <see cref="OtcExposureRules.Compute"/>
/// and appends the result as the next snapshot version for the date. The knowledge time is recorded
/// on the snapshot's inputs, so a call is refused once market data for a valued security is
/// recorded after it, or when it is earlier than the computation (a replay). An as-of date after
/// the current UTC date is refused before anything is read or valued.
/// </summary>
public sealed class OtcExposureService : IOtcExposureService
{
    private readonly IOtcContractStore _contracts;
    private readonly IOtcCollateralStore _collateral;
    private readonly IOtcContractValuationSource _valuation;
    private readonly ILogger<OtcExposureService> _logger;

    public OtcExposureService(
        IOtcContractStore contracts,
        IOtcCollateralStore collateral,
        IOtcContractValuationSource valuation,
        ILogger<OtcExposureService> logger)
    {
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        _collateral = collateral ?? throw new ArgumentNullException(nameof(collateral));
        _valuation = valuation ?? throw new ArgumentNullException(nameof(valuation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OtcExposureSnapshotDto?> ComputeAsync(Guid nettingSetId, DateOnly asOfDate, string actor, DateTimeOffset? knowledgeAsOf = null, CancellationToken ct = default)
    {
        if (nettingSetId == Guid.Empty)
            throw new ArgumentException("An exposure computation names the netting set it values.", nameof(nettingSetId));
        if (asOfDate == default)
            throw new ArgumentException("An exposure computation states the date it values.", nameof(asOfDate));
        OtcExposureRules.RequireNotFutureDated(asOfDate, DateTimeOffset.UtcNow);
        var computedBy = OtcCollateralRules.RequireActor(actor);

        var view = await _contracts.GetNettingSetAsync(nettingSetId, ct).ConfigureAwait(false);
        if (view is null)
            return null;

        var currency = OtcExposureRules.ResolveCurrency(view, asOfDate);
        IReadOnlyList<OtcCollateralBalanceDto> balances = [];
        IReadOnlyList<Guid> nettingSetsUnderAgreement = [];
        if (view.CollateralAgreement is { } agreement)
        {
            balances = await _collateral.ListCollateralBalancesAsync(agreement.CollateralAgreementId, ct).ConfigureAwait(false);
            nettingSetsUnderAgreement = await _collateral.ListNettingSetsUnderCollateralAgreementAsync(agreement.CollateralAgreementId, ct).ConfigureAwait(false);
        }

        // One knowledge time for every contract, so the snapshot's marks are mutually consistent:
        // the caller's cutoff, else the instant the snapshot is stamped as computed.
        var computedAt = DateTimeOffset.UtcNow;
        var knowledgeTime = knowledgeAsOf ?? computedAt;
        var selected = OtcExposureRules.SelectContracts(view.Contracts, asOfDate);
        var valuations = new List<OtcContractValuationDto>(selected.Count);
        foreach (var contract in selected)
        {
            ct.ThrowIfCancellationRequested();
            valuations.Add(await _valuation.ValueAsync(new OtcContractValuationRequest(contract, asOfDate, currency, knowledgeTime), ct).ConfigureAwait(false));
        }

        var computed = OtcExposureRules.Compute(view, valuations, balances, nettingSetsUnderAgreement, asOfDate, computedBy, computedAt, knowledgeTime);
        var stored = await _collateral.AppendExposureSnapshotAsync(computed, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Recorded OTC exposure snapshot {SnapshotId} version {Version} for netting set {NettingSetId} as of {AsOfDate}: {Status} on a {NettingBasis} basis, net exposure {NetExposure} {Currency}, {MovementCount} movement(s), {BlockerCount} blocker(s).",
            stored.SnapshotId,
            stored.Version,
            stored.NettingSetId,
            stored.AsOfDate,
            stored.Status,
            stored.NettingBasis,
            stored.NetExposure,
            stored.Currency,
            stored.Movements.Count,
            stored.Blockers.Count);
        return stored;
    }
}
