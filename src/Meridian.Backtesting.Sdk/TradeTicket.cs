namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Human-readable audit ticket that explains why a trading or asset-event cash movement occurred.
/// </summary>
public sealed record TradeTicket(
    Guid TicketId,
    DateTimeOffset Timestamp,
    string ActivityType,
    string? Symbol,
    string Narrative,
    decimal CashImpact,
    long? Quantity = null,
    decimal? UnitPrice = null,
    string? AccountId = null,
    Guid? OrderId = null,
    Guid? FillId = null);
