using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Onboarding;
using Meridian.Ui.Shared.Services;
using Fixture = Meridian.Tests.Ui.OnboardingWorkspaceServiceTests.Fixture;

namespace Meridian.Tests.Ui;

public sealed class FileOnboardingWorkspaceStoreTests
{
    [Fact]
    public async Task MutationOfRequestsSourceAndReturnedRows_CannotRewriteRetainedInputs()
    {
        using var fixture = new Fixture();
        var accounts = new[] { Fixture.Account };
        var dates = new[] { Fixture.January };
        var request = fixture.Request() with
        {
            Scope = fixture.Scope with { AccountIds = accounts },
            Criteria = fixture.Criteria with { RequiredDates = dates }
        };
        var service = fixture.Service();
        var workspace = await service.CreateAsync(request, Fixture.Owner);
        accounts[0] = "mutated-account";
        dates[0] = Fixture.March;
        var capture = fixture.Capture(Fixture.January, "retained", 101m, 10m, 100m);
        fixture.Source.Set("retained", capture);
        workspace = await fixture.Compare(service, workspace, Fixture.January, "retained");
        var savedJson = JsonSerializer.Serialize(workspace);
        ((OnboardingObservationDto[])capture.Observations)[0] = capture.Observations[0] with { ExternalAmount = 500m };
        ((IList<string>)workspace.Scope.AccountIds)[0] = "changed-after-return";
        ((IList<OnboardingComparisonDto>)workspace.Comparisons)[0] = workspace.Comparisons[0] with { MappingVersion = "changed-after-return" };
        var restarted = new FileOnboardingWorkspaceStore(fixture.StorePath);
        var retained = await restarted.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId);
        JsonSerializer.Serialize(retained).Should().Be(savedJson);
        retained!.Scope.AccountIds.Should().Equal(Fixture.Account);
        retained.Criteria.RequiredDates.Should().Equal(Fixture.January);
    }

    [Fact]
    public async Task ConcurrentIndependentStoreWriters_OnlyOneCanAdvanceTheSameVersion()
    {
        using var fixture = new Fixture();
        var created = await fixture.Service().CreateAsync(fixture.Request(), Fixture.Owner);
        var first = new FileOnboardingWorkspaceStore(fixture.StorePath);
        var second = new FileOnboardingWorkspaceStore(fixture.StorePath);
        var next = created with { Version = created.Version + 1, UpdatedAtUtc = Fixture.Now.AddMinutes(1) };
        var results = await Task.WhenAll(SaveOutcome(first), SaveOutcome(second));
        results.Should().ContainSingle(result => result).And.ContainSingle(result => !result);
        (await new FileOnboardingWorkspaceStore(fixture.StorePath).GetAsync(Fixture.Tenant, Fixture.Company, created.WorkspaceId))!
            .Version.Should().Be(next.Version);

        async Task<bool> SaveOutcome(FileOnboardingWorkspaceStore store)
        {
            try
            {
                await store.SaveAsync(next, created.Version);
                return true;
            }
            catch (OnboardingConcurrencyException)
            {
                return false;
            }
        }
    }

    [Fact]
    public async Task ExistingWorkspaceIdentity_CannotBeReplacedByAnotherTenantOrCompany()
    {
        using var fixture = new Fixture();
        var workspace = await fixture.Service().CreateAsync(fixture.Request(), Fixture.Owner);
        var store = new FileOnboardingWorkspaceStore(fixture.StorePath);
        foreach (var scope in new[]
                 {
                     workspace.Scope with { TenantId = "other-tenant" },
                     workspace.Scope with { CompanyId = "other-company" }
                 })
        {
            await Assert.ThrowsAsync<OnboardingValidationException>(() => store.SaveAsync(
                workspace with { Version = workspace.Version + 1, Scope = scope }, workspace.Version));
        }
        await Assert.ThrowsAsync<OnboardingConcurrencyException>(() => store.SaveAsync(
            workspace with { Scope = workspace.Scope with { TenantId = "other-tenant" } }, null));
        (await store.ListAsync("other-tenant", Fixture.Company)).Should().BeEmpty();
        (await store.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId)).Should().BeEquivalentTo(workspace);
    }

    [Fact]
    public async Task ComparisonAndPacketHistory_RejectTruncationAndReplacement()
    {
        using var fixture = new Fixture();
        var service = fixture.Service();
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        fixture.Source.Set("first", fixture.Capture(Fixture.January, "first", 101m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.January, "first");
        var packet = await service.FreezePacketAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        packet.Content.Readiness.IsReady.Should().BeFalse();
        packet.Content.Readiness.UnresolvedDifferences.Should().ContainSingle(row => row.AsOfDate == Fixture.January
            && row.Difference.Kind == "Balance" && row.Difference.OwnerId == Fixture.Owner);
        packet.Content.Readiness.UnresolvedDifferences.Single().Difference.EvidenceIds.Should().NotBeEmpty();
        var store = new FileOnboardingWorkspaceStore(fixture.StorePath);
        workspace = (await store.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!;
        var edits = new[]
        {
            workspace with { Comparisons = [] },
            workspace with { Comparisons = [workspace.Comparisons[0] with { MappingVersion = "replacement" }] },
            workspace with { Packets = [] },
            workspace with { Packets = [packet with { Content = packet.Content with { Name = "replacement" } }] }
        };
        foreach (var edited in edits)
        {
            await Assert.ThrowsAsync<OnboardingValidationException>(() => store.SaveAsync(
                edited with { Version = workspace.Version + 1 }, workspace.Version));
        }
        (await new FileOnboardingWorkspaceStore(fixture.StorePath).GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!
            .Should().BeEquivalentTo(workspace, options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData("comparison")]
    [InlineData("packet")]
    public async Task CorruptedRetainedHistory_FailsItsIntegrityCheck(string target)
    {
        using var fixture = new Fixture();
        var service = fixture.Service();
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        fixture.Source.Set("first", fixture.Capture(Fixture.January, "first", 101m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.January, "first");
        var packet = await service.FreezePacketAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(fixture.StorePath))!;
        var saved = document["workspaces"]![0]!;
        if (target == "comparison")
        {
            saved["comparisons"]![0]!["differences"]![0]!["status"] = "Resolved";
        }
        else
        {
            saved["packets"]![0]!["content"]!["readiness"]!["isReady"] = true;
        }
        await File.WriteAllTextAsync(fixture.StorePath, document.ToJsonString());
        var restarted = fixture.Service();
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId));
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.ListAsync(Fixture.Tenant, Fixture.Company));
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.FreezePacketAsync(Fixture.Tenant, Fixture.Company,
            workspace.WorkspaceId, new(workspace.Version + 1), Fixture.Owner));
        if (target == "comparison")
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => restarted.ReplayComparisonAsync(Fixture.Tenant,
                Fixture.Company, workspace.WorkspaceId, workspace.Comparisons[0].ComparisonId));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => restarted.GetPacketAsync(Fixture.Tenant,
                Fixture.Company, workspace.WorkspaceId, packet.PacketId));
        }
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("companyId")]
    public async Task CorruptRetainedScope_FailsClosedInsteadOfHidingTheWorkspace(string dimension)
    {
        using var fixture = new Fixture();
        var workspace = await fixture.Service().CreateAsync(fixture.Request(), Fixture.Owner);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(fixture.StorePath))!;
        document["workspaces"]![0]!["scope"]![dimension] = "";
        await File.WriteAllTextAsync(fixture.StorePath, document.ToJsonString());
        var restarted = new FileOnboardingWorkspaceStore(fixture.StorePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId));
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.ListAsync(Fixture.Tenant, Fixture.Company));
    }
}
