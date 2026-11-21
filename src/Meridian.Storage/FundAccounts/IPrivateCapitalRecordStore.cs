using Meridian.Contracts.PrivateCapital;

namespace Meridian.Storage.FundAccounts;

/// <summary>An immutable retained record; command receipts share the financial transaction.</summary>
public sealed record PrivateCapitalStoredRecord(string Kind, Guid Id, string Payload);

public interface IPrivateCapitalRecordStore
{
    Task<IReadOnlyList<PrivateCapitalStoredRecord>> ReadAsync(PrivateCapitalScope scope, CancellationToken ct = default);

    /// <summary>Serializes a partition mutation. Returned rows are appended, never replaced or deleted.</summary>
    Task AppendAsync(PrivateCapitalScope scope,
        Func<IReadOnlyList<PrivateCapitalStoredRecord>, IReadOnlyList<PrivateCapitalStoredRecord>> prepare,
        CancellationToken ct = default);
}

public static class PrivateCapitalScopeValidation
{
    public static void Require(PrivateCapitalScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(scope.TenantId) || string.IsNullOrWhiteSpace(scope.CompanyId))
            throw new UnauthorizedAccessException("An authenticated tenant and company are required.");
    }
}
