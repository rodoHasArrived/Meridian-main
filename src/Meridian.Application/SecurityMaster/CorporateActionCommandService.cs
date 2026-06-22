using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

public interface ICorporateActionCommandService
{
    Task<CorporateActionAppendResult> AppendAsync(Guid securityId, CorporateActionDto action, string? actor, string source, CancellationToken ct = default);
}

public sealed record CorporateActionAppendResult(bool Succeeded, string? ValidationError = null)
{
    public static CorporateActionAppendResult Success { get; } = new(true);

    public static CorporateActionAppendResult Invalid(string validationError) => new(false, validationError);
}

public sealed class CorporateActionCommandService : ICorporateActionCommandService
{
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ILogger<CorporateActionCommandService> _logger;

    public CorporateActionCommandService(
        ISecurityMasterEventStore eventStore,
        ILogger<CorporateActionCommandService> logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CorporateActionAppendResult> AppendAsync(
        Guid securityId,
        CorporateActionDto action,
        string? actor,
        string source,
        CancellationToken ct = default)
    {
        if (action.SecurityId != securityId)
        {
            return CorporateActionAppendResult.Invalid("Corporate action SecurityId must match route parameter.");
        }

        var validationError = Validate(action);
        if (validationError is not null)
        {
            return CorporateActionAppendResult.Invalid(validationError);
        }

        await _eventStore.AppendCorporateActionAsync(action, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Security Master corporate action appended for {SecurityId} with event type {EventType}, corp action {CorporateActionId}, source {Source}, actor {Actor}.",
            securityId,
            action.EventType,
            action.CorpActId,
            string.IsNullOrWhiteSpace(source) ? "unspecified" : source,
            string.IsNullOrWhiteSpace(actor) ? "system" : actor);

        return CorporateActionAppendResult.Success;
    }

    public static string? Validate(CorporateActionDto action)
    {
        if (string.IsNullOrWhiteSpace(action.EventType))
        {
            return "Corporate actions must include EventType.";
        }

        if (string.Equals(action.EventType, "StockSplit", StringComparison.OrdinalIgnoreCase))
        {
            if (!action.SplitRatio.HasValue)
            {
                return "StockSplit corporate actions must include SplitRatio.";
            }

            if (action.SplitRatio.Value <= 0m || action.SplitRatio.Value > 1_000m)
            {
                return "StockSplit SplitRatio must be greater than 0 and less than or equal to 1000.";
            }
        }

        if (string.Equals(action.EventType, "Dividend", StringComparison.OrdinalIgnoreCase) &&
            action.DividendPerShare.HasValue &&
            action.DividendPerShare.Value < 0m)
        {
            return "DividendPerShare must be greater than or equal to 0.";
        }

        return null;
    }
}
