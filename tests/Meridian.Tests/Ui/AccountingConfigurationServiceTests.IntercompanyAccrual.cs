using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Ui;

public sealed partial class AccountingConfigurationServiceTests
{
    private const string AccrualIssue = "manual-je.intercompany-accrual-evidence";

    [Fact]
    public async Task IntercompanyAccrual_ClientSaveCannotStateOrReplaceRetainedProvenance()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);

        var invented = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(AccrualDraft("first"), "ops-user"));
        var retained = await service.SaveAutomatedDraftAsync(new SaveManualJournalEntryDraftRequest(AccrualDraft("first"), "ops-user"),
            CancellationToken.None);
        var resaved = await service.SaveDraftAsync(new SaveManualJournalEntryDraftRequest(retained with
        {
            IntercompanyAccrualEvidenceJson = "invented",
            IntercompanyAccrualEvidenceDigest = "invented"
        }, "ops-user", LedgerBookId: retained.LedgerBookId));

        invented.IntercompanyAccrualEvidenceJson.Should().BeNull();
        invented.IntercompanyAccrualEvidenceDigest.Should().BeNull();
        invented.ValidationIssues.Should().NotContain(issue => issue.Code == AccrualIssue);
        retained.IntercompanyAccrualEvidenceJson.Should().NotBeNull();
        resaved.IntercompanyAccrualEvidenceJson.Should().Be(retained.IntercompanyAccrualEvidenceJson);
        resaved.IntercompanyAccrualEvidenceDigest.Should().Be(retained.IntercompanyAccrualEvidenceDigest);
    }

    [Fact]
    public async Task IntercompanyAccrual_LinesThatNoLongerMatchTheComputationBlockReviewAsCritical()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);

        // The fixture's ordinary cash/income lines do not carry the computed agreement and profile pins.
        var saved = await service.SaveAutomatedDraftAsync(new SaveManualJournalEntryDraftRequest(AccrualDraft("first"), "ops-user"),
            CancellationToken.None);

        saved.ValidationIssues.Should().Contain(issue => issue.Code == AccrualIssue
            && issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task IntercompanyAccrual_OnlyTrustedRecomputationMayRefreshAnUnreviewedDraft()
    {
        var configuration = CreateService();
        await SeedBalancedConfigurationAsync(configuration);
        var service = CreateManualJournalEntryWorkbenchService(configuration);
        var first = AccrualDraft("first");
        var saved = await service.SaveAutomatedDraftAsync(new SaveManualJournalEntryDraftRequest(first, "ops-user"), CancellationToken.None);
        var recomputed = AccrualDraft("second", first.JournalEntryId) with { Version = saved.Version };

        var refreshed = await service.SaveAutomatedDraftAsync(new SaveManualJournalEntryDraftRequest(recomputed, "ops-user",
            LedgerBookId: saved.LedgerBookId), CancellationToken.None);

        refreshed.IntercompanyAccrualEvidenceDigest.Should().Be(recomputed.IntercompanyAccrualEvidenceDigest);
        refreshed.IntercompanyAccrualEvidenceDigest.Should().NotBe(saved.IntercompanyAccrualEvidenceDigest);
    }

    private static ManualJournalEntryDraftDto AccrualDraft(string principalReference, Guid? journalEntryId = null)
    {
        var draft = BalancedManualJournalEntry() with { TenantId = null };
        if (journalEntryId is { } id)
            draft = draft with { JournalEntryId = id };
        var agreement = Guid.Parse("cc111111-1111-1111-1111-111111111111");
        var revision = Guid.Parse("cc222222-2222-2222-2222-222222222222");
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 7, 1);
        var computation = new IntercompanyAccrualPreviewDto(IntercompanyAccrualRules.StatusReady, IntercompanyAccrualRules.RuleVersion,
            agreement, "USD", from, to, 100m, 100m, "kernel-test", [],
            [new(1, from, to, revision, 1, new string('a', 64), 1, 24_000m, 0.05m, "ACT/360", 0.0833333333m, 100m, principalReference)], []);
        var evidence = new IntercompanyAccrualEvidenceDto(IntercompanyAccrualRules.RuleVersion, draft.JournalEntryId, "tenant-a",
            draft.LedgerBookId!.Value, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), revision, agreement, 1, new string('a', 64),
            IntercompanyAccrualEvidenceGuard.TransactionId(from, to), draft.AccountingDate, IntercompanyAccountRoleDto.Receivable,
            "USD", 100m, computation, new(Guid.NewGuid(), IntercompanyAccountRoleDto.Receivable, 24_000m, 1, new string('d', 64), []));
        var json = IntercompanyAccrualEvidenceGuard.Serialize(evidence);
        return draft with
        {
            IntercompanyAccrualEvidenceJson = json,
            IntercompanyAccrualEvidenceDigest = IntercompanyAccrualEvidenceGuard.Digest(json)
        };
    }
}
