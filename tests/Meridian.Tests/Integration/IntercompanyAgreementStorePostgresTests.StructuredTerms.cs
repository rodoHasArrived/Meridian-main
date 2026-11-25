using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Storage.FundStructure;
using Meridian.Tests.Storage.FundAccounts;
using Xunit;

namespace Meridian.Tests.Integration;

public sealed partial class IntercompanyAgreementStorePostgresTests
{
    private const string LegacyReviewedPayload =
        """{"draft":{"code":"IC-001","name":"Synthetic bilateral loan","partyOneId":"11111111-1111-1111-1111-111111111111","partyOneRole":"Lender","partyTwoId":"22222222-2222-2222-2222-222222222222","partyTwoRole":"Borrower","agreementType":"Loan","currency":"USD","effectiveFrom":"2026-01-01","terms":{"notes":"Synthetic fixed agreement terms"},"action":"Activate"},"rationale":"Synthetic approval","agreementId":"44444444-4444-4444-4444-444444444444","proposalId":"55555555-5555-5555-5555-555555555555"}""";
    private const string LegacyReviewedHash = "7bca97a6244bd1b104427afd6abcc6d04bbd4631902100937d8e320bf2a3e088";

    [FundAccountDatabaseFact]
    public async Task StructuredTerms_ReloadRetainsExactDeclarationsAndReviewedLegalHash()
    {
        await using var data = await Fixture.CreateAsync();
        var draft = Initial().Revisions[0].Terms with
        {
            Terms = new(Notes: "Declared loan terms; not calculated interest")
            {
                StructuredTerms = StructuredLoanTerms()
            }
        };
        var state = ApprovedInitial(draft);
        await SaveAsync(data.Store, state);

        // Each Store property access creates a fresh store over the durable PostgreSQL payload.
        var reloaded = Assert.IsType<IntercompanyAgreementStoreState>(await data.Store.GetAsync(Tenant, state.AgreementId));
        var revision = Assert.Single(reloaded.Revisions);
        var terms = Assert.IsType<IntercompanyStructuredTermsDto>(revision.Terms.Terms!.StructuredTerms);
        Assert.Equal(0.1234567890123456789012345678m, terms.FixedAnnualRate);
        Assert.Equal(IntercompanyDayCountDto.Actual365, terms.DayCount);
        Assert.Equal(IntercompanyCompoundingDto.Simple, terms.Compounding);
        Assert.Equal("WEEKENDS", terms.Schedule!.CalendarId);
        Assert.Equal(2, terms.Schedule.SettlementBusinessDays);
        Assert.Equal(state.Revisions[0].PayloadHash, revision.PayloadHash);
        Assert.Equal(revision.PayloadHash, IntercompanyAgreementRules.ReviewedHash(ReviewedPayload(revision)));
        Assert.NotNull(reloaded.Proposals[0].Draft.Terms!.StructuredTerms);
        Assert.NotNull(reloaded.History[0].Snapshot!.Terms!.StructuredTerms);
        Assert.NotNull(reloaded.Receipts[0].Detail.ApplicableRevision!.Terms.Terms!.StructuredTerms);
        Assert.Equal(1, await data.CountAsync("intercompany_agreement_revision"));
    }

    [FundAccountDatabaseFact]
    public async Task StructuredSuccessor_PreservesLegacyApprovedBytesAndHash()
    {
        await using var data = await Fixture.CreateAsync();
        var reviewed = JsonSerializer.Deserialize(LegacyReviewedPayload,
            FundStructureContractsJsonContext.Default.IntercompanyAgreementReviewPayloadDto)!;
        Assert.Equal(LegacyReviewedPayload, JsonSerializer.Serialize(reviewed,
            FundStructureContractsJsonContext.Default.IntercompanyAgreementReviewPayloadDto));
        Assert.Equal(LegacyReviewedHash, IntercompanyAgreementRules.ReviewedHash(reviewed));
        var legacy = ApprovedInitial(reviewed.Draft, reviewed.AgreementId, reviewed.ProposalId);
        Assert.Equal(LegacyReviewedHash, legacy.Revisions[0].PayloadHash);
        await SaveAsync(data.Store, legacy);

        var draft = reviewed.Draft with
        {
            Action = IntercompanyAgreementActionDto.Amend,
            EffectiveFrom = new(2026, 2, 1),
            Terms = reviewed.Draft.Terms! with { StructuredTerms = StructuredLoanTerms(new(2026, 2, 1)) }
        };
        await SaveAsync(data.Store, ApprovedSuccessor(legacy, draft));

        var reloaded = (await data.Store.GetAsync(Tenant, legacy.AgreementId))!;
        Assert.Equal(2, reloaded.Revisions.Count);
        var prior = reloaded.Revisions[0];
        Assert.Null(prior.Terms.Terms!.StructuredTerms);
        Assert.Equal(LegacyReviewedHash, prior.PayloadHash);
        Assert.Equal(LegacyReviewedPayload, JsonSerializer.Serialize(ReviewedPayload(prior),
            FundStructureContractsJsonContext.Default.IntercompanyAgreementReviewPayloadDto));
        Assert.NotNull(reloaded.Revisions[1].Terms.Terms!.StructuredTerms);
        Assert.NotEqual(prior.PayloadHash, reloaded.Revisions[1].PayloadHash);
        Assert.Null(reloaded.Receipts[0].Detail.ApplicableRevision!.Terms.Terms!.StructuredTerms);
        Assert.Equal(2, await data.CountAsync("intercompany_agreement_revision"));
    }

