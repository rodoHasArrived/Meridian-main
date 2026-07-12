using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class StatementImportEvidenceBridgeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"meridian-statement-evidence-{Guid.NewGuid():N}");

    [Fact]
    public async Task RetainAsync_CopiesStatementSourceIntoEvidenceVaultAndReturnsRoutes()
    {
        var retainedDirectory = Path.Combine(_root, "reconciliation", "statement-connector-imports", "sc-123");
        Directory.CreateDirectory(retainedDirectory);
        await File.WriteAllTextAsync(Path.Combine(retainedDirectory, "statement.csv"), "symbol,quantity\nAAPL,10\n");

        var store = new FileEvidenceArtifactStore(_root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var bridge = new StatementImportEvidenceBridge(store, _root);
        var result = new StatementImportCommitResultDto(
            RunId: "stmt-run-77",
            Duplicate: false,
            RecordCount: 1,
            KindSummaries: [],
            BreakCount: 2,
            CaseCount: 2,
            RetainedSourcePath: "reconciliation/statement-connector-imports/sc-123/statement.csv",
            RetainedCanonicalPath: "reconciliation/statement-connector-imports/sc-123/canonical.csv",
            Status: "Imported",
            NextAction: "Review the reconciliation queue.")
        {
            BreakIds = ["break-cash-1", "break-position-2"],
            CaseIds = ["case:break-cash-1", "case:break-position-2"],
            ReconciliationCaseLinks =
            [
                new(
                    "case:break-cash-1",
                    "break-cash-1",
                    "/accounting/reconciliation/match?runId=stmt-run-77&caseId=case%3Abreak-cash-1&breakId=break-cash-1",
                    "Cash break case",
                    "Open",
                    "High",
                    "Cash balance break from imported statement.",
                    "Assign the case and attach cash support."),
                new(
                    "case:break-position-2",
                    "break-position-2",
                    "/accounting/reconciliation/match?runId=stmt-run-77&caseId=case%3Abreak-position-2&breakId=break-position-2",
                    "Position break case",
                    "Open",
                    "Normal",
                    "Position break from imported statement.",
                    "Compare the retained statement row to position evidence.")
            ]
        };

        var retained = await bridge.RetainAsync(
            result,
            new StatementImportEvidenceBridgeRequest(
                SourceKind: "broker",
                SourceInstitution: "Interactive Brokers",
                FundAccountId: "FUND-A",
                ExternalAccountId: "U-100",
                PeriodStart: new DateOnly(2026, 6, 1),
                PeriodEnd: new DateOnly(2026, 6, 30),
                ImportedBy: "controller"));

        retained.EvidenceVaultIdentity.Should().NotBeNull();
        retained.EvidenceVaultIdentity!.SubjectKind.Should().Be("statement-run");
        retained.EvidenceVaultIdentity.SubjectId.Should().Be("stmt-run-77");
        retained.EvidenceWorkbenchRoute.Should().Be(
            "/accounting/evidence?subjectKind=statement-run&subjectId=stmt-run-77&documentClassification=Statement");
        retained.ReconciliationRoute.Should().Be("/accounting/reconciliation/match?runId=stmt-run-77");
        retained.NextActions.Should().Contain("Open retained statement evidence in Evidence Vault.");

        var artifact = retained.EvidenceVaultIdentity.Artifacts.Should().ContainSingle().Subject;
        File.Exists(Path.Combine(_root, artifact.RelativePath)).Should().BeTrue();
        artifact.CanonicalSubjectKind.Should().Be("statement-run");
        artifact.CanonicalSubjectId.Should().Be("stmt-run-77");
        artifact.SourcePath.Should().EndWith(Path.Combine("sc-123", "statement.csv"));

        var document = retained.EvidenceVaultIdentity.Documents.Should().ContainSingle().Subject;
        document.Classification.Should().Be(EvidenceDocumentClassificationDto.Statement);
        document.ChannelKind.Should().Be(EvidenceDocumentIntakeChannelDto.ImportedFileReference);
        document.ReviewerState.Status.Should().Be(EvidenceDocumentReviewStatusDto.NeedsReview);
        document.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.StatementRun &&
            link.ObjectId == "stmt-run-77" &&
            link.Route == "/accounting/reconciliation/match?runId=stmt-run-77");
        document.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.Account &&
            link.ObjectId == "FUND-A");
        document.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.ReconciliationCase &&
            link.ObjectId == "case:break-cash-1" &&
            link.Label == "Cash break case" &&
            link.Route == "/accounting/reconciliation/match?runId=stmt-run-77&caseId=case%3Abreak-cash-1&breakId=break-cash-1");
        document.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.ReconciliationCase &&
            link.ObjectId == "case:break-position-2" &&
            link.Route == "/accounting/reconciliation/match?runId=stmt-run-77&caseId=case%3Abreak-position-2&breakId=break-position-2");

        var matches = await store.FindByLinkageAsync(new EvidenceVaultLookupRequestDto(
            EvidenceSubject: null,
            RunId: "stmt-run-77",
            PeriodId: null,
            ReportPackId: null,
            ReconciliationCaseId: null));
        matches.Should().ContainSingle(match => match.VaultId == retained.EvidenceVaultIdentity.VaultId);

        var caseMatches = await store.FindByLinkageAsync(new EvidenceVaultLookupRequestDto(
            EvidenceSubject: null,
            RunId: null,
            PeriodId: null,
            ReportPackId: null,
            ReconciliationCaseId: "case:break-position-2"));
        caseMatches.Should().ContainSingle(match => match.VaultId == retained.EvidenceVaultIdentity.VaultId);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
