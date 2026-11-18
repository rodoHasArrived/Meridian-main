using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>Maker-checker fixing history over an in-memory append-only store; synthetic SOFR values only.</summary>
public sealed class IntercompanyRateFixingServiceTests
{
    private static readonly DateOnly Date = new(2026, 3, 30);

    [Fact]
    public async Task AProposedFixingSetsNoRateUntilASecondPersonApprovesIt()
    {
        var (service, _) = Create();
        var proposed = await service.ProposeAsync("Tenant-A", "maker", Propose(0.0430m));

        Assert.Null(proposed.Approved);
        Assert.Empty(await service.ApprovedAsync("tenant-a", "sofr", Date, Date));
        var self = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync("tenant-a", "MAKER", proposed.FixingId, Approve(proposed)));
        Assert.Contains("someone other than", self.Message);

        var approved = await service.DecideAsync("tenant-a", "checker", proposed.FixingId, Approve(proposed));
        var value = Assert.Single(await service.ApprovedAsync("tenant-a", " Sofr ", Date, Date));
        Assert.Equal((0.043m, "SOFR", proposed.Pending!.ValueDigest), (value.RateFraction, value.IndexReference, value.ValueDigest));
        Assert.Equal(IntercompanyRateFixingRules.FixingId("tenant-a", "SOFR", Date), approved.FixingId);
        Assert.Equal([IntercompanyRateFixingActionDto.Propose, IntercompanyRateFixingActionDto.Approve], approved.Events.Select(item => item.Action));
    }

    [Fact]
    public async Task ACorrectionLeavesTheApprovedValueInForceUntilItIsApproved_AndARejectionKeepsIt()
    {
        var (service, _) = Create();
        var first = await service.ProposeAsync("tenant-a", "maker", Propose(0.0430m));
        var approved = await service.DecideAsync("tenant-a", "checker", first.FixingId, Approve(first));

        var correction = await service.ProposeAsync("tenant-a", "maker", Propose(0.0431m, approved.Version));
        Assert.Equal(0.043m, Assert.Single(await service.ApprovedAsync("tenant-a", "SOFR", Date, Date)).RateFraction);
        var rejected = await service.DecideAsync("tenant-a", "checker", first.FixingId,
            new(Guid.NewGuid(), IntercompanyRateFixingActionDto.Reject, correction.Version, correction.Pending!.ValueDigest, "Publisher shows 4.30%."));
        Assert.Null(rejected.Pending);
        Assert.Equal(0.043m, rejected.Approved!.RateFraction);
        Assert.Equal("Publisher shows 4.30%.", rejected.Events[^1].Reason);

        var second = await service.ProposeAsync("tenant-a", "maker", Propose(0.0432m, rejected.Version));
        var replaced = await service.DecideAsync("tenant-a", "checker", first.FixingId, Approve(second));
        Assert.Equal(0.0432m, replaced.Approved!.RateFraction);
        Assert.NotEqual(approved.Approved!.ValueDigest, replaced.Approved.ValueDigest);
        Assert.Equal(6, replaced.Version);
    }

    [Fact]
    public async Task StaleVersionsDigestsAndUnreasonedRejectionsAreRefused()
    {
        var (service, _) = Create();
        var proposed = await service.ProposeAsync("tenant-a", "maker", Propose(0.0430m));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProposeAsync("tenant-a", "maker", Propose(0.044m, proposed.Version)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProposeAsync("tenant-a", "maker", Propose(0.044m, 0)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync("tenant-a", "checker", proposed.FixingId,
            new(Guid.NewGuid(), IntercompanyRateFixingActionDto.Approve, proposed.Version, new string('0', 64))));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DecideAsync("tenant-a", "checker", proposed.FixingId,
            new(Guid.NewGuid(), IntercompanyRateFixingActionDto.Reject, proposed.Version, proposed.Pending!.ValueDigest, " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProposeAsync("tenant-a", "maker",
            Propose(5.25m) with { FixingDate = Date.AddDays(1) }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProposeAsync("tenant-a", "maker",
            Propose(0.05m) with { FixingDate = Date.AddDays(1), SourceReference = "" }));
    }

    [Fact]
    public async Task ARetriedRequestReturnsTheSameRecord_AndAReusedIdentityForAnotherStepConflicts()
    {
        var (service, store) = Create();
        var request = Propose(0.0430m);
        var proposed = await service.ProposeAsync("tenant-a", "maker", request);
        Assert.Same(proposed, await service.ProposeAsync("tenant-a", "maker", request));
        Assert.Equal(1, store.Appends);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync("tenant-a", "checker", proposed.FixingId,
            new(request.RequestId, IntercompanyRateFixingActionDto.Approve, proposed.Version, proposed.Pending!.ValueDigest)));
    }

    [Fact]
    public async Task FixingsAreTenantScoped()
    {
        var (service, _) = Create();
        var proposed = await service.ProposeAsync("tenant-a", "maker", Propose(0.0430m));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DecideAsync("tenant-b", "checker", proposed.FixingId, Approve(proposed)));
        Assert.Empty(await service.ListAsync("tenant-b", "SOFR", Date, Date));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(" ", "SOFR", Date, Date));
    }

    private static ProposeIntercompanyRateFixingRequest Propose(decimal rate, long expectedVersion = 0)
        => new(Guid.NewGuid(), "SOFR", Date, rate, "Synthetic publisher SOFR 2026-03-30", "Copied from the synthetic fixture.", expectedVersion);

    private static DecideIntercompanyRateFixingRequest Approve(IntercompanyRateFixingDto fixing)
        => new(Guid.NewGuid(), IntercompanyRateFixingActionDto.Approve, fixing.Version, fixing.Pending!.ValueDigest);

    private static (IntercompanyRateFixingService Service, MemoryStore Store) Create()
    {
        var store = new MemoryStore();
        return (new IntercompanyRateFixingService(store), store);
    }

    /// <summary>Keeps every version; enforces the same expected-version contract as the durable stores.</summary>
    internal sealed class MemoryStore : IIntercompanyRateFixingStore
    {
        private readonly List<IntercompanyRateFixingDto> _versions = [];
        public int Appends { get; private set; }

        public Task<IntercompanyRateFixingDto?> GetAsync(string tenantId, Guid fixingId, CancellationToken ct = default)
            => Task.FromResult(_versions.Where(item => item.TenantId == tenantId && item.FixingId == fixingId).MaxBy(item => item.Version));

        public Task<IReadOnlyList<IntercompanyRateFixingDto>> ListAsync(string tenantId, string indexReference, DateOnly from, DateOnly to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntercompanyRateFixingDto>>(_versions
                .Where(item => item.TenantId == tenantId && item.IndexReference == indexReference && item.FixingDate >= from && item.FixingDate <= to)
                .GroupBy(item => item.FixingId).Select(group => group.MaxBy(item => item.Version)!).ToArray());

        public async Task<IntercompanyRateFixingDto> AppendAsync(IntercompanyRateFixingDto next, long expectedVersion, CancellationToken ct = default)
        {
            if (((await GetAsync(next.TenantId, next.FixingId, ct))?.Version ?? 0) != expectedVersion || next.Version != expectedVersion + 1)
                throw new InvalidOperationException("The fixing changed since it was read.");
            _versions.Add(next);
            Appends++;
            return next;
        }
    }
}
