using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.CorporateActions;

public interface ICorporateActionCommandService
{
    Task<CorporateActionAppendResult> AppendAsync(Guid securityId, CorporateActionDto action, string? actor, string source, CancellationToken ct = default);
}

/// <summary>
/// <see cref="RestatementDecision"/> is set when the append superseded a prior event whose
/// economics land in a closed ledger period; the caller surfaces the proposal to the
/// operator (the append itself still succeeds — restatement is proposed, never posted).
/// </summary>
public sealed record CorporateActionAppendResult(
    bool Succeeded,
    string? ValidationError = null,
    SecurityMasterRestatementDecision? RestatementDecision = null)
{
    public static CorporateActionAppendResult Success { get; } = new(true);

    public static CorporateActionAppendResult Invalid(string validationError) => new(false, validationError);
}

public sealed class CorporateActionCommandService : ICorporateActionCommandService
{
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ILogger<CorporateActionCommandService> _logger;
    private readonly ISecurityMasterStore? _store;
    private readonly ICorporateActionRestatementTrigger? _restatementTrigger;
    private readonly ICorporateActionOperationsService? _durableOperations;

    public CorporateActionCommandService(
        ISecurityMasterEventStore eventStore,
        ILogger<CorporateActionCommandService> logger,
        ISecurityMasterStore? store = null,
        ICorporateActionRestatementTrigger? restatementTrigger = null,
        ICorporateActionOperationsService? durableOperations = null)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _store = store;
        _restatementTrigger = restatementTrigger;
        _durableOperations = durableOperations;
    }

    public async Task<CorporateActionAppendResult> AppendAsync(
        Guid securityId,
        CorporateActionDto action,
        string? actor,
        string source,
        CancellationToken ct = default)
    {
        if (_durableOperations is not null)
        {
            return CorporateActionAppendResult.Invalid(
                "Legacy direct corporate-action append is disabled; use durable source-proposal acceptance.");
        }

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

        var assetClassError = CorporateActionValidation.ValidateForAssetClass(
            normalizedAction, await ResolveAssetClassAsync(securityId, ct).ConfigureAwait(false));
        if (assetClassError is not null)
        {
            return CorporateActionAppendResult.Invalid(assetClassError);
        }

        CorporateActionDto? superseded = null;
        if (normalizedAction.SupersedesCorpActId.HasValue)
        {
            var existing = await _eventStore.LoadCorporateActionsAsync(securityId, ct).ConfigureAwait(false);
            var supersedeError = CorporateActionValidation.ValidateSupersede(normalizedAction, existing);
            if (supersedeError is not null)
            {
                return CorporateActionAppendResult.Invalid(supersedeError);
            }

            superseded = existing.First(candidate => candidate.CorpActId == normalizedAction.SupersedesCorpActId!.Value);
        }

        await _eventStore.AppendCorporateActionAsync(normalizedAction, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Security Master corporate action appended for {SecurityId} with event type {EventType}, corp action {CorporateActionId}, source {Source}, actor {Actor}.",
            securityId,
            normalizedAction.EventType,
            normalizedAction.CorpActId,
            string.IsNullOrWhiteSpace(source) ? "unspecified" : source,
            string.IsNullOrWhiteSpace(actor) ? "system" : actor);

        // This unscoped path carries no fund profile; the trigger resolves an unscoped
        // impact and typically yields no proposal — the governed workbench path is the
        // fund-scoped one. Restatement is proposed after the append (never blocks it).
        var restatementDecision = superseded is not null && _restatementTrigger is not null
            ? await _restatementTrigger.OnSupersededAsync(
                normalizedAction, superseded, fundProfileId: null,
                actor: string.IsNullOrWhiteSpace(actor) ? "system" : actor!,
                correlationId: null, ct).ConfigureAwait(false)
            : null;

        return restatementDecision is null
            ? CorporateActionAppendResult.Success
            : new CorporateActionAppendResult(true, RestatementDecision: restatementDecision);
    }

    public static string? Validate(CorporateActionDto action)
        => CorporateActionValidation.Validate(action);

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
}
