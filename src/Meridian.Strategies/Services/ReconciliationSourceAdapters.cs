using Meridian.Contracts.Banking;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;

namespace Meridian.Strategies.Services;

public sealed record ReconciliationPortfolioInput(
    DateTimeOffset AsOf,
    decimal Cash,
    decimal TotalEquity,
    IReadOnlyList<ReconciliationPositionInput> Positions);

public sealed record ReconciliationLedgerInput(
    DateTimeOffset AsOf,
    decimal AssetBalance,
    decimal LiabilityBalance,
    IReadOnlyList<ReconciliationLedgerLineInput> TrialBalance);

public sealed record ReconciliationPositionInput(string Symbol, bool IsShort);

public sealed record ReconciliationLedgerLineInput(string AccountName, string? Symbol, decimal Balance);

public sealed record ReconciliationCashMovementInput(
    string MovementId,
    decimal Amount,
    DateTimeOffset? AsOf,
    bool IsVoided,
    string Source,
    BankTransactionDto? BankTransaction = null);

public sealed record ReconciliationExternalStatementInput(
    string StatementRowId,
    decimal Amount,
    DateTimeOffset? AsOf,
    string Source,
    string? Reference = null);

public sealed record ReconciliationNormalizedInputs(
    ReconciliationPortfolioInput? Portfolio,
    ReconciliationLedgerInput? Ledger,
    IReadOnlyList<ReconciliationCashMovementInput> InternalCashMovements,
    IReadOnlyList<ReconciliationExternalStatementInput> ExternalStatementRows);

public interface IStrategyLedgerReconciliationSourceAdapter
{
    ReconciliationLedgerInput? Adapt(StrategyRunDetail detail);
}

public interface IStrategyPortfolioReconciliationSourceAdapter
{
    ReconciliationPortfolioInput? Adapt(StrategyRunDetail detail);
}

public interface IInternalCashReconciliationSourceAdapter
{
    Task<IReadOnlyList<ReconciliationCashMovementInput>> GetCashMovementsAsync(
        ReconciliationRunRequest request,
        CancellationToken ct = default);
}

public interface IExternalStatementSource
{
    Task<IReadOnlyList<ReconciliationExternalStatementInput>> GetStatementRowsAsync(
        ReconciliationRunRequest request,
        CancellationToken ct = default);
}

public interface IExternalStatementReconciliationSourceAdapter
{
    Task<IReadOnlyList<ReconciliationExternalStatementInput>> GetStatementRowsAsync(
        ReconciliationRunRequest request,
        CancellationToken ct = default);
}

public sealed class StrategyLedgerReconciliationSourceAdapter : IStrategyLedgerReconciliationSourceAdapter
{
    public ReconciliationLedgerInput? Adapt(StrategyRunDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var ledger = detail.Ledger;
        if (ledger is null)
        {
            return null;
        }

        return new ReconciliationLedgerInput(
            ledger.AsOf,
            ledger.AssetBalance,
            ledger.LiabilityBalance,
            ledger.TrialBalance
                .Select(static line => new ReconciliationLedgerLineInput(line.AccountName, line.Symbol, line.Balance))
                .ToArray());
    }
}

public sealed class StrategyPortfolioReconciliationSourceAdapter : IStrategyPortfolioReconciliationSourceAdapter
{
    public ReconciliationPortfolioInput? Adapt(StrategyRunDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var portfolio = detail.Portfolio;
        if (portfolio is null)
        {
            return null;
        }

        return new ReconciliationPortfolioInput(
            portfolio.AsOf,
            portfolio.Cash,
            portfolio.TotalEquity,
            portfolio.Positions
                .Where(static position => !string.IsNullOrWhiteSpace(position.Symbol))
                .Select(static position => new ReconciliationPositionInput(position.Symbol, position.IsShort))
                .ToArray());
    }
}

public sealed class BankInternalCashReconciliationSourceAdapter : IInternalCashReconciliationSourceAdapter
{
    private readonly IBankTransactionSource? _bankTransactionSource;

    public BankInternalCashReconciliationSourceAdapter(IBankTransactionSource? bankTransactionSource = null)
    {
        _bankTransactionSource = bankTransactionSource;
    }

    public async Task<IReadOnlyList<ReconciliationCashMovementInput>> GetCashMovementsAsync(
        ReconciliationRunRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.BankEntityId.HasValue || _bankTransactionSource is null)
        {
            return Array.Empty<ReconciliationCashMovementInput>();
        }

        var transactions = await _bankTransactionSource
            .GetBankTransactionsAsync(request.BankEntityId.Value, ct)
            .ConfigureAwait(false);

        return transactions
            .Select(static txn => new ReconciliationCashMovementInput(
                MovementId: txn.BankTransactionId.ToString("N"),
                Amount: txn.Amount,
                AsOf: new DateTimeOffset(txn.TransactionDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                IsVoided: txn.IsVoided,
                Source: "bank",
                BankTransaction: txn))
            .ToArray();
    }
}

public sealed class ExternalStatementReconciliationSourceAdapter : IExternalStatementReconciliationSourceAdapter
{
    private readonly IExternalStatementSource _externalStatementSource;

    public ExternalStatementReconciliationSourceAdapter(IExternalStatementSource externalStatementSource)
    {
        _externalStatementSource = externalStatementSource ?? throw new ArgumentNullException(nameof(externalStatementSource));
    }

    public Task<IReadOnlyList<ReconciliationExternalStatementInput>> GetStatementRowsAsync(
        ReconciliationRunRequest request,
        CancellationToken ct = default) =>
        _externalStatementSource.GetStatementRowsAsync(request, ct);
}

public sealed class NullExternalStatementSource : IExternalStatementSource
{
    public Task<IReadOnlyList<ReconciliationExternalStatementInput>> GetStatementRowsAsync(
        ReconciliationRunRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult<IReadOnlyList<ReconciliationExternalStatementInput>>(
            Array.Empty<ReconciliationExternalStatementInput>());
    }
}
