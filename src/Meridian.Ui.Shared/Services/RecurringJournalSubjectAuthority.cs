using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public interface IRecurringJournalSubjectAuthority
{
    Task VerifyAsync(RecurringJournalScope scope, LedgerBookRecord book, DateOnly date, CancellationToken ct);
}

/// <summary>Resolves entity/account membership from retained structure rather than caller labels.</summary>
public sealed class RecurringJournalSubjectAuthority(
    IFundStructureStore? structure = null,
    IFundAccountStore? accounts = null,
    TimeProvider? clock = null) : IRecurringJournalSubjectAuthority
{
    public async Task VerifyAsync(RecurringJournalScope scope, LedgerBookRecord book, DateOnly date, CancellationToken ct)
    {
        if (structure is not PostgresFundStructureStore durable)
            throw new InvalidOperationException("Durable recurring journal entity ownership is unavailable.");
        if (!Guid.TryParse(scope.EntityId, out var entityId) || entityId == Guid.Empty)
            throw new InvalidOperationException("The recurring journal requires a retained legal-entity identity.");
        var entity = await durable.GetLegalEntityAsync(entityId, ct).ConfigureAwait(false);
        var tenants = await durable.GetNodeTenantsAsync(ct).ConfigureAwait(false);
        FundSummaryDto? fund = null;
        SleeveSummaryDto? sleeve = null;
        VehicleSummaryDto? vehicle = null;
        if (book.FundStructureNodeKind == FundStructureNodeKindDto.Fund)
            fund = await durable.GetFundAsync(book.FundStructureNodeId, ct).ConfigureAwait(false);
        if (book.FundStructureNodeKind == FundStructureNodeKindDto.Sleeve)
            sleeve = await durable.GetSleeveAsync(book.FundStructureNodeId, ct).ConfigureAwait(false);
        if (book.FundStructureNodeKind == FundStructureNodeKindDto.Vehicle)
            vehicle = await durable.GetVehicleAsync(book.FundStructureNodeId, ct).ConfigureAwait(false);

        AccountSummaryDto? account = null;
        if (scope.FundAccountId is not null)
        {
            if (accounts is not PostgresFundAccountStore accountStore ||
                !Guid.TryParse(scope.FundAccountId, out var accountId) || accountId == Guid.Empty)
                throw new InvalidOperationException("Durable recurring journal account ownership is unavailable or invalid.");
            account = await accountStore.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        }
        ValidateSubject(scope, book, entity, fund, sleeve, vehicle, account, tenants,
            (clock ?? TimeProvider.System).GetUtcNow());
    }

    internal static void ValidateSubject(RecurringJournalScope scope, LedgerBookRecord book,
        LegalEntitySummaryDto? entity, FundSummaryDto? fund, SleeveSummaryDto? sleeve,
        VehicleSummaryDto? vehicle, AccountSummaryDto? account, FundStructureTenantMap tenants,
        DateTimeOffset now)
    {
        if (!Guid.TryParse(scope.EntityId, out var entityId) || entityId == Guid.Empty || entity is null ||
            entity.EntityId != entityId || !Active(entity.IsActive, entity.EffectiveFrom, entity.EffectiveTo, now) ||
            !Owned(entityId) || !Owned(book.FundStructureNodeId) || book.LedgerBookId != scope.LedgerBookId ||
            !string.Equals(book.FundProfileId, scope.FundProfileId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The recurring journal entity or book has no active, tenant-owned retained membership.");

        var bound = book.FundStructureNodeKind switch
        {
            FundStructureNodeKindDto.Entity => book.FundStructureNodeId == entityId,
            FundStructureNodeKindDto.Fund => fund is not null && fund.FundId == book.FundStructureNodeId &&
                Active(fund.IsActive, fund.EffectiveFrom, fund.EffectiveTo, now) && fund.EntityIds.Contains(entityId),
            FundStructureNodeKindDto.Vehicle => vehicle is not null && vehicle.VehicleId == book.FundStructureNodeId &&
                Active(vehicle.IsActive, vehicle.EffectiveFrom, vehicle.EffectiveTo, now) && vehicle.LegalEntityId == entityId,
            FundStructureNodeKindDto.Sleeve => sleeve is not null && sleeve.SleeveId == book.FundStructureNodeId &&
                Active(sleeve.IsActive, sleeve.EffectiveFrom, sleeve.EffectiveTo, now) && account?.SleeveId == sleeve.SleeveId,
            FundStructureNodeKindDto.Account => account is not null && account.AccountId == book.FundStructureNodeId,
            _ => false
        };
        if (!bound)
            throw new InvalidOperationException("The recurring journal entity is not bound to the retained ledger-book owner.");

        if (scope.FundAccountId is not null || book.FundStructureNodeKind is FundStructureNodeKindDto.Account or FundStructureNodeKindDto.Sleeve)
        {
            if (!Guid.TryParse(scope.FundAccountId, out var accountId) || accountId == Guid.Empty || account is null ||
                account.AccountId != accountId || account.EntityId != entityId || !Owned(accountId) ||
                !Active(account.IsActive, account.EffectiveFrom, account.EffectiveTo, now))
                throw new InvalidOperationException("The recurring journal account lacks active, tenant-owned entity membership.");
            var accountBelongsToBook = book.FundStructureNodeKind switch
            {
                FundStructureNodeKindDto.Account => account.AccountId == book.FundStructureNodeId,
                FundStructureNodeKindDto.Entity => account.EntityId == book.FundStructureNodeId,
                FundStructureNodeKindDto.Fund => account.FundId == book.FundStructureNodeId,
                FundStructureNodeKindDto.Sleeve => account.SleeveId == book.FundStructureNodeId,
                FundStructureNodeKindDto.Vehicle => account.VehicleId == book.FundStructureNodeId,
                _ => false
            };
            if (!accountBelongsToBook)
                throw new InvalidOperationException("The recurring journal account belongs to a different ledger-book owner.");
        }

        bool Owned(Guid id) => tenants.IsPartitioned && tenants.NodeTenants.TryGetValue(id, out var tenant)
            && string.Equals(tenant, scope.TenantId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Active(bool active, DateTimeOffset from, DateTimeOffset? to, DateTimeOffset now)
        => active && from <= now && (to is null || now < to);
}
