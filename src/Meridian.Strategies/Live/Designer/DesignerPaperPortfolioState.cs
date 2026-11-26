using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Strategies.Live.Designer;

/// <summary>Reads only the durable paper session prepared for this reviewed Designer run.</summary>
internal sealed class DesignerPaperPortfolioState(PaperRunOrderContext paper, ILiveFeedAdapter feed)
    : IPortfolioState, IPositionAccountingEvidenceSource
{
    public decimal Cash => ReadPortfolio().Cash;
    public decimal PortfolioValue => ReadPortfolio().PortfolioValue;
    public decimal UnrealisedPnl => ReadPortfolio().UnrealisedPnl;
    public decimal RealisedPnl => ReadPortfolio().RealisedPnl;
    public decimal BuyingPower => ((IPortfolioState)ReadPortfolio()).BuyingPower;
    public IReadOnlyDictionary<string, IPosition> Positions => ReadPortfolio().Positions;

    public PositionAccountingEvidenceSnapshot CapturePositionAccountingEvidence() =>
        ReadPortfolio().CapturePositionAccountingEvidence();

    private PaperTradingPortfolio ReadPortfolio()
    {
        var portfolio = paper.Sessions.GetActivePortfolio(paper.Authority.PaperSessionId)
            ?? throw new InvalidOperationException("The reviewed Designer paper session is no longer available for observation.");

        // The returned book is a defensive replay projection. Mark that copy from the same
        // accepted feed used by strategy callbacks; never change durable fills or the global
        // OMS portfolio to make this session's observations look current.
        foreach (var symbol in portfolio.Positions.Keys)
        {
            var market = feed.GetSnapshot(symbol);
            decimal? price = market.Trade is { Price: > 0m } trade ? trade.Price : null;
            if (price is null && market.Quote is { BidPrice: > 0m, AskPrice: > 0m } quote && quote.BidPrice <= quote.AskPrice)
                price = quote.MidPrice is > 0m ? quote.MidPrice : (quote.BidPrice + quote.AskPrice) / 2m;
            if (price is > 0m)
                portfolio.UpdateMarketPrice(symbol, price.Value);
        }
        return portfolio;
    }
}
