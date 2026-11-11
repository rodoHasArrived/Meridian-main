using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>Shared admission rules; storage repeats them so direct callers cannot bypass workflow validation.</summary>
public static class PrivateCapitalAuthorityRules
{
    public static string Id(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 200 || value.Any(char.IsControl))
            throw new ArgumentException($"A nonempty {name} of at most 200 characters is required.", name);
        return value.Trim().ToLowerInvariant();
    }

    public static PrivateCapitalScopeDto NormalizeScope(PrivateCapitalScopeDto scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.LedgerBookId == Guid.Empty)
            throw new ArgumentException("An authoritative ledger book is required.", nameof(scope));
        var currency = scope.Currency?.Trim().ToUpperInvariant();
        if (currency is null || !CurrencyCodeCatalog.IsRecognized(currency))
            throw new ArgumentException("A recognized book currency is required.", nameof(scope));
        if (string.Equals(scope.TenantId?.Trim(), "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scope.CompanyId?.Trim(), "all", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Private-capital authority requires an exact tenant and company.", nameof(scope));
        return scope with
        {
            TenantId = Id(scope.TenantId, nameof(scope.TenantId)),
            CompanyId = Id(scope.CompanyId, nameof(scope.CompanyId)),
            EntityId = Id(scope.EntityId, nameof(scope.EntityId)),
            FundProfileId = Id(scope.FundProfileId, nameof(scope.FundProfileId)),
            Currency = currency
        };
    }

    public static void ValidateCommitment(PrivateCapitalCommitmentDto value)
    {
        ArgumentNullException.ThrowIfNull(value);
        NormalizeScope(value.Scope);
        if (value.CommitmentId != Id(value.CommitmentId, nameof(value.CommitmentId))
            || value.InvestorId != Id(value.InvestorId, nameof(value.InvestorId))
            || value.CapitalAccountId != Id(value.CapitalAccountId, nameof(value.CapitalAccountId)))
            throw new ArgumentException("Commitment, investor, and capital-account identifiers must use their canonical form.");
        Id(value.CreatedBy, nameof(value.CreatedBy));
        if (value.Revision < 1 || value.TotalCommitment <= 0m)
            throw new ArgumentException("A positive revision and commitment total are required.");
        if (value.CommitmentDate == default || value.EffectiveFrom < value.CommitmentDate
            || value.InvestmentPeriodEndDate < value.CommitmentDate)
            throw new ArgumentException("Commitment and effective dates must form a valid term range.");
        if (value.EvidenceLinks is not { Count: > 0 } || value.EvidenceLinks.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Retained commitment evidence is required.");
    }

    public static string LockKey(PrivateCapitalScopeDto scope, string commitmentId)
    {
        scope = NormalizeScope(scope);
        return $"private-capital|{scope.TenantId}|{scope.CompanyId}|{scope.FundProfileId}|{scope.LedgerBookId:D}|{Id(commitmentId, nameof(commitmentId))}";
    }

    /// <summary>Checks every effective revision and issuance date, including approved future reductions.</summary>
    public static string? FindIssuanceTimelineViolation(
        string commitmentId,
        IEnumerable<PrivateCapitalCommitmentDto> revisions,
        IEnumerable<(DateOnly EffectiveDate, decimal Amount)> postedIssuances,
        DateOnly? candidateDate = null,
        decimal candidateAmount = 0m)
    {
        var approved = revisions.Where(x => x.CommitmentId == commitmentId
                && x.ApprovalStatus == PrivateCapitalCommitmentApprovalStatus.Approved)
            .GroupBy(x => x.EffectiveFrom)
            .Select(x => x.MaxBy(revision => revision.Revision)!)
            .OrderBy(x => x.EffectiveFrom).ToArray();
        var issuances = postedIssuances.ToList();
        if (candidateDate is { } date)
        {
            if (candidateAmount <= 0m)
                return "Capital-call issuance requires a positive amount.";
            issuances.Add((date, candidateAmount));
        }
        var checkpoints = approved.Select(x => x.EffectiveFrom)
            .Concat(issuances.Select(x => x.EffectiveDate)).Distinct().Order().ToArray();
        try
        {
            var events = issuances.GroupBy(x => x.EffectiveDate)
                .ToDictionary(x => x.Key, x => x.Sum(item => item.Amount));
            var called = 0m;
            PrivateCapitalCommitmentDto? active = null;
            var nextRevision = 0;
            foreach (var checkpoint in checkpoints)
            {
                while (nextRevision < approved.Length && approved[nextRevision].EffectiveFrom <= checkpoint)
                    active = approved[nextRevision++];
                called = checked(called + events.GetValueOrDefault(checkpoint));
                if (called == 0m)
                    continue;
                if (active is null)
                    return $"Capital-call issuance on {checkpoint:yyyy-MM-dd} has no approved effective commitment authority.";
                if (called > active.TotalCommitment)
                    return FormattableString.Invariant($"Capital called would exceed approved commitment '{commitmentId}' revision {active.Revision} effective {checkpoint:yyyy-MM-dd}: {called} exceeds {active.TotalCommitment}.");
            }
        }
        catch (OverflowException)
        {
            return "Capital-call cumulative amounts exceed supported decimal precision.";
        }
        return null;
    }
}
