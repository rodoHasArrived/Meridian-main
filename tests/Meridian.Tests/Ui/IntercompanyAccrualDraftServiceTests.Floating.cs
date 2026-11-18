using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Data = Meridian.Tests.FinancialOperations.Intercompany.IntercompanyProposalTestData;

namespace Meridian.Tests.Ui;

/// <summary>
/// Rule intercompany-accrual-v2 through the draft service. June 2026 starts on a Monday, so a two-business-day lag
/// fixes on Thursday 28 May: 1,000,000 x (4.25% + 1.25%) x 30/360 = 4,583.3333... -> 4,583.33.
/// </summary>
public sealed partial class IntercompanyAccrualDraftServiceTests
{
    private static readonly DateOnly FixingDate = new(2026, 5, 28);

    [Fact]
    public async Task FloatingPreview_UsesTheApprovedFixingOnly_AndRetainsItAsV2Evidence()
    {
        var (fixings, _) = await FixingsAsync(approve: true);

        var proposal = await Service(floating: true, fixings: fixings).PreviewAsync("tenant-a", "company-a", "preparer", Request());

        Assert.True(proposal.IsReady);
        Assert.Equal(4583.33m, proposal.Computation.Amount);
        Assert.Equal(IntercompanyAccrualRules.FloatingRuleVersion, proposal.Evidence!.RuleVersion);
        var pin = Assert.Single(proposal.Computation.Slices).Fixing!;
        Assert.Equal((FixingDate, 0.0425m, 125m), (pin.FixingDate, pin.IndexRate, pin.SpreadBps));
        Assert.Null(IntercompanyAccrualEvidenceGuard.Validate(proposal.Draft!));
        // The window keeps the identity a fixed-rate accrual of it would have.
        Assert.Equal(Meridian.FinancialOperations.Intercompany.IntercompanyAccrualSourceBuilder.DraftId("tenant-a", Request()), proposal.Draft!.JournalEntryId);
    }

    [Fact]
    public async Task FloatingPreview_IsUnavailable_WhileTheFixingAwaitsApproval_OrWithoutTheFixingAuthority()
    {
        var (pending, _) = await FixingsAsync(approve: false);

        var unapproved = await Service(floating: true, fixings: pending).PreviewAsync("tenant-a", "company-a", "preparer", Request());
        var noAuthority = await Service(floating: true).PreviewAsync("tenant-a", "company-a", "preparer", Request());

        Assert.False(unapproved.IsReady);
        Assert.Contains("2026-05-28", unapproved.Diagnostics[^1]);
        Assert.False(noAuthority.IsReady);
        Assert.Contains("rate-fixing authority", noAuthority.Diagnostics[^1]);
    }

    [Fact]
    public async Task EvidenceGuard_RefusesAFloatingRateThatDiffersFromItsPinnedFixing()
    {
        var (fixings, _) = await FixingsAsync(approve: true);
        var draft = (await Service(floating: true, fixings: fixings).PreviewAsync("tenant-a", "company-a", "preparer", Request())).Draft!;
        var evidence = IntercompanyAccrualEvidenceGuard.Deserialize(draft.IntercompanyAccrualEvidenceJson!)!;
        var slice = evidence.Computation.Slices[0];
        var tampered = evidence with
        {
            Computation = evidence.Computation with { Slices = [slice with { Fixing = slice.Fixing! with { IndexRate = 0.01m } }] }
        };
        var json = IntercompanyAccrualEvidenceGuard.Serialize(tampered);

        Assert.Contains("pinned fixing", IntercompanyAccrualEvidenceGuard.Validate(draft with
        {
            IntercompanyAccrualEvidenceJson = json,
            IntercompanyAccrualEvidenceDigest = IntercompanyAccrualEvidenceGuard.Digest(json)
        }));
    }

