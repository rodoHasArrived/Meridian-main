using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class CapitalCallNoticeReportingTests
{
    private static readonly PrivateCapitalScopeDto Scope = new("tenant-a", "company-a", "entity-a", "fund-a",
        Guid.Parse("10000000-0000-0000-0000-000000000001"), "USD");
    private static readonly Guid JournalId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DateOnly NoticeDate = new(2026, 11, 10);

    [Fact]
    public async Task ResolveAsync_UsesExactApprovedRevisionAndExplicitRecipientMapping()
    {
        var (validator, store) = Setup();
        var result = await validator.ResolveAsync(Scope, "call-a", "investor-a");

        result.Recipient.Should().Be("reporting-user-17");
        result.Line.IssuanceJournalEntryId.Should().Be(JournalId);
        await store.Received(1).GetCommitmentAsync(Scope, "commitment-a", 2, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_UnpostedOrPartiallyIssuedInstallmentIsBlocked(bool partial)
    {
        var line = Line() with { IssuanceJournalEntryId = partial ? JournalId : null, IssuedAmount = partial ? 10m : 0m };
        var (validator, _) = Setup(line);

        var action = () => validator.ResolveAsync(Scope, "call-a", "investor-a");

        await action.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*no complete posted issuance*");
    }

    [Fact]
    public async Task ResolveAsync_AnotherInvestorCannotReadTheInstallment()
    {
        var (validator, _) = Setup();
        var action = () => validator.ResolveAsync(Scope, "call-a", "investor-b");
        await action.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*exactly one retained call installment*");
    }

    [Fact]
    public async Task ResolveAsync_MissingRecipientMappingDoesNotInferUserIdentity()
    {
        var (_, store) = Setup();
        var validator = new CapitalCallNoticeSourceValidator(store);
        var action = () => validator.ResolveAsync(Scope, "call-a", "investor-a");
        await action.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*explicit scoped notice recipient binding*");
    }

    [Fact]
    public async Task ValidateAsync_RejectsForgedCallAmountAndCrossTenantContext()
    {
        var (validator, _) = Setup();
        var parameters = Parameters();
        await validator.ValidateAsync(parameters, Access());
        var changed = parameters.TemplateParameters.ToDictionary(pair => pair.Key, pair => pair.Value);
        changed["callAmount"] = "1";
        var forged = () => validator.ValidateAsync(parameters with { TemplateParameters = changed }, Access());
        await forged.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();
        var foreign = () => validator.ValidateAsync(parameters, Access() with { TenantId = "tenant-b" });
        await foreign.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();
    }

    [Theory]
    [InlineData("investorId", "investor-b")]
    [InlineData("journalEntryId", "30000000-0000-0000-0000-000000000001")]
    [InlineData("externalGl.capitalActivityKind", "CapitalCallFunding")]
    [InlineData("externalGl.commitmentRevision", "3")]
    public void ValidateRows_RejectsForeignInvestorJournalAndFundingEvidence(string key, string value)
    {
        var rows = Rows();
        var altered = rows[0].ToDictionary(pair => pair.Key, pair => pair.Value);
        altered[key] = value;
        var action = () => CapitalCallNoticeSourceValidator.ValidateRows(Parameters(), [altered, rows[1]]);
        action.Should().Throw<ReportingAuthoritativeSourceUnavailableException>();
    }

    [Fact]
    public void ValidateRows_ExactRowsConserveCallAmountAndEmptySourceIsBlocked()
    {
        CapitalCallNoticeSourceValidator.ValidateRows(Parameters(), Rows());
        var missing = () => CapitalCallNoticeSourceValidator.ValidateRows(Parameters(), []);
        missing.Should().Throw<ReportingAuthoritativeSourceUnavailableException>();
        var changed = Rows()[0].ToDictionary(pair => pair.Key, pair => pair.Value);
        changed["debit"] = "99";
        var incorrectAmount = () => CapitalCallNoticeSourceValidator.ValidateRows(Parameters(), [changed, Rows()[1]]);
        incorrectAmount.Should().Throw<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*does not reproduce*");
    }

    [Fact]
    public void PrimaryDocument_ContainsOneInvestorAndRejectsBroadenedAudience()
    {
        var manifest = Manifest();
        var model = DocumentsReportingPrimaryDocumentRenderer.BuildModel(manifest);
        model.Title.Should().Be("Capital Call Notice");
        model.HeaderFields.Should().Contain(field => field.Label == "Investor" && field.Value == "investor-a");
        var broadened = manifest with
        {
            ImmutableAccessScope = manifest.ImmutableAccessScope! with { Mode = ReportingGovernanceAccessMode.CompanyWide }
        };
        var action = () => DocumentsReportingPrimaryDocumentRenderer.BuildModel(broadened);
        action.Should().Throw<ReportingGovernanceException>().WithMessage("*restricted recipient audience*");
    }

    [Fact]
    public async Task PostedOperationalNoticeCanReleaseBeforeClose_OrdinaryDraftReportsRemainBlocked()
    {
        var parameters = Parameters();
        var capturedAt = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var hash = new string('b', 64);
        var checkpoint = new ReportingAuthoritativeSourceCheckpoint("durable-ledger-journal", "ledger:notice",
            Scope.TenantId, "50000000-0000-0000-0000-000000000001", Scope.CompanyId, Scope.FundProfileId,
            Scope.LedgerBookId.ToString("D"), parameters.PeriodId, "Primary", NoticeDate,
            new DateTimeOffset(NoticeDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc), TimeSpan.Zero),
            1, 1, 2, "notice-checkpoint", hash, capturedAt,
            [$"reporting-source-checkpoint:notice-checkpoint:{hash}"]);
        var capture = new ReportingAuthoritativeSourceCapture(checkpoint, Rows());
        var source = Substitute.For<IReportingAuthoritativeSource>();
        source.CaptureAsync(Arg.Any<ReportingRunParametersDto>(), Arg.Any<ReportAccessQueryContext>(),
            Arg.Any<ReportingAuthoritativeSourceCaptureIntent>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(capture));
        var receipt = ReportingReconciliationEvidenceValidation.CreateReceipt(checkpoint,
            new ReportingReconciliationCompletionEvidence("posted-issuance-reconciled", new string('c', 64),
                capturedAt, false, ["posted-capital-call:" + JournalId]));
        receipt.CloseWorkflowCompletion.Should().BeNull("an issuance notice is not accounting-close evidence");
        var reconciliation = Substitute.For<IReportingReconciliationEvidenceSource>();
        reconciliation.ResolveAsync(Arg.Any<ReportingRunParametersDto>(), Arg.Any<ReportingAuthoritativeSourceCheckpoint>(),
            Arg.Any<ReportAccessQueryContext>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(receipt));
        var certification = new ReportingRunCertificationService(source, reconciliation);
        var template = new DefaultReportingTemplateCatalog().Get(CapitalCallNoticeReportingService.TemplateId);
        var readiness = new ReportingRunReadinessDto("evaluation", capturedAt,
            new VersionedReportTemplateIdDto(template.TemplateId, 1), parameters,
            ReportingRunReadinessStatusDto.Ready, true, false,
            [new ReportingRunReadinessCheckDto("posted-issuance", "Posted issuance", ReportingRunReadinessStatusDto.Ready,
                "The exact retained installment was posted.", 0, false, false, EvidenceReferences: ["posted:" + JournalId])],
            [], new string('d', 64));
        var certified = await certification.CertifyAsync(template, readiness, Access());
        var run = new GovernedReportingRun("notice-run", "notice-series", 1, template.TemplateId, "1.0.0",
            certified.OperationalScope, certified.AccessScope, certified.Snapshot, null!, capturedAt, null,
            GovernedReportingExecutionState.Succeeded, GovernedReportingState.Approved, 1, null, null, null, []);

        certified.AccessScope.Mode.Should().Be(ReportingGovernanceAccessMode.Restricted);
        certified.AccessScope.Principals.Should().ContainSingle(principal => principal.PrincipalId == "reporting-user-17");
        certified.AccessScope.Principals.Should().ContainSingle(principal => principal.Kind == ReportingAccessPrincipalKind.Group
            && principal.PrincipalId == "capital-reviewers");
        ReportingGovernanceCanonicalValidation.ValidateReleaseSnapshot(run);
        await certification.RevalidateForReleaseAsync(certified.Readiness.ResolvedParameters, template.TemplateId,
            checkpoint, certified.Snapshot, Access());

        var ordinaryRun = run with { TemplateId = "investor-monthly-statement" };
        var ordinaryDraft = () => ReportingGovernanceCanonicalValidation.ValidateReleaseSnapshot(ordinaryRun);
        ordinaryDraft.Should().Throw<ReportingGovernanceException>().WithMessage("*Draft-certified bytes cannot be released*");
        var ordinaryRevalidation = async () => await certification.RevalidateForReleaseAsync(
            certified.Readiness.ResolvedParameters, ordinaryRun.TemplateId, checkpoint, certified.Snapshot, Access());
        await ordinaryRevalidation.Should().ThrowAsync<ReportingGovernanceException>().WithMessage("*committed accounting-close*");
    }

    [Fact]
    public async Task NoticeReconciliationRetainsStablePostedIssuanceProofWithoutClaimingFinancialClose()
    {
        var (validator, _) = Setup();
        var parameters = Parameters();
        var capturedAt = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var hash = new string('b', 64);
        var checkpoint = new ReportingAuthoritativeSourceCheckpoint("durable-ledger-journal", "ledger:notice",
            Scope.TenantId, "50000000-0000-0000-0000-000000000001", Scope.CompanyId, Scope.FundProfileId,
            Scope.LedgerBookId.ToString("D"), parameters.PeriodId, "Primary", NoticeDate,
            new DateTimeOffset(NoticeDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc), TimeSpan.Zero),
            1, 1, 2, "notice-checkpoint", hash, capturedAt,
            [$"reporting-source-checkpoint:notice-checkpoint:{hash}"]);
        var source = Substitute.For<IReportingAuthoritativeSource>();
        source.CaptureAsync(Arg.Any<ReportingRunParametersDto>(), Arg.Any<ReportAccessQueryContext>(),
            Arg.Any<ReportingAuthoritativeSourceCaptureIntent>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new ReportingAuthoritativeSourceCapture(checkpoint, Rows())));
        var queue = Substitute.For<IReconciliationBreakQueueRepository>();
        queue.GetAllAsync(Arg.Any<ReconciliationBreakQueueScope>(), Arg.Any<ReconciliationBreakQueueStatus?>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>([]));
        var retained = new List<ReportingReconciliationEvidenceReceipt>();
        var durable = new InMemoryReportingReconciliationEvidenceStore();
        var store = Substitute.For<IReportingReconciliationEvidenceRetentionStore>();
        store.RetainAsync(Arg.Any<ReportingReconciliationEvidenceReceipt>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                retained.Add(call.ArgAt<ReportingReconciliationEvidenceReceipt>(0));
                return durable.RetainAsync(call.ArgAt<ReportingReconciliationEvidenceReceipt>(0));
            });
        var service = new CapitalCallNoticeReportingService(validator, null!, null!,
            authoritativeSource: source, reconciliationRetention: store);

        await service.ReconcilePostedIssuanceAsync(parameters, Access());
        await service.ReconcilePostedIssuanceAsync(parameters, Access());

        retained.Should().HaveCount(2);
        retained[0].CloseWorkflowCompletion.Should().BeNull();
        retained[0].EvidenceIds.Should().Contain("posted-capital-call:" + JournalId);
        retained[1].ReconciliationCheckpointHash.Should().Be(retained[0].ReconciliationCheckpointHash,
            "retrying one immutable issuance must not fabricate a newer reconciliation outcome");
        var closedClaim = () => ReportingReconciliationEvidenceValidation.RequireCommittedCloseWorkflow(retained[0]);
        closedClaim.Should().Throw<ArgumentException>();

        var reconciliation = new ReportingReconciliationEvidenceSource(durable, queue);
        var first = await reconciliation.ResolveAsync(parameters, checkpoint, Access());
        queue.GetAllAsync(Arg.Any<ReconciliationBreakQueueScope>(), Arg.Any<ReconciliationBreakQueueStatus?>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<ReconciliationBreakQueueItem>>(
                new InvalidOperationException("Unrelated close queue changed.")));
        await service.ReconcilePostedIssuanceAsync(parameters, Access());
        var template = new DefaultReportingTemplateCatalog().Get(CapitalCallNoticeReportingService.TemplateId);
        var readiness = new ReportingRunReadinessDto("operational-evaluation", capturedAt,
            new VersionedReportTemplateIdDto(template.TemplateId, 1), parameters,
            ReportingRunReadinessStatusDto.Ready, true, false,
            [new ReportingRunReadinessCheckDto("posted-issuance", "Posted issuance", ReportingRunReadinessStatusDto.Ready,
                "Retained installment agrees to its journal.", 0, false, false, EvidenceReferences: ["posted:" + JournalId])],
            [], new string('d', 64));
        var certification = new ReportingRunCertificationService(source, reconciliation);
        var certified = await certification.CertifyAsync(template, readiness, Access());
        await certification.RevalidateForReleaseAsync(parameters, template.TemplateId,
            checkpoint, certified.Snapshot, Access());
        retained[2].ReconciliationCheckpointHash.Should().Be(first.ReconciliationCheckpointHash);
        await queue.DidNotReceive().GetAllAsync(Arg.Any<ReconciliationBreakQueueScope>(),
            Arg.Any<ReconciliationBreakQueueStatus?>(), Arg.Any<CancellationToken>());
        var foreign = async () => await reconciliation.ResolveAsync(parameters with
            { Scope = parameters.Scope with { InvestorId = "investor-b" } }, checkpoint, Access());
        await foreign.Should().ThrowAsync<ReportingReconciliationEvidenceInvalidException>();
    }

    private static (CapitalCallNoticeSourceValidator, IPrivateCapitalStore) Setup(PrivateCapitalCallLineDto? line = null)
    {
        var store = Substitute.For<IPrivateCapitalStore>();
        store.GetCallAsync(Scope, "call-a", Arg.Any<CancellationToken>()).Returns(Call(line));
        store.GetCommitmentAsync(Scope, "commitment-a", 2, Arg.Any<CancellationToken>()).Returns(Commitment());
        return (new CapitalCallNoticeSourceValidator(store,
            [new CapitalCallNoticeRecipientBinding("tenant-a", "company-a", "fund-a", "investor-a", "reporting-user-17", "capital-reviewers")]), store);
    }

    private static PrivateCapitalCallLineDto Line() => new("installment-a", "commitment-a", 2,
        "investor-a", "capital-a", 100m, 500m, 400m, 100m, IssuanceJournalEntryId: JournalId);

    private static PrivateCapitalCallDto Call(PrivateCapitalCallLineDto? line = null) => new("call-a", Scope,
        "40000000-0000-0000-0000-000000000001", NoticeDate, NoticeDate.AddDays(15), 100m,
        "Investment funding", new string('a', 64), [line ?? Line()], "operator", DateTimeOffset.UtcNow);

    private static PrivateCapitalCommitmentDto Commitment() => new("commitment-a", Scope, 2,
        "investor-a", "capital-a", 500m, NoticeDate.AddMonths(-1), NoticeDate.AddMonths(-1), null, true,
        PrivateCapitalCommitmentApprovalStatus.Approved, ["vault:subscription"], "operator", DateTimeOffset.UtcNow);

    private static ReportingRunParametersDto Parameters() => new(
        new ReportingRunScopeDto("fund-a", ReportingEntityScopeKindDto.Investor, InvestorId: "investor-a",
            Dimensions: new LedgerDimensionSetDto(EntityId: "entity-a", InvestorId: "investor-a", CapitalAccountId: "capital-a",
                ExternalGlDimensions: new Dictionary<string, string>
                {
                    ["capitalCallId"] = "call-a", ["capitalActivityKind"] = "CapitalCallIssuance"
                })), Call().PeriodId, NoticeDate, new ReportingLedgerBookSelectionDto(Scope.LedgerBookId),
        ReportingAccountingBasisDto.Management, "USD", ReportingConsolidationLevelDto.Investor,
        ReportingOutputFormatDto.ClientPackage, ReportingFinalityDto.Draft, true, true,
        CapitalCallNoticeReportingService.BuildTemplateParameters(Call(), Line(), Commitment(), "reporting-user-17", "capital-reviewers"));

    private static ReportAccessQueryContext Access() => new("operator", [], "company-a", TenantId: "tenant-a", RequireBoundScope: true);

    private static ImmutableArray<IReadOnlyDictionary<string, string>> Rows()
    {
        IReadOnlyDictionary<string, string> Row(string debit, string credit) => new Dictionary<string, string>
        {
            ["investorId"] = "investor-a", ["capitalAccountId"] = "capital-a",
            ["journalEntryId"] = JournalId.ToString("D"), ["externalGl.capitalCallId"] = "call-a",
            ["externalGl.capitalActivityKind"] = "CapitalCallIssuance", ["externalGl.commitmentId"] = "commitment-a",
            ["externalGl.commitmentRevision"] = "2", ["debit"] = debit, ["credit"] = credit,
            ["recordedAtUtc"] = "2026-11-10T12:00:00.0000000+00:00"
        };
        return [Row("100", "0"), Row("0", "100")];
    }

    private static ReportingOutputManifest Manifest() => new("run-a", CapitalCallNoticeReportingService.TemplateId,
        NoticeDate, ReportingRunStatus.Draft, [], [], 1, ReportingRunTrigger.AdHoc,
        ResolvedParameters: Parameters(), CertifiedDatasetRows: Rows(),
        ImmutableAccessScope: new ReportingAccessScope("policy-a", "1", ReportingGovernanceAccessMode.Restricted,
            "operator", true, [new ReportingAccessPrincipalScope(ReportingAccessPrincipalKind.User, "reporting-user-17"),
                new ReportingAccessPrincipalScope(ReportingAccessPrincipalKind.Group, "capital-reviewers")], new string('a', 64)));
}
