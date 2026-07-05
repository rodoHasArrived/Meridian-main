using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Request to record a ticker change (e.g. FB→META) as a first-class security-master event.
/// </summary>
public sealed record RecordTickerChangeRequest(
    Guid SecurityId,
    string OldTicker,
    string NewTicker,
    DateTimeOffset EffectiveAtUtc,
    string UpdatedBy,
    string? Reason,
    string SourceSystem = "security-master-symbology");

/// <summary>
/// Records ticker changes through the standard amend path: the old ticker identifier's
/// validity interval is closed at the change date and the new ticker's interval opens there.
/// Because this flows through the event store, historical as-of resolution answers lookups
/// for the old ticker at pre-change dates, and the change is visible in the security's
/// event history like any other amendment.
/// </summary>
public sealed class SecurityMasterTickerChangeService
{
    private readonly ContractSecurityMasterQueryService _queryService;
    private readonly ISecurityMasterService _commandService;
    private readonly ILogger<SecurityMasterTickerChangeService> _logger;

    public SecurityMasterTickerChangeService(
        ContractSecurityMasterQueryService queryService,
        ISecurityMasterService commandService,
        ILogger<SecurityMasterTickerChangeService> logger)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SecurityDetailDto> RecordAsync(RecordTickerChangeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OldTicker);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NewTicker);
        if (string.Equals(request.OldTicker.Trim(), request.NewTicker.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Old and new tickers must differ.", nameof(request));
        }

        var detail = await _queryService.GetByIdAsync(request.SecurityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Security '{request.SecurityId}' was not found.");

        var oldIdentifier = detail.Identifiers.FirstOrDefault(identifier =>
                identifier.Kind == SecurityIdentifierKind.Ticker
                && string.Equals(identifier.Value, request.OldTicker.Trim(), StringComparison.OrdinalIgnoreCase)
                && identifier.ValidFrom <= request.EffectiveAtUtc
                && (!identifier.ValidTo.HasValue || identifier.ValidTo.Value > request.EffectiveAtUtc))
            ?? throw new InvalidOperationException(
                $"Security '{request.SecurityId}' has no ticker identifier '{request.OldTicker}' valid at {request.EffectiveAtUtc:O}.");

        var newIdentifier = oldIdentifier with
        {
            Value = request.NewTicker.Trim().ToUpperInvariant(),
            ValidFrom = request.EffectiveAtUtc,
            ValidTo = null,
            NormalizedValue = null,
            NormalizedProvider = null
        };

        var amended = await _commandService.AmendTermsAsync(
                new AmendSecurityTermsRequest(
                    SecurityId: request.SecurityId,
                    ExpectedVersion: detail.Version,
                    CommonTerms: null,
                    AssetSpecificTermsPatch: null,
                    IdentifiersToAdd: [newIdentifier],
                    IdentifiersToExpire: [oldIdentifier with { ValidTo = request.EffectiveAtUtc }],
                    EffectiveFrom: request.EffectiveAtUtc,
                    SourceSystem: request.SourceSystem,
                    UpdatedBy: request.UpdatedBy,
                    SourceRecordId: null,
                    Reason: request.Reason ?? $"Ticker change {request.OldTicker.Trim().ToUpperInvariant()} → {request.NewTicker.Trim().ToUpperInvariant()}."),
                ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Recorded ticker change {OldTicker} → {NewTicker} for {SecurityId} effective {EffectiveAt}",
            request.OldTicker, request.NewTicker, request.SecurityId, request.EffectiveAtUtc);

        return amended;
    }
}
