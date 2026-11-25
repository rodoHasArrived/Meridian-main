using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Published-revision side effect (<c>Order = 6</c>, after the field-edit merge and before the
/// projection rebuild): applies an approved historical identifier window proposal
/// (<see cref="SecurityMasterHistoricalIdentifierWindow"/>) to the canonical record through
/// <see cref="IGovernedPublicationAmender"/>, adding the identifier with the PROPOSED
/// <c>ValidFrom</c>/<c>ValidTo</c>. The amendment itself is effective at publish time (an amendment
/// cannot predate the security), while the identifier's validity window carries the history, so
/// as-of resolution before the ingest date finds it.
///
/// <para>Bulk imports keep restamping identifier validity with the ingest time; this handler is the
/// only write path that backdates an identifier, and the publication amender accepts it only for a
/// revision the durable store holds as Approved. The added identifier is never primary.</para>
///
/// <para>Idempotent: a retried publish whose window already landed finds the identical identifier on
/// the projection and skips the amendment. A failure leaves the revision Approved and the publish
/// retryable, per the handler seam's contract. No-op without a Security Master backend.</para>
/// </summary>
public sealed class ApprovedHistoricalIdentifierWindowHandler : ISecurityMasterRevisionPublishedHandler, ISecurityMasterCanonicalMergeOutcomeReporter
{
    private readonly ISecurityMasterRevisionStore _revisions;
    private readonly ILogger<ApprovedHistoricalIdentifierWindowHandler> _logger;
    private readonly ISecurityMasterStore? _projectionStore;
    private readonly IGovernedPublicationAmender? _amender;
    private readonly TimeProvider _timeProvider;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SecurityMasterCanonicalMergeOutcomeDto> _outcomes = new();

    public ApprovedHistoricalIdentifierWindowHandler(
        ISecurityMasterRevisionStore revisions,
        ILogger<ApprovedHistoricalIdentifierWindowHandler> logger,
        ISecurityMasterStore? projectionStore = null,
        IGovernedPublicationAmender? amender = null,
        TimeProvider? timeProvider = null)
    {
        _revisions = revisions ?? throw new ArgumentNullException(nameof(revisions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _projectionStore = projectionStore;
        _amender = amender;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>After the field-edit merge (5), before the projection rebuild (10).</summary>
    public int Order => 6;

    /// <inheritdoc />
    public SecurityMasterCanonicalMergeOutcomeDto? TakeOutcome(Guid revisionId)
        => _outcomes.TryRemove(revisionId, out var outcome) ? outcome : null;

    public async Task HandleAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        _outcomes.TryRemove(evt.RevisionId, out _);

        if (_projectionStore is null || _amender is null
            || !evt.ChangedFields.Any(SecurityMasterHistoricalIdentifierWindow.IsWindowPath))
        {
            return;
        }

        var revision = await _revisions.GetAsync(evt.RevisionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Revision '{evt.RevisionId:D}' was not found while applying its approved historical identifier window to " +
                $"security '{evt.SecurityId:D}'; the publish can be retried once the revision store is consistent.");
        if (!SecurityMasterHistoricalIdentifierWindow.IsWindowRevision(revision) || !revision.FieldValueRecorded)
        {
            throw new InvalidOperationException(
                $"Revision '{evt.RevisionId:D}' is not a recorded historical identifier window proposal.");
        }

        var window = SecurityMasterHistoricalIdentifierWindow.Parse(revision.RevisionId, revision.FieldValue);

        var projection = await _projectionStore.GetProjectionAsync(evt.SecurityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Security '{evt.SecurityId:D}' has no canonical projection to apply the approved historical identifier " +
                "window to; the publish can be retried once the projection store is consistent.");

        var identifier = new SecurityIdentifierDto(
            window.Kind,
            window.Value,
            IsPrimary: false,
            ValidFrom: window.ValidFrom,
            ValidTo: window.ValidTo,
            Provider: window.Provider);

        if (projection.Identifiers.Any(existing => IsSameWindow(existing, identifier)))
        {
            Record(evt, SecurityMasterCanonicalMergeOutcomeKind.AlreadyCanonical,
                "The canonical record already carries the approved historical identifier window.", projection.Version);
            return;
        }

        // The amendment is effective now (it cannot predate the security's EffectiveFrom); the
        // identifier's own validity window is what carries the approved history.
        var now = _timeProvider.GetUtcNow();
        var amendRequest = new AmendSecurityTermsRequest(
            SecurityId: evt.SecurityId,
            ExpectedVersion: projection.Version,
            CommonTerms: null,
            AssetSpecificTermsPatch: null,
            IdentifiersToAdd: [identifier],
            IdentifiersToExpire: [],
            EffectiveFrom: now > projection.EffectiveFrom ? now : projection.EffectiveFrom,
            SourceSystem: SecurityMasterWriteAuthority.WorkbenchSourceSystem,
            UpdatedBy: evt.Actor,
            SourceRecordId: revision.RevisionId.ToString("D"),
            Reason: $"{window.Justification} [evidence: {window.EvidenceReference}]");

        var detail = await _amender.AmendApprovedRevisionAsync(amendRequest, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Applied approved historical {Kind} window from {ValidFrom} (revision {RevisionId}) to {SecurityId}; canonical version advanced to {Version}.",
            window.Kind, window.ValidFrom, evt.RevisionId, evt.SecurityId, detail.Version);
        Record(evt, SecurityMasterCanonicalMergeOutcomeKind.Merged,
            $"The approved historical identifier window was applied to the canonical record at version {detail.Version}.",
            detail.Version);
    }

    private static bool IsSameWindow(SecurityIdentifierDto existing, SecurityIdentifierDto proposed)
        => existing.Kind == proposed.Kind
            && string.Equals(
                SecurityIdentifierNormalizer.NormalizeValue(existing.Kind, existing.Value),
                SecurityIdentifierNormalizer.NormalizeValue(proposed.Kind, proposed.Value),
                StringComparison.Ordinal)
            && string.Equals(
                SecurityIdentifierNormalizer.NormalizeProvider(existing.Provider),
                SecurityIdentifierNormalizer.NormalizeProvider(proposed.Provider),
                StringComparison.Ordinal)
            && existing.ValidFrom == proposed.ValidFrom
            && existing.ValidTo == proposed.ValidTo;

    private void Record(
        SecurityMasterRevisionPublishedEvent evt,
        SecurityMasterCanonicalMergeOutcomeKind outcome,
        string detail,
        long? canonicalVersion = null)
        => _outcomes[evt.RevisionId] = new SecurityMasterCanonicalMergeOutcomeDto(outcome, detail, canonicalVersion);
}
