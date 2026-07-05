using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterCorporateActionCommandService : ISecurityMasterCorporateActionCommandService
{
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ILogger<SecurityMasterCorporateActionCommandService> _logger;

    public SecurityMasterCorporateActionCommandService(
        ISecurityMasterEventStore eventStore,
        ILogger<SecurityMasterCorporateActionCommandService> logger)
    {
        _eventStore = eventStore;
        _logger = logger;
    }

    public async Task<SecurityMasterCorporateActionAppendResultDto> AppendAsync(
        SecurityMasterCorporateActionAppendRequestDto request,
        CancellationToken ct = default)
    {
        var action = ValidateRequest(request);

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

        return new SecurityMasterCorporateActionAppendResultDto(action, audit);
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
