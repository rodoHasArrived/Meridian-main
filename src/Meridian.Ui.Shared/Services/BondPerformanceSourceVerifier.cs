using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Instruments.AssetOperations;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>Cached calculation receipts do not replace current source-byte and accepted-review authority.</summary>
public sealed class BondPerformanceSourceVerifier(IEvidenceArtifactStore vault, TimeProvider? clock = null)
    : IBondPerformanceSourceVerifier
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task VerifyMonitoringAsync(BondMonitoringCaptureRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AccountCashForecastEngine.IsCompleteScope(request.Scope) || request.Payments is null
            || request.Payments.Any(row => row is null || row.Status != AccountCashFlowStatus.Actual
                || row.Provenance?.Role != AccountCashEvidenceRole.Observation))
            throw new AccountCashForecastSourceAuthorityException("Performance cash requires observed source evidence in the exact account/book scope.");
        return new AccountCashForecastEvidenceAdapter(vault).VerifyMonitoringAsync(request, ct);
    }

    public Task VerifyFxAsync(AccountCashForecastScopeDto scope, IReadOnlyList<AccountCashFxConversionDto> conversions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(conversions);
        if (!AccountCashForecastEngine.IsCompleteScope(scope) || conversions.Any(row => row is null
            || row.Provenance?.Role != AccountCashEvidenceRole.DeclaredFx))
            throw new AccountCashForecastSourceAuthorityException("Performance FX requires declared directional FX source evidence in the exact account/book scope.");
        var now = _clock.GetUtcNow();
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var request = new AccountCashForecastRequestDto("performance-fx-source-verification", scope,
            date, date, null, null, [], conversions, now);
        return new AccountCashForecastEvidenceAdapter(vault).VerifyAsync(request, ct);
    }
}
