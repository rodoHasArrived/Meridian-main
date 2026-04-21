using Meridian.Execution.Sdk;

namespace Meridian.Execution.TaxLotAccounting;

/// <summary>
/// Selects which open lots to relieve when a closing trade occurs.
/// Implementations encode cost-basis identification methods (FIFO, LIFO, HIFO, SpecificId).
/// </summary>
public interface ITaxLotSelector
{
    /// <summary>The accounting method this selector encodes.</summary>
    TaxLotAccountingMethod Method { get; }

    /// <summary>
    /// Selects lots from <paramref name="openLots"/> to satisfy a closing trade of
    /// <paramref name="quantityToClose"/> shares/contracts at <paramref name="closePrice"/>.
    /// </summary>
    /// <param name="openLots">All currently open lots for the symbol being closed.</param>
    /// <param name="quantityToClose">Unsigned number of shares/contracts to close.</param>
    /// <param name="closePrice">Execution price of the closing trade.</param>
    /// <returns>
    ///     A <see cref="TaxLotReliefResult"/> listing which lots were consumed and the
    ///     updated remaining lot list.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     When <paramref name="quantityToClose"/> exceeds the aggregate open quantity.
    /// </exception>
    TaxLotReliefResult Relieve(
        IReadOnlyList<TaxLot> openLots,
        long quantityToClose,
        decimal closePrice);
}
