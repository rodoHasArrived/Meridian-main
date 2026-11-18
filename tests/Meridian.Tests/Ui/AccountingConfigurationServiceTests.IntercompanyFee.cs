using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingConfigurationServiceTests
{
    [Fact]
    public async Task IntercompanyFee_RetainedEvidenceSurvivesAClientResave_AndMismatchedLinesBlockReviewUnderTheFeeRule()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);

        var retained = await service.SaveAutomatedDraftAsync(new SaveManualJournalEntryDraftRequest(FeeDraft(), "ops-user"), CancellationToken.None);
        var resaved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(retained with
        {
            IntercompanyAccrualEvidenceJson = "invented",
            IntercompanyAccrualEvidenceDigest = "invented"
        }, "ops-user", LedgerBookId: retained.LedgerBookId));

        resaved.IntercompanyAccrualEvidenceJson.Should().Be(retained.IntercompanyAccrualEvidenceJson);
        IntercompanyFeeAccrualEvidenceGuard.IsFeeEvidence(resaved.IntercompanyAccrualEvidenceJson).Should().BeTrue();
        // The fixture's ordinary cash/income lines do not carry the computed agreement and profile pins.
        retained.ValidationIssues.Should().Contain(issue => issue.Code == AccrualIssue
            && issue.Severity == AccountingConfigurationValidationSeverityDto.Critical && issue.Message.Contains("fee draft", StringComparison.Ordinal));
    }

    private static ManualJournalEntryDraftDto FeeDraft()
    {
        var draft = BalancedManualJournalEntry() with { TenantId = null };
        var agreement = Guid.Parse("cc111111-1111-1111-1111-111111111111");
        var revision = Guid.Parse("cc222222-2222-2222-2222-222222222222");
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 7, 1);
        var computation = new IntercompanyFeeAccrualPreviewDto(IntercompanyAccrualRules.StatusReady, IntercompanyFeeAccrualRules.RuleVersion,
            agreement, "USD", from, to, 100m, 100m, "kernel-test", [],
            [new(1, from, to, revision, 1, new string('a', 64), 1, from, to, IntercompanyFixedFeeBasisDto.PerPeriod, 100m, null, 1m, 100m)], []);
        var evidence = new IntercompanyFeeAccrualEvidenceDto(IntercompanyFeeAccrualRules.RuleVersion, draft.JournalEntryId, "tenant-a",
            draft.LedgerBookId!.Value, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), revision, agreement, 1, new string('a', 64),
            IntercompanyFeeAccrualEvidenceGuard.TransactionId(from, to), draft.AccountingDate, IntercompanyAccountRoleDto.Receivable,
            "fee-receivable", "fee-revenue", "USD", 100m, computation);
        var json = IntercompanyFeeAccrualEvidenceGuard.Serialize(evidence);
        return draft with
        {
            IntercompanyAccrualEvidenceJson = json,
            IntercompanyAccrualEvidenceDigest = IntercompanyAccrualEvidenceGuard.Digest(json)
        };
    }
}
