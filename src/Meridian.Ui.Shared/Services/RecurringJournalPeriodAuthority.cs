using Meridian.Contracts.Api;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed record RecurringJournalPeriodState(string PeriodId, long Version, bool IsOpen,
    string? LockOwner, string? ReopenPath, string? Blocker);

public interface IRecurringJournalPeriodAuthority
{
    Task<RecurringJournalPeriodState> ResolveAsync(RecurringJournalScope scope, DateOnly date, CancellationToken ct);
}

/// <summary>The existing PostgreSQL ledger remains the sole authority for locks and reopens.</summary>
public sealed class RecurringJournalPeriodAuthority(
    ILedgerJournalStore? journalStore = null,
    IFundProfileTenancyRegistry? tenancy = null,
    IRecurringJournalSubjectAuthority? subjects = null) : IRecurringJournalPeriodAuthority
{
    public async Task<RecurringJournalPeriodState> ResolveAsync(RecurringJournalScope scope, DateOnly date, CancellationToken ct)
    {
        if (journalStore is not PostgresLedgerJournalStore durable || tenancy is null)
            throw new InvalidOperationException("Durable ledger period authority is unavailable; recurring generation is blocked.");
        var owner = await tenancy.ResolveAsync(scope.FundProfileId, ct).ConfigureAwait(false);
        if (owner is null || !Same(owner.TenantId, scope.TenantId) || !Same(owner.CompanyId, scope.CompanyId))
            throw new InvalidOperationException("The retained recurring schedule no longer has authoritative fund ownership.");
        var book = await durable.GetLedgerBookAsync(scope.LedgerBookId, ct).ConfigureAwait(false);
        if (book is null || !Same(book.FundProfileId, scope.FundProfileId) || !Same(book.BaseCurrency, scope.Currency))
            throw new InvalidOperationException("The retained recurring book, fund or currency does not match the authoritative ledger.");
        if (subjects is null)
            throw new InvalidOperationException("Durable recurring entity/account ownership authority is unavailable.");
        await subjects.VerifyAsync(scope, book, date, ct).ConfigureAwait(false);
        var periods = await durable.ListPeriodsAsync(ledgerBookId: scope.LedgerBookId, fundProfileId: scope.FundProfileId, ct: ct).ConfigureAwait(false);
        var matching = periods.Where(p => p.LedgerBookId == scope.LedgerBookId && p.StartDate <= date && date <= p.EndDate).ToArray();
        if (matching.Length != 1)
            throw new InvalidOperationException("Exactly one authoritative accounting period must cover the recurring occurrence.");
        var period = matching[0];
        if (Same(period.Status, "Open"))
            return new(period.PeriodId.ToString("D"), period.Version, true, null, null, null);
        var lockOwner = await durable.GetPeriodLockOwnerAsync(period.PeriodId, ct).ConfigureAwait(false)
            ?? "Unattributed retained period lock — controller investigation required";
        return new(period.PeriodId.ToString("D"), period.Version, false, lockOwner,
            UiApiRoutes.LedgerCloseManagementPeriodReopen,
            $"Period '{period.Label}' is {period.Status}; lock owner: {lockOwner}. Request an evidence-backed reopen through Accounting Close.");
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
