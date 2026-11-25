using Meridian.Execution.Logging;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;

namespace Meridian.Execution;

/// <summary>
/// Default <see cref="ISecurityMasterGate"/> implementation that uses <see cref="ISecurityResolver"/>
/// to look up the symbol in the Security Master.
/// </summary>
public sealed class SecurityMasterGate : ISecurityMasterGate
{
    private readonly ISecurityResolver _resolver;
    private readonly ILogger<SecurityMasterGate> _logger;

    /// <summary>Retains the resolver constructor used by existing compiled consumers.</summary>
#pragma warning disable CS0618
    public SecurityMasterGate(Meridian.Application.SecurityMaster.ISecurityResolver resolver, ILogger<SecurityMasterGate> logger)
        : this((ISecurityResolver)resolver, logger)
    {
    }
#pragma warning restore CS0618

    public SecurityMasterGate(ISecurityResolver resolver, ILogger<SecurityMasterGate> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SecurityMasterGateResult> CheckAsync(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return new SecurityMasterGateResult(false, "Symbol must not be empty.");
        }

        // Canonical resolution: provider is identity only for provider-scoped kinds, so a ticker
        // mastered by a provider ingest (Polygon, EDGAR) is admitted, and a ticker claimed by more
        // than one active security is refused by name rather than resolved to an arbitrary one.
        var resolution = await _resolver.ResolveCanonicalAsync(
            new ResolveSecurityRequest(
                IdentifierKind: SecurityIdentifierKind.Ticker,
                IdentifierValue: symbol,
                Provider: null,
                AsOfUtc: DateTimeOffset.UtcNow,
                ActiveOnly: true),
            ct).ConfigureAwait(false);

        switch (resolution.Status)
        {
            case SecurityResolutionStatus.Resolved:
                return new SecurityMasterGateResult(true);

            case SecurityResolutionStatus.Ambiguous:
                var candidates = string.Join(", ", resolution.CandidateSecurityIds);
                _logger.LogWarning(
                    "Security Master gate: symbol '{Symbol}' is claimed by several active securities ({Candidates})",
                    LogSanitizer.Sanitize(symbol),
                    candidates);
                return new SecurityMasterGateResult(
                    false,
                    $"Symbol '{symbol}' is ambiguous: it is claimed by more than one active security ({candidates}). Resolve the duplicate in the Security Master before trading it.");

            case SecurityResolutionStatus.Inactive:
                _logger.LogWarning("Security Master gate: symbol '{Symbol}' resolves only to inactive securities", LogSanitizer.Sanitize(symbol));
                return new SecurityMasterGateResult(
                    false,
                    $"Symbol '{symbol}' is registered in the Security Master but no longer active.");

            default:
                _logger.LogWarning("Security Master gate: symbol '{Symbol}' not found", LogSanitizer.Sanitize(symbol));
                return new SecurityMasterGateResult(
                    false,
                    $"Symbol '{symbol}' is not registered as an active security in the Security Master.");
        }
    }
}
