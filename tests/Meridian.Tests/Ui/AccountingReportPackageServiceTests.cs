using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;

namespace Meridian.Tests.Ui;

public sealed class AccountingReportPackageServiceTests
{
    private static readonly Guid DefaultLedgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AlternateLedgerBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task BuildPackageAsync_BlocksStandaloneCertificationWithoutLedgerBook()
    {
        var service = new AccountingReportPackageService();

        var package = await service.BuildPackageAsync(new AccountingReportPackageRequestDto(
            FundProfileId: "fund-unbooked",
            PeriodId: "2027-01",
            Actor: "controller",
            EvidenceLinks:
            [
                "evidence:ledger:trial-balance:2027-01",
                "evidence:reconciliation:gl-tie-out:2027-01",
                "evidence:report-render:financial-statements:2027-01",
                "evidence:nav:support-package:2027-01"
            ]));

        package.FinancialStatements.LedgerBookId.Should().BeNull();
        package.Certification.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ReportPackageLedgerBookMissing" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
    }

    [Fact]
    public async Task BuildPackageAsync_BlocksCloseBackedCertificationUntilPeriodLock()
    {
        var workflowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var service = new AccountingReportPackageService(new StubCloseManagementService(
            BuildSignedOffClosePlan(workflowId, isPeriodLocked: false)));

        var package = await service.BuildPackageAsync(CompletePackageRequest(
            "fund-alpha",
            "2027-02",
            CloseWorkflowId: workflowId));

        package.Certification.State.Should().Be(AccountingCertificationStateDto.Draft);
        package.ValidationIssues.Should().Contain(issue =>
            issue.Code == "PeriodNotLocked" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == $"close-plan-{workflowId:D}");

        var certify = () => service.CertifyPackageAsync(new CertifyAccountingReportPackageRequestDto(
            package.FinancialStatements.PackageId,
            "controller",
            "Attempt to certify before the close period is locked.",
            [CertificationEvidence(package)]));
        await certify.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*current close-plan blockers*PeriodNotLocked*");
    }

    [Fact]
    public async Task CertifyPackageAsync_RevalidatesCurrentClosePlanBeforeCertification()
    {
        var workflowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var closePlan = BuildSignedOffClosePlan(workflowId, isPeriodLocked: true);
        var closeManagement = new MutableStubCloseManagementService(workflowId, closePlan);
        var service = new AccountingReportPackageService(closeManagement);

        var package = await service.BuildPackageAsync(CompletePackageRequest(
            "fund-alpha",
            "2027-02",
            CloseWorkflowId: workflowId));

        package.Certification.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        package.CloseWorkflowId.Should().Be(workflowId);
        closeManagement.ClosePlan = closePlan with
        {
            LateAdjustments =
            [
                new LateAdjustmentRequestDto(
                    "late-adjustment-nav-true-up",
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    "controller",
                    DateTimeOffset.Parse("2027-02-28T19:00:00Z"),
                    25_000m,
                    "USD",
                    "Material NAV true-up identified after package assembly.",
                    ManualJournalEntryStatusDto.Submitted,
                    closePlan.MaterialityPolicy,
                    ["evidence:late-adjustment:2027-02:nav-true-up"])
            ]
        };

        var certify = () => service.CertifyPackageAsync(new CertifyAccountingReportPackageRequestDto(
            package.FinancialStatements.PackageId,
            "controller",
            "Controller attempted stale close-backed package certification.",
            [CertificationEvidence(package)]));

        await certify.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*current close-plan blockers*LateAdjustmentApprovalPending*");
        var retained = await service.ListPackagesAsync("fund-alpha", "2027-02");
        retained.Should().ContainSingle();
        retained[0].Certification.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
    }

