using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Execution;

/// <summary>
/// Default <see cref="ISecurityMasterGate"/> implementation that uses <see cref="ISecurityResolver"/>
/// to look up the symbol in the Security Master.
/// </summary>
public sealed class SecurityMasterGate : ISecurityMasterGate
{
    private readonly ISecurityResolver _resolver;
    private readonly ILogger<SecurityMasterGate> _logger;

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

        var securityId = await _resolver.ResolveAsync(
            new ResolveSecurityRequest(
                IdentifierKind: SecurityIdentifierKind.Ticker,
                IdentifierValue: symbol,
                Provider: null,
                AsOfUtc: DateTimeOffset.UtcNow,
                ActiveOnly: true),
            ct).ConfigureAwait(false);

        if (securityId.HasValue)
        {
            return new SecurityMasterGateResult(true);
        }

        _logger.LogWarning("Security Master gate: symbol '{Symbol}' not found or inactive", symbol);
        return new SecurityMasterGateResult(
            false,
            $"Symbol '{symbol}' is not registered as an active security in the Security Master.");
    }
}
