using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>Outcome of recording a factor observation.</summary>
public sealed record SecurityFactorObservationResult(
    SecurityFactorObservationRecord Observation,
    bool IsRestatement,
    decimal? PriorFactor);

/// <summary>
/// Review C3 (owner decision 2026-11-25: observation stream). Records pool-factor observations without a
/// terms revision. A restatement (a different factor for an already-observed effective date) opens a
/// FactorRestatement case in the global exception queue, proposing a review of the paydown it changes.
/// </summary>
public sealed class SecurityFactorObservationService
{
    public const string FactorRestatementCategory = "FactorRestatement";
    private const int RestatementSlaDays = 2;

    private readonly ISecurityFactorObservationStore _observations;
    private readonly ISecurityMasterExceptionCaseStore? _cases;

    public SecurityFactorObservationService(ISecurityFactorObservationStore observations, ISecurityMasterExceptionCaseStore? cases = null)
    {
        _observations = observations ?? throw new ArgumentNullException(nameof(observations));
        _cases = cases;
    }

    public async Task<SecurityFactorObservationResult> RecordAsync(
        Guid securityId, DateOnly effectiveDate, decimal factor, string source, string? evidenceRef, string actor, CancellationToken ct = default)
    {
        var prior = (await _observations.ListEffectiveAsync(securityId, ct).ConfigureAwait(false))
            .FirstOrDefault(observation => observation.EffectiveDate == effectiveDate);
        var recorded = await _observations.RecordAsync(securityId, effectiveDate, factor, source, evidenceRef, actor, ct).ConfigureAwait(false);
        var restated = prior is not null && prior.Factor != factor;
        if (restated && _cases is not null)
        {
            var now = DateTimeOffset.UtcNow;
            await _cases.SyncAsync(
                categories: [],
                observations:
                [
                    new SecurityMasterExceptionObservation(
                        CaseKey: $"factor-restatement:{securityId:D}:{effectiveDate:yyyy-MM-dd}:{recorded.ObservationId:D}",
                        Category: FactorRestatementCategory,
                        RuleCode: "SM_FACTOR_RESTATED",
                        SecurityId: securityId,
                        Severity: "Error",
                        Title: $"Pool factor restated for {effectiveDate:yyyy-MM-dd}",
                        Detail: $"{source} restated the factor from {prior!.Factor} to {factor}; review the paydown recognised for that date and post a correcting paydown if it differs.",
                        SlaDueAt: now.AddDays(RestatementSlaDays))
                ],
                now,
                ct).ConfigureAwait(false);
        }

        return new SecurityFactorObservationResult(recorded, restated, prior?.Factor);
    }
}