    [Fact]
    public async Task BuildPackageAsync_InheritsClosePlanLedgerBookAndBlocksMismatch()
    {
        var workflowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var closeLedgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var mismatchedLedgerBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var service = new AccountingReportPackageService(new StubCloseManagementService(
            BuildSignedOffClosePlan(workflowId, isPeriodLocked: true, ledgerBookId: closeLedgerBookId)));

        var inherited = await service.BuildPackageAsync(CompletePackageRequest(
            "fund-alpha",
            "2027-02",
            CloseWorkflowId: workflowId));

        inherited.Certification.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        inherited.FinancialStatements.LedgerBookId.Should().Be(closeLedgerBookId);
        inherited.RealizedGainLoss.LedgerBookId.Should().Be(closeLedgerBookId);
        inherited.NavPackage.LedgerBookId.Should().Be(closeLedgerBookId);
        inherited.FinancialStatements.LineProvenance.Should().OnlyContain(row =>
            row.Dimensions.BookId == closeLedgerBookId.ToString("D"));

        var mismatched = await service.BuildPackageAsync(CompletePackageRequest(
            "fund-alpha",
            "2027-03",
            CloseWorkflowId: workflowId,
            LedgerBookId: mismatchedLedgerBookId));

        mismatched.Certification.State.Should().Be(AccountingCertificationStateDto.Draft);
        mismatched.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ReportPackageLedgerBookMismatch" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical &&
            issue.TargetId == $"close-plan-{workflowId:D}");
    }

    [Fact]
    public async Task CertifyPackageAsync_RevalidatesCurrentClosePlanLedgerBookBeforeCertification()
    {
        var workflowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var originalLedgerBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var driftedLedgerBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var closePlan = BuildSignedOffClosePlan(
            workflowId,
            isPeriodLocked: true,
            ledgerBookId: originalLedgerBookId);
        var closeManagement = new MutableStubCloseManagementService(workflowId, closePlan);
        var service = new AccountingReportPackageService(closeManagement);

        var package = await service.BuildPackageAsync(CompletePackageRequest(
            "fund-alpha",
            "2027-02",
            CloseWorkflowId: workflowId,
            LedgerBookId: originalLedgerBookId));

        package.Certification.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        closeManagement.ClosePlan = closePlan with { LedgerBookId = driftedLedgerBookId };

        var certify = () => service.CertifyPackageAsync(new CertifyAccountingReportPackageRequestDto(
            package.FinancialStatements.PackageId,
            "controller",
            "Controller attempted certification after close workflow book drift.",
            [CertificationEvidence(package)]));

        await certify.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*current close-plan blockers*ReportPackageLedgerBookMismatch*");
    }

    [Fact]
    public async Task CertifyPackageAsync_RequiresRetainedCertificationApprovalEvidence()
    {
        var service = new AccountingReportPackageService();
        var ready = await service.BuildPackageAsync(new AccountingReportPackageRequestDto(
            FundProfileId: "fund-alpha",
            PeriodId: "2027-03",
            Actor: "controller",
            LedgerBookId: DefaultLedgerBookId,
            BeginningCapital: 200_000m,
            Contributions: 25_000m,
            Distributions: 5_000m,
            RealizedGainLoss: 7_500m,
            Nav: 227_500m,
            EvidenceLinks:
            [
                "evidence:ledger:trial-balance:2027-03",
                "evidence:reconciliation:gl-tie-out:2027-03",
                "evidence:report-render:financial-statements:2027-03",
                "evidence:nav:support-package:2027-03"
            ]));

        var unrelatedEvidence = () => service.CertifyPackageAsync(new CertifyAccountingReportPackageRequestDto(
            ready.FinancialStatements.PackageId,
            "controller",
            "Controller certified the retained report package.",
            ["evidence:nav:support-package:2027-03"]));

        ready.Certification.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        ready.ExportArtifacts.Should().Contain(row =>
            row.ArtifactKind == "financial-statements" &&
            row.Format == "pdf" &&
            row.CertificationState == AccountingCertificationStateDto.ReadyForReview &&
            row.Route.Contains(ready.FinancialStatements.PackageId, StringComparison.OrdinalIgnoreCase) &&
            row.ContentHash.Length == 64 &&
            row.EvidenceLinks.Contains("evidence:ledger:trial-balance:2027-03"));
        ready.ExportArtifacts.Should().Contain(row =>
            row.ArtifactKind == "report-line-provenance" &&
            row.Format == "json" &&
            row.CertificationState == AccountingCertificationStateDto.ReadyForReview);
        var readyArtifact = ready.ExportArtifacts.First(row => row.ArtifactKind == "financial-statements");
        var readyArtifactHash = readyArtifact.ContentHash;
        var readyArtifactGeneratedAtUtc = readyArtifact.GeneratedAtUtc;
        ready.FinancialStatements.LineProvenance.Should().Contain(row =>
            row.StatementId == "balance-sheet" &&
            row.LineLabel == "Net assets" &&
            row.Amount == 227_500m &&
            row.Dimensions.FundId == "fund-alpha" &&
            row.EvidenceLinks.Contains("evidence:ledger:trial-balance:2027-03") &&
            row.EvidenceLinks.Contains("evidence:nav:support-package:2027-03"));
        ready.FinancialStatements.LineProvenance.Should().Contain(row =>
            row.StatementId == "income-statement" &&
            row.SourceKind == "LedgerAndReconciliation" &&
            row.Amount == 7_500m &&
            row.EvidenceLinks.Contains("evidence:reconciliation:gl-tie-out:2027-03"));
        await unrelatedEvidence.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*requires retained approval, certification, sign-off, or review evidence*");

        var wrongPeriodEvidence = () => service.CertifyPackageAsync(new CertifyAccountingReportPackageRequestDto(
            ready.FinancialStatements.PackageId,
            "controller",
            "Controller certified the wrong retained report package.",
            ["evidence:report-certification:controller-approval:2027-04"]));
        await wrongPeriodEvidence.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must reference the retained package, certification id, and exact package period in the same artifact*");

        var splitCertificationEvidence = () => service.CertifyPackageAsync(new CertifyAccountingReportPackageRequestDto(
            ready.FinancialStatements.PackageId,
            "controller",
            "Controller certified with split evidence.",
            [
                "evidence:report-render:financial-statements:2027-03",
                "evidence:report-certification:controller-approval:2027-04"
            ]));
        await splitCertificationEvidence.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must reference the retained package, certification id, and exact package period in the same artifact*");

        var missingCertificationIdEvidence = () => service.CertifyPackageAsync(new CertifyAccountingReportPackageRequestDto(
            ready.FinancialStatements.PackageId,
            "controller",
            "Controller certified with package and period evidence that omits the certification id.",
            [$"evidence:report-certification:controller-approval:{ready.FinancialStatements.PackageId}:2027-03"]));
        await missingCertificationIdEvidence.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must reference the retained package, certification id, and exact package period in the same artifact*");

        var assistantCertification = () => service.CertifyPackageAsync(new CertifyAccountingReportPackageRequestDto(
            ready.FinancialStatements.PackageId,
            "assistant",
            "Assistant drafted certification should not certify the retained report package.",
            [CertificationEvidence(ready)],
            ActionOrigin: OperationsActionOriginDto.AssistantDraft));
        await assistantCertification.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Reviewed automation cannot certify accounting report packages*human operator*");

        var certified = await service.CertifyPackageAsync(new CertifyAccountingReportPackageRequestDto(
            ready.FinancialStatements.PackageId,
            "controller",
            "Controller certified the retained report package.",
            [CertificationEvidence(ready)]));

        certified.Should().NotBeNull();
        certified!.Certification.State.Should().Be(AccountingCertificationStateDto.Certified);
        certified.Certification.EvidenceLinks.Should().Contain(CertificationEvidence(ready));
        certified.FinancialStatements.CertificationState.Should().Be(AccountingCertificationStateDto.Certified);
        certified.NavPackage.CertificationState.Should().Be(AccountingCertificationStateDto.Certified);
        certified.ExportArtifacts.Should().OnlyContain(row => row.CertificationState == AccountingCertificationStateDto.Certified);
        certified.ExportArtifacts.Should().OnlyContain(row =>
            row.EvidenceLinks.Contains(CertificationEvidence(ready)));
        certified.ExportArtifacts.Select(static row => row.ContentHash).Should().OnlyContain(hash => hash.Length == 64);

        var artifact = certified.ExportArtifacts.First(row => row.ArtifactKind == "financial-statements");
        artifact.ContentHash.Should().NotBe(readyArtifactHash);
        artifact.GeneratedAtUtc.Should().BeOnOrAfter(readyArtifactGeneratedAtUtc);
        var manifest = await service.GetExportArtifactManifestAsync(
            certified.FinancialStatements.PackageId,
            artifact.ArtifactId);

        manifest.Should().NotBeNull();
        manifest!.PackageId.Should().Be(certified.FinancialStatements.PackageId);
        manifest.ArtifactId.Should().Be(artifact.ArtifactId);
        manifest.CertificationState.Should().Be(AccountingCertificationStateDto.Certified);
        manifest.ContentHash.Should().Be(artifact.ContentHash);
        manifest.ContentType.Should().Be("application/json");
        manifest.ExternalPostingAllowed.Should().BeFalse();
        manifest.Payload.Should().Contain("\"packageId\"");
        manifest.Payload.Should().Contain(CertificationEvidence(ready));
    }

    [Fact]
    public async Task BuildPackageAsync_RestatementRequiresRetainedCertifiedPriorPackage()
    {
        var service = new AccountingReportPackageService();

        var missingPrior = await service.BuildPackageAsync(CompletePackageRequest(
            "fund-alpha",
            "2027-04",
            RestatementReasonCode: "nav-correction",
            PriorPackageId: "accounting-report-package-fund-alpha-2027-03",
            EvidenceLinks:
            [
                "evidence:ledger:trial-balance:2027-04",
                "evidence:reconciliation:gl-tie-out:2027-04",
                "evidence:report-render:financial-statements:2027-04",
                "evidence:nav:support-package:2027-04",
                "evidence:restatement:nav-correction:2027-04",
                "evidence:prior-package:lineage:2027-03"
            ]));

        missingPrior.Certification.State.Should().Be(AccountingCertificationStateDto.Draft);
        missingPrior.ValidationIssues.Should().Contain(issue =>
            issue.Code == "RestatementPriorPackageNotRetained" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);

        var draftPrior = await service.BuildPackageAsync(CompletePackageRequest("fund-alpha", "2027-03"));
        var draftRestatement = await service.BuildPackageAsync(CompletePackageRequest(
            "fund-alpha",
            "2027-04",
            RestatementReasonCode: "nav-correction",
            PriorPackageId: draftPrior.FinancialStatements.PackageId,
            EvidenceLinks:
            [
                "evidence:ledger:trial-balance:2027-04",
                "evidence:reconciliation:gl-tie-out:2027-04",
                "evidence:report-render:financial-statements:2027-04",
                "evidence:nav:support-package:2027-04",
                "evidence:restatement:nav-correction:2027-04",
                "evidence:prior-package:lineage:2027-03"
            ]));

        draftRestatement.Certification.State.Should().Be(AccountingCertificationStateDto.Draft);
        draftRestatement.ValidationIssues.Should().Contain(issue =>
            issue.Code == "RestatementPriorPackageNotCertified" &&
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);

        var certifiedPrior = await service.CertifyPackageAsync(new CertifyAccountingReportPackageRequestDto(
            draftPrior.FinancialStatements.PackageId,
            "controller",
            "Controller certified the prior report package.",
            [CertificationEvidence(draftPrior)]));
        var readyRestatement = await service.BuildPackageAsync(CompletePackageRequest(
            "fund-alpha",
            "2027-04",
            RestatementReasonCode: "nav-correction",
            PriorPackageId: certifiedPrior!.FinancialStatements.PackageId,
            EvidenceLinks:
            [
                "evidence:ledger:trial-balance:2027-04",
                "evidence:reconciliation:gl-tie-out:2027-04",
                "evidence:report-render:financial-statements:2027-04",
                "evidence:nav:support-package:2027-04",
                "evidence:restatement:nav-correction:2027-04",
                "evidence:prior-package:lineage:2027-03"
            ]));

        readyRestatement.Certification.State.Should().Be(AccountingCertificationStateDto.ReadyForReview);
        readyRestatement.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "RestatementPriorPackageNotRetained" ||
            issue.Code == "RestatementPriorPackageNotCertified");
        readyRestatement.NavPackage.Restatement.Should().NotBeNull();
        readyRestatement.NavPackage.Restatement!.PriorPackageId.Should().Be(certifiedPrior.FinancialStatements.PackageId);
        readyRestatement.NavPackage.Restatement.ApprovalState.Should().Be(ManualJournalEntryStatusDto.Submitted);
        readyRestatement.FinancialStatements.LineProvenance.Should().Contain(row =>
            row.StatementId == "restatement-workflow" &&
            row.SourceKind == "RestatementLineage" &&
            row.EvidenceLinks.Contains("evidence:restatement:nav-correction:2027-04") &&
            row.EvidenceLinks.Contains("evidence:prior-package:lineage:2027-03"));

        var certifiedRestatement = await service.CertifyPackageAsync(new CertifyAccountingReportPackageRequestDto(
            readyRestatement.FinancialStatements.PackageId,
            "controller",
            "Controller approved the restatement package.",
            [CertificationEvidence(readyRestatement, "restatement-approval")]));

        certifiedRestatement.Should().NotBeNull();
        certifiedRestatement!.Certification.State.Should().Be(AccountingCertificationStateDto.Certified);
        certifiedRestatement.FinancialStatements.Restatement.Should().NotBeNull();
        certifiedRestatement.FinancialStatements.Restatement!.ApprovalState.Should().Be(ManualJournalEntryStatusDto.Approved);
        certifiedRestatement.FinancialStatements.Restatement.EvidenceLinks.Should()
            .Contain(CertificationEvidence(readyRestatement, "restatement-approval"));
        certifiedRestatement.NavPackage.Restatement.Should().NotBeNull();
        certifiedRestatement.NavPackage.Restatement!.ApprovalState.Should().Be(ManualJournalEntryStatusDto.Approved);
        certifiedRestatement.NavPackage.Restatement.EvidenceLinks.Should()
            .Contain(CertificationEvidence(readyRestatement, "restatement-approval"));
        certifiedRestatement.ExportArtifacts.Should().Contain(row =>
            row.ArtifactKind == "restatement-workflow" &&
            row.CertificationState == AccountingCertificationStateDto.Certified &&
            row.EvidenceLinks.Contains(CertificationEvidence(readyRestatement, "restatement-approval")));
    }

    [Fact]
    public async Task BuildPackageAsync_DoesNotReplaceCertifiedPackageEvidence()
    {
        var service = new AccountingReportPackageService();
        var ready = await service.BuildPackageAsync(CompletePackageRequest("fund-alpha", "2027-05"));
        var certified = await service.CertifyPackageAsync(new CertifyAccountingReportPackageRequestDto(
            ready.FinancialStatements.PackageId,
            "controller",
            "Controller certified the May report package.",
            [CertificationEvidence(ready)]));

        var replace = () => service.BuildPackageAsync(CompletePackageRequest(
            "fund-alpha",
            "2027-05",
            EvidenceLinks:
            [
                "evidence:ledger:trial-balance:2027-05-rebuild",
                "evidence:reconciliation:gl-tie-out:2027-05-rebuild",
                "evidence:report-render:financial-statements:2027-05-rebuild",
                "evidence:nav:support-package:2027-05-rebuild"
            ]));

        await replace.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*certified and immutable*restatement package*");
        var retained = await service.ListPackagesAsync("fund-alpha", "2027-05");
        retained.Should().ContainSingle();
        retained[0].Certification.State.Should().Be(AccountingCertificationStateDto.Certified);
        retained[0].Certification.EvidenceLinks.Should().Contain(CertificationEvidence(ready));
        retained[0].FinancialStatements.EvidenceLinks.Should().Contain("evidence:ledger:trial-balance:2027-05");
        retained[0].FinancialStatements.EvidenceLinks.Should().NotContain("evidence:ledger:trial-balance:2027-05-rebuild");
        certified!.Certification.State.Should().Be(AccountingCertificationStateDto.Certified);
    }

    [Fact]
    public async Task ListPackagesAsync_FiltersRetainedHistoryByLedgerBook()
    {
        var service = new AccountingReportPackageService();
        var primary = await service.BuildPackageAsync(CompletePackageRequest(
            "fund-alpha",
            "2027-06",
            LedgerBookId: DefaultLedgerBookId));
        var gaap = await service.BuildPackageAsync(CompletePackageRequest(
            "fund-alpha",
            "2027-06",
            LedgerBookId: AlternateLedgerBookId));

        var all = await service.ListPackagesAsync("fund-alpha", "2027-06");
        var primaryOnly = await service.ListPackagesAsync("fund-alpha", "2027-06", DefaultLedgerBookId);
        var gaapOnly = await service.ListPackagesAsync("fund-alpha", "2027-06", AlternateLedgerBookId);

        all.Should().Contain(row => row.FinancialStatements.LedgerBookId == primary.FinancialStatements.LedgerBookId);
        all.Should().Contain(row => row.FinancialStatements.LedgerBookId == gaap.FinancialStatements.LedgerBookId);
        primaryOnly.Should().ContainSingle(row => row.FinancialStatements.LedgerBookId == DefaultLedgerBookId);
        gaapOnly.Should().ContainSingle(row => row.FinancialStatements.LedgerBookId == AlternateLedgerBookId);
    }

    private static AccountingReportPackageRequestDto CompletePackageRequest(
        string fundProfileId,
        string periodId,
        string? RestatementReasonCode = null,
        string? PriorPackageId = null,
        Guid? CloseWorkflowId = null,
        Guid? LedgerBookId = null,
        IReadOnlyList<string>? EvidenceLinks = null)
        => new(
            FundProfileId: fundProfileId,
            PeriodId: periodId,
            Actor: "controller",
            LedgerBookId: LedgerBookId ?? DefaultLedgerBookId,
            CloseWorkflowId: CloseWorkflowId,
            BeginningCapital: 200_000m,
            Contributions: 25_000m,
            Distributions: 5_000m,
            RealizedGainLoss: 7_500m,
            Nav: 227_500m,
            RestatementReasonCode: RestatementReasonCode,
            PriorPackageId: PriorPackageId,
            EvidenceLinks: EvidenceLinks ??
            [
                $"evidence:ledger:trial-balance:{periodId}",
                $"evidence:reconciliation:gl-tie-out:{periodId}",
                $"evidence:report-render:financial-statements:{periodId}",
                $"evidence:nav:support-package:{periodId}"
            ]);

    private static string CertificationEvidence(
        AccountingReportPackageBundleDto package,
        string approvalLabel = "controller-approval")
        => $"evidence:report-certification:{approvalLabel}:{package.FinancialStatements.PackageId}:{package.Certification.CertificationId}:{package.FinancialStatements.PeriodId}";

    private static ClosePeriodPlanDto BuildSignedOffClosePlan(
        Guid workflowId,
        bool isPeriodLocked,
        Guid? ledgerBookId = null)
    {
        var periodStart = new DateOnly(2027, 2, 1);
        var periodEnd = new DateOnly(2027, 2, 28);
        var evidenceLinks = new[]
        {
            "evidence:close-task:controller-signoff",
            "evidence:close-package:period-lock"
        };
        var requirement = new CloseSignOffRequirementDto(
            "close-signoff-controller",
            "Controller",
            RequiredApprovalCount: 1,
            ApprovedCount: 1,
            IsSatisfied: true,
            EvidenceRequirement: "Evidence link with close sign-off or approval.");
        var task = new CloseTaskDto(
            "close-task-controller-review",
            "Controller review",
            CloseTaskStatusDto.SignedOff,
            "Controller",
            periodEnd,
            Dependencies: [],
            SignOffs:
            [
                new CloseSignOffDto(
                    "close-signoff-controller-review",
                    "Controller",
                    "controller",
                    ManualJournalEntryStatusDto.Approved,
                    DateTimeOffset.Parse("2027-02-28T18:00:00Z"),
                    evidenceLinks,
                    "Controller signed off the close task.")
            ],
            EvidenceLinks: evidenceLinks,
            SignOffRequirements: [requirement]);
        return new ClosePeriodPlanDto(
            $"close-plan-{workflowId:D}",
            "fund-alpha",
            LedgerBookId: ledgerBookId,
            "2027-02",
            periodStart,
            periodEnd,
            periodEnd,
            isPeriodLocked,
            Tasks: [task],
            LateAdjustments: [],
            new MaterialityPolicyDto(
                "close-materiality",
                AmountThreshold: 10_000m,
                PercentThreshold: 0.01m,
                "USD",
                "Controller",
                RequiresLateAdjustmentApproval: true),
            ValidationIssues: [],
            CloseCalendar:
            [
                new CloseCalendarMilestoneDto(
                    "close-calendar-controller-review",
                    task.TaskId,
                    task.DisplayName,
                    task.Owner,
                    task.DueDate,
                    task.Status,
                    IsBlocked: false,
                    IsSatisfied: true,
                    IsPeriodLocked: isPeriodLocked,
                    DependencyCount: 0,
                    RequiredSignOffCount: 1,
                    ApprovedSignOffCount: 1,
                    EvidenceLinks: evidenceLinks)
            ]);
    }

    private sealed class StubCloseManagementService(ClosePeriodPlanDto closePlan) : IAccountingCloseManagementService
    {
        public Task<ClosePeriodPlanDto?> GetPeriodPlanAsync(Guid workflowId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<ClosePeriodPlanDto?>(workflowId == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
                ? closePlan
                : null);
        }

        public Task<ClosePeriodPlanDto?> RequestLateAdjustmentAsync(
            CreateLateAdjustmentRequestDto request,
            string actor,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ClosePeriodPlanDto?> ReviewLateAdjustmentAsync(
            ReviewLateAdjustmentRequestDto request,
            string actor,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ClosePeriodPlanDto?> SignOffCloseTaskAsync(
            SignOffCloseTaskRequestDto request,
            string actor,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class MutableStubCloseManagementService(
        Guid workflowId,
        ClosePeriodPlanDto closePlan) : IAccountingCloseManagementService
    {
        public ClosePeriodPlanDto ClosePlan { get; set; } = closePlan;

        public Task<ClosePeriodPlanDto?> GetPeriodPlanAsync(Guid requestedWorkflowId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<ClosePeriodPlanDto?>(requestedWorkflowId == workflowId ? ClosePlan : null);
        }

        public Task<ClosePeriodPlanDto?> RequestLateAdjustmentAsync(
            CreateLateAdjustmentRequestDto request,
            string actor,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ClosePeriodPlanDto?> ReviewLateAdjustmentAsync(
            ReviewLateAdjustmentRequestDto request,
            string actor,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ClosePeriodPlanDto?> SignOffCloseTaskAsync(
            SignOffCloseTaskRequestDto request,
            string actor,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
