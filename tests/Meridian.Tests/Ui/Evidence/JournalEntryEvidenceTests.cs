using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui.Evidence;

/// <summary>
/// Guards the journal-entry proof chain: the journal-entry detail screen links every posting to
/// the evidence workbench with subject kind "journal-entry", so the resolver and contributor must
/// turn a retained manual-journal draft into a packet instead of dead-ending at an unsupported
/// kind. The failure mode under guard: an operator clicking "Attach evidence" or an evidence link
/// on a posted journal entry and landing on an unsupported-subject error, or a fabricated packet
/// for an id the workbench never retained.
/// </summary>
public sealed class JournalEntryEvidenceTests
{
    private static readonly Guid JournalEntryId = Guid.Parse("0aaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");
    private static readonly Guid LedgerBookId = Guid.Parse("0bbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1");

    [Fact]
    public async Task EvidenceSubjectResolver_DuringJournalEntryEvidenceReview_ResolvesRetainedWorkbenchDraft()
    {
        var resolver = new EvidenceSubjectResolver(BuildServiceProvider(BuildDraft()));

        resolver.IsSupportedKind(EvidenceSubjectResolver.JournalEntryKind).Should().BeTrue();

        var subject = await resolver.ResolveAsync(
            EvidenceSubjectResolver.JournalEntryKind,
            JournalEntryId.ToString("D"));

        subject.Should().NotBeNull();
        subject!.SubjectKind.Should().Be(EvidenceSubjectResolver.JournalEntryKind);
        subject.SubjectId.Should().Be(JournalEntryId.ToString("D"));
        subject.Workspace.Should().Be("Accounting");
        subject.PageTag.Should().Be("AccountingJournalEntries");
        subject.LedgerBookId.Should().Be(LedgerBookId);
        subject.Route.Should().Contain($"/accounting/journal-entries/detail?journalEntryId={JournalEntryId:D}");
        subject.Route.Should().Contain($"ledgerBookId={LedgerBookId:D}");
    }

    [Fact]
    public async Task EvidenceSubjectResolver_DuringJournalEntryEvidenceReview_ReturnsNullForUnknownOrForeignEntries()
    {
        var resolver = new EvidenceSubjectResolver(BuildServiceProvider(BuildDraft()));

        var unknownEntry = await resolver.ResolveAsync(
            EvidenceSubjectResolver.JournalEntryKind,
            Guid.Parse("0ccccccc-cccc-4ccc-8ccc-ccccccccccc1").ToString("D"));
        var nonGuidEntry = await resolver.ResolveAsync(
            EvidenceSubjectResolver.JournalEntryKind,
            "run-ledger-summary-entry");
        var foreignLedgerBookEntry = await resolver.ResolveAsync(
            EvidenceSubjectResolver.JournalEntryKind,
            JournalEntryId.ToString("D"),
            ledgerBookId: Guid.Parse("0ddddddd-dddd-4ddd-8ddd-ddddddddddd1"));

        unknownEntry.Should().BeNull("an id the workbench never retained must resolve as not found, not fabricate a subject");
        nonGuidEntry.Should().BeNull();
        foreignLedgerBookEntry.Should().BeNull();
    }

    [Fact]
    public async Task EvidenceGraphService_DuringJournalEntryEvidenceReview_ProjectsPacketWithRetainedEvidenceLinks()
    {
        var provider = BuildServiceProvider(BuildDraft());
        var graph = new EvidenceGraphService(
            new EvidenceSubjectResolver(provider),
            new EvidenceTemplateRegistry(),
            [new JournalEntryEvidenceContributor(provider)],
            NullLogger<EvidenceGraphService>.Instance);

        var packet = await graph.GetPacketAsync(
            EvidenceSubjectResolver.JournalEntryKind,
            JournalEntryId.ToString("D"));

        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.JournalEntryKind);
        packet.Nodes.Select(static node => node.Kind).Should().Contain([
            "manual-journal-entry",
            "retained-evidence",
            "approval-state"
        ]);
        packet.Warnings.Should().NotContain(warning =>
            warning.Contains("No evidence contributors support", StringComparison.OrdinalIgnoreCase));

        var rootNode = packet.Nodes.Single(node => node.Kind == "manual-journal-entry");
        rootNode.Status.Should().Be(EvidenceStatusDto.Ready);
        rootNode.ArtifactRefs.Should().Contain(artifact =>
            artifact.Kind == "journal-entry-detail-route" &&
            artifact.Route!.Contains("/accounting/journal-entries/detail", StringComparison.OrdinalIgnoreCase));

        var evidenceNode = packet.Nodes.Single(node => node.Kind == "retained-evidence");
        evidenceNode.Status.Should().Be(EvidenceStatusDto.Ready);
        evidenceNode.Summary.Should().Contain("3 retained evidence item(s)");
        evidenceNode.ArtifactRefs.Should().OnlyContain(artifact => artifact.Retained);
        evidenceNode.ArtifactRefs.Should().Contain(artifact =>
            artifact.Route == "/evidence/fund-alpha/bank-statement-2026-03.pdf");
        evidenceNode.ArtifactRefs.Should().Contain(artifact =>
            artifact.Path == "vault:bank-confirmation-2026-03");

