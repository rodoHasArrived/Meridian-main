using Meridian.PortfolioRecords.FundAccounts;
using Xunit;

namespace Meridian.Tests.PortfolioRecords.FundAccounts;

/// <summary>
/// Runs the shared fund-account behavioral contract against
/// <see cref="InMemoryFundAccountService"/>. Runs everywhere (no external
/// dependencies); a failure here means the contract itself regressed.
/// </summary>
public sealed class InMemoryFundAccountServiceContractTests : FundAccountServiceContractTests
{
    protected override IFundAccountService CreateService() => new InMemoryFundAccountService();

    [Fact]
    public Task CreateAccountAsync_NewAccount_ReturnsActiveSummaryAndIsReadable() => CreateAccountAsync_NewAccount_ReturnsActiveSummaryAndIsReadable_Core();

    [Fact]
    public Task CreateAccountAsync_DuplicateAccountId_ThrowsInvalidOperationException() => CreateAccountAsync_DuplicateAccountId_ThrowsInvalidOperationException_Core();

    [Fact]
    public Task CreateAccountAsync_NullRequest_ThrowsArgumentNullException() => CreateAccountAsync_NullRequest_ThrowsArgumentNullException_Core();

    [Fact]
    public Task Getters_UnknownAccount_ReturnNullOrEmpty() => Getters_UnknownAccount_ReturnNullOrEmpty_Core();

    [Fact]
    public Task QueryAccountsAsync_ByFundIdActiveOnly_ExcludesDeactivatedAndOtherFunds() => QueryAccountsAsync_ByFundIdActiveOnly_ExcludesDeactivatedAndOtherFunds_Core();

    [Fact]
    public Task QueryAccountsAsync_ByStrategyIdAndRunId_ReturnsExactMatches() => QueryAccountsAsync_ByStrategyIdAndRunId_ReturnsExactMatches_Core();

    [Fact]
    public Task UpdateCustodianDetailsAsync_ActiveAccount_ReplacesAndPersistsDetails() => UpdateCustodianDetailsAsync_ActiveAccount_ReplacesAndPersistsDetails_Core();

    [Fact]
    public Task UpdateCustodianDetailsAsync_UnknownAccount_ReturnsNull() => UpdateCustodianDetailsAsync_UnknownAccount_ReturnsNull_Core();

    [Fact]
    public Task UpdateCustodianDetailsAsync_ClosedAccount_ThrowsAccountStatusPolicyException() => UpdateCustodianDetailsAsync_ClosedAccount_ThrowsAccountStatusPolicyException_Core();

    [Fact]
    public Task UpdateBankDetailsAsync_SuspendedAccount_ThrowsAccountStatusPolicyException() => UpdateBankDetailsAsync_SuspendedAccount_ThrowsAccountStatusPolicyException_Core();

    [Fact]
    public Task UpdateBankDetailsAsync_ActiveBankAccount_ReplacesAndPersistsDetails() => UpdateBankDetailsAsync_ActiveBankAccount_ReplacesAndPersistsDetails_Core();

    [Fact]
    public Task DeactivateAccountAsync_ActiveAccount_SetsInactiveWithEffectiveTo() => DeactivateAccountAsync_ActiveAccount_SetsInactiveWithEffectiveTo_Core();

    [Fact]
    public Task GetFundAccountsAsync_MixedAccountTypes_GroupsByTypeAndExcludesInactive() => GetFundAccountsAsync_MixedAccountTypes_GroupsByTypeAndExcludesInactive_Core();

    [Fact]
    public Task RecordBalanceSnapshotAsync_ActiveAccount_PersistsMoneyFieldsExactly() => RecordBalanceSnapshotAsync_ActiveAccount_PersistsMoneyFieldsExactly_Core();

    [Fact]
    public Task RecordBalanceSnapshotAsync_ClosedAccount_ThrowsAccountStatusPolicyException() => RecordBalanceSnapshotAsync_ClosedAccount_ThrowsAccountStatusPolicyException_Core();

    [Fact]
    public Task GetBalanceHistoryAsync_FromToRange_FiltersInclusivelyNewestFirst() => GetBalanceHistoryAsync_FromToRange_FiltersInclusivelyNewestFirst_Core();

    [Fact]
    public Task GetLatestBalanceSnapshotAsync_MultipleDates_ReturnsLatestAsOfDate() => GetLatestBalanceSnapshotAsync_MultipleDates_ReturnsLatestAsOfDate_Core();

    [Fact]
    public Task IngestCustodianStatementAsync_ActiveAccount_RoundTripsPositionLines() => IngestCustodianStatementAsync_ActiveAccount_RoundTripsPositionLines_Core();

    [Fact]
    public Task IngestCustodianStatementAsync_ReIngestingSameLines_DoesNotDuplicatePositions() => IngestCustodianStatementAsync_ReIngestingSameLines_DoesNotDuplicatePositions_Core();

    [Fact]
    public Task IngestCustodianStatementAsync_SuspendedAccount_IsAllowed() => IngestCustodianStatementAsync_SuspendedAccount_IsAllowed_Core();

    [Fact]
    public Task IngestCustodianStatementAsync_ClosedAccount_ThrowsAccountStatusPolicyException() => IngestCustodianStatementAsync_ClosedAccount_ThrowsAccountStatusPolicyException_Core();

    [Fact]
    public Task IngestBankStatementAsync_ActiveAccount_RoundTripsStatementLines() => IngestBankStatementAsync_ActiveAccount_RoundTripsStatementLines_Core();

