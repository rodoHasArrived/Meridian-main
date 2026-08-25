using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// The canonical order of ledger books: by display name, with a stable id tie-break.
/// <para>
/// This lives on the API seam, and the books route applies it before serving, because the order
/// decides which book a freshly opened surface scopes itself to — and therefore which period and
/// whose figures an operator is shown. When each client sorted the store's response for itself,
/// the browser and the desktop could open the same governed ledger on different books.
/// </para>
/// <para>
/// Sorting here rather than in each client is not merely deduplication. The comparison is
/// <see cref="StringComparer.OrdinalIgnoreCase"/> over names and <see cref="Guid"/> order over
/// ids, and neither is reproducible in a browser: JavaScript has no simple-uppercase mapping
/// (<c>toUpperCase</c> applies full case mapping, so it expands "ß" to "SS" and folds
/// "ᾀ" nowhere near .NET's "ᾈ"), its case data is a different Unicode version from the
/// runtime's, and <c>Guid</c> orders by .NET's byte groups rather than by the textual form.
/// A faithful client-side re-implementation was not available to be written; one authoritative
/// ordering, computed once and consumed as sent, is.
/// </para>
/// </summary>
public static class LedgerBookOrdering
{
    /// <inheritdoc cref="LedgerBookOrdering"/>
    public static IReadOnlyList<LedgerBookDto> Sort(IEnumerable<LedgerBookDto>? books)
        => books?
            .OrderBy(book => book.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(book => book.LedgerBookId)
            .ToList() ?? [];
}
