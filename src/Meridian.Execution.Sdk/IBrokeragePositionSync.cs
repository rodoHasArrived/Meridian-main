namespace Meridian.Execution.Sdk;

/// <summary>
/// Provides read-only access to live positions and account summaries from a connected brokerage.
/// Implement this interface on each <see cref="IBrokerageGateway"/> that supports position queries.
/// <para>
/// Return types use primitive/value representations so that <c>Meridian.Execution.Sdk</c>
/// has no dependency on <c>Meridian.Execution.Models</c> types.
/// </para>
/// </summary>
public interface IBrokeragePositionSync
{
    /// <summary>
    /// Returns all open positions for the given <paramref name="accountId"/> as raw DTOs.
    /// </summary>
    Task<IReadOnlyList<BrokeragePositionDto>> GetCurrentPositionsAsync(
        string accountId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a lightweight account summary for the given account.
    /// </summary>
    Task<BrokerageAccountSummaryDto> GetAccountSummaryAsync(
        string accountId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all account IDs accessible via this brokerage connection.
    /// </summary>
    Task<IReadOnlyList<string>> GetAllAccountsAsync(CancellationToken ct = default);
}

/// <summary>Position data returned by <see cref="IBrokeragePositionSync"/>.</summary>
/// <param name="Symbol">Ticker symbol.</param>
/// <param name="Quantity">Signed quantity (positive = long, negative = short).</param>
/// <param name="CostBasis">Average cost per share.</param>
/// <param name="UnrealisedPnl">Mark-to-market unrealised P&amp;L, if available.</param>
public sealed record BrokeragePositionDto(
    string Symbol,
    decimal Quantity,
    decimal CostBasis,
    decimal UnrealisedPnl = 0m);

/// <summary>Account summary returned by <see cref="IBrokeragePositionSync"/>.</summary>
/// <param name="AccountId">Account identifier.</param>
/// <param name="DisplayName">Human-readable label (may be empty for some brokerages).</param>
/// <param name="Cash">Cash / settled balance.</param>
/// <param name="MarginBalance">Margin used (0 for cash accounts).</param>
public sealed record BrokerageAccountSummaryDto(
    string AccountId,
    string DisplayName,
    decimal Cash,
    decimal MarginBalance = 0m);
