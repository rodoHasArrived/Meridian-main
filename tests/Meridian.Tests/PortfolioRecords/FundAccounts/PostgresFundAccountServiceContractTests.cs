using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.FundAccounts;
using Meridian.Tests.Storage.FundAccounts;
using Xunit;

namespace Meridian.Tests.PortfolioRecords.FundAccounts;

/// <summary>
/// Runs the shared fund-account behavioral contract against
/// <see cref="PostgresFundAccountService"/> backed by the real
/// <see cref="PostgresFundAccountStore"/> and PostgreSQL schema — the production
/// persistence path that previously had only DI-registration coverage. Skips at
/// discovery time when Docker and MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING are both
/// unavailable.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgresFundAccountServiceContractTests : FundAccountServiceContractTests, IClassFixture<FundAccountDatabaseFixture>
{
    private readonly FundAccountDatabaseFixture _fixture;

    public PostgresFundAccountServiceContractTests(FundAccountDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    protected override IFundAccountService CreateService() =>
        new PostgresFundAccountService(new PostgresFundAccountStore(_fixture.Options));

    [FundAccountDatabaseFact]
    public Task CreateAccountAsync_NewAccount_ReturnsActiveSummaryAndIsReadable() => CreateAccountAsync_NewAccount_ReturnsActiveSummaryAndIsReadable_Core();

    [FundAccountDatabaseFact]
    public Task CreateAccountAsync_DuplicateAccountId_ThrowsInvalidOperationException() => CreateAccountAsync_DuplicateAccountId_ThrowsInvalidOperationException_Core();

    [FundAccountDatabaseFact]
    public Task CreateAccountAsync_NullRequest_ThrowsArgumentNullException() => CreateAccountAsync_NullRequest_ThrowsArgumentNullException_Core();

    [FundAccountDatabaseFact]
    public Task Getters_UnknownAccount_ReturnNullOrEmpty() => Getters_UnknownAccount_ReturnNullOrEmpty_Core();

    [FundAccountDatabaseFact]
    public Task QueryAccountsAsync_ByFundIdActiveOnly_ExcludesDeactivatedAndOtherFunds() => QueryAccountsAsync_ByFundIdActiveOnly_ExcludesDeactivatedAndOtherFunds_Core();

    [FundAccountDatabaseFact]
    public Task QueryAccountsAsync_ByStrategyIdAndRunId_ReturnsExactMatches() => QueryAccountsAsync_ByStrategyIdAndRunId_ReturnsExactMatches_Core();

    [FundAccountDatabaseFact]
    public Task UpdateCustodianDetailsAsync_ActiveAccount_ReplacesAndPersistsDetails() => UpdateCustodianDetailsAsync_ActiveAccount_ReplacesAndPersistsDetails_Core();

    [FundAccountDatabaseFact]
    public Task UpdateCustodianDetailsAsync_UnknownAccount_ReturnsNull() => UpdateCustodianDetailsAsync_UnknownAccount_ReturnsNull_Core();

    [FundAccountDatabaseFact]
    public Task UpdateCustodianDetailsAsync_ClosedAccount_ThrowsAccountStatusPolicyException() => UpdateCustodianDetailsAsync_ClosedAccount_ThrowsAccountStatusPolicyException_Core();

    [FundAccountDatabaseFact]
    public Task UpdateBankDetailsAsync_SuspendedAccount_ThrowsAccountStatusPolicyException() => UpdateBankDetailsAsync_SuspendedAccount_ThrowsAccountStatusPolicyException_Core();

    [FundAccountDatabaseFact]
    public Task UpdateBankDetailsAsync_ActiveBankAccount_ReplacesAndPersistsDetails() => UpdateBankDetailsAsync_ActiveBankAccount_ReplacesAndPersistsDetails_Core();

    [FundAccountDatabaseFact]
    public Task DeactivateAccountAsync_ActiveAccount_SetsInactiveWithEffectiveTo() => DeactivateAccountAsync_ActiveAccount_SetsInactiveWithEffectiveTo_Core();

    [FundAccountDatabaseFact]
    public Task GetFundAccountsAsync_MixedAccountTypes_GroupsByTypeAndExcludesInactive() => GetFundAccountsAsync_MixedAccountTypes_GroupsByTypeAndExcludesInactive_Core();

    [FundAccountDatabaseFact]
    public Task RecordBalanceSnapshotAsync_ActiveAccount_PersistsMoneyFieldsExactly() => RecordBalanceSnapshotAsync_ActiveAccount_PersistsMoneyFieldsExactly_Core();

    [FundAccountDatabaseFact]
    public Task RecordBalanceSnapshotAsync_ClosedAccount_ThrowsAccountStatusPolicyException() => RecordBalanceSnapshotAsync_ClosedAccount_ThrowsAccountStatusPolicyException_Core();

    [FundAccountDatabaseFact]
    public Task GetBalanceHistoryAsync_FromToRange_FiltersInclusivelyNewestFirst() => GetBalanceHistoryAsync_FromToRange_FiltersInclusivelyNewestFirst_Core();

    [FundAccountDatabaseFact]
    public Task GetLatestBalanceSnapshotAsync_MultipleDates_ReturnsLatestAsOfDate() => GetLatestBalanceSnapshotAsync_MultipleDates_ReturnsLatestAsOfDate_Core();

    [FundAccountDatabaseFact]
    public Task IngestCustodianStatementAsync_ActiveAccount_RoundTripsPositionLines() => IngestCustodianStatementAsync_ActiveAccount_RoundTripsPositionLines_Core();

    [FundAccountDatabaseFact]
    public Task IngestCustodianStatementAsync_ReIngestingSameLines_DoesNotDuplicatePositions() => IngestCustodianStatementAsync_ReIngestingSameLines_DoesNotDuplicatePositions_Core();

    [FundAccountDatabaseFact]
    public Task IngestCustodianStatementAsync_SuspendedAccount_IsAllowed() => IngestCustodianStatementAsync_SuspendedAccount_IsAllowed_Core();

    [FundAccountDatabaseFact]
    public Task IngestCustodianStatementAsync_ClosedAccount_ThrowsAccountStatusPolicyException() => IngestCustodianStatementAsync_ClosedAccount_ThrowsAccountStatusPolicyException_Core();

    [FundAccountDatabaseFact]
    public Task IngestBankStatementAsync_ActiveAccount_RoundTripsStatementLines() => IngestBankStatementAsync_ActiveAccount_RoundTripsStatementLines_Core();

    [FundAccountDatabaseFact]
    public Task GetBankStatementLinesAsync_FromToRange_FiltersByTransactionDate() => GetBankStatementLinesAsync_FromToRange_FiltersByTransactionDate_Core();

    [FundAccountDatabaseFact]
    public Task ReconcileAccountAsync_SnapshotAndPositions_ProducesMatchedRun() => ReconcileAccountAsync_SnapshotAndPositions_ProducesMatchedRun_Core();

    [FundAccountDatabaseFact]
    public Task ReconcileAccountAsync_CashDivergingFromBankClosingBalance_ProducesCashBreak() => ReconcileAccountAsync_CashDivergingFromBankClosingBalance_ProducesCashBreak_Core();

    [FundAccountDatabaseFact]
    public Task ReconcileAccountAsync_SnapshotWithoutIndependentEvidence_ReportsUnverifiedNotMatched() => ReconcileAccountAsync_SnapshotWithoutIndependentEvidence_ReportsUnverifiedNotMatched_Core();

    [FundAccountDatabaseFact]
    public Task ReconcileAccountAsync_PositionCountDivergingFromDeclaredLineCount_ProducesPositionBreak() => ReconcileAccountAsync_PositionCountDivergingFromDeclaredLineCount_ProducesPositionBreak_Core();

    [FundAccountDatabaseFact]
    public Task ReconcileAccountAsync_DivergentBrokerageSyncCash_ProducesContinuityBreak() => ReconcileAccountAsync_DivergentBrokerageSyncCash_ProducesContinuityBreak_Core();

    [FundAccountDatabaseFact]
    public Task GetOpenBreaksAsync_ContinuityOnlyBreak_ReturnsNoCashOrPositionBreaks() => GetOpenBreaksAsync_ContinuityOnlyBreak_ReturnsNoCashOrPositionBreaks_Core();

    [FundAccountDatabaseFact]
    public Task RecordSyncHistoryAsync_BlankCapability_ThrowsArgumentException() => RecordSyncHistoryAsync_BlankCapability_ThrowsArgumentException_Core();

    [FundAccountDatabaseFact]
    public Task RecordSyncHistoryAsync_FailedWithoutFailureKind_NormalizesToUnknown() => RecordSyncHistoryAsync_FailedWithoutFailureKind_NormalizesToUnknown_Core();

    [FundAccountDatabaseFact]
    public Task RecordSyncHistoryAsync_SucceededWithFailureKind_ForcesFailureKindNone() => RecordSyncHistoryAsync_SucceededWithFailureKind_ForcesFailureKindNone_Core();

    [FundAccountDatabaseFact]
    public Task RecordSyncHistoryAsync_MessyWarnings_TrimsAndDeduplicates() => RecordSyncHistoryAsync_MessyWarnings_TrimsAndDeduplicates_Core();

    [FundAccountDatabaseFact]
    public Task RecordSyncHistoryAsync_SameCapabilityAndCorrelation_ReusesEntryInsteadOfAppending() => RecordSyncHistoryAsync_SameCapabilityAndCorrelation_ReusesEntryInsteadOfAppending_Core();

    [FundAccountDatabaseFact]
    public Task GetSyncHistoryAsync_CapabilityFilter_MatchesCaseInsensitively() => GetSyncHistoryAsync_CapabilityFilter_MatchesCaseInsensitively_Core();

    [FundAccountDatabaseFact]
    public Task GetLatestSyncHistoryAsync_MultipleAttempts_ReturnsMostRecent() => GetLatestSyncHistoryAsync_MultipleAttempts_ReturnsMostRecent_Core();

    [FundAccountDatabaseFact]
    public Task GetReadinessAsync_HealthyLinkedAccount_IsReadyWithNoIssues() => GetReadinessAsync_HealthyLinkedAccount_IsReadyWithNoIssues_Core();

    [FundAccountDatabaseFact]
    public Task GetReadinessAsync_NoSyncNoLedgerReference_FlagsNeverRunAndLedgerMapping() => GetReadinessAsync_NoSyncNoLedgerReference_FlagsNeverRunAndLedgerMapping_Core();

    [FundAccountDatabaseFact]
    public Task GetReadinessAsync_MarginAccountWithoutSnapshot_FlagsCriticalMarginIssue() => GetReadinessAsync_MarginAccountWithoutSnapshot_FlagsCriticalMarginIssue_Core();

    [FundAccountDatabaseFact]
    public Task RecordMarginSnapshotAsync_LowercaseCurrency_NormalizesToUppercase() => RecordMarginSnapshotAsync_LowercaseCurrency_NormalizesToUppercase_Core();

    [FundAccountDatabaseFact]
    public Task RecordMarginSnapshotAsync_BlankCurrency_ThrowsArgumentException() => RecordMarginSnapshotAsync_BlankCurrency_ThrowsArgumentException_Core();

    [FundAccountDatabaseFact]
    public Task RecordMarginSnapshotAsync_SameCorrelationSameEffectiveAt_ReplacesSnapshot() => RecordMarginSnapshotAsync_SameCorrelationSameEffectiveAt_ReplacesSnapshot_Core();

    [FundAccountDatabaseFact]
    public Task GetLatestMarginSnapshotAsync_MultipleEffectiveTimes_ReturnsLatest() => GetLatestMarginSnapshotAsync_MultipleEffectiveTimes_ReturnsLatest_Core();

    [FundAccountDatabaseFact]
    public Task RecordSyncHistoryAsync_PreCancelledToken_ThrowsOperationCanceled() => RecordSyncHistoryAsync_PreCancelledToken_ThrowsOperationCanceled_Core();

    [FundAccountDatabaseFact]
    public Task RecordMarginSnapshotAsync_PreCancelledToken_ThrowsOperationCanceled() => RecordMarginSnapshotAsync_PreCancelledToken_ThrowsOperationCanceled_Core();
}
