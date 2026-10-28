using FluentAssertions;
using Meridian.Contracts.FundStructure;
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

    [Theory]
    [InlineData("genuine", true)]
    [InlineData("name-only", false)]
    [InlineData("wrong-line-id", false)]
    [InlineData("wrong-batch-id", false)]
    [InlineData("partial-batch", false)]
    [InlineData("wrong-line-account", false)]
    [InlineData("wrong-batch-account", false)]
    [InlineData("wrong-stored-account", false)]
    [InlineData("case-tied", false)]
    [InlineData("unsupported-reference", false)]
    [InlineData("separator-reference", false)]
    [InlineData("missing-reference", false)]
    [InlineData("balance", false)]
    [InlineData("duplicate-batch", false)]
    public async Task PersistedPlaidBatchFromBeforeFeeds_RequiresExactHistoricalProof(string condition, bool upgrade)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meridian-fund-accounts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fund-accounts.json");
        try
        {
            var original = new InMemoryFundAccountService(path);
            var account = await original.CreateAccountAsync(MakeBankRequest());
            HistoricalGuid("plaid-bank-line:txn-1").Should().Be(Guid.Parse("5d28c62f-8bb7-c575-9b7e-ec168ea31c9c"));
            var asOf = new DateOnly(2026, 10, 28);
            string[] references = condition == "case-tied" ? ["Txn-1", "txn-1"]
                : condition == "unsupported-reference" ? ["txn-é"]
                : condition == "separator-reference" ? ["txn|1"] : ["txn-1"];
            var batchId = HistoricalGuid($"plaid-bank-batch:{account.AccountId:D}:{string.Join('|', references.Order(StringComparer.OrdinalIgnoreCase))}");
            if (condition is "name-only" or "wrong-batch-id")
                batchId = Guid.NewGuid();
            var lines = references.Select(reference => new BankStatementLineDto(
                condition is "name-only" or "wrong-line-id" ? Guid.NewGuid() : HistoricalGuid($"plaid-bank-line:{reference}"),
                batchId, account.AccountId, asOf, asOf, -25.10m, "USD", "DEBIT", "Coffee",
                condition == "missing-reference" ? null : reference, condition == "balance" ? 100m : null)).ToArray();
            await original.IngestBankStatementAsync(new IngestBankStatementRequest(
                batchId, account.AccountId, asOf, "Plaid", null, lines, "sync", IsBackfill: true));

            // A malformed restored snapshot cannot acquire connector authority from the bank label.
            var state = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            var retained = state["accounts"]![0]!;
            if (condition == "partial-batch")
                retained["bankBatches"]![0]!["lineCount"] = lines.Length + 1;
            if (condition is "wrong-line-account" or "wrong-stored-account")
                retained["bankLines"]![0]!["accountId"] = Guid.NewGuid().ToString();
            if (condition is "wrong-batch-account" or "wrong-stored-account")
                retained["bankBatches"]![0]!["accountId"] = condition == "wrong-stored-account"
                    ? retained["bankLines"]![0]!["accountId"]!.GetValue<string>() : Guid.NewGuid().ToString();
            if (condition == "wrong-stored-account")
            {
                var foreign = retained["bankBatches"]![0]!["accountId"]!.GetValue<string>();
                batchId = HistoricalGuid($"plaid-bank-batch:{foreign}:{string.Join('|', references.Order(StringComparer.OrdinalIgnoreCase))}");
                retained["bankBatches"]![0]!["batchId"] = batchId.ToString();
                retained["bankLines"]![0]!["batchId"] = batchId.ToString();
                lines = lines.Select(line => line with { BatchId = batchId }).ToArray();
            }
            if (condition == "duplicate-batch")
                retained["bankBatches"]!.AsArray().Add(retained["bankBatches"]![0]!.DeepClone());
            await File.WriteAllTextAsync(path, state.ToJsonString());

            var reloaded = new InMemoryFundAccountService(path);
            var restoredLines = await reloaded.GetBankStatementLinesAsync(account.AccountId);
            restoredLines.Should().HaveCount(lines.Length, "raw retained records cannot disappear during authority review");
            if (upgrade)
                restoredLines.Should().OnlyContain(line => line.IsTransactionFeed == true);
            else
                restoredLines.Should().OnlyContain(line => line.IsTransactionFeed != true,
                "reloading cannot assign feed authority to unproven retained records");
            var redeliver = () => reloaded.IngestBankStatementAsync(new IngestBankStatementRequest(
                batchId, account.AccountId, asOf, "Plaid", null,
                lines.Select(line => line with { Amount = -27.10m, ClosingBalance = null }).ToArray(), "sync",
                IsBackfill: true, IsTransactionFeed: true));
            if (upgrade)
            {
                await redeliver.Should().NotThrowAsync("every retained identity proves this was a historical Plaid delivery");
                (await reloaded.GetBankStatementLinesAsync(account.AccountId)).Should().ContainSingle()
                    .Which.Amount.Should().Be(-27.10m);
            }
            else
            {
                await redeliver.Should().ThrowAsync<InvalidOperationException>("unproven statement authority must remain retained");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Guid HistoricalGuid(string value) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

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
    public Task ReconcileAccountAsync_SameDayBalances_UsesSourceSequence() => ReconcileAccountAsync_SameDayBalances_UsesSourceSequence_Core();

    [Fact]
    public Task ReconcileAccountAsync_CorrectedStatement_UsesRevisionRegardlessOfArrival() => ReconcileAccountAsync_CorrectedStatement_UsesRevisionRegardlessOfArrival_Core();

    [Fact]
    public Task IngestBankStatementAsync_RetainedBatchCannotReplaceLineIdentity() => IngestBankStatementAsync_RetainedBatchCannotReplaceLineIdentity_Core();

    [Fact]
    public Task IngestBankStatementAsync_TransactionFeedBatches_AreAdditiveAndSupersedeByLineIdentity() => IngestBankStatementAsync_TransactionFeedBatches_AreAdditiveAndSupersedeByLineIdentity_Core();

    [Fact]
    public Task IngestBankStatementAsync_TransactionFeedRedelivery_MovesItsHeaderDate() => IngestBankStatementAsync_TransactionFeedRedelivery_MovesItsHeaderDate_Core();

    [Fact]
    public Task IngestBankStatementAsync_TransactionFeedRedeliveryInAnotherOrder_ReassignsItsOrdinals() => IngestBankStatementAsync_TransactionFeedRedeliveryInAnotherOrder_ReassignsItsOrdinals_Core();

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

    [Fact]
    public Task RecordBalanceSnapshotAsync_PreservesAccountFundOwnership() => RecordBalanceSnapshotAsync_PreservesAccountFundOwnership_Core();

    [Fact]
    public Task ClosedAccount_ExplicitBackfillRecordsHistoricalEvidence() => ClosedAccount_ExplicitBackfillRecordsHistoricalEvidence_Core();

    [Fact]
    public Task IngestCustodianStatementAsync_InvalidLineLineageRejectsEntireBatch() => IngestCustodianStatementAsync_InvalidLineLineageRejectsEntireBatch_Core();

    [Fact]
    public Task IngestBankStatementAsync_InvalidLineLineageRejectsEntireBatch() => IngestBankStatementAsync_InvalidLineLineageRejectsEntireBatch_Core();

    [Fact]
    public Task ReconcileAccountAsync_ForeignCurrencySyncCannotVerifyOrBreakCashContinuity() => ReconcileAccountAsync_ForeignCurrencySyncCannotVerifyOrBreakCashContinuity_Core();

    [Fact]
    public Task ReconcileAccountAsync_UsesLatestSyncInInternalSnapshotCurrency() => ReconcileAccountAsync_UsesLatestSyncInInternalSnapshotCurrency_Core();

    [Fact]
    public Task GetReadinessAsync_UnavailableAccountCannotBeReady() => GetReadinessAsync_UnavailableAccountCannotBeReady_Core();

    [Fact]
    public Task GetReadinessAsync_IncompleteSyncCannotBeReady() => GetReadinessAsync_IncompleteSyncCannotBeReady_Core();

    [Fact]
    public Task GetReadinessAsync_UnavailableProviderSyncCannotBeReady() => GetReadinessAsync_UnavailableProviderSyncCannotBeReady_Core();

    [Fact]
    public Task RecordMarginSnapshotAsync_SameCorrelationCorrectedEffectiveTime_PreservesIdentity() => RecordMarginSnapshotAsync_SameCorrelationCorrectedEffectiveTime_PreservesIdentity_Core();

    [Fact]
    public Task RecordMarginSnapshotAsync_CorrectedEffectiveTimeAlreadyOccupied_RejectsWithoutReplacing() => RecordMarginSnapshotAsync_CorrectedEffectiveTimeAlreadyOccupied_RejectsWithoutReplacing_Core();
    [Fact]
    public Task Reconciliation_CorrectedEvidenceClearsOnlyMatchingActiveScopeAndRetainsHistory()
        => Reconciliation_CorrectedEvidenceClearsOnlyMatchingActiveScopeAndRetainsHistory_Core();

    [Fact]
    public Task SyncHistory_SimultaneousCorrelatedRetriesRetainSuccessWithoutRegression()
        => SyncHistory_SimultaneousCorrelatedRetriesRetainSuccessWithoutRegression_Core();
    [Fact]
    public Task Reconciliation_BatchedReadsKeepExactAccountScopeAndRetainedHistory()
        => Reconciliation_BatchedReadsKeepExactAccountScopeAndRetainedHistory_Core();
}
