using Meridian.Contracts.FundStructure;

namespace Meridian.PortfolioRecords.FundAccounts;

/// <summary>Account and sync status gates shared by local and PostgreSQL readiness projections.</summary>
internal static class AccountReadinessChecks
{
    internal static IEnumerable<AccountReadinessIssueDto> BuildStatusIssues(
        AccountSummaryDto account,
        AccountSyncHistoryEntryDto? latestSync)
    {
        if (!account.IsActive || account.OperationalStatus != AccountOperationalStatusDto.Active)
        {
            yield return new AccountReadinessIssueDto(
                "account.operational_status.unavailable",
                AccountReadinessSeverityDto.Critical,
                "Account is unavailable",
                !account.IsActive ? "Account has been deactivated." : $"Account status is {account.OperationalStatus}.",
                account.AccountId,
                SuggestedAction: "Review account operational status before using it for downstream workflows.");
        }

        if (latestSync is { Status: AccountSyncStatusDto.Pending or AccountSyncStatusDto.Cancelled or AccountSyncStatusDto.Degraded })
        {
            yield return new AccountReadinessIssueDto(
                $"account.sync.{latestSync.Status.ToString().ToLowerInvariant()}",
                AccountReadinessSeverityDto.Warning,
                $"Latest account sync is {latestSync.Status.ToString().ToLowerInvariant()}",
                $"Latest {latestSync.Capability} sync has not supplied a complete successful result.",
                account.AccountId,
                latestSync.ProviderId,
                latestSync.ExternalAccountId,
                latestSync.Capability,
                "Complete or retry account sync and review retained evidence before accepting readiness.",
                latestSync.RawEvidencePath ?? latestSync.ProjectionEvidencePath);
        }

        if (latestSync is { ProviderLinkStatus: AccountProviderLinkStatusDto.Degraded
            or AccountProviderLinkStatusDto.SyncPending or AccountProviderLinkStatusDto.SyncFailed })
        {
            yield return new AccountReadinessIssueDto(
                "account.provider_link.sync_unavailable",
                latestSync.ProviderLinkStatus == AccountProviderLinkStatusDto.SyncFailed
                    ? AccountReadinessSeverityDto.Critical : AccountReadinessSeverityDto.Warning,
                "Provider link sync is unavailable",
                $"Provider link status is {latestSync.ProviderLinkStatus}.",
                account.AccountId,
                latestSync.ProviderId,
                latestSync.ExternalAccountId,
                latestSync.Capability,
                "Review provider connectivity and complete a successful sync before accepting readiness.",
                latestSync.RawEvidencePath ?? latestSync.ProjectionEvidencePath);
        }
    }
}
