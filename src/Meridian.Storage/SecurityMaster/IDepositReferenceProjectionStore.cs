using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public interface IDepositReferenceProjectionStore
{
    Task<DepositProjectionRow?> GetDepositAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<DepositProjectionRow>> GetByInstitutionAsync(string institutionName, CancellationToken ct = default);
    Task<IReadOnlyList<DepositProjectionRow>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default);
}

public sealed record DepositProjectionRow(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string DepositType,
    string InstitutionName,
    DateOnly? Maturity,
    decimal? InterestRate,
    string? DayCount,
    bool IsCallable,
    string PrimaryIdentifierValue,
    long Version);
