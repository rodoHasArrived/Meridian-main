using System.Globalization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.PortfolioRecords.Accounts;

namespace Meridian.Ui.Shared.Services;

/// <summary>Proves the declared close subject from retained account membership and ledger-book ownership.</summary>
public sealed class CloseReadinessSubjectSource(
    ILedgerBookService? books = null,
    IAccountQueryService? accounts = null) : ICloseReadinessSubjectSource
{
    public async Task<CloseReadinessSubjectDto?> GetSubjectAsync(CloseReadinessScopeDto scope, CancellationToken ct = default)
    {
        if (books is null || accounts is null || scope.LedgerBookId is not { } bookId || scope.FundAccountId is not { } accountId)
            return null;

        var book = await books.GetBookAsync(bookId, ct).ConfigureAwait(false);
        var account = await accounts.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        if (book is null || account is null)
            return null;

        var belongsToBook = book.FundStructureNodeId != Guid.Empty && (book.FundStructureNodeKind switch
        {
            FundStructureNodeKindDto.Account => book.FundStructureNodeId == account.AccountId,
            FundStructureNodeKindDto.Entity => book.FundStructureNodeId == account.EntityId,
            FundStructureNodeKindDto.Fund => book.FundStructureNodeId == account.FundId,
            FundStructureNodeKindDto.Sleeve => book.FundStructureNodeId == account.SleeveId,
            FundStructureNodeKindDto.Vehicle => book.FundStructureNodeId == account.VehicleId,
            _ => false
        });
        var now = DateTimeOffset.UtcNow;
        var bound = book.LedgerBookId == bookId && book.FundProfileId == scope.FundProfileId
            && account.AccountId == accountId && account.IsActive
            && account.EffectiveFrom <= now && (account.EffectiveTo is null || account.EffectiveTo > now)
            && Guid.TryParse(scope.EntityId, out var entityId) && entityId != Guid.Empty
            && account.EntityId == entityId && belongsToBook;
        // Only fields governing membership enter the token. An unrelated balance refresh must
        // not invalidate close, but reassignment/deactivation during evaluation must do so.
        var version = Sha256Digest.ComputeUtf8(string.Join("|",
            book.LedgerBookId, book.FundProfileId, book.FundStructureNodeId, book.FundStructureNodeKind,
            book.UpdatedAt.ToString("O", CultureInfo.InvariantCulture), account.AccountId,
            account.EntityId, account.FundId, account.SleeveId, account.VehicleId, account.IsActive,
            account.EffectiveFrom.ToString("O", CultureInfo.InvariantCulture),
            account.EffectiveTo?.ToString("O", CultureInfo.InvariantCulture)));
        return new(scope, bound ? "Ready" : "ScopeMismatch", now, version,
            [book.LedgerBookId.ToString("D"), account.AccountId.ToString("D"), account.EntityId?.ToString("D") ?? "entity:missing"]);
    }
}
