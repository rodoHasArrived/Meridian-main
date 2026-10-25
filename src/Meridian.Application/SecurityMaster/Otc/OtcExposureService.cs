using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.Otc;

/// <summary>
/// Computes and records a netting set's exposure: reads the netting set with its agreements and
/// contracts, the collateral balances under its collateral agreement, values each contract that
/// enters the exposure through the registered <see cref="IOtcContractValuationSource"/>, applies
/// <see cref="OtcExposureRules.Compute"/> and appends the result as the next snapshot version for
/// the date.
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
        var computedBy = OtcCollateralRules.RequireActor(actor);

        var view = await _contracts.GetNettingSetAsync(nettingSetId, ct).ConfigureAwait(false);
        if (view is null)
            return null;

        var currency = OtcExposureRules.ResolveCurrency(view);
        var balances = view.CollateralAgreement is { } agreement
            ? await _collateral.ListCollateralBalancesAsync(agreement.CollateralAgreementId, ct).ConfigureAwait(false)
            : [];

        var selected = OtcExposureRules.SelectContracts(view.Contracts, asOfDate);
        var valuations = new List<OtcContractValuationDto>(selected.Count);
        foreach (var contract in selected)
        {
            ct.ThrowIfCancellationRequested();
            valuations.Add(await _valuation.ValueAsync(new OtcContractValuationRequest(contract, asOfDate, currency, knowledgeAsOf), ct).ConfigureAwait(false));
        }

        var computed = OtcExposureRules.Compute(view, valuations, balances, asOfDate, computedBy, DateTimeOffset.UtcNow);
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