    [Fact]
    public async Task ACorrectedFixingApprovedAfterDrafting_StopsTheDraftBeforeReviewOrPosting()
    {
        var (fixings, store) = await FixingsAsync(approve: true);
        var evidence = (await Service(floating: true, fixings: fixings).PreviewAsync("tenant-a", "company-a", "preparer", Request())).Evidence!;
        var interest = Profile(Interest, IntercompanyAccountingTemplateKindDto.LoanInterest,
            [new(Data.EntityA, Data.BookA, "a-int-recv", "a-int-pay", null, "a-int-income", "a-int-expense"),
             new(Data.EntityB, Data.BookB, "b-int-recv", "b-int-pay", null, "b-int-income", "b-int-expense")], structured: true, floating: true);
        interest = interest with { Arrangement = interest.Arrangement with { RevisionId = evidence.PostingArrangementRevisionId } };

        await Authority(History(interest), interest, store).VerifyAccrualAuthorityAsync("tenant-a", evidence);

        var current = (await fixings.ListAsync("tenant-a", "SOFR", FixingDate, FixingDate))[0];
        var correction = await fixings.ProposeAsync("tenant-a", "rates-maker",
            new(Guid.NewGuid(), "SOFR", FixingDate, 0.0426m, "Synthetic publisher, republished", "Publisher correction.", current.Version));
        await fixings.DecideAsync("tenant-a", "rates-checker", correction.FixingId,
            new(Guid.NewGuid(), IntercompanyRateFixingActionDto.Approve, correction.Version, correction.Pending!.ValueDigest));

        var stale = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Authority(History(interest), interest, store).VerifyAccrualAuthorityAsync("tenant-a", evidence));
        Assert.Contains("SOFR fixing for 2026-05-28 changed after drafting", stale.Message);
        var unavailable = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Authority(History(interest), interest).VerifyAccrualAuthorityAsync("tenant-a", evidence));
        Assert.Contains("rate-fixing authority", unavailable.Message);
    }

    private static async Task<(IntercompanyRateFixingService Service, IIntercompanyRateFixingStore Store)> FixingsAsync(bool approve)
    {
        var store = new MemoryFixingStore();
        var service = new IntercompanyRateFixingService(store);
        var proposed = await service.ProposeAsync("tenant-a", "rates-maker",
            new(Guid.NewGuid(), "SOFR", FixingDate, 0.0425m, "Synthetic publisher SOFR", "Synthetic fixture.", 0));
        if (approve)
            await service.DecideAsync("tenant-a", "rates-checker", proposed.FixingId,
                new(Guid.NewGuid(), IntercompanyRateFixingActionDto.Approve, proposed.Version, proposed.Pending!.ValueDigest));
        return (service, store);
    }

    private sealed class MemoryFixingStore : IIntercompanyRateFixingStore
    {
        private readonly List<IntercompanyRateFixingDto> _versions = [];

        public Task<IntercompanyRateFixingDto?> GetAsync(string tenantId, Guid fixingId, CancellationToken ct = default)
            => Task.FromResult(_versions.Where(item => item.TenantId == tenantId && item.FixingId == fixingId).MaxBy(item => item.Version));

        public Task<IReadOnlyList<IntercompanyRateFixingDto>> ListAsync(string tenantId, string indexReference, DateOnly from, DateOnly to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntercompanyRateFixingDto>>(_versions
                .Where(item => item.TenantId == tenantId && item.IndexReference == indexReference && item.FixingDate >= from && item.FixingDate <= to)
                .GroupBy(item => item.FixingId).Select(group => group.MaxBy(item => item.Version)!).ToArray());

        public async Task<IntercompanyRateFixingDto> AppendAsync(IntercompanyRateFixingDto next, long expectedVersion, CancellationToken ct = default)
        {
            if (((await GetAsync(next.TenantId, next.FixingId, ct))?.Version ?? 0) != expectedVersion)
                throw new InvalidOperationException("The fixing changed since it was read.");
            _versions.Add(next);
            return next;
        }
    }
}
