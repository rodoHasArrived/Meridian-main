using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Produces the certified partners-capital roll-forward for a governed reporting run by hydrating the
/// scoped ledger from the durable journal store and reusing the tested
/// <see cref="LedgerFinancialStatementBuilder.BuildForPeriod"/> computation — the same
/// hydrate-and-build pattern the live capital-account reconciliation resolver uses. Read-only: it
/// posts nothing and mutates no state.
/// </summary>
public sealed class LedgerReportingPartnersCapitalSource : IReportingPartnersCapitalSource
{
    private readonly ILedgerJournalStore? _journalStore;

    public LedgerReportingPartnersCapitalSource(ILedgerJournalStore? journalStore = null)
        => _journalStore = journalStore;

    public async Task<CertifiedPartnersCapitalProjection?> CaptureAsync(
        ReportingRunParametersDto parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (_journalStore is null)
        {
            return null;
        }

        if (parameters.LedgerBook.LedgerBookId is not { } bookId || bookId == Guid.Empty)
        {
            return null;
        }

        var fundProfileId = NormalizeOptional(parameters.Scope.FundProfileId);
        var periods = await _journalStore
            .ListPeriodsAsync(bookId, fundProfileId: fundProfileId, ct: cancellationToken)
            .ConfigureAwait(false);
        var period = periods.FirstOrDefault(candidate => MatchesPeriod(candidate, parameters.PeriodId));
        if (period is null)
        {
            return null;
        }

        var dimensions = new LedgerLineDimensionSet(
            FundId: fundProfileId,
            EntityId: NormalizeOptional(parameters.Scope.EntityId),
            InvestorId: NormalizeOptional(parameters.Scope.InvestorId),
            BookId: bookId.ToString("D"));

        var periodStart = new DateTimeOffset(period.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var asOf = new DateTimeOffset(parameters.AsOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var ledger = await _journalStore
            .HydrateLedgerAsOfAsync(bookId, asOf, dimensions, cancellationToken)
            .ConfigureAwait(false);

        var statements = LedgerFinancialStatementBuilder.BuildForPeriod(
            ledger,
            periodStart,
            asOf,
            chart: null,
            financialAccountId: null,
            lineDimensions: dimensions);

        return statements.PartnersCapital is { } partnersCapital
            ? Map(partnersCapital)
            : null;
    }

    private static CertifiedPartnersCapitalProjection Map(LedgerPartnersCapitalStatement statement)
        => new(
            statement.PeriodStart,
            statement.AsOf,
            statement.BeginningCapital,
            statement.Contributions,
            statement.Distributions,
            statement.AllocatedResult,
            statement.OtherMovements,
            statement.EndingCapital,
            statement.ReconciliationVariance,
            statement.IsReconciled,
            statement.Accounts
                .Select(static account => new CertifiedPartnersCapitalAccount(
                    account.AccountName,
                    NormalizeOptional(account.InvestorId),
                    account.BeginningCapital,
                    account.Contributions,
                    account.Distributions,
                    account.AllocatedResult,
                    account.OtherMovements,
                    account.EndingCapital,
                    account.ReconciliationVariance))
                .ToArray());

    // Mirrors LedgerCapitalAccountReconciliationResolver.MatchesPeriod: a reporting PeriodId string
    // can be the period label, the fiscal "YYYY-NN" token, or the raw period GUID.
    private static bool MatchesPeriod(LedgerAccountingPeriod period, string periodId)
    {
        if (string.IsNullOrWhiteSpace(periodId))
        {
            return false;
        }

        var trimmed = periodId.Trim();
        return string.Equals(period.Label, trimmed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                FormattableString.Invariant($"{period.FiscalYear:D4}-{period.PeriodNo:D2}"),
                trimmed,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(period.PeriodId.ToString("D"), trimmed, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
