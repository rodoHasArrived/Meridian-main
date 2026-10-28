using Meridian.Contracts.AccountingSystem;

namespace Meridian.ProviderSdk.AccountingSystem;

/// <summary>
/// Defines provider capabilities and canonical imports from an external accounting system.
/// </summary>
public interface IAccountingSystemProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    AccountingSystemProviderCapabilities Capabilities { get; }

    Task<AccountingSystemImportDetailDto> ImportAsync(
        AccountingSystemImportRequestDto request,
        CancellationToken ct = default);
}

/// <summary>
/// Retrieves metadata describing the external accounting-system connection.
/// </summary>
public interface IAccountingSystemConnectionMetadataProvider
{
    Task<AccountingSystemConnectionMetadataDto> GetConnectionMetadataAsync(CancellationToken ct = default);
}

/// <summary>
/// Verifies an external accounting-system connection and returns its observed verification result.
/// </summary>
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
    IReadOnlyList<string> Warnings)
{
    /// <summary>Opaque generation that must still be current when recording this result.</summary>
    public string? ExpectedCredentialGeneration { get; init; }
}
