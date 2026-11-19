using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

public partial interface IWorkstationIntercompanyAgreementApiClient
{
    /// <summary>
    /// Ledger books visible to the signed-in account, read from the same route the browser workstation uses.
    /// It only populates selection lists; every command still authorizes the chosen book on the server.
    /// </summary>
    Task<ApiResponse<LedgerBookDto[]>> GetLedgerBooksAsync(CancellationToken ct = default)
        => Task.FromResult(ApiResponse<LedgerBookDto[]>.Fail("Ledger books are unavailable.", 503));
}

public sealed partial class WorkstationIntercompanyAgreementApiClient
{
    public async Task<ApiResponse<LedgerBookDto[]>> GetLedgerBooksAsync(CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<LedgerBookDto[]>(UiApiRoutes.LedgerBooks, ct).ConfigureAwait(false);
        return response.Success && response.Data is null ? ApiResponse<LedgerBookDto[]>.Ok([]) : response;
    }
}

/// <summary>
/// One selectable identifier with a readable label. The selection list is a convenience over shared reads:
/// the bound value is the identifier the command sends, and the server remains the authority for it.
/// </summary>
public sealed record IntercompanyChoice(string Id, string Label)
{
    public override string ToString() => Label;

    internal static IntercompanyChoice Book(LedgerBookDto book)
        => new(book.LedgerBookId.ToString("D"), $"{book.DisplayName} · {book.BaseCurrency} · {book.LedgerBookId:D}");

    internal static IntercompanyChoice Journal(IntercompanyCounterpartCandidateDto candidate)
        => new(candidate.JournalEntryId.ToString("D"),
            $"{candidate.EffectiveDate:yyyy-MM-dd} · {candidate.Description} · {IntercompanyExactAmount.Text(candidate.Amount, candidate.Currency)} · {candidate.JournalEntryId:D}");

    internal static IntercompanyChoice Profile(ResolvedIntercompanyAccountingArrangementDto profile)
        => new(profile.ArrangementId.ToString("D"),
            $"{profile.ArrangementReference} · {profile.TemplateKind} · {profile.Currency} · legal v{profile.CanonicalAgreementVersion} · {profile.ArrangementId:D}");

    /// <summary>Replaces the list contents, ordered by label so operators find the same entry in the same place.</summary>
    internal static void Replace(System.Collections.ObjectModel.ObservableCollection<IntercompanyChoice> target, IEnumerable<IntercompanyChoice> items)
    {
        target.Clear();
        foreach (var item in items.OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase))
            target.Add(item);
    }
}
