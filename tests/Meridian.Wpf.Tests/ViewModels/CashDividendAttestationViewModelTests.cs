using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels.SecurityMaster;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class CashDividendAttestationViewModelTests
{
    [Fact]
    public async Task DefaultOffAndMissingBusinessCapabilityNeverEnableFinancialActions()
    {
        var fixture = new Fixture();
        fixture.Workspace = fixture.Workspace with { PolicyEnabled = false };
        using var vm = await fixture.OpenAsync();
        vm.DeclarationReason = "Selected book reviewed";
        vm.IsRoleBindingConfirmed = vm.IsConfirmed = vm.IsPostingConfirmed = true;
        vm.Reason = vm.PostingReason = "Inspected";
        vm.AttestCommand.CanExecute(null).Should().BeFalse();
        vm.PostCommand.CanExecute(null).Should().BeFalse();
        fixture.Workspace = fixture.Workspace with { PolicyEnabled = true, Declaration = null, CanBindAccountingRoles = false };
        await vm.RefreshWorkspaceAsync();
        vm.IsRoleBindingConfirmed = true;
        vm.ChangeDeclarationCommand.CanExecute(null).Should().BeFalse();
        await vm.ChangeDeclarationAsync();
        fixture.Declarations.Should().BeEmpty();
    }

    [Fact]
    public async Task DeclarationEchoesOnlyInspectedServerBookAndExplicitRoleBinding()
    {
        var fixture = new Fixture();
        fixture.Workspace = fixture.Workspace with { Declaration = null };
        using var vm = await fixture.OpenAsync();
        vm.DeclarationReason = "Reviewed existing book and promoted role binding";
        vm.ChangeDeclarationCommand.CanExecute(null).Should().BeFalse();
        vm.IsRoleBindingConfirmed = true;
        await vm.ChangeDeclarationCommand.ExecuteAsync(null);
        var request = fixture.Declarations.Should().ContainSingle().Which;
        request.Enabled.Should().BeTrue();
        request.LedgerBookId.Should().Be(fixture.BookId);
        request.AccountRoleBinding.Should().BeEquivalentTo(fixture.Binding);
        request.Authority.Should().BeNull("only the server may stamp business authority");
        request.PolicyHash.Should().Be(CorporateActionCashDividendAttestationPolicy.PolicyHash);
    }

    [Fact]
    public async Task AttestationUsesExactCaseVersionProjectionEligibilityAndCanonicalConfirmation()
    {
        var fixture = new Fixture();
        using var vm = await fixture.OpenAsync();
        vm.Reason = "Inspected complete related-event entitlement";
        vm.AttestCommand.CanExecute(null).Should().BeFalse();
        vm.IsConfirmed = true;
        await vm.AttestCommand.ExecuteAsync(null);
        var command = fixture.Attestations.Should().ContainSingle().Which;
        command.CaseId.Should().Be(fixture.Case.CaseId);
        command.ExpectedVersion.Should().Be(7);
        command.ProjectionId.Should().Be(fixture.Projection.ProjectionId);
        command.ExpectedDeclarationRevision.Should().Be(3);
        command.ExpectedEligibilityHash.Should().Be(fixture.Workspace.EvidenceHash);
        command.ScopeAssertion.Should().Be(fixture.Case.Scope);
        command.Confirmation.Should().Be(CorporateActionCashDividendAttestationPolicy.Confirmation);
        command.Authority.Should().BeNull();
        vm.IsConfirmed.Should().BeFalse();
        vm.Workspace.Should().BeNull("a successful command requires a fresh authoritative read");
    }

    [Fact]
    public async Task PostingIsASeparateConfirmedCommandBoundToRetainedAttestation()
    {
        var fixture = new Fixture();
        fixture.PreparePostingWorkspace();
        using var vm = await fixture.OpenAsync();
        vm.PostingReason = "Authorize the governed posting after retained attestation review";
        vm.PostCommand.CanExecute(null).Should().BeFalse();
        vm.IsPostingConfirmed = true;
        await vm.PostCommand.ExecuteAsync(null);
        var command = fixture.Posts.Should().ContainSingle().Which;
        command.AttestationId.Should().Be(fixture.Attestation.AttestationId);
        command.ProjectionId.Should().Be(fixture.Attestation.ProjectionId);
        command.ScopeAssertion.Should().Be(fixture.Case.Scope);
        command.Authority.Should().BeNull();
        fixture.Attestations.Should().BeEmpty("posting is not an implicit second attestation or ordinary approval");
    }

    [Fact]
    public async Task RenewedDeclarationAllowsExplicitReaffirmationOfActiveHistoricalAttestation()
    {
        var fixture = new Fixture();
        fixture.PreparePostingWorkspace();
        using var vm = await fixture.OpenAsync();
        vm.Reason = "Earlier confirmation must not authorize a renewed declaration";
        vm.IsConfirmed = true;

        fixture.PrepareReaffirmationWorkspace();
        await vm.RefreshWorkspaceAsync();
        vm.IsConfirmed.Should().BeFalse("renewing the declaration requires a fresh confirmation");
        vm.Reason = "Reaffirm unchanged entitlement under the renewed declaration";
        vm.AttestCommand.CanExecute(null).Should().BeFalse();
        vm.IsConfirmed = true;
        vm.AttestCommand.CanExecute(null).Should().BeTrue();
        vm.PostCommand.CanExecute(null).Should().BeFalse("the historical attestation is not bound to the renewed declaration");

        await vm.AttestCommand.ExecuteAsync(null);

        var command = fixture.Attestations.Should().ContainSingle().Which;
        command.ExpectedVersion.Should().Be(fixture.Workspace.Case.Version);
        command.ProjectionId.Should().Be(fixture.Attestation.ProjectionId);
        command.ExpectedDeclarationRevision.Should().Be(5).And.NotBe(fixture.Attestation.DeclarationRevision);
        command.ExpectedEligibilityHash.Should().Be(fixture.Workspace.EvidenceHash);
        command.Confirmation.Should().Be(CorporateActionCashDividendAttestationPolicy.Confirmation);
        command.Authority.Should().BeNull();
        fixture.Posts.Should().BeEmpty("reaffirmation does not implicitly post the journal");
        vm.IsConfirmed.Should().BeFalse();
        vm.Workspace.Should().BeNull("a new attestation requires a fresh authoritative workspace before posting");
    }

    [Theory]
    [InlineData("server-denied")]
    [InlineData("policy-disabled")]
    [InlineData("withdrawn-declaration")]
    [InlineData("posted-journal")]
    [InlineData("missing-evidence")]
    public async Task HistoricalAttestationCannotBypassCurrentReaffirmationControls(string blocker)
    {
        var fixture = new Fixture();
        fixture.PrepareReaffirmationWorkspace();
        fixture.Workspace = blocker switch
        {
            "server-denied" => fixture.Workspace with { CanAttestAccounting = false },
            "policy-disabled" => fixture.Workspace with { PolicyEnabled = false },
            "withdrawn-declaration" => fixture.Workspace with { Declaration = fixture.Workspace.Declaration! with { Enabled = false } },
            "posted-journal" => fixture.Workspace with { Posting = fixture.Posting },
            _ => fixture.Workspace with { EvidenceHash = null }
        };
        using var vm = await fixture.OpenAsync();
        vm.Reason = "Inspect renewed declaration and unchanged entitlement";
        vm.IsConfirmed = true;

        vm.AttestCommand.CanExecute(null).Should().BeFalse();
        await vm.AttestAsync();
        fixture.Attestations.Should().BeEmpty();
        fixture.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task UncertainPostingRetriesSameIdentityEvenAfterCommittedCaseVersionAdvances()
    {
        var fixture = new Fixture();
        fixture.PreparePostingWorkspace();
        fixture.PostResponse = ApiResponse<CorporateActionCashDividendPostingResultDto>.Fail("Response interrupted", 0, true);
        using var vm = await fixture.OpenAsync();
        vm.PostingReason = "Original reviewed posting";
        vm.IsPostingConfirmed = true;
        await vm.PostAsync();
        vm.HasPendingCommand.Should().BeTrue();
        fixture.Workspace = fixture.Workspace with
        {
            Case = fixture.Case with { Version = 9, State = CorporateActionCaseStates.Posted },
            Posting = fixture.Posting, CanPostAttestedAccounting = false
        };
        await vm.RefreshWorkspaceAsync();
        fixture.PostResponse = ApiResponse<CorporateActionCashDividendPostingResultDto>.Ok(new(fixture.Workspace.Case, fixture.Posting, null!, true));
        await vm.RetryCommand.ExecuteAsync(null);
        fixture.Posts.Should().HaveCount(2);
        fixture.Posts[1].Should().BeEquivalentTo(fixture.Posts[0]);
        vm.HasPendingCommand.Should().BeFalse();
    }

    [Fact]
    public async Task StaleRefusalClearsFinancialConfirmationAndRequiresNewWorkspace()
    {
        var fixture = new Fixture();
        fixture.AttestResponse = ApiResponse<CorporateActionCashDividendAttestationResultDto>.Fail("Stale eligibility", 409);
        using var vm = await fixture.OpenAsync();
        vm.Reason = "Original inspected operands";
        vm.IsConfirmed = true;
        await vm.AttestAsync();
        vm.Workspace.Should().BeNull();
        vm.IsConfirmed.Should().BeFalse();
        vm.HasPendingCommand.Should().BeFalse();
        vm.AttestCommand.CanExecute(null).Should().BeFalse();
        vm.Status.Should().Contain("refused");
    }

    [Fact]
    public async Task LateWorkspaceCannotAttachToANewerSecuritySelection()
    {
        var fixture = new Fixture();
        using var vm = await fixture.OpenAsync();
        var response = new TaskCompletionSource<ApiResponse<CorporateActionCashDividendWorkspaceDto>>();
        fixture.WorkspaceTask = response.Task;
        var refresh = vm.RefreshWorkspaceAsync();
        vm.SetSecurity(Guid.NewGuid());
        response.SetResult(ApiResponse<CorporateActionCashDividendWorkspaceDto>.Ok(fixture.Workspace));
        await refresh;
        vm.Workspace.Should().BeNull();
        vm.ProjectionText.Should().Contain("No eligible");
        vm.AttestCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LateMutationDoesNotOverwriteNewContextDraftOrClaimItsPosting()
    {
        var fixture = new Fixture();
        using var vm = await fixture.OpenAsync();
        var response = new TaskCompletionSource<ApiResponse<CorporateActionCashDividendAttestationResultDto>>();
        fixture.AttestationTask = response.Task;
        vm.Reason = "Original source review";
        vm.IsConfirmed = true;
        var mutation = vm.AttestAsync();
        vm.SetSecurity(Guid.NewGuid());
        vm.Reason = "New security draft";
        response.SetResult(ApiResponse<CorporateActionCashDividendAttestationResultDto>.Ok(new(fixture.Case, fixture.Attestation, null!, false)));
        await mutation;
        vm.Workspace.Should().BeNull();
        vm.Reason.Should().Be("New security draft");
        vm.PostingText.Should().Contain("No attested");
        vm.Status.Should().Contain("original context");
    }

    [Fact]
    public async Task ForeignCaseWorkspaceCannotSupplyCapabilities()
    {
        var fixture = new Fixture();
        fixture.Workspace = fixture.Workspace with { Case = fixture.Case with { SecurityId = Guid.NewGuid() } };
        using var vm = await fixture.OpenAsync();
        vm.Workspace.Should().BeNull();
        vm.IsConfirmed = true;
        vm.Reason = "Rejected foreign source";
        vm.AttestCommand.CanExecute(null).Should().BeFalse();
        await vm.AttestAsync();
        fixture.Attestations.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposedCommandsCannotReadDisposedTokenOrMutate()
    {
        var fixture = new Fixture();
        var vm = await fixture.OpenAsync();
        vm.Dispose();
        foreach (var command in new[] { vm.LoadCasesCommand, vm.RefreshWorkspaceCommand, vm.ChangeDeclarationCommand, vm.AttestCommand, vm.PostCommand, vm.RetryCommand })
        {
            command.CanExecute(null).Should().BeFalse();
            await command.ExecuteAsync(null);
        }
        fixture.Attestations.Should().BeEmpty();
        fixture.Posts.Should().BeEmpty();
    }

    [Fact]
    public void AccountingProofClassificationNeverSuppliesOrdinaryApprovalOrCoverageCredit()
    {
        var scope = new LedgerAmountScopeDto("tenant-a", "company-a", "fund-a", Guid.NewGuid(), Guid.NewGuid());
        var disclosure = new AccountingPreparerAttestationDisclosureDto(1, "PreparerAttestation", SinglePrincipalAttestationPolicy.Disclosure,
            scope, [Guid.NewGuid()], []) { AttestationId = Guid.NewGuid(), CaseId = Guid.NewGuid(), Actor = "principal-a" };
        var proof = new LedgerAmountProofDto("amount-a", scope, 100m, "USD", EvidenceStatusDto.Ready, [], [])
        { AccountingPreparerAttestations = [disclosure], OrdinaryRecordedApprovalSatisfied = false };
        var details = AccountingPreparerAttestationPresentation.Describe(proof, out var summary);
        details.Should().ContainSingle().Which.Should().Contain(SinglePrincipalAttestationPolicy.Disclosure);
        summary.Should().Contain("not satisfied").And.Contain("do not supply Verified Coverage");
        proof.Status.Should().Be(EvidenceStatusDto.Ready, "review classification is separate from source readiness");
        LedgerAmountApprovalClassification.MayCountAsOrdinaryApproval(proof, true).Should().BeFalse();
        var unknown = proof with { AccountingPreparerAttestations = [disclosure with { Classification = "IndependentReview" }] };
        AccountingPreparerAttestationPresentation.Describe(unknown, out _).Single().Should().Contain("unavailable");
    }

    [Theory]
    [InlineData("unsupported-status")]
    [InlineData("foreign-scope")]
    [InlineData("foreign-attestation")]
    public async Task PostingDisplayRequiresExactDurablePostedClassification(string defect)
    {
        var fixture = new Fixture();
        fixture.PreparePostingWorkspace();
        fixture.Workspace = fixture.Workspace with { Posting = defect switch
        {
            "unsupported-status" => fixture.Posting with { PostingStatus = "Pending" },
            "foreign-scope" => fixture.Posting with { LedgerBookId = Guid.NewGuid() },
            _ => fixture.Posting with { AttestationId = Guid.NewGuid() }
        } };
        using var vm = await fixture.OpenAsync();
        vm.PostingText.Should().Contain("classification unavailable").And.NotContain("Posted journal");
        fixture.Workspace = fixture.Workspace with { Posting = fixture.Posting };
        await vm.RefreshWorkspaceAsync();
        vm.PostingText.Should().Contain("Posted journal").And.Contain(CorporateActionCashDividendAttestationPolicy.Disclosure);
    }

    private sealed class Fixture : ICashDividendAttestationApiClient
    {
        public Guid BookId { get; } = Guid.NewGuid();
        public CorporateActionProcessingCaseDto Case { get; }
        public CorporateActionCaseAccountingProjectionDto Projection { get; }
        public CashDividendAccountRoleBindingDto Binding { get; }
        public CorporateActionCashDividendAttestationDto Attestation { get; }
        public CorporateActionCashDividendPostingDto Posting { get; }
        public CorporateActionCashDividendWorkspaceDto Workspace { get; set; }
        public Task<ApiResponse<CorporateActionCashDividendWorkspaceDto>>? WorkspaceTask { get; set; }
        public Task<ApiResponse<CorporateActionCashDividendAttestationResultDto>>? AttestationTask { get; set; }
        public ApiResponse<CorporateActionCashDividendAttestationResultDto> AttestResponse { get; set; }
        public ApiResponse<CorporateActionCashDividendPostingResultDto> PostResponse { get; set; }
        public List<ChangeCashDividendAttestationDeclarationRequestDto> Declarations { get; } = [];
        public List<AttestCorporateActionCashDividendRequestDto> Attestations { get; } = [];
        public List<PostAttestedCorporateActionCashDividendRequestDto> Posts { get; } = [];

        public Fixture()
        {
            var period = Guid.NewGuid();
            var now = DateTimeOffset.Parse("2026-11-12T12:00:00Z");
            var scope = new CorporateActionCaseScopeDto("tenant-a", "company-a", FundProfileId: "fund-a",
                FinancialAccountId: "account-a", LedgerBookId: BookId.ToString("D"), PeriodId: period.ToString("D"), AccountingBasis: "Accrual", FunctionalCurrency: "USD");
            Case = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), scope, CorporateActionCaseStates.ReadyForApproval, 7,
                null, "principal-a", null, "principal-a", now, "principal-a", now);
            Projection = new(Guid.NewGuid(), Case.CaseId, 7, Guid.NewGuid(), 1, 1, "projection-input", "posting-intent", "posting-key",
                "candidate", Guid.NewGuid(), 1, "synthetic-pack", "1", "dividend-declaration", "2", BookId, period, 1,
                "Accrual", "fund-a", "USD", new(2026, 11, 12), 100m, 100m, Guid.NewGuid(), 1, true, "principal-a", now, true);
            Binding = new(BookId, "receivable-existing", "income-existing", "synthetic-pack", "1", "dividend-declaration", "2");
            var declaration = new CashDividendAttestationDeclarationDto(Guid.NewGuid(), 3, "tenant-a", "company-a", BookId, true, "principal-a",
                CorporateActionCashDividendAttestationPolicy.Version, CorporateActionCashDividendAttestationPolicy.PolicyHash, "principal-a", now, "Selected book and roles", Binding);
            var eligibility = new CorporateActionCashDividendEligibilityDto(Case.SecurityId, new(2026, 11, 12), "related-event-a", "synthetic-provider",
                "source-a", "1", "source-hash", 1m, 100m, 100m, new(2026, 11, 1), "entitlement-hash", "{}", Binding);
            var preview = new CorporateActionCashDividendPreviewDto(Case, Projection, eligibility,
                CorporateActionCashDividendAttestationPolicy.Disclosure, CorporateActionCashDividendAttestationPolicy.Confirmation);
            Attestation = new(Guid.NewGuid(), Case.CaseId, Projection.ProjectionId, 8, declaration.DeclarationId, 3,
                CorporateActionCashDividendAttestationPolicy.Version, CorporateActionCashDividendAttestationPolicy.PolicyHash, Guid.NewGuid(), "related-event-a",
                new(2026, 11, 12), "synthetic-provider", "source-a", "1", "source-hash", "entitlement-hash", "{}", 100m, 100m,
                new(2026, 11, 1), "principal-a", now, "Inspected source", "evidence-ref", "eligibility-hash",
                CorporateActionCashDividendAttestationPolicy.Confirmation, CorporateActionCashDividendAttestationPolicy.Disclosure, AccountRoleBinding: Binding);
            Posting = new(Guid.NewGuid(), Case.CaseId, Projection.ProjectionId, Attestation.AttestationId, Guid.NewGuid(), BookId, period,
                "Accrual", "USD", 100m, 100m, "Posted", null, "principal-a", now);
            Workspace = new(1, "Available", CorporateActionCashDividendAttestationPolicy.Version, CorporateActionCashDividendAttestationPolicy.PolicyHash,
                true, "principal-a", scope, Case, true, true, false, declaration, [declaration], preview, null, null,
                "cash-dividend-eligibility:eligibility-hash", "eligibility-hash", CorporateActionCashDividendAttestationPolicy.Disclosure,
                CorporateActionCashDividendAttestationPolicy.Confirmation, 10000m, 100000m, [], CanBindAccountingRoles: true);
            AttestResponse = ApiResponse<CorporateActionCashDividendAttestationResultDto>.Ok(new(Case, Attestation, null!, false));
            PostResponse = ApiResponse<CorporateActionCashDividendPostingResultDto>.Ok(new(Case, Posting, null!, false));
        }
        public void PreparePostingWorkspace() => Workspace = Workspace with
        { Case = Case with { Version = 8, State = CorporateActionCaseStates.PreparerAttested }, Attestation = Attestation, CanAttestAccounting = false, CanPostAttestedAccounting = true };
        public void PrepareReaffirmationWorkspace()
        {
            PreparePostingWorkspace();
            Workspace = Workspace with
            {
                Declaration = Workspace.Declaration! with { Revision = 5 },
                Preview = Workspace.Preview! with { Case = Workspace.Case },
                CanAttestAccounting = true,
                CanPostAttestedAccounting = false
            };
        }
        public async Task<CashDividendAttestationViewModel> OpenAsync()
        {
            var vm = new CashDividendAttestationViewModel(this);
            vm.SetSecurity(Case.SecurityId);
            await vm.LoadCasesAsync();
            vm.SelectedCase = vm.Cases.Single();
            await vm.RefreshWorkspaceAsync();
            return vm;
        }
        public Task<ApiResponse<CorporateActionProcessingCaseDto[]>> ListCashDividendCasesAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<CorporateActionProcessingCaseDto[]>.Ok([Case]));
        public Task<ApiResponse<CorporateActionCashDividendWorkspaceDto>> GetCashDividendWorkspaceAsync(Guid caseId, CancellationToken ct = default)
            => WorkspaceTask ?? Task.FromResult(ApiResponse<CorporateActionCashDividendWorkspaceDto>.Ok(Workspace));
        public Task<ApiResponse<CashDividendAttestationDeclarationResultDto>> ChangeCashDividendDeclarationAsync(Guid caseId, ChangeCashDividendAttestationDeclarationRequestDto request, CancellationToken ct = default)
        {
            Declarations.Add(request);
            return Task.FromResult(ApiResponse<CashDividendAttestationDeclarationResultDto>.Ok(new(Workspace.Declaration
                ?? new(Guid.NewGuid(), 1, request.TenantId, request.CompanyId, request.LedgerBookId, request.Enabled, request.Actor,
                    request.PolicyVersion, request.PolicyHash, request.Actor, DateTimeOffset.UtcNow, request.Reason, request.AccountRoleBinding), false)));
        }
        public Task<ApiResponse<CorporateActionCashDividendAttestationResultDto>> AttestCashDividendAsync(AttestCorporateActionCashDividendRequestDto request, CancellationToken ct = default)
        { Attestations.Add(request); return AttestationTask ?? Task.FromResult(AttestResponse); }
        public Task<ApiResponse<CorporateActionCashDividendPostingResultDto>> PostAttestedCashDividendAsync(PostAttestedCorporateActionCashDividendRequestDto request, CancellationToken ct = default)
        { Posts.Add(request); return Task.FromResult(PostResponse); }
    }
}
