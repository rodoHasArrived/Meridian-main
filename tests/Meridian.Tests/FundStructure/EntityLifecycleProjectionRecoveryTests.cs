using System.Collections.Concurrent;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.FundStructure;
using Moq;
using Xunit;

namespace Meridian.Tests.FundStructure;

public sealed class EntityLifecycleProjectionRecoveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactRetry_RepairsCommittedHistoryAfterProjectionFailure(bool cancelFirstAttempt)
    {
        using var fixture = await Fixture.CreateAsync();
        var command = fixture.Request();
        using var firstCancellation = new CancellationTokenSource();
        fixture.Project = (_, ct) =>
        {
            if (cancelFirstAttempt)
            {
                firstCancellation.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            throw new IOException("Synthetic profile persistence failure after lifecycle append.");
        };
        if (cancelFirstAttempt)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.TransitionAsync(command, "operator", firstCancellation.Token));
        else
            await Assert.ThrowsAsync<IOException>(() => fixture.Service.TransitionAsync(command, "operator", firstCancellation.Token));

        var retained = (await fixture.Store.GetLatestAsync("tenant-a", fixture.EntityId))!;
        Assert.Equal(2, retained.Revision);
        Assert.Equal(LegalEntityLifecycleStatusDto.Active, await fixture.ReadPersistedStatusAsync());
        fixture.Project = fixture.ApplyProjectionAsync;

        var replay = await fixture.Service.TransitionAsync(command, "operator");

        Assert.Equal(retained.Revision, replay.Revision);
        Assert.Equal(retained.ContentHash, replay.ContentHash);
        Assert.Equal(2, (await fixture.Store.GetHistoryAsync("tenant-a", fixture.EntityId)).Count);
        Assert.Equal(LegalEntityLifecycleStatusDto.WindingDown, await fixture.ReadPersistedStatusAsync());
        Assert.Equal(new long[] { 2, 2 }, fixture.Projections.Select(revision => revision.Revision));
        fixture.EvidenceAuthority.Verify(authority => authority.ResolveAsync("tenant-a", "operator", fixture.EntityId,
            It.IsAny<IntercompanyAgreementEvidenceDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OlderRetry_RepairsLatestHistoryWithoutRollingBackOrApplyingFutureStatus(bool futureTransition)
    {
        using var fixture = await Fixture.CreateAsync();
        var command = fixture.Request();
        fixture.Project = (_, _) => throw new IOException("Synthetic interrupted projection.");
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.TransitionAsync(command, "operator"));
        var original = (await fixture.Store.GetLatestAsync("tenant-a", fixture.EntityId))!;
        fixture.Project = fixture.ApplyProjectionAsync;
        var future = Now.AddDays(30);
        var later = command with
        {
            ExpectedRevision = 2,
            Reason = "Later evidenced correction",
            ExternalReview = null,
            Timeline = futureTransition
                ? [command.Timeline[0] with { Status = LegalEntityLifecycleStatusDto.Active, EffectiveTo = future },
                    new(Guid.NewGuid(), future, null, LegalEntityLifecycleStatusDto.WindingDown, LegalEntityLifecycleEventKindDto.Restructure)]
                : [command.Timeline[0] with { Status = LegalEntityLifecycleStatusDto.Dissolved }]
        };
        var latest = await fixture.Service.TransitionAsync(later, "another-operator");

        var replay = await fixture.Service.TransitionAsync(command, "operator");

        Assert.Equal(original.Revision, replay.Revision);
        Assert.Equal(original.ContentHash, replay.ContentHash);
        Assert.Equal(latest.ContentHash, fixture.Projections.Last().ContentHash);
        Assert.Equal(new long[] { 2, 3, 3 }, fixture.Projections.Select(revision => revision.Revision));
        Assert.Equal(futureTransition ? LegalEntityLifecycleStatusDto.Active : LegalEntityLifecycleStatusDto.Dissolved,
            await fixture.ReadPersistedStatusAsync());
        Assert.Equal(3, (await fixture.Store.GetHistoryAsync("tenant-a", fixture.EntityId)).Count);
    }

    [Fact]
    public async Task RetryProjection_HoldsLatestRevisionAuthorityUntilProjectionCompletes()
    {
        using var fixture = await Fixture.CreateAsync();
        var command = fixture.Request();
        var original = await fixture.Service.TransitionAsync(command, "operator");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Project = async (revision, ct) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            await fixture.ApplyProjectionAsync(revision, ct);
        };

        var retry = fixture.Service.TransitionAsync(command, "operator");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var writer = fixture.Store.AppendAsync(NextRevision(original), original.Revision);
        try
        {
            Assert.False(writer.IsCompleted);
        }
        finally
        {
            release.SetResult();
        }
        Assert.Equal(original.ContentHash, (await retry.WaitAsync(TimeSpan.FromSeconds(10))).ContentHash);
        Assert.Equal(3, (await writer.WaitAsync(TimeSpan.FromSeconds(10))).Revision);
    }

    [Fact]
    public async Task CanceledRetryProjection_DoesNotReportSuccessAndReleasesAuthorityLease()
    {
        using var fixture = await Fixture.CreateAsync();
        var command = fixture.Request();
        fixture.Project = (_, _) => throw new IOException("Synthetic interrupted projection.");
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.TransitionAsync(command, "operator"));
        var original = (await fixture.Store.GetLatestAsync("tenant-a", fixture.EntityId))!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Project = async (_, ct) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        };
        using var cancellation = new CancellationTokenSource();
        var retry = fixture.Service.TransitionAsync(command, "operator", cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry);
        Assert.Equal(LegalEntityLifecycleStatusDto.Active, await fixture.ReadPersistedStatusAsync());
        var later = await fixture.Store.AppendAsync(NextRevision(original), original.Revision).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, later.Revision);
    }

    private static EntityLifecycleRevisionDto NextRevision(EntityLifecycleRevisionDto previous)
        => EntityLifecycleRevisionIntegrity.Seal(previous with
        {
            Revision = previous.Revision + 1,
            PreviousHash = previous.ContentHash,
            ExternalReview = null,
            Reason = "Later retained correction",
            Timeline = [previous.Timeline[0] with { Status = LegalEntityLifecycleStatusDto.Dissolved }]
        });

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-lifecycle-recovery-" + Guid.NewGuid().ToString("N"));
        private string StatePath => Path.Combine(_directory, "structure.json");
        private readonly TenantAccessor _tenant = new();
        public Guid EntityId { get; } = Guid.NewGuid();
        public InMemoryEntityLifecycleStore Store { get; } = new();
        public Mock<IEntityLifecycleExternalReviewEvidenceAuthority> EvidenceAuthority { get; } = new();
        public ConcurrentQueue<EntityLifecycleRevisionDto> Projections { get; } = new();
        public Func<EntityLifecycleRevisionDto, CancellationToken, Task> Project { get; set; } = null!;
        public EntityLifecycleService Service { get; private set; } = null!;
        private InMemoryFundStructureService Structure { get; set; } = null!;
        private IntercompanyAgreementEvidenceDto Evidence { get; } = new("vault:lifecycle-review", "lifecycle-review-v1",
            Sha256Digest.ComputeUtf8("synthetic retained legal lifecycle review"));

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture._directory);
            fixture.Structure = new(new InMemoryFundAccountService(), null, null, fixture.StatePath, fixture.Store, fixture._tenant);
            var organizationId = Guid.NewGuid();
            var businessId = Guid.NewGuid();
            var fundId = Guid.NewGuid();
            await fixture.Structure.CreateOrganizationAsync(new(organizationId, "ORG", "Synthetic organization", "USD", Now.AddDays(-3), "setup-operator"));
            await fixture.Structure.CreateBusinessAsync(new(businessId, organizationId, BusinessKindDto.FundManager,
                "BUSINESS", "Synthetic manager", "USD", Now.AddDays(-3), "setup-operator"));
            await fixture.Structure.CreateFundAsync(new(fundId, "FUND", "Synthetic fund", "USD", Now.AddDays(-3), "setup-operator", BusinessId: businessId));
            await fixture.Structure.CreateLegalEntityAsync(new(fixture.EntityId, LegalEntityTypeDto.Vehicle,
                "ENTITY", "Synthetic entity", "US", "USD", Now.AddDays(-3), "setup-operator"));
            await fixture.Structure.LinkNodesAsync(new(Guid.NewGuid(), fundId, fixture.EntityId, OwnershipRelationshipTypeDto.Owns,
                Now.AddDays(-3), "setup-operator", OwnershipPercent: 100m));
            var structure = new Mock<IFundStructureService>();
            structure.Setup(service => service.GetOrganizationStructureAsync(It.IsAny<OrganizationStructureQuery>(), It.IsAny<CancellationToken>()))
                .Returns((OrganizationStructureQuery query, CancellationToken ct) => fixture.Structure.GetOrganizationStructureAsync(query, ct));
            structure.As<IEntityLifecycleProjectionSink>()
                .Setup(sink => sink.ApplyLifecycleProjectionAsync(It.IsAny<EntityLifecycleRevisionDto>(), It.IsAny<CancellationToken>()))
                .Returns((EntityLifecycleRevisionDto revision, CancellationToken ct) =>
                {
                    fixture.Projections.Enqueue(revision);
                    return fixture.Project(revision, ct);
                });
            fixture.EvidenceAuthority.Setup(authority => authority.ResolveAsync("tenant-a", "operator", fixture.EntityId,
                It.IsAny<IntercompanyAgreementEvidenceDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(fixture.Evidence);
            fixture.Project = fixture.ApplyProjectionAsync;
            fixture.Service = new(structure.Object, fixture.Store, fixture._tenant, fixture.EvidenceAuthority.Object, new FixedClock());
            return fixture;
        }

        public Task ApplyProjectionAsync(EntityLifecycleRevisionDto revision, CancellationToken ct)
            => Structure.ApplyLifecycleProjectionAsync(revision, ct);

        public async Task<LegalEntityLifecycleStatusDto> ReadPersistedStatusAsync()
        {
            // Deliberately omit the history store: normal graph reads overlay retained history,
            // which would hide an unpersisted local lifecycle projection.
            var restarted = new InMemoryFundStructureService(new InMemoryFundAccountService(), StatePath);
            return Assert.Single((await restarted.GetOrganizationStructureAsync(new(ActiveOnly: false))).Entities).LifecycleStatus;
        }

        public AppendEntityLifecycleRevisionRequestDto Request()
        {
            var request = new AppendEntityLifecycleRevisionRequestDto(EntityId, 1,
                [new(Guid.NewGuid(), Now.AddDays(-2), null, LegalEntityLifecycleStatusDto.WindingDown, LegalEntityLifecycleEventKindDto.Restructure)],
                "Externally reviewed wind-down", Evidence.Reference);
            return request with
            {
                ExternalReview = new("External counsel", "counsel:synthetic-entity", DateOnly.FromDateTime(Now.UtcDateTime.AddDays(-1)), Evidence,
                    "I attest external review of the exact retained lifecycle timeline.", EntityLifecycleExternalReviewIntegrity.SubmittedHash(request))
            };
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    private sealed class TenantAccessor : IFundScopeTenantAccessor { public string? ResolveCallerTenant() => "tenant-a"; }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
