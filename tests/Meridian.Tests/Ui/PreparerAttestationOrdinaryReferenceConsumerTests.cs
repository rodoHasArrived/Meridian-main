using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Reporting;
using Meridian.Storage;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Meridian.Tests.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class PreparerAttestationOrdinaryReferenceConsumerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-reserved-approval-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> References => new()
    {
        "preparer-attestation:v1:11111111-1111-4111-8111-111111111111",
        "  PREPARER-ATTESTATION:v99:unknown ",
        "https://localhost/api/workstation/evidence/attestations/receipts/11111111-1111-4111-8111-111111111111",
        "a77e57ed-1111-8111-8111-111111111111",
        "{A77E57ED-1111-8111-8111-111111111111}"
    };

    [Theory]
    [MemberData(nameof(References))]
    public async Task AssetProfileCannotApproveOrRollbackUsingPreparerAssertion(string reference)
    {
        var service = new SecurityAssetProfileGovernanceService(new StorageOptions { RootPath = _root });
        var draft = await service.DraftProfileAsync(
            SecurityAssetProfileGovernanceServiceTests.CreateDraftRequest("custom-attestation-ref"), "preparer");
        var approve = SecurityAssetProfileGovernanceServiceTests.Approval(draft.Profile.ProfileId, draft.Profile.Version);
        await service.Invoking(value => value.ApproveProfileAsync(approve with { ApprovalReference = reference }, "controller"))
            .Should().ThrowAsync<ArgumentException>();
        service.GetAllProfiles().Single(value => value.ProfileId == draft.Profile.ProfileId).Status
            .Should().Be(SecurityAssetProfileStatusDto.Draft);

        var approved = await service.ApproveProfileAsync(approve, "controller");
        var rollback = new SecurityAssetProfileRollbackRequestDto(approved.Profile.ProfileId, approved.Profile.Version,
            new DateOnly(2026, 7, 1), reference, null, "Reviewed rollback", "rollback-refusal");
        await service.Invoking(value => value.RollbackProfileAsync(rollback, "controller"))
            .Should().ThrowAsync<ArgumentException>();
        service.GetLineage(approved.Profile.ProfileId).Versions.Should().ContainSingle()
            .Which.Version.Should().Be(approved.Profile.Version);
    }

    [Theory]
    [MemberData(nameof(References))]
    public async Task LedgerReopenCannotTreatPreparerAssertionAsControllerApproval(string reference)
    {
        var store = Substitute.For<ILedgerJournalStore>();
        var service = new PostgresLedgerBookService(store);
        var book = new LedgerBookRecord(Guid.NewGuid(), "alpha-fund", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Alpha Fund", "USD", Now, Now);
        var period = new LedgerAccountingPeriod(Guid.NewGuid(), book.LedgerBookId, 2026, 5, "2026-P05",
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), "HardClosed", Now.AddDays(-2), Now.AddDays(-1), 3);
        store.GetPeriodAsync(period.PeriodId, Arg.Any<CancellationToken>()).Returns(period);
        store.GetLedgerBookAsync(book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(book);
        var request = new ReopenLedgerPeriodRequest("controller", "Controller", "Reviewed restatement", reference,
            [$"evidence://restatement/reversal/period/{period.PeriodId:D}/book/{book.LedgerBookId:D}/approval/{reference}"]);

        await service.Invoking(value => value.ReopenPeriodAsync(period.PeriodId, request))
            .Should().ThrowAsync<ArgumentException>();

        await store.DidNotReceiveWithAnyArgs().SavePeriodAsync(default!, default, default, default);
    }

    [Theory]
    [MemberData(nameof(References))]
    public async Task InstrumentProjectionCannotRetainPreparerAssertionAsWriteApproval(string reference)
    {
        var store = new InMemoryAssetOperationsProjectionStore();
        var projection = InstrumentPositionProjectionFixture.Create();
        var approval = InstrumentPositionProjectionFixture.Approval with { ApprovalReference = reference };

        await store.Invoking(value => value.UpsertAsync(projection, approval)).Should().ThrowAsync<ArgumentException>();

        (await store.GetAsync(InstrumentPositionProjectionFixture.SecurityId)).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(References))]
    public void OperationsReopenAndBreakDispositionCannotUsePreparerAssertionApproval(string reference)
    {
        var workflow = new OperationsContinuityWorkflow { IsClosed = true };
        var reopen = workflow.GetReopenTransitionBlocker(new OperationsReopenWorkflowRequestDto(
            1, "controller", "Reviewed reopen", "INC-1", true, "Controller review", reference, "Review period"));
        reopen.Should().NotBeNull();
        reopen!.Code.Should().Be("PREPARER_ATTESTATION_NOT_APPROVAL");
        workflow.IsClosed.Should().BeTrue();

        var breakCase = new OperationsBreakCaseDto("break-one", "check-one", "Cash", "Critical", "Open", "preparer",
            null, "statement", "ledger", 100m, 110m, 10m, null, null, "Investigate", []);
        workflow.BreakCases.Add(breakCase);
        var disposition = workflow.ResolveBreakCase(breakCase.BreakId,
            new OperationsResolveBreakCaseRequestDto(1, "preparer", "Waived", "Reviewed waiver",
                ApprovalActor: "controller", ApprovalReference: reference), [], Now);
        disposition.Should().NotBeNull();
        disposition!.Code.Should().Be("PREPARER_ATTESTATION_NOT_APPROVAL");
        workflow.BreakCases.Single().Should().Be(breakCase);
    }

    [Theory]
    [MemberData(nameof(References))]
    public void NewReportingReceiptCannotPromotePreparerAssertionToOrdinaryApproval(string reference)
    {
        var item = new ReconciliationBreakQueueItem("break-one", "run-one", "Reconciliation",
            ReconciliationBreakCategory.ExternalStatementMismatch, ReconciliationBreakQueueStatus.Resolved,
            0m, "Reviewed exception", "preparer", Now, Now,
            ResolvedBy: "preparer", EvidenceLinks: ["evidence:statement-one"],
            Measures:
            [
                new(ReconciliationBreakMeasureKindDto.Value, 100m, 100m, 0m, 0m, "USD"),
                new(ReconciliationBreakMeasureKindDto.Quantity, 10m, 10m, 0m, 0m, "units"),
                new(ReconciliationBreakMeasureKindDto.CostBasis, 100m, 100m, 0m, 0m, "USD")
            ],
            Disposition: ReconciliationBreakDispositionDto.Waived,
            DispositionReason: "Reviewed exception", DispositionApprovedBy: "controller",
            DispositionApprovalReference: reference);
        var evidence = ReportingReconciliationEvidenceValidation.CreateBreakEvidence(item);
        var completion = new ReportingReconciliationCompletionEvidence("completion-one", new string('5', 64), Now,
            false, ["evidence:statement-one"], [evidence]);
        var source = new ReportingAuthoritativeSourceCheckpoint("durable-ledger-journal", "ledger:book:period",
            "tenant-one", "organization-one", "company-one", "fund-one", "book-one", "period-one", "Gaap",
            new DateOnly(2026, 11, 12), Now, 1, 1, 2, "source-one", new string('1', 64), Now, ["evidence:statement-one"]);

        // Preserve the historical validation contract; only new authority construction refuses
        // the newly reserved namespace rather than rewriting an old frozen receipt's labels.
        Action readHistoricalOperands = () => ReportingReconciliationEvidenceValidation.ValidateCompletion(completion);
        readHistoricalOperands.Should().NotThrow();
        Action create = () => ReportingReconciliationEvidenceValidation.CreateReceipt(source, completion);
        create.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
