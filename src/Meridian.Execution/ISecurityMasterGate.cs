using Meridian.Contracts.SecurityMaster;

namespace Meridian.Execution;

/// <summary>
/// Result from a Security Master gate check for an order symbol.
/// </summary>
public sealed record SecurityMasterGateResult(bool IsApproved, string? Reason = null);

/// <summary>
/// Pre-trade governance gate: verifies that a symbol is registered and active in the
/// Security Master before an order is accepted by the OMS.
/// </summary>
public interface ISecurityMasterGate
{
    /// <summary>
    /// Checks whether <paramref name="symbol"/> resolves to an active security in the Security Master.
    /// </summary>
    /// <param name="symbol">Ticker or identifier to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="SecurityMasterGateResult.IsApproved"/> is <c>true</c> when the symbol resolves;
    /// <c>false</c> with a rejection <see cref="SecurityMasterGateResult.Reason"/> otherwise.
    /// </returns>
    Task<SecurityMasterGateResult> CheckAsync(string symbol, CancellationToken ct = default);
}
