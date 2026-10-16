using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Onboarding;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class OnboardingWorkspaceServiceTests
{
    [Fact]
    public async Task MultiPeriodImport_ResolutionReviewAndFrozenPacket_ReplayAfterStoreAndSourceChanges()
    {
        using var fixture = new Fixture();
        var service = fixture.Service();
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        fixture.Source.Set("january-initial", fixture.Capture(Fixture.January, "january-initial", 101m, 9m, null));
        workspace = await fixture.Compare(service, workspace, Fixture.January, "january-initial");
        var january = workspace.Comparisons.Single();
        january.Differences.Should().ContainSingle(x => x.Kind == "Balance" && x.Status == "New");
        january.Differences.Should().ContainSingle(x => x.Kind == "Position" && x.Status == "New");
        workspace.Readiness.MissingSources.Should().NotBeEmpty();
        workspace.Readiness.IsReady.Should().BeFalse();

        var balanceKey = january.Differences.Single(x => x.Kind == "Balance").DifferenceKey;
        workspace = await service.AssignDifferenceAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            balanceKey, new(workspace.Version, "analyst", "Investigate the imported cash line.", ["evidence:cash-case"]), Fixture.Owner);
        fixture.Source.Set("february-initial", fixture.Capture(Fixture.February, "february-initial", 102m, 10m, 105m));
        workspace = await fixture.Compare(service, workspace, Fixture.February, "february-initial");
        var february = workspace.Comparisons.Last();
        february.Differences.Should().ContainSingle(x => x.Kind == "Balance" && x.Status == "Persistent" && x.OwnerId == "analyst");
        february.Differences.Single(x => x.Kind == "Balance").EvidenceIds.Should().Contain("evidence:cash-case");
        february.Differences.Should().ContainSingle(x => x.Kind == "Position" && x.Status == "Resolved");
        // A newly available NAV source exposes a numerical break, never resolves the missing source as zero.
        february.Differences.Should().ContainSingle(x => x.Kind == "Nav" && x.Status == "New");

        fixture.Source.Set("march-final", fixture.Capture(Fixture.March, "march-final", 100m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.March, "march-final");
        workspace.CurrentDifferences.Should().OnlyContain(x => x.Status == "Resolved");
        workspace.Readiness.IsReady.Should().BeFalse("earlier required dates still have missing sources and unresolved differences");
        fixture.Source.Set("january-corrected", fixture.Capture(Fixture.January, "january-corrected", 100m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.January, "january-corrected");
        fixture.Source.Set("february-corrected", fixture.Capture(Fixture.February, "february-corrected", 100m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.February, "february-corrected");
        workspace.Readiness.MissingDates.Should().BeEmpty();
        workspace.Readiness.MissingSources.Should().BeEmpty();
        workspace.Readiness.UnresolvedDifferenceCount.Should().Be(0);
        workspace.Readiness.IsReady.Should().BeFalse("independent review is still required");

        workspace = await service.ReviewAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, workspace.DataRevision, "ChangesRequested", "Check the cash correction evidence.", ["review:request"]), Fixture.Reviewer);
        workspace.Readiness.IsReady.Should().BeFalse();
        workspace = await service.ReviewAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, workspace.DataRevision, "Approved", "All required source dates and corrections checked.", ["review:approval"]), Fixture.Reviewer);
        workspace.Readiness.IsReady.Should().BeTrue();
        var packet = await service.FreezePacketAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        packet.Content.Readiness.IsReady.Should().BeTrue();
        packet.Content.Comparisons.Should().HaveCount(5);
        packet.Content.Reviews.Select(x => x.Decision).Should().Equal("ChangesRequested", "Approved");
        packet.Content.Scope.Should().BeEquivalentTo(fixture.Scope);
        packet.ContentHash.Should().HaveLength(64);
        packet.ContentHash.Should().Be(OnboardingWorkspaceService.HashPacketContent(packet.Content));
        packet.Content.AuthorityPosture.Should().Be(OnboardingWorkspaceService.ReadOnlyAuthorityPosture);
        packet.Content.Comparisons.Should().OnlyContain(x => OnboardingWorkspaceService.VerifyComparisonHash(x));
        packet.Content.Comparisons.Skip(1).Select(x => x.PreviousContentHash)
            .Should().Equal(packet.Content.Comparisons.SkipLast(1).Select(x => x.ContentHash));
        var frozenJson = JsonSerializer.Serialize(packet);
        var originalJanuaryJson = JsonSerializer.Serialize(january);
        var originalFebruaryJson = JsonSerializer.Serialize(february);

        // Use an independently constructed file store, then mutate current inputs, mapping, assignment and policy.
        service = fixture.Service();
        workspace = (await service.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!;
        fixture.Source.Set("march-new-mapping", fixture.Capture(Fixture.March, "march-new-mapping", 110m, 10m, 100m, "mapping-v2"));
        workspace = await fixture.Compare(service, workspace, Fixture.March, "march-new-mapping", "mapping-v2");
        workspace = await service.AssignDifferenceAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            balanceKey, new(workspace.Version, "second-analyst", "Review mapping change.", ["evidence:remap"]), Fixture.Owner);
        workspace = await service.UpdateCriteriaAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, workspace.Criteria with { BalanceTolerance = 0.001m, ReviewInstructions = "Recheck mapping v2." }), Fixture.Owner);
        workspace.Readiness.IsReady.Should().BeFalse();
        fixture.Source.FailOnCapture = true;
        var restarted = fixture.Service();
        JsonSerializer.Serialize(await restarted.ReplayComparisonAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, january.ComparisonId))
            .Should().Be(originalJanuaryJson);
        JsonSerializer.Serialize(await restarted.ReplayComparisonAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, february.ComparisonId))
            .Should().Be(originalFebruaryJson);
        JsonSerializer.Serialize(await restarted.GetPacketAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, packet.PacketId))
            .Should().Be(frozenJson);
    }

    [Fact]
    public async Task RequiredEarlierDate_WithNoComparison_BlocksEvenWhenLatestDateIsClean()
    {
        using var fixture = new Fixture();
        var service = fixture.Service();
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        fixture.Source.Set("latest", fixture.Capture(Fixture.March, "latest", 100m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.March, "latest");
        workspace.Readiness.MissingDates.Should().BeEquivalentTo([Fixture.January, Fixture.February]);
        workspace.Readiness.IsReady.Should().BeFalse();
        var packet = await service.FreezePacketAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        packet.Content.Readiness.IsReady.Should().BeFalse();
        packet.Content.Readiness.MissingDates.Should().BeEquivalentTo([Fixture.January, Fixture.February]);
    }

    [Fact]
    public async Task DisappearingPosition_CannotResolvePreviouslyObservedBreak()
    {
        using var fixture = new Fixture();
        var service = fixture.Service();
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        fixture.Source.Set("first", fixture.Capture(Fixture.January, "first", 100m, 9m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.January, "first");
        var first = workspace.Comparisons.Single().Differences.Single(x => x.Kind == "Position");
        var next = fixture.Capture(Fixture.February, "missing-position", 100m, 10m, 100m);
        fixture.Source.Set("missing-position", next with
        {
            Observations = next.Observations.Where(x => x.Kind != "Position").ToArray(),
            MissingSources = [new("missing-position", "Position", Fixture.Account, "Position source not received.")]
        });
        workspace = await fixture.Compare(service, workspace, Fixture.February, "missing-position");
        var missing = workspace.Comparisons.Last().Differences.Single(x => x.DifferenceKey == first.DifferenceKey);
        missing.Status.Should().NotBe("Resolved");
        missing.FirstSeenComparisonId.Should().Be(first.FirstSeenComparisonId);
        workspace.Readiness.MissingSources.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SubcentTolerances_UseExactDecimalsAndIncludeTheBoundary()
    {
        using var fixture = new Fixture();
        var service = fixture.Service();
        var request = fixture.Request() with
        {
            Criteria = fixture.Criteria with { RequiredDates = [Fixture.January], BalanceTolerance = 0.005m, PositionTolerance = 0.0001m, NavTolerance = 0.005m }
        };
        var workspace = await service.CreateAsync(request, Fixture.Owner);
        fixture.Source.Set("boundary", fixture.Capture(Fixture.January, "boundary", 100.005m, 10.0001m, 100.005m));
        workspace = await fixture.Compare(service, workspace, Fixture.January, "boundary");
        workspace.Comparisons.Last().Differences.Should().BeEmpty();
        fixture.Source.Set("over", fixture.Capture(Fixture.January, "over", 100.0051m, 10.0002m, 100.0051m));
        workspace = await fixture.Compare(service, workspace, Fixture.January, "over");
        workspace.Comparisons.Last().Differences.Should().HaveCount(3).And.OnlyContain(x => x.Status == "New");
        workspace.Comparisons.Last().Differences.Single(x => x.Kind == "Balance").Difference.Should().Be(-0.0051m);
        workspace.Readiness.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task TenantCompanyOwnerAndReviewerBoundaries_RejectUnauthorizedChanges()
    {
        using var fixture = new Fixture();
        var service = fixture.Service();
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        (await service.GetAsync("other-tenant", Fixture.Company, workspace.WorkspaceId)).Should().BeNull();
        (await service.GetAsync(Fixture.Tenant, "other-company", workspace.WorkspaceId)).Should().BeNull();
        (await service.ListAsync("other-tenant", Fixture.Company)).Should().BeEmpty();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateCriteriaAsync(Fixture.Tenant, Fixture.Company,
            workspace.WorkspaceId, new(workspace.Version, workspace.Criteria), "stranger"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReviewAsync(Fixture.Tenant, Fixture.Company,
            workspace.WorkspaceId, new(workspace.Version, workspace.DataRevision, "Approved", "Self review.", []), Fixture.Owner));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReviewAsync(Fixture.Tenant, Fixture.Company,
            workspace.WorkspaceId, new(workspace.Version, workspace.DataRevision, "Approved", "Unauthorized review.", []), "stranger"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.FreezePacketAsync(Fixture.Tenant, Fixture.Company,
            workspace.WorkspaceId, new(workspace.Version), "stranger"));
    }

    [Fact]
    public async Task SourcePayloadTampering_AndStaleVersionsCannotAppendAComparison()
    {
        using var fixture = new Fixture();
        var service = fixture.Service();
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        var capture = fixture.Capture(Fixture.January, "tampered", 100m, 10m, 100m);
        fixture.Source.Set("tampered", capture with { Snapshots = [capture.Snapshots.Single() with { PayloadJson = "{\"changed\":true}" }] });
        await Assert.ThrowsAsync<OnboardingValidationException>(() => fixture.Compare(service, workspace, Fixture.January, "tampered"));
        (await service.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!.Comparisons.Should().BeEmpty();
        var updated = await service.UpdateCriteriaAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, workspace.Criteria with { ReviewInstructions = "Updated review." }), Fixture.Owner);
        fixture.Source.Set("valid", capture);
        await Assert.ThrowsAsync<OnboardingConcurrencyException>(() => fixture.Compare(service, workspace, Fixture.January, "valid"));
        await Assert.ThrowsAsync<OnboardingConcurrencyException>(() => service.ReviewAsync(Fixture.Tenant, Fixture.Company,
            workspace.WorkspaceId, new(updated.Version, workspace.DataRevision, "Approved", "Stale review.", []), Fixture.Reviewer));
    }

    [Fact]
    public async Task ReviewerIdentityCaseVariants_CannotSatisfyTwoIndependentReviews()
    {
        using var fixture = new Fixture();
        var service = fixture.Service();
        var request = fixture.Request() with
        {
            Criteria = fixture.Criteria with { RequiredReviewerIds = [Fixture.Reviewer, "second-controller"], MinimumReviewers = 2 }
        };
        var workspace = await service.CreateAsync(request, Fixture.Owner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReviewAsync(Fixture.Tenant, Fixture.Company,
            workspace.WorkspaceId, new(workspace.Version, workspace.DataRevision, "Approved", "Self review.", ["evidence:self"]), Fixture.Owner.ToUpperInvariant()));
        workspace = await service.ReviewAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, workspace.DataRevision, "Approved", "First decision.", ["evidence:first"]), Fixture.Reviewer);
        workspace = await service.ReviewAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, workspace.DataRevision, "Approved", "Same person, alternate case.", ["evidence:second"]), Fixture.Reviewer.ToUpperInvariant());
        workspace.Readiness.ApprovedReviewers.Should().Be(1);
        workspace.Readiness.IsReady.Should().BeFalse();
    }

    internal sealed class Fixture : IDisposable
    {
        internal const string Tenant = "tenant-onboarding";
        internal const string Company = "company-onboarding";
        internal const string Owner = "onboarding-owner";
        internal const string Reviewer = "controller";
        internal const string Account = "104d9cb4-60ca-472a-9564-d2d664098405";
        internal static readonly DateOnly January = new(2026, 1, 31);
        internal static readonly DateOnly February = new(2026, 2, 28);
        internal static readonly DateOnly March = new(2026, 3, 31);
        internal static readonly DateTimeOffset Now = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"meridian-onboarding-{Guid.NewGuid():N}");
        internal string StorePath => Path.Combine(_directory, "onboarding.json");
        internal CaptureSource Source { get; } = new();
        internal OnboardingScopeDto Scope { get; } = new(Tenant, Company,
            "ba7467d1-26e0-4f3b-9baa-528063de6774", "fund-profile", Guid.Parse("c0749249-2738-44be-b336-fb586f5b200b"),
            [Account], January, March);
        internal OnboardingCriteriaDto Criteria { get; } = new([January, February, March], ["Balance", "Position", "Nav"],
            0.005m, 0.0001m, 0.01m, 100m, [Reviewer], 1, true, "Check every required date and source correction.");
        internal OnboardingWorkspaceService Service() => new(new FileOnboardingWorkspaceStore(StorePath), Source, new FixedTimeProvider());
        internal CreateOnboardingWorkspaceRequestDto Request() => new("Selected account onboarding", Scope, Criteria);
        internal Task<OnboardingWorkspaceDto> Compare(OnboardingWorkspaceService service, OnboardingWorkspaceDto workspace,
            DateOnly date, string importId, string mapping = "mapping-v1")
            => service.CompareAsync(Tenant, Company, workspace.WorkspaceId,
                new(workspace.Version, date, "readonly-provider", importId, "account-map", mapping), Owner);

        internal OnboardingSourceCaptureDto Capture(DateOnly date, string id, decimal balance, decimal position, decimal? nav,
            string mapping = "mapping-v1")
        {
            var snapshotId = $"snapshot:{id}";
            var observations = new List<OnboardingObservationDto>
            {
                new("Balance", Account, "USD", null, 100m, balance, [snapshotId], [$"evidence:{id}:balance"]),
                new("Position", Account, "USD", "security-a", 10m, position, [snapshotId], [$"evidence:{id}:position"])
            };
            if (nav is not null)
            {
                observations.Add(new("Nav", Account, "USD", null, 100m, nav, [snapshotId], [$"evidence:{id}:nav"]));
            }
            var payload = JsonSerializer.Serialize(new { Scope, AsOfDate = date, MappingVersion = mapping, Observations = observations });
            var snapshot = new OnboardingSourceSnapshotDto(snapshotId, "RetainedComparisonSource", id, id,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(), Now, date,
                mapping, payload, [$"evidence:{id}:source"]);
            var close = new CloseReadinessProjectionDto(new(Scope.FundProfileId, Scope.LedgerBookId, Guid.Parse(Account), Scope.EntityId,
                date.ToString("yyyy-MM")), Now, "Ready", true, true,
                [new("retained-close", "controller", "Ready", Now, [$"close:{id}"])], []);
            return new([snapshot], observations.ToArray(), nav is null
                ? [new("missing-nav", "Nav", Account, "The selected account NAV source has not arrived.")]
                : [], close);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private sealed class FixedTimeProvider : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }

    internal sealed class CaptureSource : IOnboardingSourceProvider
    {
        private readonly Dictionary<string, OnboardingSourceCaptureDto> _captures = new(StringComparer.Ordinal);
        internal bool FailOnCapture { get; set; }
        internal void Set(string id, OnboardingSourceCaptureDto capture) => _captures[id] = capture;
        public Task<OnboardingSourceCaptureDto> CaptureAsync(OnboardingWorkspaceDto workspace,
            CaptureOnboardingComparisonRequestDto request, CancellationToken ct = default)
            => FailOnCapture ? throw new InvalidOperationException("Historical replay must not read live sources.")
                : Task.FromResult(_captures[request.ImportId]);
    }
}
