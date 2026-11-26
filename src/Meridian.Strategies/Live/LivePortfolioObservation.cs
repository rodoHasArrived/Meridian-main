using Meridian.Backtesting.Sdk;
using Meridian.Execution.Interfaces;

namespace Meridian.Strategies.Live;

internal sealed record LivePortfolioObservation(decimal Cash, decimal Equity,
    IReadOnlyDictionary<string, Position> Positions,
    IReadOnlyDictionary<string, FinancialAccountSnapshot> Accounts,
    PositionAccountingEvidenceSnapshot? AccountingEvidence);
