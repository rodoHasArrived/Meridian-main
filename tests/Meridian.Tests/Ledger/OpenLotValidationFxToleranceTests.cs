using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;

namespace Meridian.Tests.Ledger;

/// <summary>
/// The acquisition FX reconciliation in <see cref="OpenLotValidation"/> allows one minor unit of
/// the functional currency, never less than 0.01: a basis rounded to whole yen can sit up to a yen
/// away from the unrounded transaction basis times the acquisition FX rate.
/// </summary>
public sealed class OpenLotValidationFxToleranceTests
{
    private const string ReconciliationRefusal = "*does not reconcile to acquisition FX*";

    [Theory]
    // USD 1,234.56 at 151.37 is JPY 186,875.3472; the retained basis is whole yen.
    [InlineData("JPY", 1234.56, 151.37, 186875)]
    [InlineData("JPY", 1234.56, 151.37, 186876)]
    // Exactly one yen away is the edge of the tolerance.
    [InlineData("JPY", 100, 150, 15001)]
    [InlineData("JPY", 100, 150, 14999)]
    // USD 1,234.56 at 1,387.45 is KRW 1,712,890.272.
    [InlineData("KRW", 1234.56, 1387.45, 1712890)]
    public void ZeroDecimalFunctionalBasis_WithinOneMinorUnit_IsAccepted(
        string functionalCurrency, decimal transactionBasis, decimal fxRate, decimal functionalBasis)
    {
        var lot = CrossCurrencyLot("USD", functionalCurrency, transactionBasis, fxRate, functionalBasis);

        var validate = () => OpenLotValidation.Validate(lot);

        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData("JPY", 100, 150, 15001.01)]
    [InlineData("JPY", 100, 150, 14998.99)]
    [InlineData("JPY", 100, 150, 15002)]
    [InlineData("JPY", 1234.56, 151.37, 186877)]
    [InlineData("JPY", 1234.56, 151.37, 186874)]
    [InlineData("KRW", 1234.56, 1387.45, 1712892)]
    public void ZeroDecimalFunctionalBasis_BeyondOneMinorUnit_IsStillRejected(
        string functionalCurrency, decimal transactionBasis, decimal fxRate, decimal functionalBasis)
    {
        var lot = CrossCurrencyLot("USD", functionalCurrency, transactionBasis, fxRate, functionalBasis);

        var validate = () => OpenLotValidation.Validate(lot);

        validate.Should().Throw<ArgumentException>().WithMessage(ReconciliationRefusal);
    }

    [Theory]
    // EUR 1,000 at 1.1 is USD 1,100 exactly: one cent either way is accepted, more is not.
    [InlineData("USD", 1100.01, true)]
    [InlineData("USD", 1099.99, true)]
    [InlineData("USD", 1100.011, false)]
    [InlineData("USD", 1099.989, false)]
    [InlineData("USD", 1100.02, false)]
    // Three-decimal and unlisted currencies keep the 0.01 floor.
    [InlineData("KWD", 1100.01, true)]
    [InlineData("KWD", 1100.011, false)]
    [InlineData("XXX", 1100.01, true)]
    [InlineData("XXX", 1100.011, false)]
    public void FractionalFunctionalBasis_KeepsTheOneCentBoundary(
        string functionalCurrency, decimal functionalBasis, bool accepted)
    {
        var lot = CrossCurrencyLot("EUR", functionalCurrency, 1000m, 1.1m, functionalBasis);

        var validate = () => OpenLotValidation.Validate(lot);

        if (accepted)
            validate.Should().NotThrow();
        else
            validate.Should().Throw<ArgumentException>().WithMessage(ReconciliationRefusal);
    }

    private static OpenLotDto CrossCurrencyLot(
        string acquisitionCurrency, string functionalCurrency, decimal transactionBasis, decimal fxRate, decimal functionalBasis)
    {
        var lot = OpenLotConvergenceTests.Lot(1).ToOpenLot();
        return lot with
        {
            OpenTransactionCostBasis = transactionBasis,
            OpenFunctionalCostBasis = functionalBasis,
            Acquisition = lot.Acquisition with
            {
                AcquisitionCurrency = acquisitionCurrency,
                FunctionalCurrency = functionalCurrency,
                AcquisitionFxRateToFunctional = fxRate,
                TransactionCostBasis = transactionBasis,
                FunctionalCostBasis = functionalBasis
            }
        };
    }
}