        var approvalNode = packet.Nodes.Single(node => node.Kind == "approval-state");
        approvalNode.Status.Should().Be(EvidenceStatusDto.Ready);
        approvalNode.Summary.Should().Contain("controller-1");

        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
        packet.Completeness.MissingIds.Should().BeEmpty();
    }

    [Fact]
    public async Task EvidenceGraphService_DuringJournalEntryEvidenceReview_ReturnsNullPacketForUnknownEntry()
    {
        var provider = BuildServiceProvider(BuildDraft());
        var graph = new EvidenceGraphService(
            new EvidenceSubjectResolver(provider),
            new EvidenceTemplateRegistry(),
            [new JournalEntryEvidenceContributor(provider)],
            NullLogger<EvidenceGraphService>.Instance);

        var packet = await graph.GetPacketAsync(
            EvidenceSubjectResolver.JournalEntryKind,
            Guid.Parse("0eeeeeee-eeee-4eee-8eee-eeeeeeeeeee1").ToString("D"));

        packet.Should().BeNull("an unknown journal entry id must surface as a clean not-found packet");
    }

    private static ServiceProvider BuildServiceProvider(params ManualJournalEntryDraftDto[] drafts)
        => new ServiceCollection()
            .AddSingleton<IManualJournalEntryWorkbenchService>(new StubManualJournalEntryWorkbenchService(drafts))
            .BuildServiceProvider();

    private static ManualJournalEntryDraftDto BuildDraft() =>
        new(
            JournalEntryId: JournalEntryId,
            Status: ManualJournalEntryStatusDto.Posted,
            FundProfileId: "fund-alpha",
            LedgerBookId: LedgerBookId,
            AccountingBasis: AccountingBasisKindDto.Primary,
            AccountingDate: new DateOnly(2026, 3, 31),
            PeriodId: "2026-P03",
            EntityId: null,
            FundNodeId: null,
            Currency: "USD",
            Memo: "March bank fee accrual",
            PreparedBy: "fund-accountant-1",
            CreatedAtUtc: DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Version: 3,
            Lines: [],
            EvidenceLinks:
            [
                "/evidence/fund-alpha/bank-statement-2026-03.pdf",
                "vault:bank-confirmation-2026-03"
            ],
            ValidationIssues: [],
            EvidenceAttachments:
            [
                new ManualJournalEntryEvidenceAttachmentDto(
                    AttachmentId: "att-1",
                    DisplayName: "Bank statement March 2026",
                    EvidenceKind: "bank-statement",
                    Uri: "/workstation/evidence/journal-entry/att-1/manifest.json",
                    SourceSystem: "evidence-vault",
                    AddedAtUtc: DateTimeOffset.UtcNow,
                    AddedBy: "ops-1")
            ],
            TotalDebits: 125.50m,
            TotalCredits: 125.50m,
            ApprovedAtUtc: DateTimeOffset.UtcNow,
            ApprovedBy: "controller-1",
            PostedAtUtc: DateTimeOffset.UtcNow,
            PostedBy: "controller-1");

    private sealed class StubManualJournalEntryWorkbenchService : IManualJournalEntryWorkbenchService
    {
        private readonly IReadOnlyList<ManualJournalEntryDraftDto> _drafts;

        public StubManualJournalEntryWorkbenchService(IReadOnlyList<ManualJournalEntryDraftDto> drafts)
        {
            _drafts = drafts;
        }

        public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>(["fund-alpha"]);
        }

        public Task<ManualJournalEntryWorkbenchDto> GetWorkbenchAsync(
            string? fundProfileId = null,
            Guid? ledgerBookId = null,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
        {
            ct.ThrowIfCancellationRequested();
            var drafts = ledgerBookId.HasValue
                ? _drafts.Where(draft => draft.LedgerBookId == ledgerBookId.Value).ToArray()
                : _drafts;
            return Task.FromResult(new ManualJournalEntryWorkbenchDto(
                FundProfileId: fundProfileId ?? "fund-alpha",
                LedgerBookId: ledgerBookId,
                LoadedAtUtc: DateTimeOffset.UtcNow,
                LedgerBooks: [],
                ChartOfAccounts: [],
                Drafts: drafts,
                AuditTrail: []));
        }

        public Task<PrivateCapitalActivityProjectionDto> GetPrivateCapitalActivityAsync(
            string? fundProfileId = null,
            Guid? ledgerBookId = null,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
            => throw new NotSupportedException("Journal-entry evidence tests do not project private-capital activity.");

        public Task<ManualJournalEntryDraftDto> SaveDraftAsync(
            SaveManualJournalEntryDraftRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("Journal-entry evidence tests do not mutate manual journal drafts.");

        public Task<ManualJournalEntryDraftDto> ValidateDraftAsync(
            ValidateManualJournalEntryDraftRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("Journal-entry evidence tests do not mutate manual journal drafts.");

        public Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(
            SubmitManualJournalEntryApprovalRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("Journal-entry evidence tests do not mutate manual journal drafts.");
    }
}
