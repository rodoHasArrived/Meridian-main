using Meridian.Contracts.Workstation;
using Meridian.Execution.Interfaces;
using Meridian.Ledger;
using System.Collections.ObjectModel;

namespace Meridian.Execution.Services;

public sealed partial class PaperTradingPortfolio
{
    private string? _positionEvidenceAccountScope;
    private string? _positionEvidenceCurrency;
    private DateTimeOffset _positionEvidenceCutoff;

    /// <summary>Binds read-side evidence to an already authorized book; grants no execution or posting authority.</summary>
    internal void BindPositionAccountingEvidenceScope(string financialAccountId, string functionalCurrency,
        DateTimeOffset? capturedAsOf = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(financialAccountId);
        var currency = new LedgerPolicy(_chartOfAccounts, functionalCurrency).FunctionalCurrency;
        lock (_lock)
        {
            if (_accounts.Count != 1 || _positionEvidenceAccountScope is { } existing
                    && !string.Equals(existing, financialAccountId, StringComparison.OrdinalIgnoreCase)
                || _positionEvidenceCurrency is { } previousCurrency && previousCurrency != currency
                || _ledger?.Policy is { } policy && policy.FunctionalCurrency != currency)
                throw new InvalidOperationException("Native position evidence requires one immutable account and functional-currency scope.");
            _positionEvidenceAccountScope = financialAccountId.Trim();
            _positionEvidenceCurrency = currency;
            if (capturedAsOf is { } at && at > _positionEvidenceCutoff)
                _positionEvidenceCutoff = at;
        }
    }

    private void RetainAccountingEvidenceCutoff(DateTimeOffset occurredAt)
    {
        if (occurredAt > _positionEvidenceCutoff)
            _positionEvidenceCutoff = occurredAt;
    }

    /// <summary>Copies exact book facts under the fill lock; displayed average prices are never a basis source.</summary>
    public PositionAccountingEvidenceSnapshot CapturePositionAccountingEvidence()
    {
        lock (_lock)
        {
            var currency = _ledger?.Policy?.FunctionalCurrency ?? _positionEvidenceCurrency;
            var rows = new List<StrategyPositionAccountingEvidence>();
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_scopeLedgerAccounts)
                covered.UnionWith(_accounts.Keys);
            else if (_positionEvidenceAccountScope is { } accountScope)
                covered.Add(accountScope);
            var ambiguousUnscopedPartition = false;

            foreach (var account in _accounts.Values.OrderBy(static item => item.AccountId, StringComparer.Ordinal))
                foreach (var position in account.Positions.Values
                             .Where(static item => item.Quantity != 0m || item.HasOwnerExposure)
                             .OrderBy(static item => item.Symbol, StringComparer.Ordinal))
                {
                    var owners = position.OwnerQuantities.Where(static item => item.Value != 0m).ToArray();
                    var financialAccountId = _scopeLedgerAccounts ? account.AccountId : _positionEvidenceAccountScope;
                    if (financialAccountId is null && owners.Length == 1 && owners[0].Value == position.Quantity)
                        financialAccountId = owners[0].Key;
                    var problems = new List<string>();
                    if (financialAccountId is null)
                    {
                        problems.Add("Native carrying basis has no exact single financial-account owner.");
                        ambiguousUnscopedPartition = true;
                    }
                    else if (owners.Any(owner => !string.Equals(owner.Key, financialAccountId, StringComparison.OrdinalIgnoreCase))
                        || owners.Length > 1 || owners.Length == 1 && owners[0].Value != position.Quantity)
                        problems.Add("Native position ownership cannot allocate this carrying basis to one account.");
                    if (currency is null)
                        problems.Add("No functional currency was declared by the native book.");
                    if (position.AccountingSecurityId is null)
                        problems.Add("Native acquisition or relief identity is missing or mixed.");
                    if (position.AccountingRunId is null)
                        problems.Add("Native acquisition or relief run ownership is missing or mixed.");
                    if (!position.AccountingChronologyKnown)
                        problems.Add("Native fill occurrence chronology is incomplete.");
                    var lots = position.UnitizedLots;
                    if (lots.Any(lot => lot.UnitKind != position.LotUnit || lot.Multiplier != position.ContractMultiplier)
                        || lots.Sum(static lot => lot.Quantity) != Math.Abs(position.Quantity)
                        || lots.Cast<PositionLot>().Sum(static lot => lot.OpenCostBasis) != position.OpenCostBasis)
                        problems.Add("The native unitized lot population does not agree with the position book.");
                    var failure = problems.Count == 0 ? null : string.Join(" ", problems);
                    rows.Add(new(position.Symbol, financialAccountId, position.AccountingSecurityId, position.Quantity < 0m,
                        failure is null ? position.Quantity : null,
                        failure is null ? position.UsesVariationMargin ? 0m : position.OpenCostBasis : null,
                        position.LotUnit.ToString(), position.ContractMultiplier, currency, failure,
                        Array.AsReadOnly(position.AccountingFillIds.Order().Select(static id => $"fill:{id:D}").ToArray()))
                    { StrategyRunId = position.AccountingRunId });
                }
            // An unowned holding could belong to any observed owner. Do not turn filtering that
            // unknown scope into an authoritative empty position partition for another run.
            if (!_scopeLedgerAccounts && _positionEvidenceAccountScope is null && ambiguousUnscopedPartition)
                covered.Clear();
            var now = DateTimeOffset.UtcNow;
            var asOf = _positionEvidenceCutoff > now ? _positionEvidenceCutoff : now;
            var accounts = _accounts.Values.Where(account => _scopeLedgerAccounts || _positionEvidenceAccountScope is not null)
                .OrderBy(static account => account.AccountId, StringComparer.Ordinal)
                .Select(account => account.TakeSnapshot() with
                {
                    AccountId = _scopeLedgerAccounts ? account.AccountId : _positionEvidenceAccountScope!,
                    AsOf = asOf,
                    FunctionalCurrency = currency,
                    AccountingEquity = account.Cash + account.Positions.Values.Sum(static position => position.SignedMarketValue)
                        - account.MarginBalance + account.ShortMarginCollateral,
                    AccountingLongMarketValue = account.Positions.Values.Sum(static position => Math.Max(position.SignedMarketValue, 0m)),
                    AccountingShortMarketValue = account.Positions.Values.Sum(static position => Math.Min(position.SignedMarketValue, 0m)),
                    Positions = Array.AsReadOnly(account.Positions.Values.Select(static position =>
                        position.ToExecutionPosition() with
                        {
                            OwnerQuantities = new ReadOnlyDictionary<string, decimal>(
                                new Dictionary<string, decimal>(position.OwnerQuantities, StringComparer.OrdinalIgnoreCase))
                        }).ToArray())
                }).ToArray();
            return new(asOf, Array.AsReadOnly(rows.ToArray()))
            {
                CoveredFinancialAccountIds = Array.AsReadOnly(covered.Order(StringComparer.Ordinal).ToArray()),
                Accounts = Array.AsReadOnly(accounts)
            };
        }
    }
}
