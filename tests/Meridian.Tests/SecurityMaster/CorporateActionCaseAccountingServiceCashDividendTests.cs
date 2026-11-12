using FluentAssertions;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class CorporateActionCaseAccountingServiceTests
{
    [Fact]
    public async Task CashDividendAttestation_IsDistinctFromOrdinaryApproval_AndUsesCompleteEventProof()
    {
        var (fixture, native, _, service, eligibility) = await CashDividendFixtureAsync();
        CorporateActionCashDividendAttestationDto? retained = null;
        native.AttestCashDividendAsync(Arg.Any<AttestCorporateActionCashDividendRequestDto>(),
            Arg.Do<CorporateActionCashDividendAttestationDto>(value => retained = value), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => CashDividendResult(
                fixture.Case, call.Arg<CorporateActionCashDividendAttestationDto>()));

        var result = await service.AttestCashDividendAsync(CashDividendRequest(fixture.Case, eligibility));

        retained.Should().NotBeNull();
        retained!.DecisionKind.Should().Be("PreparerAttestation");
        retained.AttestedBy.Should().Be("fund-accountant");
        retained.Disclosure.Should().Be(SinglePrincipalAttestationPolicy.Disclosure);
        retained.EntitlementSnapshotHash.Should().Be(eligibility.EntitlementSnapshotHash);
        retained.SourceEvidenceHash.Should().Be(eligibility.SourceEvidenceHash);
        PreparerAttestationApprovalBoundary.IsReservedReference(retained.AttestationId.ToString("D")).Should().BeTrue();
        result.Case.State.Should().Be(CorporateActionCaseStates.PreparerAttested);
        await fixture.Store.DidNotReceiveWithAnyArgs().ApproveAccountingAsync(default!, default!, default, default!, default);
    }

    [Theory]
    [InlineData(10_000.01)]
    [InlineData(100_000)]
    public async Task CashDividendAttestation_PartUnderCapCannotHideAnAboveLimitWholeEvent(decimal wholeGross)
    {
        var (fixture, native, resolver, service, eligibility) = await CashDividendFixtureAsync();
        resolver.ResolveAsync(Arg.Any<CorporateActionProcessingCaseDto>(), Arg.Any<CorporateActionCaseAccountingProjectionDto>(),
            Arg.Any<AssetAccountingEventSpineDto>(), Arg.Any<CancellationToken>()).Returns(eligibility with { WholeEventGrossUsd = wholeGross });

        var exception = await Assert.ThrowsAsync<CorporateActionOperationException>(() =>
            service.AttestCashDividendAsync(CashDividendRequest(fixture.Case, eligibility)));

        exception.Code.Should().Be(CorporateActionCashDividendAttestationValidation.ExcludedProblemCode);
        await native.DidNotReceiveWithAnyArgs().AttestCashDividendAsync(default!, default!, default, default!, default);
    }

    [Fact]
    public async Task CashDividendAttestation_RequiresTheProjectionPreparerAndCurrentBookDeclaration()
    {
        var (fixture, native, _, service, eligibility) = await CashDividendFixtureAsync();
        await Assert.ThrowsAsync<CorporateActionPermissionDeniedException>(() => service.AttestCashDividendAsync(
            CashDividendRequest(fixture.Case, eligibility) with { Actor = "another-operator" }));
        native.GetCashDividendDeclarationAsync("tenant-a", "company-a", Arg.Any<CancellationToken>())
            .Returns(CashDividendDeclaration() with { LedgerBookId = Guid.NewGuid() });
        var error = await Assert.ThrowsAsync<CorporateActionOperationException>(() =>
            service.AttestCashDividendAsync(CashDividendRequest(fixture.Case, eligibility)));
        error.Code.Should().Be("cash_dividend_declaration_stale");
        await native.DidNotReceiveWithAnyArgs().AttestCashDividendAsync(default!, default!, default, default!, default);
    }

    [Theory]
    [InlineData("caller-claimed-proof", false)]
    [InlineData("cash-dividend-eligibility:wrong", false)]
    [InlineData("", true)]
    public async Task CashDividendAttestation_CannotUseArbitraryProofReferences(string reference, bool invalidRequest)
    {
        var (fixture, native, _, service, eligibility) = await CashDividendFixtureAsync();
        var action = () => service.AttestCashDividendAsync(CashDividendRequest(fixture.Case, eligibility)
            with { EvidenceReference = reference });
        if (invalidRequest)
            await action.Should().ThrowAsync<CorporateActionValidationException>();
        else
            (await action.Should().ThrowAsync<CorporateActionOperationException>()).Which.Code
                .Should().Be(CorporateActionProblemCodes.ProjectionStale);
        await native.DidNotReceiveWithAnyArgs().AttestCashDividendAsync(default!, default!, default, default!, default);
    }

    [Theory]
    [InlineData(OperationsActionOriginDto.AssistantDraft)]
    [InlineData(OperationsActionOriginDto.AutomationAssistant)]
    public async Task CashDividendAttestation_RefusesNonHumanOriginBeforeNativeAuthority(OperationsActionOriginDto origin)
    {
        var (fixture, native, _, service, eligibility) = await CashDividendFixtureAsync();
        await Assert.ThrowsAsync<CorporateActionPermissionDeniedException>(() => service.AttestCashDividendAsync(
            CashDividendRequest(fixture.Case, eligibility) with { Authority = new(true, true, origin) }));
        await native.DidNotReceiveWithAnyArgs().GetCashDividendAttestationReceiptAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task CashDividendAttestation_ExactHistoricalRetryDoesNotRequireCurrentWriteGrantOrProjection()
    {
        var (fixture, native, _, _, eligibility) = await CashDividendFixtureAsync();
        var request = CashDividendRequest(fixture.Case, eligibility);
        var receipt = CashDividendAttestation(fixture.Case, eligibility);
        native.GetCashDividendAttestationReceiptAsync(CaseId, request.IdempotencyKey, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CashDividendResult(fixture.Case, receipt));
        var service = new CorporateActionCaseAccountingService(fixture.Store, cashDividendStore: native);

        var result = await service.AttestCashDividendAsync(request with { Authority = new(false, false) });

        result.Replayed.Should().BeTrue();
        result.Attestation.AttestationId.Should().Be(receipt.AttestationId);
        await native.DidNotReceiveWithAnyArgs().AttestCashDividendAsync(default!, default!, default, default!, default);
    }

    [Fact]
    public async Task CashDividendAttestation_ExactRetryFingerprintIgnoresNewHttpTraceAndCurrentGrantSnapshot()
    {
        var (fixture, native, _, _, eligibility) = await CashDividendFixtureAsync();
        var request = CashDividendRequest(fixture.Case, eligibility);
        var fingerprints = new List<string>();
        native.GetCashDividendAttestationReceiptAsync(CaseId, request.IdempotencyKey,
                Arg.Do<string>(fingerprint => fingerprints.Add(fingerprint)), Arg.Any<CancellationToken>())
            .Returns(CashDividendResult(fixture.Case, CashDividendAttestation(fixture.Case, eligibility)));
        var service = new CorporateActionCaseAccountingService(fixture.Store, cashDividendStore: native);

        await service.AttestCashDividendAsync(request with { CorrelationId = "http-request-1" });
        await service.AttestCashDividendAsync(request with { CorrelationId = "http-request-2", Authority = new(false, false) });

        fingerprints.Should().HaveCount(2).And.OnlyContain(fingerprint => fingerprint == fingerprints[0]);
    }

    [Fact]
    public async Task CashDividendAttestation_DoesNotRelaxTheOrdinarySelfApprovalGuard()
    {
        var (fixture, _, _, service, _) = await CashDividendFixtureAsync();
        var exception = await Assert.ThrowsAsync<CorporateActionOperationException>(() => service.ApproveAsync(
            ApproveRequest() with { Actor = "fund-accountant" }));
        exception.Code.Should().Be(CorporateActionProblemCodes.MakerCheckerRequired);
        await fixture.Store.DidNotReceiveWithAnyArgs().ApproveAccountingAsync(default!, default!, default, default!, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OrdinaryAccountingApproval_CannotReusePreparerReceiptIdentityOrReference(bool receiptIdentity)
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.ReadyForApproval).WithCurrentProjection();
        var receiptId = PreparerAttestationApprovalBoundary.NewReceiptId();
        var request = receiptIdentity
            ? ApproveRequest() with { ApprovalId = receiptId }
            : ApproveRequest() with { EvidenceReference = PreparerAttestationApprovalBoundary.ReceiptReference(receiptId) };

        await Assert.ThrowsAsync<CorporateActionValidationException>(() => fixture.Service.ApproveAsync(request));

        await fixture.Store.DidNotReceiveWithAnyArgs().ApproveAccountingAsync(default!, default!, default, default!, default);
    }

    private static async Task<(Fixture Fixture, ICorporateActionCashDividendAccountingStore Native,
        ICorporateActionCashDividendEligibilityResolver Resolver, CorporateActionCaseAccountingService Service,
        CorporateActionCashDividendEligibilityDto Eligibility)> CashDividendFixtureAsync()
    {
        var fixture = Fixture.Create(caseState: CorporateActionCaseStates.ReadyForApproval)
            .WithCurrentProjection().WithPeriod(LedgerPeriodStatusDto.Open, version: 4);
        var action = new CorporateActionDto(Guid.NewGuid(), SecurityId, CorporateActionEventTypes.Dividend,
            new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 14), 0.01m, "USD", null, null, null,
            null, null, null, null, RecordDate: new DateOnly(2026, 8, 14), LifecycleState: "Confirmed");
        var processingCase = fixture.Case with { SourceSnapshot = new(action,
            new("fixture-provider", "dividend-20260814", "1", DateTimeOffset.UtcNow, new string('f', 64),
                "provider://fixture/dividend/v1", CorporateActionProviderReleaseStatusDto.AcceptanceEligible), null) };
        fixture.Store.GetCaseAsync(CaseId, "tenant-a", "company-a", Arg.Any<CancellationToken>()).Returns(processingCase);
        var projection = await fixture.Store.GetAccountingProjectionAsync(CaseId, "tenant-a", "company-a");
        const string snapshot = "{\"synthetic\":true,\"population\":\"full-book\"}";
        var eligibility = new CorporateActionCashDividendEligibilityDto(SecurityId, action.ExDate,
            CorporateActionCashDividendAttestationValidation.RelatedEventKey(SecurityId, action.ExDate),
            "fixture-provider", "dividend-20260814", "1", new string('f', 64), 0.01m,
            projection!.TotalDebits, projection.TotalDebits, new(2026, 8, 1), Sha256Digest.ComputeUtf8(snapshot), snapshot,
            CashDividendBinding());
        var native = Substitute.For<ICorporateActionCashDividendAccountingStore>();
        native.GetCashDividendDeclarationAsync("tenant-a", "company-a", Arg.Any<CancellationToken>())
            .Returns(CashDividendDeclaration());
        var resolver = Substitute.For<ICorporateActionCashDividendEligibilityResolver>();
        resolver.ResolveAsync(Arg.Any<CorporateActionProcessingCaseDto>(), Arg.Any<CorporateActionCaseAccountingProjectionDto>(),
            Arg.Any<AssetAccountingEventSpineDto>(), Arg.Any<CancellationToken>()).Returns(eligibility);
        var service = new CorporateActionCaseAccountingService(fixture.Store, fixture.SpineStore,
            fixture.PostingService, fixture.LedgerBookService, native, resolver);
        // Keep the fixture's existing identity/version, but return the source-complete case to C callers.
        var wrapped = new Fixture
        {
            Store = fixture.Store, SpineStore = fixture.SpineStore, PostingService = fixture.PostingService,
            LedgerBookService = fixture.LedgerBookService, Service = service, Case = processingCase,
            Spine = fixture.Spine, DraftedCandidateFingerprint = fixture.DraftedCandidateFingerprint
        };
        return (wrapped, native, resolver, service, eligibility);
    }

    private static CashDividendAttestationDeclarationDto CashDividendDeclaration() => new(
        Guid.Parse("4ad0c000-0000-4000-8000-000000000001"), 1, "tenant-a", "company-a", LedgerBookId,
        true, "fund-accountant", CorporateActionCashDividendAttestationPolicy.Version,
        CorporateActionCashDividendAttestationPolicy.PolicyHash, "fund-accountant", DateTimeOffset.UtcNow,
        "Synthetic scoped declaration", AccountRoleBinding: CashDividendBinding());

    private static CashDividendAccountRoleBindingDto CashDividendBinding() => new(LedgerBookId,
        "assets:dividend-receivable", "income:dividends", "corp-act-pack", "7", "cash-dividend", "3",
        DividendReceivableAccountName: "Dividend receivable", DividendIncomeAccountName: "Dividend income");

    private static AttestCorporateActionCashDividendRequestDto CashDividendRequest(
        CorporateActionProcessingCaseDto processingCase, CorporateActionCashDividendEligibilityDto eligibility) => new(
        CaseId, processingCase.Version, "attest-dividend:v1", "tenant-a", "company-a", ProjectionId, 1,
        eligibility.EntitlementSnapshotHash, "Inspected this exact complete-book entitlement",
        "cash-dividend-eligibility:" + eligibility.EntitlementSnapshotHash, eligibility.EntitlementSnapshotHash,
        CorporateActionCashDividendAttestationPolicy.Confirmation, "fund-accountant", Scope(),
        Authority: new(true, false));

    private static CorporateActionCashDividendAttestationDto CashDividendAttestation(
        CorporateActionProcessingCaseDto processingCase, CorporateActionCashDividendEligibilityDto eligibility) => new(
        PreparerAttestationApprovalBoundary.NewReceiptId(), CaseId, ProjectionId, processingCase.Version,
        CashDividendDeclaration().DeclarationId, 1, CorporateActionCashDividendAttestationPolicy.Version,
        CorporateActionCashDividendAttestationPolicy.PolicyHash, Guid.NewGuid(), eligibility.RelatedEventKey,
        eligibility.ExDate, eligibility.ProviderId, eligibility.SourceEventId, eligibility.SourceEventVersion,
        eligibility.SourceEvidenceHash, eligibility.EntitlementSnapshotHash, eligibility.EntitlementSnapshotJson,
        eligibility.GrossImpactUsd, eligibility.WholeEventGrossUsd, eligibility.BudgetMonth, "fund-accountant",
        DateTimeOffset.UtcNow, "Synthetic retained attestation", "cash-dividend-eligibility:" + eligibility.EntitlementSnapshotHash,
        eligibility.EntitlementSnapshotHash, CorporateActionCashDividendAttestationPolicy.Confirmation,
        CorporateActionCashDividendAttestationPolicy.Disclosure, AccountRoleBinding: CashDividendBinding());

    private static CorporateActionCashDividendAttestationResultDto CashDividendResult(
        CorporateActionProcessingCaseDto processingCase, CorporateActionCashDividendAttestationDto receipt) => new(
        processingCase with { State = CorporateActionCaseStates.PreparerAttested, Version = processingCase.Version + 1 },
        receipt, Transition(CorporateActionCaseStates.ReadyForApproval, CorporateActionCaseStates.PreparerAttested), false);
}
