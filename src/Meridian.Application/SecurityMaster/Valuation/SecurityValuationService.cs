using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.Valuation;

/// <summary>
/// Dispatches a valuation request to the registered <see cref="ISecurityValuationModel"/> whose kind
/// the request names, or failing that the kind the asset class declares as its
/// <see cref="SecurityAssetClassDescriptor.DefaultValuationModel"/>. The dispatch reads the catalog
/// fact, never the class name.
/// </summary>
public sealed class SecurityValuationService : ISecurityValuationService
{
    private const string NoModelKey = "none";

    private readonly ISecurityMasterQueryService _queryService;
    private readonly IReadOnlyDictionary<SecurityValuationModelKind, ISecurityValuationModel> _models;
    private readonly ILogger<SecurityValuationService> _logger;

    public SecurityValuationService(
        ISecurityMasterQueryService queryService,
        IEnumerable<ISecurityValuationModel> models,
        ILogger<SecurityValuationService> logger)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(models);

        var byKind = new Dictionary<SecurityValuationModelKind, ISecurityValuationModel>();
        foreach (var model in models)
        {
            if (!byKind.TryAdd(model.Kind, model))
            {
                throw new InvalidOperationException(
                    $"Two valuation models are registered for {model.Kind}: '{byKind[model.Kind].ModelKey}' and '{model.ModelKey}'. Register one model per kind.");
            }
        }

        _models = byKind;
    }

    public async Task<SecurityValuationResult?> ValueAsync(SecurityValuationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var security = await _queryService.GetByIdAsync(request.SecurityId, ct).ConfigureAwait(false);
        if (security is null)
        {
            _logger.LogDebug("Security {SecurityId} not found when valuing.", request.SecurityId);
            return null;
        }

        var kind = request.Model ?? SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).DefaultValuationModel;
        if (!_models.TryGetValue(kind, out var model))
        {
            _logger.LogWarning("No valuation model is registered for {ModelKind}; security {SecurityId} is blocked.", kind, request.SecurityId);
            return SecurityValuationResult.Blocked(
                request,
                security,
                kind,
                NoModelKey,
                NoModelKey,
                request.KnownAt ?? DateTimeOffset.UtcNow,
                [new SecurityValuationBlockerDto("NoModel", $"No valuation model is registered for {kind}.")]);
        }

        return await model.ValueAsync(request, security, ct).ConfigureAwait(false);
    }
}
