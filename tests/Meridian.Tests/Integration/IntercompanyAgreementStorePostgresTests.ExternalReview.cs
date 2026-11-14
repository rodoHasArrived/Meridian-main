using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Services;
using Meridian.Tests.Storage.FundAccounts;
using Xunit;

namespace Meridian.Tests.Integration;

public sealed partial class IntercompanyAgreementStorePostgresTests
{
    [FundAccountDatabaseFact]
    public async Task ExternalReview_SameOperatorNativeApprovalReloadAndLegalLeaseRetainExactAttestation()
    {
        await using var data = await Fixture.CreateAsync();
        var initial = Initial();
        var draft = initial.Revisions[0].Terms with { Evidence = [new("vault:external-review")] };
        await data.SeedEntityAsync(draft.PartyOneId, Tenant);
        await data.SeedEntityAsync(draft.PartyTwoId, Tenant);
        var sourceHash = Sha256Digest.ComputeUtf8("synthetic external legal review source bytes");
        var service = new IntercompanyAgreementService(data.Store, new ExternalReviewAccess(),
            new ExternalReviewEvidence(sourceHash), new ExternalReviewClock());
        var actor = new IntercompanyAgreementCallContext(Tenant, "maker");
        var proposalId = Guid.NewGuid();
        var agreementId = Guid.NewGuid();
        await service.CreateAsync(actor, new(Guid.NewGuid(), agreementId, 0, proposalId, draft, "Synthetic creation"));
        var submitted = await service.SubmitAsync(actor, new(Guid.NewGuid(), agreementId, 1, proposalId, Rationale: "Submitted exact terms"));
        var hash = submitted.Detail.Proposals.Single().SubmittedHash!;
        var command = new IntercompanyAgreementCommandDto(Guid.NewGuid(), agreementId, 2, proposalId,
            Rationale: "Externally reviewed; attested by maker", SubmittedHash: hash)
        {
            ExternalReview = new("External counsel", "counsel:synthetic-matter", new(2026, 11, 11),
                new("vault:external-review", "immutable-review-artifact", sourceHash), "I attest external counsel reviewed the exact submitted legal terms.", hash)
        };
        var approved = await service.ApproveAsync(actor, command);
        var reloaded = (await data.Store.GetAsync(Tenant, agreementId))!;
        var revision = Assert.Single(reloaded.Revisions);
        Assert.Equal("maker", revision.Submitter);
        Assert.Equal("maker", revision.Approver);
        Assert.Equal("External counsel", revision.ExternalReview!.ExternalReviewer);
        Assert.Equal("maker", revision.ExternalReview.Operator);
        Assert.Equal(sourceHash, revision.ExternalReview.Evidence.ContentHash);
        Assert.Equal(revision.ExternalReview, reloaded.History.Last().ExternalReview);
        Assert.Equal(approved.Receipt.PayloadHash, reloaded.Receipts.Last().PayloadHash);

        var legal = new IntercompanyAccountingArrangementLegalAuthority(data.Store);
        var reference = new IntercompanyCanonicalAgreementReferenceDto(agreementId, 1, hash);
        await using (var lease = await legal.AcquireLeaseAsync(Tenant, [reference]))
            Assert.Equal(revision.ExternalReview, lease.ResolveExact(reference).ExternalReview);

        var retry = await service.ApproveAsync(actor, command);
        Assert.True(retry.Replayed);
        Assert.Equal(3, retry.Detail.AggregateVersion);
        Assert.Equal(3, await data.CountAsync("intercompany_agreement_command_receipt"));
    }

    [FundAccountDatabaseFact]
    public async Task ExternalReview_StoreRejectsUnattestedSelfApprovalWithoutRetainingAnyState()
    {
        await using var data = await Fixture.CreateAsync();
        var state = Initial();
        state = state with { Revisions = [state.Revisions[0] with { Approver = "maker" }] };
        await Assert.ThrowsAsync<IntercompanyAgreementForbiddenException>(() => SaveAsync(data.Store, state));
        Assert.Null(await data.Store.GetAsync(Tenant, state.AgreementId));
        Assert.Equal(0, await data.CountAsync("intercompany_agreement_revision"));
    }

    private sealed class ExternalReviewAccess : IIntercompanyAgreementAccessAuthority
    {
        public Task<bool> CanAccessEntitiesAsync(IntercompanyAgreementCallContext context, IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
            => Task.FromResult(context.TenantId == Tenant && entityIds.Count == 2);
    }

    private sealed class ExternalReviewEvidence(string sourceHash) : IIntercompanyAgreementEvidenceAuthority
    {
        public Task<IReadOnlyList<IntercompanyAgreementEvidenceDto>> ResolveAsync(IntercompanyAgreementCallContext context,
            Guid agreementId, Guid proposalId, IReadOnlyList<IntercompanyAgreementEvidenceDto> evidence, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntercompanyAgreementEvidenceDto>>(evidence.Select(item =>
                new IntercompanyAgreementEvidenceDto(item.Reference, "immutable-review-artifact", sourceHash)).ToArray());
    }

    private sealed class ExternalReviewClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
