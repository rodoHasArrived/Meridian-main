using Meridian.Contracts.Accounting.Lots;

namespace Meridian.Ledger;

/// <summary>
/// The one rule for which account type carries an open lot: long lots are assets, short lots are
/// proceeds liabilities. Storage admission and projectors consult this rule rather than restating it.
/// </summary>
public static class OpenLotCarryingAccounts
{
    public static LedgerAccountType RequiredAccountType(OpenLotDirection direction)
        => direction == OpenLotDirection.Short ? LedgerAccountType.Liability : LedgerAccountType.Asset;

    public static bool Matches(LedgerAccount account, OpenLotDirection direction)
    {
        ArgumentNullException.ThrowIfNull(account);
        return account.AccountType == RequiredAccountType(direction);
    }
}
