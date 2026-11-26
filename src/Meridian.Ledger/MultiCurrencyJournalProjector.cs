using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>
/// Converts local-currency journal lines into balanced base-currency ledger posting lines.
/// </summary>
/// <remarks>
/// Superseded in production: multi-currency journals post through manual journal workbench drafts whose
/// lines carry the functional amount plus transaction currency, amount and FX rate, and
/// <see cref="LedgerEntry"/> rejects a leg more than one functional minor unit (at least one cent) from transaction amount times
/// rate. This projector rounds each line independently, requires an exact base balance with no rounding line,
/// and drops line dimensions, so it must not become a second posting path.
/// </remarks>
[PostingCalculationNotYetConnected("Superseded, do not connect: multi-currency journals post through manual journal workbench drafts whose lines carry functional amount, transaction currency, amount and FX rate, and LedgerEntry checks each leg against amount times rate; this projector rounds lines independently with no rounding line and drops dimensions, so it would reject valid FX journals and lose dimensional scope.")]
public static class MultiCurrencyJournalProjector
{
    public static MultiCurrencyJournalProjection Project(MultiCurrencyJournalInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var lines = input.Lines
            .Select(line => new MultiCurrencyJournalLineProjection(
                line.Account,
                line.LocalCurrency,
                input.BaseCurrency,
                line.LocalDebit,
                line.LocalCredit,
                line.FxRateToBase,
                RoundCurrency(line.LocalDebit * line.FxRateToBase),
                RoundCurrency(line.LocalCredit * line.FxRateToBase)))
            .ToList();
        var projection = new MultiCurrencyJournalProjection(input, lines);

        if (!projection.IsBaseBalanced)
        {
            throw new LedgerValidationException(
                $"Multi-currency journal entry is not balanced in base currency {input.BaseCurrency}: " +
                $"debits={projection.TotalBaseDebits}, credits={projection.TotalBaseCredits}.");
        }

        return projection;
    }
}
