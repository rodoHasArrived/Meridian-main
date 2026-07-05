using System.Threading;
using Meridian.Core.Logging;
using Meridian.Ledger;
using Serilog;

namespace Meridian.Application.Accounting;

/// <summary>
/// One portfolio position eligible for a daily fair-value mark.
/// </summary>
public sealed record MarkToMarketPosition(
    string Symbol,
    decimal Quantity,
    decimal CostPrice,
    string? FinancialAccountId = null,
    string? InstrumentType = null);

/// <summary>
/// A resolved mark price with its provenance for valuation evidence.
/// </summary>
public sealed record MarkPriceQuote(
    decimal Price,
    string Source,
    string EvidenceReference);

/// <summary>
/// Supplies mark prices for daily portfolio valuation. Implementations return null when
/// no reliable price exists for the symbol at the requested date; the caller surfaces the
/// gap instead of silently marking at cost.
/// </summary>
public interface IMarkPriceSource
{
    Task<MarkPriceQuote?> GetMarkPriceAsync(string symbol, DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// Request to prepare a governed daily mark-to-market draft for a fund's positions.
/// </summary>
public sealed record DailyMarkToMarketRequest(
    DailyPortfolioPricingPolicy Policy,
    string PeriodId,
    DateTimeOffset AsOf,
    string BaseCurrency,
    IReadOnlyList<MarkToMarketPosition> Positions,
    string Actor,
    string Reason);

/// <summary>
/// Outcome of a daily mark-to-market preparation run. <see cref="Approval"/> is a
/// submitted governed draft awaiting approve/post; <see cref="UnpricedSymbols"/> lists
/// positions that could not be marked and therefore need operator attention.
/// </summary>
public sealed record DailyMarkToMarketRun(
    DailyPortfolioPricingProjection? Projection,
    AutomatedJournalApproval? Approval,
    IReadOnlyList<string> UnpricedSymbols)
{
    /// <summary>True when a governed draft was submitted for approval.</summary>
    public bool HasDraft => Approval is not null;
}

/// <summary>
/// Wires the daily valuation loop: prices positions through an <see cref="IMarkPriceSource"/>,
/// projects balanced fair-value adjustments with <see cref="DailyPortfolioPricingProjector"/>,
/// and submits the result as a governed <see cref="AutomatedJournalApproval"/> draft so the
/// books carry market values once an operator approves and posts it.
/// </summary>
public sealed class DailyMarkToMarketService
{
    private readonly IMarkPriceSource _priceSource;
    private readonly ILogger _log;

    public DailyMarkToMarketService(IMarkPriceSource priceSource, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(priceSource);
        _priceSource = priceSource;
        _log = log ?? LoggingSetup.ForContext<DailyMarkToMarketService>();
    }

    /// <summary>
    /// Prices the requested positions and submits a governed fair-value draft.
    /// Positions without a price are reported in <see cref="DailyMarkToMarketRun.UnpricedSymbols"/>
    /// and excluded from the draft rather than silently marked at cost.
    /// </summary>
    public async Task<DailyMarkToMarketRun> PrepareAsync(DailyMarkToMarketRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Positions.Count == 0)
            throw new ArgumentException("At least one position is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Actor))
            throw new ArgumentException("Actor is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Reason is required.", nameof(request));

        var asOfDate = DateOnly.FromDateTime(request.AsOf.UtcDateTime);
        var marks = new List<DailyPortfolioPriceMark>(request.Positions.Count);
        var unpriced = new List<string>();

        foreach (var position in request.Positions)
        {
            ct.ThrowIfCancellationRequested();

            var quote = await _priceSource.GetMarkPriceAsync(position.Symbol, asOfDate, ct).ConfigureAwait(false);
            if (quote is null)
            {
                unpriced.Add(position.Symbol);
                _log.Warning(
                    "No mark price available for {Symbol} as of {AsOfDate}; position excluded from fair-value draft",
                    position.Symbol, asOfDate);
                continue;
            }

            marks.Add(new DailyPortfolioPriceMark(
                position.Symbol,
                position.Quantity,
                position.CostPrice,
                quote.Price,
                quote.Source,
                quote.EvidenceReference,
                position.FinancialAccountId,
                position.InstrumentType));
        }

        if (marks.Count == 0)
        {
            _log.Warning(
                "Daily mark-to-market run for fund {FundId} period {PeriodId} priced no positions ({UnpricedCount} unpriced)",
                request.Policy.FundId, request.PeriodId, unpriced.Count);
            return new DailyMarkToMarketRun(null, null, unpriced);
        }

        var projection = DailyPortfolioPricingProjector.Project(new DailyPortfolioPricingInput(
            request.Policy,
            request.PeriodId,
            request.AsOf,
            request.BaseCurrency,
            marks));

        var draft = DailyPortfolioPricingDraftBuilder.BuildDraft(projection);
        if (draft is null)
        {
            _log.Information(
                "Daily marks for fund {FundId} period {PeriodId} produced no unrealized movement; nothing to post",
                request.Policy.FundId, request.PeriodId);
            return new DailyMarkToMarketRun(projection, null, unpriced);
        }

        var approval = AutomatedJournalApproval.Submit(
            draft,
            request.Actor,
            DateTimeOffset.UtcNow,
            request.Reason,
            draft.Metadata.EvidenceReferences.Select(static reference => reference.Uri).ToArray());

        _log.Information(
            "Submitted fair-value draft {ApprovalId} for fund {FundId} period {PeriodId}: {LineCount} lines, net unrealized {NetUnrealized} ({UnpricedCount} unpriced)",
            approval.ApprovalId, request.Policy.FundId, request.PeriodId,
            draft.Lines.Count, projection.NetUnrealizedGainOrLoss, unpriced.Count);

        return new DailyMarkToMarketRun(projection, approval, unpriced);
    }
}
