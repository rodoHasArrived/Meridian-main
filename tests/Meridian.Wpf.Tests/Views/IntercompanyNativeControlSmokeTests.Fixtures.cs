using Meridian.Contracts.Api;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Wpf.Tests.Views;

public sealed partial class IntercompanyNativeControlSmokeTests
{
    private const decimal ExactBalance = 12345678901234567890.12345678m;
    private static readonly DateOnly ObservationDate = new(2026, 11, 13);
    private static readonly DateTimeOffset RecordedAt = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly string SyntheticHash = new('a', 64);

    /// <summary>Synthetic server workflow; asserts UI transport/state, never domain approval or accounting correctness.</summary>
    private sealed class AgreementClient : IWorkstationIntercompanyAgreementApiClient
    {
        private readonly Guid _agreementId = Guid.NewGuid();
        private readonly Guid _partyOne = Guid.NewGuid();
        private readonly Guid _partyTwo = Guid.NewGuid();
        public Guid BookId { get; } = Guid.NewGuid();
        public string SubmittedHash => SyntheticHash;
        public IntercompanyAgreementDetailDto Detail { get; private set; }
        public IntercompanyAgreementCommandDto? LastSave { get; private set; }
        public IntercompanyAgreementCommandDto? LastApproval { get; private set; }

        public AgreementClient()
        {
            var structured = new IntercompanyStructuredTermsDto(
                new(new(2026, 1, 1), new(2027, 1, 1), IntercompanyFrequencyDto.Monthly, "WEEKENDS", BusinessDayConvention.Following, 2),
                IntercompanyRateBasisDto.Fixed, 0.0523456789m, DayCount: IntercompanyDayCountDto.Actual365,
                Compounding: IntercompanyCompoundingDto.Simple);
            var draft = new IntercompanyAgreementDraftDto("SYN-LOAN", "Synthetic treasury loan", _partyOne, "Lender", _partyTwo, "Borrower",
                IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 1, 1), new(2027, 1, 1),
                new IntercompanyAgreementTermsDto(Notes: "Synthetic executed loan terms") { StructuredTerms = structured },
                [new("evidence://evidence-vault/synthetic-loan", "synthetic-document", SyntheticHash)]);
            var proposal = new IntercompanyAgreementProposalDto(Guid.NewGuid(), IntercompanyAgreementActionDto.Activate,
                IntercompanyAgreementProposalStateDto.Draft, draft, null, ["synthetic-operator"], null, null, null,
                RecordedAt, RecordedAt, "Synthetic review scenario");
            Detail = new(_agreementId, 1, draft.Code, draft.Name, _partyOne, "Lender", _partyTwo, "Borrower", draft.AgreementType,
                "USD", ObservationDate, IntercompanyAgreementStatusDto.Unapproved, null, null, [proposal], [], [], Capabilities(proposal.State));
        }

