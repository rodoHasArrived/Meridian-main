using Meridian.Contracts.AccountingSystem;

namespace Meridian.ProviderSdk.AccountingSystem;

public interface IAccountingSystemProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    AccountingSystemProviderCapabilities Capabilities { get; }

    Task<AccountingSystemImportDetailDto> ImportAsync(
        AccountingSystemImportRequestDto request,
        CancellationToken ct = default);
}

public interface IAccountingSystemConnectionMetadataProvider
{
    Task<AccountingSystemConnectionMetadataDto> GetConnectionMetadataAsync(CancellationToken ct = default);
}

public interface IAccountingSystemConnectionVerifier
{
    Task<AccountingSystemConnectionVerificationResult> VerifyConnectionAsync(CancellationToken ct = default);
}

public sealed record AccountingSystemProviderCapabilities(
    bool SupportsChartOfAccounts,
    bool SupportsJournalEntries,
    bool SupportsTrialBalance,
    bool SupportsPosting,
    IReadOnlyList<string> EvidenceKinds,
    bool RequiresCredentials = false);

public sealed record AccountingSystemConnectionVerificationResult(
    bool Success,
    string? ExternalCompanyId,
    string? LastError,
    DateTimeOffset? VerifiedAtUtc,
    IReadOnlyList<string> Warnings);
