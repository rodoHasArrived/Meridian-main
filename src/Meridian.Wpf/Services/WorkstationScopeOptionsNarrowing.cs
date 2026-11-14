using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Services;

/// <summary>
/// The entity, fund and account choices left once an entity and/or fund is picked.
/// </summary>
/// <param name="Entities">Every visible entity (entities are never narrowed).</param>
/// <param name="Funds">The funds left for the chosen entity, or every fund.</param>
/// <param name="Accounts">The accounts left for the chosen entity and fund, or every account.</param>
/// <param name="FundsConstrained">True when a chosen entity narrowed the fund list.</param>
/// <param name="AccountsConstrained">True when a chosen entity or fund narrowed the account list.</param>
/// <param name="AccountsWithoutKnownRelationship">
/// Accounts left out of a narrowed list because the data does not say which fund or entity they
/// belong to.
/// </param>
public sealed record WorkstationScopeNarrowedOptions(
    IReadOnlyList<WorkstationScopeEntityOptionDto> Entities,
    IReadOnlyList<WorkstationScopeFundOptionDto> Funds,
    IReadOnlyList<WorkstationScopeAccountOptionDto> Accounts,
    bool FundsConstrained,
    bool AccountsConstrained,
    int AccountsWithoutKnownRelationship);

/// <summary>
/// The desktop port of the browser scope picker's narrowing rule
/// (<c>constrainScopeOptions</c> in <c>src/Meridian.Ui/dashboard/src/lib/operating-scope/structure-options.ts</c>),
/// applied to <see cref="WorkstationScopeOptionsDto"/> so both clients narrow the same payload the
/// same way:
/// <list type="bullet">
/// <item>an entity keeps the funds that hold it (<c>funds[].entityIds</c>) or that own an account
/// booked to it;</item>
/// <item>an entity keeps the accounts booked to it, and accounts with no entity of their own whose
/// fund holds it;</item>
/// <item>a fund keeps its own accounts (<c>accounts[].fundId</c>).</item>
/// </list>
/// When the options are unavailable nothing is narrowed. Otherwise an account whose fund (or
/// entity) the data does not state is left out of a narrowed list and counted, never guessed.
/// </summary>
public static class WorkstationScopeOptionsNarrowing
{
    public static WorkstationScopeNarrowedOptions Constrain(
        WorkstationScopeOptionsDto options,
        bool optionsAvailable,
        Guid? entityId,
        Guid? fundId)
    {
        ArgumentNullException.ThrowIfNull(options);

        var entity = Normalize(entityId);
        var fund = Normalize(fundId);
        if (!optionsAvailable || (entity is null && fund is null))
        {
            return new WorkstationScopeNarrowedOptions(
                options.Entities,
                options.Funds,
                options.Accounts,
                FundsConstrained: false,
                AccountsConstrained: false,
                AccountsWithoutKnownRelationship: 0);
        }

        var fundsForEntity = entity is { } chosenEntity ? FundIdsForEntity(options, chosenEntity) : null;
        IReadOnlyList<WorkstationScopeFundOptionDto> funds = fundsForEntity is null
            ? options.Funds
            : options.Funds.Where(candidate => fundsForEntity.Contains(candidate.FundId)).ToArray();

        var withoutKnownRelationship = 0;
        var accounts = new List<WorkstationScopeAccountOptionDto>();
        foreach (var account in options.Accounts)
        {
            var accountFund = Normalize(account.FundId);
            var accountEntity = Normalize(account.EntityId);

            if (fund is { } chosenFund)
            {
                if (accountFund is null)
                {
                    withoutKnownRelationship++;
                    continue;
                }

                if (accountFund.Value != chosenFund)
                {
                    continue;
                }
            }

            if (entity is { } chosen)
            {
                if (accountEntity is { } bookedEntity)
                {
                    if (bookedEntity == chosen)
                    {
                        accounts.Add(account);
                    }

                    continue;
                }

                if (accountFund is { } heldBy)
                {
                    if (fundsForEntity!.Contains(heldBy))
                    {
                        accounts.Add(account);
                    }

                    continue;
                }

                if (fund is null)
                {
                    withoutKnownRelationship++;
                }

                continue;
            }

            accounts.Add(account);
        }

        return new WorkstationScopeNarrowedOptions(
            options.Entities,
            funds,
            accounts,
            FundsConstrained: entity is not null,
            AccountsConstrained: true,
            AccountsWithoutKnownRelationship: withoutKnownRelationship);
    }

    /// <summary>
    /// Whether a picked fund is known not to hold the picked entity (so choosing the entity should
    /// clear it). An unknown fund or unavailable options are never treated as a conflict.
    /// </summary>
    public static bool FundConflictsWithEntity(
        WorkstationScopeOptionsDto options,
        bool optionsAvailable,
        Guid? fundId,
        Guid? entityId)
    {
        ArgumentNullException.ThrowIfNull(options);

        var fund = Normalize(fundId);
        var entity = Normalize(entityId);
        if (!optionsAvailable || fund is null || entity is null)
        {
            return false;
        }

        if (!options.Funds.Any(candidate => candidate.FundId == fund.Value))
        {
            return false;
        }

        return !FundIdsForEntity(options, entity.Value).Contains(fund.Value);
    }

    /// <summary>
    /// Whether a picked account is known to sit outside the picked fund or entity (so it should be
    /// cleared). An account whose relationships the data does not state is never a conflict.
    /// </summary>
    public static bool AccountConflictsWithScope(
        WorkstationScopeOptionsDto options,
        bool optionsAvailable,
        Guid? accountId,
        Guid? entityId,
        Guid? fundId)
    {
        ArgumentNullException.ThrowIfNull(options);

        var id = Normalize(accountId);
        if (!optionsAvailable || id is null)
        {
            return false;
        }

        var account = options.Accounts.FirstOrDefault(candidate => candidate.AccountId == id.Value);
        if (account is null || (Normalize(account.FundId) is null && Normalize(account.EntityId) is null))
        {
            return false;
        }

        return !Constrain(options, optionsAvailable, entityId, fundId)
            .Accounts
            .Any(candidate => candidate.AccountId == id.Value);
    }

    private static HashSet<Guid> FundIdsForEntity(WorkstationScopeOptionsDto options, Guid entityId)
    {
        var ids = new HashSet<Guid>();
        foreach (var fund in options.Funds)
        {
            if (fund.EntityIds is not null && fund.EntityIds.Contains(entityId))
            {
                ids.Add(fund.FundId);
            }
        }

        foreach (var account in options.Accounts)
        {
            if (Normalize(account.FundId) is { } accountFund && Normalize(account.EntityId) == entityId)
            {
                ids.Add(accountFund);
            }
        }

        return ids;
    }

    private static Guid? Normalize(Guid? id) => id is { } value && value != Guid.Empty ? value : null;
}
