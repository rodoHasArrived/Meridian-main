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
        ValidateRequest(request);

        var action = request.CorporateAction;
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

    private static void ValidateRequest(SecurityMasterCorporateActionAppendRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CorporateAction);

        if (request.SecurityId == Guid.Empty)
        {
            throw new ArgumentException("Corporate action SecurityId is required.", nameof(request));
        }

        if (request.CorporateAction.SecurityId != request.SecurityId)
        {
            throw new ArgumentException("Corporate action SecurityId must match route parameter", nameof(request));
        }

        if (request.CorporateAction.CorpActId == Guid.Empty)
        {
            throw new ArgumentException("Corporate action CorpActId is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.CorporateAction.EventType))
        {
            throw new ArgumentException("Corporate action EventType is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SourceSystem))
        {
            throw new ArgumentException("Corporate action SourceSystem is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Actor))
        {
            throw new ArgumentException("Corporate action Actor is required.", nameof(request));
        }

        ValidateCorporateAction(request.CorporateAction);
    }

    private static void ValidateCorporateAction(CorporateActionDto dto)
    {
        if (string.Equals(dto.EventType, "StockSplit", StringComparison.OrdinalIgnoreCase))
        {
            if (!dto.SplitRatio.HasValue)
            {
                throw new ArgumentException("StockSplit corporate actions must include SplitRatio.");
            }

            if (dto.SplitRatio.Value <= 0m || dto.SplitRatio.Value > 1_000m)
            {
                throw new ArgumentException("StockSplit SplitRatio must be greater than 0 and less than or equal to 1000.");
            }
        }

        if (string.Equals(dto.EventType, "Dividend", StringComparison.OrdinalIgnoreCase) &&
            dto.DividendPerShare.HasValue &&
            dto.DividendPerShare.Value < 0m)
        {
            throw new ArgumentException("DividendPerShare must be greater than or equal to 0.");
        }
    }
}