        public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyAgreementListDto>.Ok(new([Detail], null) { CanCreate = true }));
        public Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(agreementId == _agreementId ? ApiResponse<IntercompanyAgreementDetailDto>.Ok(Detail with { AsOf = asOf })
                : ApiResponse<IntercompanyAgreementDetailDto>.Fail("Synthetic agreement not found", 404));
        public Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyAgreementJournalLinesDto>.Ok(new(agreementId, ledgerBookId, asOf, "Covered", [], null)));

        public Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default)
        {
            var pending = Detail.Proposals.Last();
            switch (action)
            {
                case "save":
                    LastSave = command;
                    pending = pending with { Draft = command.Draft!, UpdatedAtUtc = RecordedAt };
                    break;
                case "submit":
                    pending = pending with { State = IntercompanyAgreementProposalStateDto.Submitted, Submitter = "synthetic-operator", SubmittedAtUtc = RecordedAt, SubmittedHash = SubmittedHash };
                    break;
                case "approve":
                    if (command.ExternalReview is not { } review)
                        return Task.FromResult(ApiResponse<IntercompanyAgreementCommandResultDto>.Fail("Independent approval is unavailable to this preparer.", 403));
                    LastApproval = command;
                    pending = pending with { State = IntercompanyAgreementProposalStateDto.Approved };
                    var revision = new IntercompanyAgreementRevisionDto(_agreementId, 1, null, null, pending.Action, pending.Draft,
                        pending.ProposalId, SubmittedHash, pending.Authors, "synthetic-operator", "synthetic-operator", RecordedAt, command.Rationale!)
                    {
                        ExternalReview = new(review.ExternalReviewer, review.ReviewerReference, review.ReviewedOn, review.Evidence,
                            review.Rationale, review.SubmittedHash, "synthetic-operator", RecordedAt)
                    };
                    Detail = Detail with { Revisions = [revision], ApplicableRevision = revision, LatestApprovedVersion = 1, EffectiveStatus = IntercompanyAgreementStatusDto.Active };
                    break;
                default:
                    throw new InvalidOperationException("The control invoked an unexpected synthetic workflow action.");
            }
            Detail = Detail with { AggregateVersion = Detail.AggregateVersion + 1, Proposals = [pending], Capabilities = Capabilities(pending.State) };
            var receipt = new IntercompanyAgreementCommandReceiptDto(command.CommandId, action, SyntheticHash, "synthetic-operator", RecordedAt, Detail.AggregateVersion, Detail);
            return Task.FromResult(ApiResponse<IntercompanyAgreementCommandResultDto>.Ok(new(Detail, receipt)));
        }

        public Task<ApiResponse<IntercompanyStructuredTermsPreviewDto>> PreviewStructuredTermsAsync(Guid agreementId, DateOnly asOf,
            int? agreementVersion = null, Guid? proposalId = null, CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IntercompanyStructuredTermsPreviewDto>.Ok(new("Current", "StructuredV1", SubmittedHash,
                "synthetic-schedule-v1", "WEEKENDS", "synthetic-calendar", [new(1, new(2026, 1, 1), new(2026, 2, 1), new(2026, 2, 4))], [])));

        public Task<ApiResponse<IntercompanyObligationMonitorDto>> GetObligationMonitorAsync(IntercompanyObligationMonitorRequestDto request, CancellationToken ct = default)
        {
            var row = new IntercompanyBalanceScheduleRowDto(BookId, _partyOne, _partyTwo, null, null, "Loan", "ar",
                IntercompanyAccountRoleDto.Receivable, "USD", "USD", ExactBalance, ExactBalance, true, [], [])
            { IntercompanyAgreementId = _agreementId, IntercompanyAgreementVersion = 1, IntercompanyAgreementPayloadHash = SubmittedHash };
            var schedule = new IntercompanyBalanceScheduleDto("synthetic-tenant", "synthetic-company", "synthetic-fund", BookId, request.AsOf, true, [row], [], SyntheticHash);
            var result = new IntercompanyObligationMonitorDto(request.AsOf, request.ExpiryHorizonDays, RecordedAt, "Partial", false,
                SyntheticHash, [new("LedgerBook", BookId.ToString("D"), "3", SyntheticHash)],
                [new("LifecycleUnknown", "Warning", "Synthetic counterparty has no reviewed lifecycle baseline.", _partyTwo, _agreementId, 1, BookId)],
                schedule, [Guarantee()]);
            return Task.FromResult(ApiResponse<IntercompanyObligationMonitorDto>.Ok(result));
        }

        private IntercompanyAgreementDetailDto Guarantee()
        {
            var id = Guid.NewGuid();
            var draft = Detail.Revisions.Single().Terms with
            {
                Code = "SYN-GUARANTEE",
                Name = "Synthetic disclosure guarantee",
                AgreementType = IntercompanyAgreementTypeDto.Guarantee,
                PartyOneRole = "Guarantor",
                PartyTwoRole = "Obligor",
                Terms = new IntercompanyAgreementTermsDto { StructuredTerms = new(Guarantee: new("Synthetic beneficiary", 2500000m)) }
            };
            var revision = Detail.Revisions.Single() with { AgreementId = id, Terms = draft };
            return Detail with
            {
                AgreementId = id,
                Code = draft.Code,
                Name = draft.Name,
                AgreementType = draft.AgreementType,
                ApplicableRevision = revision,
                Revisions = [revision],
                Proposals = [],
                Capabilities = Capabilities(IntercompanyAgreementProposalStateDto.Approved)
            };
        }
        private static IntercompanyAgreementCapabilitiesDto Capabilities(IntercompanyAgreementProposalStateDto state)
            => new(state == IntercompanyAgreementProposalStateDto.Draft, state == IntercompanyAgreementProposalStateDto.Draft,
                false, false, false, false, false)
            { CanAttestExternalReview = state == IntercompanyAgreementProposalStateDto.Submitted };
    }

    private sealed class EntityClient : IWorkstationEntityConsolidationApiClient
    {
        public OrganizationSummaryDto Organization { get; } = new(Guid.NewGuid(), "SYN-ORG", "Synthetic institution", "USD", true, RecordedAt.AddYears(-1), null, []);
        public LegalEntitySummaryDto Entity { get; } = new(Guid.NewGuid(), LegalEntityTypeDto.Other, "SYN-ENTITY", "Synthetic legal entity", "US", "USD", true, RecordedAt.AddYears(-1), null);
        public AppendEntityLifecycleRevisionRequestDto? LastAppend { get; private set; }
        public Task<OrganizationStructureGraphDto> GetStructureAsync(CancellationToken ct = default)
            => Task.FromResult(new OrganizationStructureGraphDto([Organization], [], [], [], [], [], [Entity], [], [], [],
                [new(Guid.NewGuid(), Organization.OrganizationId, Entity.EntityId, OwnershipRelationshipTypeDto.Owns, 100m, true, RecordedAt.AddYears(-1), null, null)], []));
        public Task<EntityTimelineDto> GetTimelineAsync(Guid organizationId, Guid entityId, DateTimeOffset asOf, CancellationToken ct = default)
        {
            var state = new EntityLifecycleStateDto(Guid.NewGuid(), new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null, LegalEntityLifecycleStatusDto.Active, LegalEntityLifecycleEventKindDto.Formation);
            var revision = new EntityLifecycleRevisionDto("synthetic-tenant", entityId, 2, RecordedAt, "synthetic-operator", "Synthetic reviewed baseline",
                "evidence://evidence-vault/synthetic-baseline", SyntheticHash, SyntheticHash, [state]);
            return Task.FromResult(new EntityTimelineDto(organizationId, Entity, asOf, [revision], [], [], [], [], []) { CanAttestExternalLifecycleReview = true });
        }
        public Task<IReadOnlyList<LedgerPeriodDto>> GetPeriodsAsync(Guid ledgerBookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConsolidationViewDto> PreviewAsync(ConsolidationRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ConsolidationViewDto> CreateDraftsAsync(ConsolidationRequestDto request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EntityLifecycleReviewPreviewDto> PreviewLifecycleReviewAsync(AppendEntityLifecycleRevisionRequestDto request, CancellationToken ct = default)
            => Task.FromResult(new EntityLifecycleReviewPreviewDto(EntityLifecycleExternalReviewIntegrity.SubmittedHash(request)));
        public Task<EntityLifecycleRevisionDto> AppendLifecycleRevisionAsync(AppendEntityLifecycleRevisionRequestDto request, CancellationToken ct = default)
        {
            LastAppend = request;
            var review = request.ExternalReview ?? throw new InvalidOperationException("The lifecycle control omitted external review evidence.");
            return Task.FromResult(new EntityLifecycleRevisionDto("synthetic-tenant", request.EntityId, request.ExpectedRevision + 1, RecordedAt,
                "synthetic-operator", request.Reason, request.EvidenceReference, SyntheticHash, SyntheticHash, request.Timeline)
            { ExternalReview = new(review.ExternalReviewer, review.ReviewerReference, review.ReviewedOn, review.Evidence, review.Rationale, review.SubmittedHash, "synthetic-operator", RecordedAt) });
        }
    }
}
