using System.IO;
using Meridian.Application.EnvironmentDesign;
using Meridian.Contracts.EnvironmentDesign;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class WorkstationOperatingContextServiceTests
{
    [Fact]
    public async Task LoadAsync_ShouldBuildFundCompatibilityContexts()
    {
        var fundContext = await CreateFundContextAsync();
        var storagePath = BuildStoragePath("operating-context");
        var service = new WorkstationOperatingContextService(fundContext, storagePath: storagePath);

        await service.LoadAsync();

        service.Contexts.Should().ContainSingle(context =>
            context.ScopeKind == OperatingContextScopeKind.Fund &&
            context.DisplayName == "Alpha Credit" &&
            context.CompatibilityFundProfileId == "alpha-credit");
    }

    [Fact]
    public async Task SetWindowModeAsync_ShouldPersistModeAndPresetAcrossReload()
    {
        var fundContext = await CreateFundContextAsync();
        var storagePath = BuildStoragePath("operating-context");
        var service = new WorkstationOperatingContextService(fundContext, storagePath: storagePath);

        await service.LoadAsync();
        await service.SelectContextAsync(service.Contexts[0].ContextKey);
        await service.SetWindowModeAsync(BoundedWindowMode.WorkbenchPreset, "accounting-review");

        var reloaded = new WorkstationOperatingContextService(fundContext, storagePath: storagePath);
        await reloaded.LoadAsync();

        reloaded.CurrentWindowMode.Should().Be(BoundedWindowMode.WorkbenchPreset);
        reloaded.CurrentLayoutPresetId.Should().Be("accounting-review");
        reloaded.CurrentContext.Should().NotBeNull();
        reloaded.CurrentContext!.CompatibilityFundProfileId.Should().Be("alpha-credit");
    }

    [Fact]
    public async Task LoadAsync_ShouldRestoreLastContextWithoutRaisingChangedEvent()
    {
        var fundContext = await CreateFundContextAsync();
        var storagePath = BuildStoragePath("operating-context");
        var service = new WorkstationOperatingContextService(fundContext, storagePath: storagePath);

        await service.LoadAsync();
        await service.SelectContextAsync(service.Contexts[0].ContextKey);

        var reloaded = new WorkstationOperatingContextService(fundContext, storagePath: storagePath);
        var changedEventCount = 0;
        reloaded.ActiveContextChanged += (_, _) => changedEventCount++;

        await reloaded.LoadAsync();

        changedEventCount.Should().Be(0);
        reloaded.CurrentContext.Should().NotBeNull();
        reloaded.CurrentContext!.ContextKey.Should().Be(service.Contexts[0].ContextKey);
    }

    [Fact]
    public async Task LoadAsync_WhenConcurrent_ShouldAwaitInFlightLoad()
    {
        var fundContext = await CreateFundContextAsync();
        var runtimeService = new BlockingEnvironmentRuntimeProjectionService();
        var service = new WorkstationOperatingContextService(
            fundContext,
            environmentRuntimeProjectionService: runtimeService,
            storagePath: BuildStoragePath("operating-context"));

        var firstLoad = service.LoadAsync();
        await runtimeService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondLoad = service.LoadAsync();

        secondLoad.IsCompleted.Should().BeFalse("startup callers must not observe operating context load as complete before restore finishes");

        runtimeService.Release();
        await Task.WhenAll(firstLoad, secondLoad);

        service.Contexts.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoadAsync_WithPublishedEnvironmentRuntime_ShouldApplyLaneMetadata()
    {
        var fundContext = await CreateFundContextAsync();
        var runtimeService = new EnvironmentDesignerService(persistencePath: null);
        var draft = await runtimeService.CreateDraftAsync(new CreateEnvironmentDraftRequest(
            Guid.NewGuid(),
            "Advisory Practice",
            "test",
            new OrganizationEnvironmentDefinitionDto(
                Guid.NewGuid(),
                "org-root",
                "ADV-ORG",
                "Northwind Advisory Group",
                "USD",
                [
                    new EnvironmentLaneDefinitionDto(
                        "advisory-lane",
                        "Advisory Practice",
                        EnvironmentLaneArchetype.AdvisoryPractice,
                        "governance",
                        "GovernanceShell",
                        "advisory-business",
                        "investor-client",
                        [
                            EnvironmentManagedScopeKind.IndividualInvestor,
                            EnvironmentManagedScopeKind.FamilyOffice,
                            EnvironmentManagedScopeKind.Fund
                        ])
                ],
                [
                    new EnvironmentNodeDefinitionDto("org-root", EnvironmentNodeKind.Organization, "ADV-ORG", "Northwind Advisory Group", "USD"),
                    new EnvironmentNodeDefinitionDto("advisory-business", EnvironmentNodeKind.Business, "ADV-HYB", "Advisory Operating Shell", "USD", ParentNodeDefinitionId: "org-root", ParentRelationshipType: OwnershipRelationshipTypeDto.Owns, LaneId: "advisory-lane", BusinessKind: BusinessKindDto.Hybrid),
                    new EnvironmentNodeDefinitionDto("investor-client", EnvironmentNodeKind.Client, "CLI-001", "Individual Investor Mandate", "USD", ParentNodeDefinitionId: "advisory-business", ParentRelationshipType: OwnershipRelationshipTypeDto.Advises, LaneId: "advisory-lane", ClientSegmentKind: ClientSegmentKind.IndividualInvestor)
                ],
                [])));
        await runtimeService.PublishAsync(new EnvironmentPublishPlanDto(draft.DraftId, "test"));

        var service = new WorkstationOperatingContextService(
            fundContext,
            fundStructureService: null,
            environmentRuntimeProjectionService: runtimeService,
            storagePath: BuildStoragePath("operating-context"));

        await service.LoadAsync();

        service.Contexts.Should().Contain(context =>
            context.EnvironmentLaneId == "advisory-lane" &&
            context.EnvironmentLaneName == "Advisory Practice" &&
            context.OperatingEnvironmentKind == OperatingEnvironmentKind.AdvisoryPractice &&
            context.DefaultWorkspaceId == "governance");
    }

    private static async Task<FundContextService> CreateFundContextAsync()
    {
        var storagePath = BuildStoragePath("fund-context");
        var service = new FundContextService(storagePath);
        await service.UpsertProfileAsync(new FundProfileDetail(
            FundProfileId: "alpha-credit",
            DisplayName: "Alpha Credit",
            LegalEntityName: "Alpha Credit Master Fund LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "governance",
            DefaultLandingPageTag: "GovernanceShell",
            DefaultLedgerScope: FundLedgerScope.Consolidated,
            IsDefault: true));
        await service.SelectFundProfileAsync("alpha-credit");
        return service;
    }

    private static string BuildStoragePath(string category)
        => Path.Combine(
            Path.GetTempPath(),
            "meridian-operating-context-tests",
            category,
            $"{Guid.NewGuid():N}.json");

    private sealed class BlockingEnvironmentRuntimeProjectionService : IEnvironmentRuntimeProjectionService
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered => _entered;

        public async Task<PublishedEnvironmentRuntimeDto?> GetCurrentRuntimeAsync(
            Guid? organizationId = null,
            CancellationToken ct = default)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(ct);
            return null;
        }

        public Task<PublishedEnvironmentRuntimeDto?> GetRuntimeForVersionAsync(Guid versionId, CancellationToken ct = default)
            => Task.FromResult<PublishedEnvironmentRuntimeDto?>(null);

        public void Release() => _release.TrySetResult();
    }
}
