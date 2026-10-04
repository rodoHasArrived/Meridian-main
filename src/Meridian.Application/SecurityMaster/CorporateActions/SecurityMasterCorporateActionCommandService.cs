using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.CorporateActions;

public sealed class SecurityMasterCorporateActionCommandService : ISecurityMasterCorporateActionCommandService
{
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ILogger<SecurityMasterCorporateActionCommandService> _logger;
    private readonly ISecurityMasterStore? _store;
    private readonly ICorporateActionRestatementTrigger? _restatementTrigger;
    private readonly ICorporateActionOperationsService? _durableOperations;

    public SecurityMasterCorporateActionCommandService(
        ISecurityMasterEventStore eventStore,
        ILogger<SecurityMasterCorporateActionCommandService> logger,
        ISecurityMasterStore? store = null,
        ICorporateActionRestatementTrigger? restatementTrigger = null,
        ICorporateActionOperationsService? durableOperations = null)
    {
        _eventStore = eventStore;
        _logger = logger;
        _store = store;
        _restatementTrigger = restatementTrigger;
        _durableOperations = durableOperations;
    }

    public async Task<SecurityMasterCorporateActionAppendResultDto> AppendAsync(
        SecurityMasterCorporateActionAppendRequestDto request,
        CancellationToken ct = default)
    {
        if (_durableOperations is not null)
        {
            throw new CorporateActionOperationException(
                CorporateActionProblemCodes.DownstreamAuthorityRequired,
                "Legacy direct corporate-action append is disabled; use durable source-proposal acceptance.");
        }

        var action = ValidateRequest(request);

        var assetClassError = CorporateActionValidation.ValidateForAssetClass(
            action, await ResolveAssetClassAsync(request.SecurityId, ct).ConfigureAwait(false));
        if (assetClassError is not null)
        {
            throw new ArgumentException(assetClassError, nameof(request));
        }

        CorporateActionDto? superseded = null;
        if (action.SupersedesCorpActId.HasValue)
        {
            var existing = await _eventStore.LoadCorporateActionsAsync(request.SecurityId, ct).ConfigureAwait(false);
            var supersedeError = CorporateActionValidation.ValidateSupersede(action, existing);
            if (supersedeError is not null)
            {
                throw new ArgumentException(supersedeError, nameof(request));
            }

            superseded = existing.First(candidate => candidate.CorpActId == action.SupersedesCorpActId!.Value);
        }

        await _eventStore.AppendCorporateActionAsync(action, ct).ConfigureAwait(false);

        var audit = new SecurityMasterCorporateActionAuditDto(
            AuditId: $"security-master-corporate-action:{action.CorpActId:D}",
            SecurityId: request.SecurityId,
            CorporateActionId: action.CorpActId,
            EventType: action.EventType,
            SourceSystem: request.SourceSystem,
            Actor: request.Actor,
            RecordedAtUtc: DateTimeOffset.UtcNow,
            SourceRecordId: request.SourceRecordId,
            Reason: request.Reason,
            CorrelationId: request.CorrelationId);

        _logger.LogInformation(
            "Appended Security Master corporate action {CorporateActionId} for {SecurityId} from {SourceSystem} by {Actor}",
            action.CorpActId,
            request.SecurityId,
            request.SourceSystem,
            request.Actor);

        // Restatement is proposed after the append succeeds (never blocks it); the request's
        // fund scope bounds impact resolution exactly like the revision-publish path.
        var restatement = superseded is not null && _restatementTrigger is not null
            ? ToRestatementDto(await _restatementTrigger.OnSupersededAsync(
                action, superseded, request.FundProfileId, request.Actor, request.CorrelationId, ct)
                .ConfigureAwait(false))
            : null;

        return new SecurityMasterCorporateActionAppendResultDto(action, audit, restatement);
    }

    private static SecurityMasterCorporateActionRestatementDto? ToRestatementDto(
        SecurityMasterRestatementDecision? decision)
        => decision is null || (!decision.RestatementRequired && decision.Candidates.Count == 0)
            ? null
            : new SecurityMasterCorporateActionRestatementDto(decision.RestatementRequired, decision.Candidates);

    private async Task<string?> ResolveAssetClassAsync(Guid securityId, CancellationToken ct)
    {
        if (_store is null)
        {
            return null;
        }

        try
        {
            var projection = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
            return projection?.AssetClass;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Asset-class validity is fail-open by design: a projection-store fault must not
            // block an otherwise valid append. The taxonomy check itself stays fail-closed.
            _logger.LogWarning(ex,
                "Asset-class lookup failed for {SecurityId}; skipping corporate-action asset-class validation.",
                securityId);
            return null;
        }
    }

    private static CorporateActionDto ValidateRequest(SecurityMasterCorporateActionAppendRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CorporateAction);

        var action = CorporateActionValidation.Normalize(request.CorporateAction);

        if (request.SecurityId == Guid.Empty)
        {
            throw new ArgumentException("Corporate action SecurityId is required.", nameof(request));
        }

        if (action.SecurityId != request.SecurityId)
        {
            throw new ArgumentException("Corporate action SecurityId must match route parameter", nameof(request));
        }

        if (action.CorpActId == Guid.Empty)
        {
            throw new ArgumentException("Corporate action CorpActId is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SourceSystem))
        {
            throw new ArgumentException("Corporate action SourceSystem is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Actor))
        {
            throw new ArgumentException("Corporate action Actor is required.", nameof(request));
        }

        var validationError = CorporateActionValidation.Validate(action);
        if (validationError is not null)
        {
            throw new ArgumentException(validationError, nameof(request));
        }

        return action;
    }
}
