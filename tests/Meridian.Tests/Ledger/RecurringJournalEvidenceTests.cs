using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class RecurringJournalEvidenceTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid JournalId = Guid.Parse("7c79c8e4-821d-4a8a-b5d9-2da1a271872c");
    private static readonly Guid BookId = Guid.Parse("c7ff43aa-5f2e-452d-9a7c-88a88d4a95be");
    private static JournalEvidenceReference Source() => new("contract-1", "vault://contracts/1", "Contract", "Vault", At,
        "controller", "fund-a", new string('a', 64), EvidenceVersion: 3);

    private static RecurringJournalEvidence Evidence() => new(new string('b', 64), JournalId, "schedule-a", 2,
        "template-a", 3, "{\"scheduleId\":\"schedule-a\",\"version\":2}",
        "{\"templateId\":\"template-a\",\"version\":3}", "fund-a", BookId,
        "entity-a", "tenant-a", "company-a", "USD", new DateOnly(2026, 10, 1), "2026-10", 5, [Source()]);

    private static ManualJournalEntryDraftDto Draft(RecurringJournalEvidence? evidence = null)
    {
        evidence ??= Evidence();
        var json = RecurringJournalEvidenceGuard.Serialize(evidence);
        return new(JournalId, ManualJournalEntryStatusDto.Draft, "fund-a", BookId, AccountingBasisKindDto.Primary,
            new DateOnly(2026, 10, 1), "2026-10", "entity-a", null, "USD", "Recurring fee", "preparer", At, At, 1,
            [], [Source().Uri], [], TreasuryContext: new TreasuryLedgerContextDto(IdempotencyKey: $"recurring|{evidence.OccurrenceKey}"),
            TenantId: "tenant-a", CompanyId: "company-a", RecurringJournalEvidenceJson: json,
            RecurringJournalEvidenceDigest: Sha256Digest.ComputeUtf8(json), RequiresRecurringJournalEvidence: true);
    }

    [Fact]
    public void ValidSourceAndExactDraftScope_PassesAtEveryLifecycleStatus()
    {
        foreach (var status in new[] { ManualJournalEntryStatusDto.Draft, ManualJournalEntryStatusDto.Submitted,
                     ManualJournalEntryStatusDto.Approved, ManualJournalEntryStatusDto.Posted })
            RecurringJournalEvidenceGuard.Validate(Draft() with { Status = status }).Should().BeNull();
    }

    [Fact]
    public void MissingSourceEvidence_FailsBeforeApproval()
    {
        RecurringJournalEvidenceGuard.ValidateSources(null).Should().NotBeNull();
        RecurringJournalEvidenceGuard.ValidateSources([]).Should().NotBeNull();
        RecurringJournalEvidenceGuard.Validate(Draft(Evidence() with { SourceEvidence = [] })).Should().NotBeNull();
    }

    [Fact]
    public void SourceRequiresContentIdentityAndRetentionCustodian()
    {
        RecurringJournalEvidenceGuard.ValidateSources([Source() with { ContentHash = null, EvidenceVersion = null }]).Should().NotBeNull();
        RecurringJournalEvidenceGuard.ValidateSources([Source() with { RetainedBy = "" }]).Should().NotBeNull();
        RecurringJournalEvidenceGuard.ValidateSources([Source() with { RetainedAtUtc = default }]).Should().NotBeNull();
        RecurringJournalEvidenceGuard.ValidateSources([Source() with { Uri = "" }]).Should().NotBeNull();
        RecurringJournalEvidenceGuard.ValidateSources([Source() with { ContentHash = null, EvidenceVersion = 3 }]).Should().BeNull();
    }

    [Fact]
    public void MissingOrAlteredProvenance_FailsClosed()
    {
        var draft = Draft();
        RecurringJournalEvidenceGuard.Validate(draft with { RecurringJournalEvidenceJson = null }).Should().NotBeNull();
        RecurringJournalEvidenceGuard.Validate(draft with { RecurringJournalEvidenceDigest = null }).Should().NotBeNull();
        RecurringJournalEvidenceGuard.Validate(draft with { RecurringJournalEvidenceDigest = "invalid" }).Should().NotBeNull();
        RecurringJournalEvidenceGuard.Validate(draft with { RecurringJournalEvidenceJson = draft.RecurringJournalEvidenceJson!.Replace("contract-1", "contract-2", StringComparison.Ordinal) }).Should().NotBeNull();
    }

    [Fact]
    public void MalformedProvenance_WithMatchingDigestStillFailsClosed()
    {
        const string json = "{invalid-json";
        RecurringJournalEvidenceGuard.Validate(Draft() with
        {
            RecurringJournalEvidenceJson = json,
            RecurringJournalEvidenceDigest = Sha256Digest.ComputeUtf8(json)
        }).Should().NotBeNull();
    }

    [Fact]
    public void ChangedDraftIdentityDateScopeOrPeriod_CannotReuseSourceProvenance()
    {
        var draft = Draft();
        var mismatches = new[]
        {
            draft with { JournalEntryId = Guid.NewGuid() },
            draft with { AccountingDate = draft.AccountingDate.AddDays(1) },
            draft with { LedgerBookId = Guid.NewGuid() },
            draft with { FundProfileId = "fund-b" },
            draft with { EntityId = "entity-b" },
            draft with { TenantId = "tenant-b" },
            draft with { CompanyId = "company-b" },
            draft with { Currency = "EUR" },
            draft with { PeriodId = "2026-11" },
            draft with { TreasuryContext = new TreasuryLedgerContextDto(IdempotencyKey: "recurring|another") }
        };
        foreach (var mismatch in mismatches)
            RecurringJournalEvidenceGuard.Validate(mismatch).Should().NotBeNull();
    }

    [Fact]
    public void RemovedSourceLinks_FailEvenWhenDraftRemainsBalanced()
    {
        RecurringJournalEvidenceGuard.Validate(Draft() with { EvidenceLinks = [] }).Should().NotBeNull();
        RecurringJournalEvidenceGuard.Validate(Draft() with { EvidenceLinks = ["vault://other/contract"] }).Should().NotBeNull();
    }

    [Fact]
    public void MissingDefinitionVersionOrSnapshot_FailsClosed()
    {
        RecurringJournalEvidenceGuard.Validate(Draft(Evidence() with { ScheduleVersion = 0 })).Should().NotBeNull();
        RecurringJournalEvidenceGuard.Validate(Draft(Evidence() with { TemplateVersion = 0 })).Should().NotBeNull();
        RecurringJournalEvidenceGuard.Validate(Draft(Evidence() with { ScheduleDefinitionJson = "" })).Should().NotBeNull();
        RecurringJournalEvidenceGuard.Validate(Draft(Evidence() with { TemplateDefinitionJson = "" })).Should().NotBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GovernedCorrection_RetainsOriginalProvenanceInAdjustmentPeriodWithDistinctKey(bool reverse)
    {
        var original = Draft();
        var correctionId = Guid.NewGuid();
        var correction = original with
        {
            JournalEntryId = correctionId,
            AccountingDate = new DateOnly(2026, 11, 1),
            PeriodId = "2026-11",
            ReversalOfJournalEntryId = reverse ? JournalId : null,
            RebookedFromJournalEntryId = reverse ? null : JournalId,
            Reversal = reverse ? new(JournalId, correctionId, "Correction", At, "controller") : null,
            Rebook = reverse ? null : new(JournalId, correctionId, "Correction", At, "controller"),
            TreasuryContext = new TreasuryLedgerContextDto(IdempotencyKey:
                RecurringJournalEvidenceGuard.CorrectionKey(Evidence().OccurrenceKey, correctionId))
        };
        RecurringJournalEvidenceGuard.Validate(correction).Should().BeNull();
        correction.RecurringJournalEvidenceJson.Should().Be(original.RecurringJournalEvidenceJson);
        correction.RecurringJournalEvidenceDigest.Should().Be(original.RecurringJournalEvidenceDigest);
        RecurringJournalEvidenceGuard.Validate(correction with { TreasuryContext = original.TreasuryContext }).Should().NotBeNull();
        RecurringJournalEvidenceGuard.Validate(correction with { Reversal = null, Rebook = null }).Should().NotBeNull();
        RecurringJournalEvidenceGuard.Validate(correction with { LedgerBookId = Guid.NewGuid() }).Should().NotBeNull();
        RecurringJournalEvidenceGuard.Validate(correction with { TenantId = "another-tenant" }).Should().NotBeNull();
    }
}
