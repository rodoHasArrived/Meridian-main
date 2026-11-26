using Meridian.Contracts.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Structural and historical invariants shared by retained account mapping stores.</summary>
public static class IntercompanyAccountBindingRules
{
    public static IReadOnlyList<IntercompanyAccountBindingDto> Resolve(
        IEnumerable<IntercompanyAccountBindingDto> history, DateOnly asOf)
        => history.Where(x => x.EffectiveFrom <= asOf)
            .GroupBy(x => x.BindingId)
            .Select(x => x.OrderByDescending(y => y.RevisionNumber).First())
            .Where(x => x.EffectiveTo is null || asOf < x.EffectiveTo)
            .OrderBy(x => x.ChartAccountNodeId, StringComparer.Ordinal)
            .ToArray();

    public static void ValidateAppend(IntercompanyAccountBindingDto revision,
        Guid? expectedPreviousRevisionId, IReadOnlyList<IntercompanyAccountBindingDto> history)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(history);
        if (revision.BindingId == Guid.Empty || revision.RevisionId == Guid.Empty || revision.LedgerBookId == Guid.Empty)
            throw new InvalidOperationException("Account bindings require nonempty binding, revision and book identities.");
        foreach (var value in new[] { revision.ChartAccountNodeId, revision.AccountPath, revision.AccountName,
                     revision.AccountType, revision.TenantId, revision.RecordedBy, revision.Reason })
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                throw new InvalidOperationException("Account binding identity, account, tenant and audit fields require trimmed, nonblank text.");
        if (!Enum.IsDefined(revision.Role) || !IsCompatible(revision.Role, revision.AccountType))
            throw new InvalidOperationException("The intercompany account role is incompatible with its ledger account type.");
        if (revision.TenantId.Equals("all", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Account bindings require a concrete owning tenant.");
        if (revision.EffectiveTo is { } end && end <= revision.EffectiveFrom)
            throw new InvalidOperationException("Account binding dates must form a nonempty half-open effective window.");
        if (revision.HistoricalNames is null || revision.HistoricalNames.Any(string.IsNullOrWhiteSpace)
            || revision.HistoricalNames.Any(x => x != x.Trim())
            || revision.HistoricalNames.Distinct(StringComparer.Ordinal).Count() != revision.HistoricalNames.Count)
            throw new InvalidOperationException("Historical account names must be nonblank, trimmed and unique.");
        if (revision.RecordedAt == default)
            throw new InvalidOperationException("Account bindings require a recorded timestamp.");
        if (history.Any(x => x.LedgerBookId != revision.LedgerBookId
            || !string.Equals(x.TenantId, revision.TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(x.CompanyId, revision.CompanyId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Account binding history must be scoped to the same owning book, tenant and company.");
        var current = history.Where(x => x.BindingId == revision.BindingId)
            .OrderByDescending(x => x.RevisionNumber).FirstOrDefault();
        if (current?.RevisionId != expectedPreviousRevisionId || revision.PreviousRevisionId != expectedPreviousRevisionId
            || revision.RevisionNumber != (current?.RevisionNumber ?? 0) + 1)
            throw new InvalidOperationException("The account binding revision changed; reload before appending a successor.");
        if (current is not null)
        {
            if (current.ChartAccountNodeId != revision.ChartAccountNodeId || current.Role != revision.Role
                || current.AccountType != revision.AccountType)
                throw new InvalidOperationException("A retained binding cannot change its stable chart identity, role or account type.");
            if (revision.EffectiveFrom < current.EffectiveFrom)
                throw new InvalidOperationException("A binding successor cannot precede its retained effective date.");
            var retainedNames = current.HistoricalNames.Append(current.AccountName);
            if (retainedNames.Except(revision.HistoricalNames.Append(revision.AccountName), StringComparer.Ordinal).Any())
                throw new InvalidOperationException("A renamed binding must retain all previous account names for immutable journal history.");
        }
        var names = revision.HistoricalNames.Append(revision.AccountName).ToHashSet(StringComparer.Ordinal);
        if (history.Any(x => x.BindingId != revision.BindingId
            && (x.ChartAccountNodeId == revision.ChartAccountNodeId
                || x.HistoricalNames.Append(x.AccountName).Any(names.Contains))))
            throw new InvalidOperationException("A stable chart node or historical account name cannot identify more than one binding in a book.");
    }

    public static bool IsCompatible(IntercompanyAccountRoleDto role, string accountType)
        => (role, accountType) switch
        {
            (IntercompanyAccountRoleDto.Receivable, "Asset") => true,
            (IntercompanyAccountRoleDto.Payable, "Liability") => true,
            (IntercompanyAccountRoleDto.Revenue, "Revenue") => true,
            (IntercompanyAccountRoleDto.Expense, "Expense") => true,
            (IntercompanyAccountRoleDto.TranslationReserve, "Equity") => true,
            _ => false
        };
}
