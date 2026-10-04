using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.CorporateActions;

/// <summary>
/// Maps an accepted superseding corporate action (amendment or cancellation) to the
/// period-aware restatement decision. Pure routing — it never posts; it only proposes,
/// following the same contract as the revision-publish path. Returns null when no
/// restatement machinery is available or no ledger book is exposed. Implementations used by a
/// durable acceptance boundary should return an explicit negative decision when the lookup is
/// authoritative and no book is exposed; <c>null</c> means no authoritative decision was possible.
/// </summary>
public interface ICorporateActionRestatementTrigger
{
    Task<SecurityMasterRestatementDecision?> OnSupersededAsync(
        CorporateActionDto superseding,
        CorporateActionDto superseded,
        string? fundProfileId,
        string actor,
        string? correlationId,
        CancellationToken ct = default);
}

/// <summary>
/// Default trigger for hosts without the workstation restatement stack; a superseding
/// append succeeds with no restatement proposal.
/// </summary>
public sealed class NullCorporateActionRestatementTrigger : ICorporateActionRestatementTrigger
{
    public Task<SecurityMasterRestatementDecision?> OnSupersededAsync(
        CorporateActionDto superseding,
        CorporateActionDto superseded,
        string? fundProfileId,
        string actor,
        string? correlationId,
        CancellationToken ct = default)
        => Task.FromResult<SecurityMasterRestatementDecision?>(null);
}

/// <summary>
/// Resolves the superseded action's downstream impact (via the trust snapshot), maps the
/// impacted fund to its durable ledger books, and delegates to
/// <see cref="IPeriodAwareRestatementResolver"/> with a
/// <see cref="SecurityMasterRevisionPublishedEvent"/> shaped as: RevisionId = superseding
/// CorpActId, EffectiveFrom = superseded ExDate, ChangedFields =
/// ["corporateActions.&lt;CanonicalName&gt;"].
///
/// <para>Registered as a singleton beside the command services; the workstation
/// restatement stack is scoped, so dependencies are resolved per invocation through
/// <see cref="IServiceScopeFactory"/>. When any piece of the stack is absent (a host
/// without the workstation layer), the result is non-authoritative (<c>null</c>) so a durable
/// acceptance caller can fail closed instead of silently losing closed-period handling.</para>
/// </summary>
public sealed class CorporateActionSupersedeRestatementTrigger : ICorporateActionRestatementTrigger
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CorporateActionSupersedeRestatementTrigger> _logger;

    public CorporateActionSupersedeRestatementTrigger(
        IServiceScopeFactory scopeFactory,
        ILogger<CorporateActionSupersedeRestatementTrigger> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SecurityMasterRestatementDecision?> OnSupersededAsync(
        CorporateActionDto superseding,
        CorporateActionDto superseded,
        string? fundProfileId,
        string actor,
        string? correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(superseding);
        ArgumentNullException.ThrowIfNull(superseded);

        using var scope = _scopeFactory.CreateScope();
        var queryService = scope.ServiceProvider.GetService<ISecurityMasterWorkbenchQueryService>();
        var bookResolver = scope.ServiceProvider.GetService<IAffectedLedgerBookResolver>();
        var periodResolver = scope.ServiceProvider.GetService<IPeriodAwareRestatementResolver>();
        if (queryService is null || bookResolver is null || periodResolver is null)
        {
            _logger.LogDebug(
                "Corporate action supersede for {SecurityId} has no restatement stack available; skipping period-aware routing.",
                superseding.SecurityId);
            return null;
        }

        var fundScope = string.IsNullOrWhiteSpace(fundProfileId) ? null : fundProfileId.Trim();
        var snapshot = await queryService
            .GetTrustSnapshotAsync(superseding.SecurityId, fundProfileId: fundScope, ct)
            .ConfigureAwait(false);
        if (snapshot?.DownstreamImpact is not { } impact)
        {
            return new SecurityMasterRestatementDecision(RestatementRequired: false, Candidates: []);
        }

        var affectedLedgerBookIds = await bookResolver.ResolveAsync(impact, ct).ConfigureAwait(false);
        if (affectedLedgerBookIds.Count == 0)
        {
            return new SecurityMasterRestatementDecision(RestatementRequired: false, Candidates: []);
        }

        // The economics changed as of the original event's ex-date (possibly long before the
        // amendment arrived), so period-lock routing keys on the superseded ex-date.
        var effectiveFrom = new DateTimeOffset(
            superseded.ExDate.Year, superseded.ExDate.Month, superseded.ExDate.Day, 0, 0, 0, TimeSpan.Zero);
        var canonicalEventType = CorporateActionEventTypes.Normalize(superseded.EventType);

        var publishedEvent = new SecurityMasterRevisionPublishedEvent(
            SecurityId: superseding.SecurityId,
            RevisionId: superseding.CorpActId,
            Version: 0,
            EffectiveFrom: effectiveFrom,
            ChangedFields: [$"corporateActions.{canonicalEventType}"],
            DownstreamImpact: impact,
            AffectedLedgerBookIds: affectedLedgerBookIds,
            Actor: actor,
            CorrelationId: correlationId);

        var decision = await periodResolver.ResolveAsync(publishedEvent, ct).ConfigureAwait(false);
        if (decision.RestatementRequired)
        {
            _logger.LogInformation(
                "Superseding corporate action {CorpActId} ({EventType}) for {SecurityId} affects a closed period at {EffectiveDate}; proposing restatement with {CandidateCount} candidate(s).",
                superseding.CorpActId, canonicalEventType, superseding.SecurityId, superseded.ExDate, decision.Candidates.Count);
        }

        return decision;
    }
}
