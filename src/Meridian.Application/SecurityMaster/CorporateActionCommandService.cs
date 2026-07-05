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

        var normalizedAction = CorporateActionValidation.Normalize(action);
        var validationError = Validate(normalizedAction);
        if (validationError is not null)
        {
            return CorporateActionAppendResult.Invalid(validationError);
        }

        await _eventStore.AppendCorporateActionAsync(normalizedAction, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Security Master corporate action appended for {SecurityId} with event type {EventType}, corp action {CorporateActionId}, source {Source}, actor {Actor}.",
            securityId,
            normalizedAction.EventType,
            normalizedAction.CorpActId,
            string.IsNullOrWhiteSpace(source) ? "unspecified" : source,
            string.IsNullOrWhiteSpace(actor) ? "system" : actor);

        return CorporateActionAppendResult.Success;
    }

    public static string? Validate(CorporateActionDto action)
        => CorporateActionValidation.Validate(action);
}
