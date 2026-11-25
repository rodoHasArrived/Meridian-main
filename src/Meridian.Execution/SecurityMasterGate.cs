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

        var resolution = await _resolver.ResolveCanonicalAsync(
            new ResolveCanonicalSecurityRequest(
                SecurityIdentifierKind.Ticker,
                symbol,
                DateTimeOffset.UtcNow,
                ActiveOnly: true),
            ct).ConfigureAwait(false);

        if (resolution.IsResolved)
        {
            return new SecurityMasterGateResult(true) { SecurityId = resolution.SecurityId };
        }

        if (resolution.Status == CanonicalSecurityResolutionStatus.Ambiguous)
        {
            _logger.LogWarning("Security Master gate: symbol '{Symbol}' is ambiguous across {CandidateCount} securities",
                LogSanitizer.Sanitize(symbol), resolution.Candidates.Count);
            return new SecurityMasterGateResult(
                false,
                $"Symbol '{symbol}' matches {resolution.Candidates.Count} active securities in the Security Master; refusing an ambiguous order.");
        }

        _logger.LogWarning("Security Master gate: symbol '{Symbol}' not found or inactive", LogSanitizer.Sanitize(symbol));
        return new SecurityMasterGateResult(
            false,
            $"Symbol '{symbol}' is not registered as an active security in the Security Master.");
    }
}
