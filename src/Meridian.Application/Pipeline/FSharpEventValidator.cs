using Meridian.Application.Monitoring;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using FSharpQuoteEventWrapper = Meridian.FSharp.Interop.QuoteEventWrapper;
using FSharpQuoteValidator = Meridian.FSharp.Interop.QuoteValidator;
using FSharpTradeEventWrapper = Meridian.FSharp.Interop.TradeEventWrapper;
using FSharpTradeValidator = Meridian.FSharp.Interop.TradeValidator;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Validates <see cref="MarketEvent"/> instances by delegating to the F# Railway-Oriented
/// validation pipeline (<c>Meridian.FSharp.Validation</c>).
/// </summary>
/// <remarks>
/// <para>Trade and BBO-quote events are validated against the F# validators.
/// All other event types (L2 snapshots, order-flow, integrity, heartbeat, historical bars, etc.)
/// pass through unconditionally — only data that was produced in real-time from a provider
/// carries the invariants the validators check.</para>
///
/// <para>Per-symbol relaxation is opt-in: add <c>"UseRelaxedValidation": true</c> to a symbol's
/// entry in <c>appsettings.json</c> to apply the historical (lenient) F# preset — useful for
/// illiquid instruments, preferreds, or wide-spread options where the default 5-minute timestamp
/// window or strict spread check would produce excessive false positives.</para>
///
/// <para>Validation outcomes are tracked by <see cref="ValidationMetrics"/> and exported to
/// Prometheus via <see cref="PrometheusMetrics"/> on the periodic update cycle.</para>
/// </remarks>
[ImplementsAdr("ADR-007", "F# validation gate before WAL/sink persistence")]
public sealed class FSharpEventValidator : IEventValidator
{
    private readonly IReadOnlySet<string> _relaxedSymbols;
    private readonly bool _useRealTimeMode;
    private readonly ILogger<FSharpEventValidator> _logger;

    /// <summary>
    /// Initialises the validator.
    /// </summary>
    /// <param name="symbolConfigs">
    /// Optional array of configured symbols. Symbols with <c>UseRelaxedValidation = true</c>
    /// are validated with the lenient historical preset instead of the default preset.
    /// </param>
    /// <param name="useRealTimeMode">
    /// When <see langword="true"/>, the strict real-time preset is applied for non-relaxed symbols
    /// (5-second max timestamp age). When <see langword="false"/> (default), the standard default
    /// preset is used (5-minute max timestamp age).
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public FSharpEventValidator(
        SymbolConfig[]? symbolConfigs = null,
        bool useRealTimeMode = false,
        ILogger<FSharpEventValidator>? logger = null)
    {
        _useRealTimeMode = useRealTimeMode;
        _logger = logger ?? NullLogger<FSharpEventValidator>.Instance;
        _relaxedSymbols = symbolConfigs?
            .Where(s => s.UseRelaxedValidation == true)
            .Select(s => s.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? (IReadOnlySet<string>)new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ValidationResult Validate(in MarketEvent evt)
    {
        return evt.Type switch
        {
            MarketEventType.Trade => ValidateTrade(in evt),
            MarketEventType.BboQuote => ValidateQuote(in evt),
            // All other types (L2, OrderFlow, Integrity, Heartbeat, HistoricalBar, etc.)
            // pass through without validation.
            _ => ValidationResult.Valid,
        };
    }

    // ── Trade validation ──────────────────────────────────────────────────────

    private ValidationResult ValidateTrade(in MarketEvent evt)
    {
        if (evt.Payload is not ContractsTrade trade)
            return ValidationResult.Valid;

        var isRelaxed = _relaxedSymbols.Contains(evt.EffectiveSymbol);

        // Build the F# TradeEvent via the interop wrapper (zero-allocation path uses struct fields).
        var fsharpTrade = FSharpTradeEventWrapper.Create(
            trade.Symbol,
            trade.Price,
            trade.Size,
            (int)trade.Aggressor,   // AggressorSide: Unknown=0, Buy=1, Sell=2 maps 1:1 to F# enum
            trade.SequenceNumber,
            evt.Timestamp
        ).ToFSharpEvent();

        var result = isRelaxed
            ? FSharpTradeValidator.ValidateHistorical(fsharpTrade)
            : FSharpTradeValidator.Validate(fsharpTrade);

        ValidationMetrics.RecordValidated("trade");

        if (result.IsSuccess)
            return ValidationResult.Valid;

        ValidationMetrics.RecordRejection("trade", result.Errors);

        _logger.LogDebug(
            "Trade event failed validation for {Symbol} seq={Sequence}: {Errors}",
            evt.EffectiveSymbol, evt.Sequence, string.Join("; ", result.Errors));

        return ValidationResult.Failed(result.Errors);
    }

    // ── Quote validation ──────────────────────────────────────────────────────

    private ValidationResult ValidateQuote(in MarketEvent evt)
    {
        if (evt.Payload is not ContractsBboQuotePayload quote)
            return ValidationResult.Valid;

        var isRelaxed = _relaxedSymbols.Contains(evt.EffectiveSymbol);

        var fsharpQuote = FSharpQuoteEventWrapper.Create(
            quote.Symbol,
            quote.BidPrice,
            quote.BidSize,
            quote.AskPrice,
            quote.AskSize,
            quote.SequenceNumber,
            evt.Timestamp
        ).ToFSharpEvent();

        var result = isRelaxed
            ? FSharpQuoteValidator.ValidateHistorical(fsharpQuote)
            : FSharpQuoteValidator.Validate(fsharpQuote);

        ValidationMetrics.RecordValidated("quote");

        if (result.IsSuccess)
            return ValidationResult.Valid;

        ValidationMetrics.RecordRejection("quote", result.Errors);

        _logger.LogDebug(
            "Quote event failed validation for {Symbol} seq={Sequence}: {Errors}",
            evt.EffectiveSymbol, evt.Sequence, string.Join("; ", result.Errors));

        return ValidationResult.Failed(result.Errors);
    }
}
