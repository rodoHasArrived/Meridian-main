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

public sealed record AccountingSystemProviderCapabilities(
    bool SupportsChartOfAccounts,
    bool SupportsJournalEntries,
    bool SupportsTrialBalance,
    bool SupportsPosting,
    IReadOnlyList<string> EvidenceKinds);
