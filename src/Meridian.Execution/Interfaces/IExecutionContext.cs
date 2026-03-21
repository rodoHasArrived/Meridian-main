using Meridian.Execution.Models;

namespace Meridian.Execution.Interfaces;

/// <summary>
/// The live-mode analogue of <c>IBacktestContext</c>. Provides a strategy with a
/// unified view of the current feed, portfolio state, and order gateway without
/// exposing any broker-specific or provider-specific type. Enforced by ADR-015.
/// </summary>
public interface IExecutionContext
{
    /// <summary>The order gateway used to submit and cancel orders in this session.</summary>
    IOrderGateway Gateway { get; }

    /// <summary>Live market data adapter for the symbols in this session.</summary>
    ILiveFeedAdapter Feed { get; }

    /// <summary>Read-only view of the current portfolio state.</summary>
    IPortfolioState Portfolio { get; }

    /// <summary>Symbols available in this execution session.</summary>
    IReadOnlySet<string> Universe { get; }

    /// <summary>Wall-clock time at the moment of the current market event.</summary>
    DateTimeOffset CurrentTime { get; }
}