    [FundAccountDatabaseFact]
    public async Task AddingStructuredTermsToApprovedHistoryInPlace_IsRejectedWithoutAdvancingHead()
    {
        await using var data = await Fixture.CreateAsync();
        var legacy = ApprovedInitial(Initial().Revisions[0].Terms);
        await SaveAsync(data.Store, legacy);
        var attempted = Next(legacy) with
        {
            Revisions = [legacy.Revisions[0] with
            {
                Terms = legacy.Revisions[0].Terms with
                {
                    Terms = legacy.Revisions[0].Terms.Terms! with { StructuredTerms = StructuredLoanTerms() }
                }
            }]
        };

        await Assert.ThrowsAsync<IntercompanyAgreementConflictException>(() => SaveAsync(data.Store, attempted));
        var retained = (await data.Store.GetAsync(Tenant, legacy.AgreementId))!;
        Assert.Equal(1, retained.AggregateVersion);
        Assert.Null(retained.Revisions[0].Terms.Terms!.StructuredTerms);
        Assert.Equal(legacy.Revisions[0].PayloadHash, retained.Revisions[0].PayloadHash);
        Assert.Equal(1, await data.CountAsync("intercompany_agreement_snapshot"));
        Assert.Equal(1, await data.CountAsync("intercompany_agreement_command_receipt"));
    }

    private static IntercompanyStructuredTermsDto StructuredLoanTerms(DateOnly? start = null) => new(
        Schedule: new(start ?? new(2026, 1, 1), new(2026, 12, 31), IntercompanyFrequencyDto.Monthly,
            "WEEKENDS", BusinessDayConvention.Following, SettlementBusinessDays: 2),
        RateBasis: IntercompanyRateBasisDto.Fixed, FixedAnnualRate: 0.1234567890123456789012345678m,
        DayCount: IntercompanyDayCountDto.Actual365, Compounding: IntercompanyCompoundingDto.Simple);

    private static IntercompanyAgreementReviewPayloadDto ReviewedPayload(IntercompanyAgreementRevisionDto revision) =>
        new(revision.Terms, revision.BaseAgreementVersion, revision.PredecessorAgreementVersion, revision.Rationale)
        { AgreementId = revision.AgreementId, ProposalId = revision.ProposalId };

    private static IntercompanyAgreementStoreState ApprovedInitial(IntercompanyAgreementDraftDto draft,
        Guid? agreementId = null, Guid? proposalId = null)
    {
        var seed = Initial();
        var id = agreementId ?? seed.AgreementId;
        var proposal = seed.Proposals[0] with { ProposalId = proposalId ?? seed.Proposals[0].ProposalId, Draft = draft };
        var revision = seed.Revisions[0] with { AgreementId = id, ProposalId = proposal.ProposalId, Terms = draft };
        revision = revision with { PayloadHash = IntercompanyAgreementRules.ReviewedHash(ReviewedPayload(revision)) };
        proposal = proposal with { SubmittedHash = revision.PayloadHash };
        var history = new[] { seed.History[0] with { ProposalId = proposal.ProposalId, Snapshot = draft } };
        var detail = Detail(id, 1, draft, [proposal], [revision], history);
        var receipt = seed.Receipts[0] with { Detail = detail };
        return new(id, 1, [proposal], [revision], history, [receipt]);
    }

    private static IntercompanyAgreementStoreState ApprovedSuccessor(IntercompanyAgreementStoreState prior,
        IntercompanyAgreementDraftDto draft)
    {
        var next = Next(prior);
        var proposalId = Guid.NewGuid();
        var revision = prior.Revisions[^1] with
        {
            AgreementVersion = prior.Revisions.Count + 1,
            PredecessorAgreementVersion = prior.Revisions[^1].AgreementVersion,
            BaseAgreementVersion = prior.Revisions[^1].AgreementVersion,
            Action = IntercompanyAgreementActionDto.Amend,
            Terms = draft,
            ProposalId = proposalId,
            ApprovedAtUtc = Now.AddSeconds(next.AggregateVersion)
        };
        revision = revision with { PayloadHash = IntercompanyAgreementRules.ReviewedHash(ReviewedPayload(revision)) };
        var proposal = prior.Proposals[0] with
        {
            ProposalId = proposalId,
            Action = IntercompanyAgreementActionDto.Amend,
            Draft = draft,
            BaseAgreementVersion = prior.Revisions[^1].AgreementVersion,
            SubmittedHash = revision.PayloadHash
        };
        var proposals = prior.Proposals.Append(proposal).ToArray();
        var revisions = prior.Revisions.Append(revision).ToArray();
        var history = next.History.Take(next.History.Count - 1).Append(next.History[^1] with
        {
            ProposalId = proposalId,
            Action = "Approve",
            Snapshot = draft
        }).ToArray();
        var detail = Detail(prior.AgreementId, next.AggregateVersion, draft, proposals, revisions, history);
        return next with
        {
            Proposals = proposals,
            Revisions = revisions,
            History = history,
            Receipts = [.. prior.Receipts, next.Receipts[^1] with { Action = "Approve", Detail = detail }]
        };
    }
}