    [Fact]
    public Task GetBankStatementLinesAsync_FromToRange_FiltersByTransactionDate() => GetBankStatementLinesAsync_FromToRange_FiltersByTransactionDate_Core();

    [Fact]
    public Task ReconcileAccountAsync_SnapshotAndPositions_ProducesMatchedRun() => ReconcileAccountAsync_SnapshotAndPositions_ProducesMatchedRun_Core();

    [Fact]
    public Task ReconcileAccountAsync_CashDivergingFromBankClosingBalance_ProducesCashBreak() => ReconcileAccountAsync_CashDivergingFromBankClosingBalance_ProducesCashBreak_Core();

    [Fact]
    public Task ReconcileAccountAsync_SnapshotWithoutIndependentEvidence_ReportsUnverifiedNotMatched() => ReconcileAccountAsync_SnapshotWithoutIndependentEvidence_ReportsUnverifiedNotMatched_Core();

    [Fact]
    public Task ReconcileAccountAsync_PositionCountDivergingFromDeclaredLineCount_ProducesPositionBreak() => ReconcileAccountAsync_PositionCountDivergingFromDeclaredLineCount_ProducesPositionBreak_Core();

    [Fact]
    public Task ReconcileAccountAsync_DivergentBrokerageSyncCash_ProducesContinuityBreak() => ReconcileAccountAsync_DivergentBrokerageSyncCash_ProducesContinuityBreak_Core();

    [Fact]
    public Task GetOpenBreaksAsync_ContinuityOnlyBreak_ReturnsNoCashOrPositionBreaks() => GetOpenBreaksAsync_ContinuityOnlyBreak_ReturnsNoCashOrPositionBreaks_Core();

    [Fact]
    public Task RecordSyncHistoryAsync_BlankCapability_ThrowsArgumentException() => RecordSyncHistoryAsync_BlankCapability_ThrowsArgumentException_Core();

    [Fact]
    public Task RecordSyncHistoryAsync_FailedWithoutFailureKind_NormalizesToUnknown() => RecordSyncHistoryAsync_FailedWithoutFailureKind_NormalizesToUnknown_Core();

    [Fact]
    public Task RecordSyncHistoryAsync_SucceededWithFailureKind_ForcesFailureKindNone() => RecordSyncHistoryAsync_SucceededWithFailureKind_ForcesFailureKindNone_Core();

    [Fact]
    public Task RecordSyncHistoryAsync_MessyWarnings_TrimsAndDeduplicates() => RecordSyncHistoryAsync_MessyWarnings_TrimsAndDeduplicates_Core();

    [Fact]
    public Task RecordSyncHistoryAsync_SameCapabilityAndCorrelation_ReusesEntryInsteadOfAppending() => RecordSyncHistoryAsync_SameCapabilityAndCorrelation_ReusesEntryInsteadOfAppending_Core();

    [Fact]
    public Task GetSyncHistoryAsync_CapabilityFilter_MatchesCaseInsensitively() => GetSyncHistoryAsync_CapabilityFilter_MatchesCaseInsensitively_Core();

    [Fact]
    public Task GetLatestSyncHistoryAsync_MultipleAttempts_ReturnsMostRecent() => GetLatestSyncHistoryAsync_MultipleAttempts_ReturnsMostRecent_Core();

    [Fact]
    public Task GetReadinessAsync_HealthyLinkedAccount_IsReadyWithNoIssues() => GetReadinessAsync_HealthyLinkedAccount_IsReadyWithNoIssues_Core();

    [Fact]
    public Task GetReadinessAsync_NoSyncNoLedgerReference_FlagsNeverRunAndLedgerMapping() => GetReadinessAsync_NoSyncNoLedgerReference_FlagsNeverRunAndLedgerMapping_Core();

    [Fact]
    public Task GetReadinessAsync_MarginAccountWithoutSnapshot_FlagsCriticalMarginIssue() => GetReadinessAsync_MarginAccountWithoutSnapshot_FlagsCriticalMarginIssue_Core();

    [Fact]
    public Task RecordMarginSnapshotAsync_LowercaseCurrency_NormalizesToUppercase() => RecordMarginSnapshotAsync_LowercaseCurrency_NormalizesToUppercase_Core();

    [Fact]
    public Task RecordMarginSnapshotAsync_BlankCurrency_ThrowsArgumentException() => RecordMarginSnapshotAsync_BlankCurrency_ThrowsArgumentException_Core();

    [Fact]
    public Task RecordMarginSnapshotAsync_SameCorrelationSameEffectiveAt_ReplacesSnapshot() => RecordMarginSnapshotAsync_SameCorrelationSameEffectiveAt_ReplacesSnapshot_Core();

    [Fact]
    public Task GetLatestMarginSnapshotAsync_MultipleEffectiveTimes_ReturnsLatest() => GetLatestMarginSnapshotAsync_MultipleEffectiveTimes_ReturnsLatest_Core();

    [Fact]
    public Task RecordSyncHistoryAsync_PreCancelledToken_ThrowsOperationCanceled() => RecordSyncHistoryAsync_PreCancelledToken_ThrowsOperationCanceled_Core();

    [Fact]
    public Task RecordMarginSnapshotAsync_PreCancelledToken_ThrowsOperationCanceled() => RecordMarginSnapshotAsync_PreCancelledToken_ThrowsOperationCanceled_Core();
}
