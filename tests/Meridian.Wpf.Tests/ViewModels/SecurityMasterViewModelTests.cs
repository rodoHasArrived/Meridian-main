#if WINDOWS
using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Moq;
using ISmQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ISmService = Meridian.Contracts.SecurityMaster.ISecurityMasterService;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SecurityMasterViewModelTests
{
    [Fact]
    public void RefreshWorkflowCommand_LoadsIngestStatus_AndResolvesConflictQueue()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var workflowClient = new StubSecurityMasterOperatorWorkflowClient(
                new SecurityMasterIngestStatusResponse
                {
                    RetrievedAtUtc = new DateTimeOffset(2026, 4, 16, 14, 15, 0, TimeSpan.Zero),
                    LastCompleted = new SecurityMasterCompletedImportStatusResponse
                    {
                        FileExtension = ".csv",
                        Total = 12,
                        Processed = 12,
                        Imported = 10,
                        Skipped = 1,
                        Failed = 1,
                        ConflictsDetected = 1,
                        ErrorCount = 1,
                        StartedAtUtc = new DateTimeOffset(2026, 4, 16, 14, 0, 0, TimeSpan.Zero),
                        CompletedAtUtc = new DateTimeOffset(2026, 4, 16, 14, 5, 0, TimeSpan.Zero)
                    }
                },
                [
                    new SecurityMasterConflict(
                        ConflictId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                        SecurityId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        ConflictKind: "IdentifierMismatch",
                        FieldPath: "Identifiers.Primary",
                        ProviderA: "provider-a",
                        ValueA: "AAPL",
                        ProviderB: "provider-b",
                        ValueB: "APPL",
                        DetectedAt: new DateTimeOffset(2026, 4, 16, 14, 4, 0, TimeSpan.Zero),
                        Status: "Open")
                ]);

            var queryService = new Mock<ISmQueryService>();
            queryService
                .Setup(service => service.GetCorporateActionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            queryService
                .Setup(service => service.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid securityId, CancellationToken _) => CreateSecurityDetail(securityId));

            using var viewModel = new SecurityMasterViewModel(
                LoggingService.Instance,
                NotificationService.Instance,
                Mock.Of<ITradingParametersBackfillService>(),
                Mock.Of<ISecurityMasterImportService>(),
                new StubSecurityMasterRuntimeStatus(),
                workflowClient,
                new StubWorkstationSecurityMasterApiClient(),
                CreateFundContextService(),
                navigation,
                queryService.Object,
                Mock.Of<ISmService>());

            await WaitForConditionAsync(() =>
                viewModel.OpenConflictCount == 1 &&
                viewModel.RuntimeStatusDetail.Contains("Last ingest completed", StringComparison.OrdinalIgnoreCase));

            viewModel.SelectedConflict.Should().NotBeNull();
            viewModel.ConflictGroups.Should().ContainSingle();
            viewModel.SelectedConflictSummaryText.Should().Contain("Primary identifier");
            viewModel.SelectedConflictSeverityText.Should().Be("Critical");
            viewModel.SelectedConflictConfidenceText.Should().Contain("Low-confidence");
            viewModel.ShowFundReviewActions.Should().BeTrue();
            viewModel.ShowReconciliationAction.Should().BeTrue();
            viewModel.ShowReportPackAction.Should().BeTrue();
            viewModel.RuntimeStatusDetail.Should().Contain("Last ingest completed");
            viewModel.ImportSessionText.Should().Contain("Last ingest:");

            viewModel.ConflictNoteText = "Confirmed against downstream books.";
            await viewModel.AcceptSecondaryConflictCommand.ExecuteAsync(null);

            await WaitForConditionAsync(() =>
                viewModel.OpenConflictCount == 0 &&
                viewModel.SelectedConflict is null);

            workflowClient.LastResolution.Should().Be("AcceptB");
            workflowClient.LastResolvedBy.Should().Be("desktop-user");
            workflowClient.LastReason.Should().Be("Confirmed against downstream books.");
            viewModel.ConflictGroups.Should().BeEmpty();
        });
    }

    [Fact]
    public void RefreshWorkflowCommand_TradingParameterConflict_SurfacesBackfillPath()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var workflowClient = new StubSecurityMasterOperatorWorkflowClient(
                new SecurityMasterIngestStatusResponse
                {
                    RetrievedAtUtc = new DateTimeOffset(2026, 4, 16, 14, 15, 0, TimeSpan.Zero)
                },
                [
                    new SecurityMasterConflict(
                        ConflictId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                        SecurityId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                        ConflictKind: "FieldMismatch",
                        FieldPath: "TradingParameters.TickSize",
                        ProviderA: "provider-a",
                        ValueA: "0.01",
                        ProviderB: "provider-b",
                        ValueB: "0.05",
                        DetectedAt: new DateTimeOffset(2026, 4, 16, 14, 4, 0, TimeSpan.Zero),
                        Status: "Open")
                ]);

            var queryService = new Mock<ISmQueryService>();
            queryService
                .Setup(service => service.GetCorporateActionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            using var viewModel = new SecurityMasterViewModel(
                LoggingService.Instance,
                NotificationService.Instance,
                Mock.Of<ITradingParametersBackfillService>(),
                Mock.Of<ISecurityMasterImportService>(),
                new StubSecurityMasterRuntimeStatus(),
                workflowClient,
                new StubWorkstationSecurityMasterApiClient(),
                CreateFundContextService(),
                navigation,
                queryService.Object,
                Mock.Of<ISmService>());

            await WaitForConditionAsync(() => viewModel.OpenConflictCount == 1);

            viewModel.SelectedConflictSeverityText.Should().Be("Critical");
            viewModel.SelectedConflictAutoResolveText.Should().Contain("provenance");
            viewModel.ShowBackfillTradingParamsAction.Should().BeTrue();
            viewModel.ShowReconciliationAction.Should().BeTrue();
            viewModel.ShowCashFlowAction.Should().BeFalse();
        });
    }

    [Fact]
    public void OpenFundReportPackCommand_NavigatesToFundReportPackContext()
    {
        WpfTestThread.Run(() =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            using var viewModel = new SecurityMasterViewModel(
                LoggingService.Instance,
                NotificationService.Instance,
                Mock.Of<ITradingParametersBackfillService>(),
                Mock.Of<ISecurityMasterImportService>(),
                new StubSecurityMasterRuntimeStatus(),
                new StubSecurityMasterOperatorWorkflowClient(new SecurityMasterIngestStatusResponse
                {
                    RetrievedAtUtc = DateTimeOffset.UtcNow
                }, []),
                new StubWorkstationSecurityMasterApiClient(),
                CreateFundContextService(),
                navigation,
                Mock.Of<ISmQueryService>(),
                Mock.Of<ISmService>());

            viewModel.OpenFundReportPackCommand.Execute(null);

            navigation.GetCurrentPageTag().Should().Be("FundReportPack");
            navigation.GetBreadcrumbs().First().Parameter.Should().BeOfType<FundOperationsNavigationContext>();
            ((FundOperationsNavigationContext)navigation.GetBreadcrumbs().First().Parameter!)
                .Tab.Should().Be(FundOperationsTab.ReportPack);
        });
    }

    [Fact]
    public void SearchAsync_WhenRuntimeIsUnavailable_ShouldExposeConfigurationStatus()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            using var viewModel = new SecurityMasterViewModel(
                LoggingService.Instance,
                NotificationService.Instance,
                Mock.Of<ITradingParametersBackfillService>(),
                Mock.Of<ISecurityMasterImportService>(),
                new StubSecurityMasterRuntimeStatus(
                    isAvailable: false,
                    availabilityDescription: "Security Master is not configured for this workstation."),
                new StubSecurityMasterOperatorWorkflowClient(new SecurityMasterIngestStatusResponse
                {
                    RetrievedAtUtc = DateTimeOffset.UtcNow
                }, []),
                new StubWorkstationSecurityMasterApiClient(),
                CreateFundContextService(),
                navigation,
                Mock.Of<ISmQueryService>(),
                Mock.Of<ISmService>());

            viewModel.SearchQuery = "AAPL";

            await viewModel.SearchAsync();

            viewModel.StatusText.Should().Be("Security Master is not configured for this workstation.");
            viewModel.Results.Should().BeEmpty();
            viewModel.SelectedSecurity.Should().BeNull();
            viewModel.IsLoading.Should().BeFalse();
            viewModel.IsSearchRecoveryVisible.Should().BeTrue();
            viewModel.SearchRecoveryTitle.Should().Be("Security Master unavailable");
            viewModel.SearchRecoveryDetail.Should().Be("Security Master is not configured for this workstation.");
            viewModel.ClearSearchCommand.CanExecute(null).Should().BeTrue();
        });
    }

    [Fact]
    public void ClearSearchCommand_ShouldResetSearchResultsAndSelection()
    {
        WpfTestThread.Run(() =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            using var viewModel = CreateViewModel(
                navigation,
                new StubWorkstationSecurityMasterApiClient());
            var security = CreateTrustSnapshot(Guid.Parse("11111111-1111-1111-1111-111111111111")).Security;

            viewModel.SearchQuery = "AAPL";
            viewModel.Results.Add(security);
            viewModel.SelectedSecurity = security;

            viewModel.HasSearchQuery.Should().BeTrue();
            viewModel.HasSearchResults.Should().BeTrue();
            viewModel.ClearSearchCommand.CanExecute(null).Should().BeTrue();

            viewModel.ClearSearchCommand.Execute(null);

            viewModel.SearchQuery.Should().BeEmpty();
            viewModel.Results.Should().BeEmpty();
            viewModel.SelectedSecurity.Should().BeNull();
            viewModel.StatusText.Should().Be("Enter a query and press Search.");
            viewModel.IsSearchRecoveryVisible.Should().BeFalse();
            viewModel.ClearSearchCommand.CanExecute(null).Should().BeFalse();
        });
    }

    [Fact]
    public void SecurityMasterPageSource_BindsSearchRecoveryAction()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SecurityMasterViewModel.cs"));

        xaml.Should().Contain("SecurityMasterClearSearchButton");
        xaml.Should().Contain("SecurityMasterSearchRecoveryCard");
        xaml.Should().Contain("SecurityMasterSearchRecoveryClearButton");
        xaml.Should().Contain("{Binding IsSearchRecoveryVisible");
        xaml.Should().Contain("{Binding SearchRecoveryTitle}");
        xaml.Should().Contain("{Binding SearchRecoveryDetail}");
        xaml.Should().Contain("{Binding ClearSearchCommand}");
        viewModel.Should().Contain("ClearSearchCommand = new RelayCommand(OnClearSearch, CanClearSearch);");
        viewModel.Should().Contain("private void OnClearSearch()");
    }

    [Fact]
    public void LoadSelectedTrustSnapshotAsync_WithOpenConflicts_ShouldSurfaceBlockedTrustPosture()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var securityId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
            var snapshotClient = new StubWorkstationSecurityMasterApiClient
            {
                SnapshotFactory = (_, _) => CreateTrustSnapshot(
                    securityId,
                    assessments:
                    [
                        CreateAssessment(
                            securityId,
                            Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222"),
                            recommendation: SecurityMasterConflictRecommendationKind.PreserveWinner,
                            recommendedResolution: "AcceptA")
                    ])
            };

            using var viewModel = CreateViewModel(navigation, snapshotClient);

            await viewModel.LoadSelectedTrustSnapshotAsync(securityId);

            viewModel.SelectedTrustPostureText.ToUpperInvariant().Should().Contain("BLOCKED");
            viewModel.SelectedSecurityConflictSummaryText.Should().Contain("1 open conflict");
            viewModel.TrustScoreText.Should().Contain("Blocked");
        });
    }

    [Fact]
    public void LoadSelectedTrustSnapshotAsync_WhenTradingParametersAreIncomplete_ShouldRecommendBackfill()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var securityId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111");
            var snapshotClient = new StubWorkstationSecurityMasterApiClient
            {
                SnapshotFactory = (_, _) => CreateTrustSnapshot(
                    securityId,
                    tradingParametersComplete: false)
            };

            using var viewModel = CreateViewModel(navigation, snapshotClient);

            await viewModel.LoadSelectedTrustSnapshotAsync(securityId);

            viewModel.RecommendedActions.Should().Contain(action =>
                action.Kind == SecurityMasterRecommendedActionKind.BackfillTradingParameters);
            viewModel.ShowBackfillTradingParamsAction.Should().BeTrue();
            viewModel.SelectedTradingParameterCoverageText.ToUpperInvariant().Should().Contain("INCOMPLETE");
        });
    }

    [Fact]
    public void LoadSelectedTrustSnapshotAsync_ShouldDefaultConflictFiltersToSelectedSecurityOnly()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var selectedSecurityId = Guid.Parse("cccccccc-1111-1111-1111-111111111111");
            var otherSecurityId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");
            var workflowClient = new StubSecurityMasterOperatorWorkflowClient(
                new SecurityMasterIngestStatusResponse
                {
                    RetrievedAtUtc = DateTimeOffset.UtcNow
                },
                [
                    new SecurityMasterConflict(
                        ConflictId: Guid.Parse("cccccccc-2222-2222-2222-222222222222"),
                        SecurityId: selectedSecurityId,
                        ConflictKind: "IdentifierMismatch",
                        FieldPath: "Identifiers.Primary",
                        ProviderA: "golden-edm",
                        ValueA: "AAPL",
                        ProviderB: "vendor-b",
                        ValueB: "AAPL.O",
                        DetectedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
                        Status: "Open"),
                    new SecurityMasterConflict(
                        ConflictId: Guid.Parse("cccccccc-4444-4444-4444-444444444444"),
                        SecurityId: otherSecurityId,
                        ConflictKind: "IdentifierMismatch",
                        FieldPath: "Identifiers.Primary",
                        ProviderA: "golden-edm",
                        ValueA: "MSFT",
                        ProviderB: "vendor-b",
                        ValueB: "MSFT.O",
                        DetectedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                        Status: "Open")
                ]);
            var snapshotClient = new StubWorkstationSecurityMasterApiClient
            {
                SnapshotFactory = (_, _) => CreateTrustSnapshot(
                    selectedSecurityId,
                    assessments:
                    [
                        CreateAssessment(
                            selectedSecurityId,
                            Guid.Parse("cccccccc-2222-2222-2222-222222222222"),
                            recommendation: SecurityMasterConflictRecommendationKind.PreserveWinner,
                            recommendedResolution: "AcceptA")
                    ])
            };

            using var viewModel = CreateViewModel(navigation, snapshotClient, workflowClient);

            await WaitForConditionAsync(() => viewModel.OpenConflictCount == 2);
            await viewModel.LoadSelectedTrustSnapshotAsync(selectedSecurityId);

            viewModel.ShowOnlySelectedSecurityConflicts.Should().BeTrue();
            viewModel.FilteredConflicts.Should().ContainSingle();
            viewModel.FilteredConflicts[0].SecurityId.Should().Be(selectedSecurityId);
        });
    }

    [Fact]
    public void ApplyRecommendedConflictResolutionCommand_ShouldDispatchSuggestedResolution()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var securityId = Guid.Parse("dddddddd-1111-1111-1111-111111111111");
            var conflictId = Guid.Parse("dddddddd-2222-2222-2222-222222222222");
            var workflowClient = new StubSecurityMasterOperatorWorkflowClient(
                new SecurityMasterIngestStatusResponse
                {
                    RetrievedAtUtc = DateTimeOffset.UtcNow
                },
                [
                    new SecurityMasterConflict(
                        ConflictId: conflictId,
                        SecurityId: securityId,
                        ConflictKind: "IdentifierMismatch",
                        FieldPath: "Identifiers.Primary",
                        ProviderA: "golden-edm",
                        ValueA: "BRK-B",
                        ProviderB: "vendor-b",
                        ValueB: "BRK/B",
                        DetectedAt: DateTimeOffset.UtcNow,
                        Status: "Open")
                ]);
            var snapshotClient = new StubWorkstationSecurityMasterApiClient
            {
                SnapshotFactory = (_, _) => CreateTrustSnapshot(
                    securityId,
                    assessments:
                    [
                        CreateAssessment(
                            securityId,
                            conflictId,
                            currentWinningValue: "BRK-B",
                            challengerValue: "BRK/B",
                            recommendation: SecurityMasterConflictRecommendationKind.DismissAsEquivalent,
                            recommendedResolution: "Dismiss",
                            isBulkEligible: true)
                    ])
            };

            using var viewModel = CreateViewModel(navigation, snapshotClient, workflowClient);

            await WaitForConditionAsync(() => viewModel.OpenConflictCount == 1);
            await viewModel.LoadSelectedTrustSnapshotAsync(securityId);
            await viewModel.ApplyRecommendedConflictResolutionCommand.ExecuteAsync(null);

            workflowClient.LastResolution.Should().Be("Dismiss");
            workflowClient.LastResolvedBy.Should().Be("desktop-user");
        });
    }

    [Fact]
    public void ApplyBulkRecommendedResolutionsCommand_ShouldOnlySendLowRiskConflicts()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var securityId = Guid.Parse("eeeeeeee-1111-1111-1111-111111111111");
            var eligibleConflictId = Guid.Parse("eeeeeeee-2222-2222-2222-222222222222");
            var skippedConflictId = Guid.Parse("eeeeeeee-3333-3333-3333-333333333333");
            var snapshotClient = new StubWorkstationSecurityMasterApiClient
            {
                SnapshotFactory = (_, _) => CreateTrustSnapshot(
                    securityId,
                    downstreamImpact: CreateDownstreamImpact(isScoped: true, severity: SecurityMasterImpactSeverity.None),
                    assessments:
                    [
                        CreateAssessment(
                            securityId,
                            eligibleConflictId,
                            currentWinningValue: "BRK-B",
                            challengerValue: "BRK/B",
                            recommendation: SecurityMasterConflictRecommendationKind.DismissAsEquivalent,
                            recommendedResolution: "Dismiss",
                            isBulkEligible: true),
                        CreateAssessment(
                            securityId,
                            skippedConflictId,
                            currentWinningValue: "BRK-B",
                            challengerValue: "MSFT",
                            recommendation: SecurityMasterConflictRecommendationKind.PreserveWinner,
                            recommendedResolution: "AcceptA",
                            isBulkEligible: false)
                    ]),
                BulkResolveResult = new BulkResolveSecurityMasterConflictsResult(
                    Requested: 1,
                    Eligible: 1,
                    Resolved: 1,
                    Skipped: 0,
                    ResolvedConflictIds: [eligibleConflictId],
                    SkippedReasons: new Dictionary<Guid, string>())
            };

            using var viewModel = CreateViewModel(navigation, snapshotClient);

            await viewModel.LoadSelectedTrustSnapshotAsync(securityId);
            await viewModel.ApplyBulkRecommendedResolutionsCommand.ExecuteAsync(null);

            snapshotClient.LastBulkRequest.Should().NotBeNull();
            snapshotClient.LastBulkRequest!.ConflictIds.Should().ContainSingle().Which.Should().Be(eligibleConflictId);
            viewModel.BulkPreviewSummaryText.ToUpperInvariant().Should().Contain("RESOLVED");
        });
    }

    [Fact]
    public void LoadSelectedTrustSnapshotAsync_WithUpcomingCorporateActions_ShouldRefreshReadinessSummary()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var securityId = Guid.Parse("ffffffff-1111-1111-1111-111111111111");
            var upcomingAction = new CorporateActionDto(
                CorpActId: Guid.Parse("ffffffff-2222-2222-2222-222222222222"),
                SecurityId: securityId,
                EventType: "Dividend",
                ExDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
                PayDate: null,
                DividendPerShare: 0.52m,
                Currency: "USD",
                SplitRatio: null,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null);
            var snapshotClient = new StubWorkstationSecurityMasterApiClient
            {
                SnapshotFactory = (_, _) => CreateTrustSnapshot(
                    securityId,
                    corporateActions: [upcomingAction],
                    corporateActionsTrusted: false)
            };

            using var viewModel = CreateViewModel(navigation, snapshotClient);

            await viewModel.LoadSelectedTrustSnapshotAsync(securityId);

            viewModel.CorporateActionReadinessText.ToUpperInvariant().Should().Contain("UPCOMING");
            viewModel.CorporateActionImpactSummaryText.ToUpperInvariant().Should().Contain("REQUIRE REVIEW");
        });
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, int attempts = 40)
    {
        for (var index = 0; index < attempts; index++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        condition().Should().BeTrue();
    }

    private static SecurityMasterViewModel CreateViewModel(
        NavigationService navigation,
        StubWorkstationSecurityMasterApiClient snapshotClient,
        StubSecurityMasterOperatorWorkflowClient? workflowClient = null,
        Mock<ISmQueryService>? queryService = null)
    {
        queryService ??= new Mock<ISmQueryService>();
        queryService
            .Setup(service => service.GetCorporateActionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        queryService
            .Setup(service => service.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid securityId, CancellationToken _) => CreateSecurityDetail(securityId));

        return new SecurityMasterViewModel(
            LoggingService.Instance,
            NotificationService.Instance,
            Mock.Of<ITradingParametersBackfillService>(),
            Mock.Of<ISecurityMasterImportService>(),
            new StubSecurityMasterRuntimeStatus(),
            workflowClient ?? new StubSecurityMasterOperatorWorkflowClient(new SecurityMasterIngestStatusResponse
            {
                RetrievedAtUtc = DateTimeOffset.UtcNow
            }, []),
            snapshotClient,
            CreateFundContextService(),
            navigation,
            queryService.Object,
            Mock.Of<ISmService>());
    }

    private static FundContextService CreateFundContextService()
        => new(Path.Combine(Path.GetTempPath(), $"fund-context-{Guid.NewGuid():N}.json"));

    private static SecurityMasterTrustSnapshotDto CreateTrustSnapshot(
        Guid securityId,
        IReadOnlyList<SecurityMasterConflictAssessmentDto>? assessments = null,
        bool tradingParametersComplete = true,
        IReadOnlyList<CorporateActionDto>? corporateActions = null,
        SecurityMasterDownstreamImpactDto? downstreamImpact = null,
        bool? corporateActionsTrusted = null)
    {
        var effectiveAssessments = assessments ?? [];
        var effectiveCorporateActions = corporateActions ?? [];
        var hasUpcomingCorporateActions = effectiveCorporateActions.Any(action => action.ExDate >= DateOnly.FromDateTime(DateTime.UtcNow));
        var areCorporateActionsTrusted = corporateActionsTrusted ?? !hasUpcomingCorporateActions;
        var postureTone = effectiveAssessments.Count > 0
            ? SecurityMasterTrustTone.Blocked
            : !tradingParametersComplete || !areCorporateActionsTrusted
                ? SecurityMasterTrustTone.Review
                : SecurityMasterTrustTone.Trusted;
        var recommendedActions = new List<SecurityMasterRecommendedActionDto>();
        if (effectiveAssessments.Count > 0)
        {
            recommendedActions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.ResolveSelectedConflict,
                Title: "Resolve selected conflict",
                Detail: effectiveAssessments[0].RecommendedWinner,
                IsPrimary: true,
                IsEnabled: !string.IsNullOrWhiteSpace(effectiveAssessments[0].RecommendedResolution),
                ConflictId: effectiveAssessments[0].Conflict.ConflictId));
        }

        if (effectiveAssessments.Any(assessment => assessment.IsBulkEligible))
        {
            recommendedActions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.BulkResolveLowRiskConflicts,
                Title: "Apply low-risk bulk resolutions",
                Detail: "At least one filtered conflict qualifies for low-risk bulk assist.",
                IsPrimary: effectiveAssessments.Count == 0,
                IsEnabled: true));
        }

        if (!tradingParametersComplete)
        {
            recommendedActions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.BackfillTradingParameters,
                Title: "Backfill trading parameters",
                Detail: "Trading parameters incomplete: missing tick size.",
                IsPrimary: false,
                IsEnabled: true));
        }

        if (!areCorporateActionsTrusted)
        {
            recommendedActions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.ReviewCorporateActions,
                Title: "Review corporate actions",
                Detail: "Upcoming corporate actions require operator review.",
                IsPrimary: false,
                IsEnabled: true));
        }

        return new SecurityMasterTrustSnapshotDto(
            SecurityId: securityId,
            Security: new SecurityMasterWorkstationDto(
                SecurityId: securityId,
                DisplayName: "Apple Inc.",
                Status: SecurityStatusDto.Active,
                Classification: new SecurityClassificationSummaryDto(
                    AssetClass: "Equity",
                    SubType: "CommonStock",
                    PrimaryIdentifierKind: "Ticker",
                    PrimaryIdentifierValue: "AAPL"),
                EconomicDefinition: new SecurityEconomicDefinitionSummaryDto(
                    Currency: "USD",
                    Version: 4,
                    EffectiveFrom: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
                    EffectiveTo: null,
                    SubType: "CommonStock",
                    AssetFamily: "Public Equity",
                    IssuerType: "Corporate")),
            Identity: new SecurityIdentityDrillInDto(
                SecurityId: securityId,
                DisplayName: "Apple Inc.",
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                Version: 4,
                EffectiveFrom: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null,
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        Kind: SecurityIdentifierKind.Ticker,
                        Value: "AAPL",
                        IsPrimary: true,
                        ValidFrom: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero))
                ],
                Aliases: []),
            EconomicDefinition: new SecurityMasterEconomicDefinitionDrillInDto(
                SecurityId: securityId,
                AssetClass: "Equity",
                Currency: "USD",
                Version: 4,
                EffectiveFrom: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null,
                AssetFamily: "Public Equity",
                SubType: "CommonStock",
                IssuerType: "Corporate",
                RiskCountry: "US",
                WinningSourceSystem: "golden-edm",
                WinningSourceRecordId: "EDM-123",
                WinningSourceAsOf: new DateTimeOffset(2026, 4, 20, 9, 30, 0, TimeSpan.Zero),
                WinningSourceUpdatedBy: "workflow.bot",
                WinningSourceReason: "golden-copy"),
            TrustPosture: new SecurityMasterTrustPostureDto(
                Tone: postureTone,
                TrustScore: postureTone == SecurityMasterTrustTone.Blocked ? 55 : postureTone == SecurityMasterTrustTone.Review ? 78 : 92,
                Summary: postureTone == SecurityMasterTrustTone.Blocked
                    ? $"Trust is blocked by {effectiveAssessments.Count} open conflict(s)."
                    : !tradingParametersComplete
                        ? "Golden copy is stable, but trading readiness is incomplete."
                        : !areCorporateActionsTrusted
                            ? "Golden copy is stable, but upcoming corporate actions require review."
                            : "Golden copy is trusted for downstream governance workflows.",
                GoldenCopySource: "golden-edm",
                GoldenCopyRule: "Preserve winner unless the current winner is blank or the values are equivalent.",
                TradingParametersStatus: tradingParametersComplete
                    ? "Trading parameters complete as of 4/20/2026 9:30 AM."
                    : "Trading parameters incomplete: missing tick size.",
                CorporateActionReadiness: areCorporateActionsTrusted
                    ? "No upcoming corporate actions are scheduled in the current review window."
                    : "Upcoming corporate actions should be reviewed before downstream close.",
                HasOpenConflicts: effectiveAssessments.Count > 0,
                OpenConflictCount: effectiveAssessments.Count,
                TradingParametersComplete: tradingParametersComplete,
                HasUpcomingCorporateActions: hasUpcomingCorporateActions,
                CorporateActionsTrusted: areCorporateActionsTrusted),
            ProvenanceCandidates:
            [
                new SecurityMasterSourceCandidateDto(
                    ConflictId: null,
                    FieldPath: "EconomicDefinition",
                    SourceSystem: "golden-edm",
                    DisplayValue: "Apple Inc.",
                    IsWinningSource: true,
                    AsOf: new DateTimeOffset(2026, 4, 20, 9, 30, 0, TimeSpan.Zero),
                    UpdatedBy: "workflow.bot",
                    Reason: "golden-copy",
                    SourceRecordId: "EDM-123",
                    ImpactSeverity: SecurityMasterImpactSeverity.None)
            ],
            ConflictAssessments: effectiveAssessments,
            DownstreamImpact: downstreamImpact ?? CreateDownstreamImpact(),
            RecommendedActions: recommendedActions,
            History:
            [
                new SecurityMasterEventEnvelope(
                    GlobalSequence: 1,
                    SecurityId: securityId,
                    StreamVersion: 4,
                    EventType: "SecurityAmended",
                    EventTimestamp: new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero),
                    Actor: "workflow.bot",
                    CorrelationId: null,
                    CausationId: null,
                    Payload: JsonSerializer.SerializeToElement(new { field = "displayName" }),
                    Metadata: JsonSerializer.SerializeToElement(new { source = "test" }))
            ],
            CorporateActions: effectiveCorporateActions,
            RetrievedAtUtc: new DateTimeOffset(2026, 4, 20, 10, 5, 0, TimeSpan.Zero));
    }

    private static SecurityMasterConflictAssessmentDto CreateAssessment(
        Guid securityId,
        Guid conflictId,
        string currentWinningValue = "AAPL",
        string challengerValue = "AAPL.O",
        SecurityMasterConflictRecommendationKind recommendation = SecurityMasterConflictRecommendationKind.PreserveWinner,
        string recommendedResolution = "AcceptA",
        bool isBulkEligible = false)
        => new(
            Conflict: new SecurityMasterConflict(
                ConflictId: conflictId,
                SecurityId: securityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: currentWinningValue,
                ProviderB: "vendor-b",
                ValueB: challengerValue,
                DetectedAt: new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero),
                Status: "Open"),
            CurrentWinningValue: currentWinningValue,
            ChallengerValue: challengerValue,
            CurrentWinningSource: "golden-edm",
            ChallengerSource: "vendor-b",
            Recommendation: recommendation,
            RecommendedResolution: recommendedResolution,
            RecommendedWinner: recommendation switch
            {
                SecurityMasterConflictRecommendationKind.DismissAsEquivalent => "golden-edm and vendor-b normalize to the same value.",
                SecurityMasterConflictRecommendationKind.Challenger => "Accept vendor-b because the current winning value is blank.",
                _ => "Preserve golden-edm as the current winner."
            },
            ImpactSeverity: isBulkEligible ? SecurityMasterImpactSeverity.None : SecurityMasterImpactSeverity.Medium,
            ImpactSummary: isBulkEligible ? "Low-risk scoped exposure only." : "Manual review required for downstream governance workflows.",
            ImpactDetail: isBulkEligible ? "Eligible for low-risk bulk assist." : "Keep this resolution operator-reviewed.",
            IsBulkEligible: isBulkEligible,
            BulkIneligibilityReason: isBulkEligible ? null : "Conflict does not meet the low-risk bulk policy.");

    private static SecurityMasterDownstreamImpactDto CreateDownstreamImpact(
        bool isScoped = false,
        SecurityMasterImpactSeverity severity = SecurityMasterImpactSeverity.Unknown)
        => new(
            FundProfileId: isScoped ? "fund-alpha" : null,
            IsScoped: isScoped,
            Severity: severity,
            Summary: isScoped ? "No downstream exposure detected across the scoped fund profile." : "Not scoped to a fund profile. Downstream impact is unknown.",
            PortfolioExposureSummary: isScoped ? "No scoped portfolio exposure detected." : "Portfolio impact is not scoped.",
            LedgerExposureSummary: isScoped ? "No scoped ledger exposure detected." : "Ledger impact is not scoped.",
            ReconciliationExposureSummary: isScoped ? "No scoped reconciliation exposure detected." : "Reconciliation impact is not scoped.",
            ReportPackExposureSummary: isScoped ? "No scoped report-pack exposure detected." : "Report-pack impact is not scoped.",
            MatchedRunCount: 0,
            PortfolioExposureCount: 0,
            LedgerExposureCount: 0,
            ReconciliationExposureCount: 0,
            ReportPackExposureCount: 0,
            Links: []);

    private sealed class StubWorkstationSecurityMasterApiClient : IWorkstationSecurityMasterApiClient
    {
        public Func<Guid, string?, SecurityMasterTrustSnapshotDto?>? SnapshotFactory { get; init; }

        public BulkResolveSecurityMasterConflictsResult BulkResolveResult { get; init; } =
            new(
                Requested: 0,
                Eligible: 0,
                Resolved: 0,
                Skipped: 0,
                ResolvedConflictIds: [],
                SkippedReasons: new Dictionary<Guid, string>());

        public BulkResolveSecurityMasterConflictsRequest? LastBulkRequest { get; private set; }

        public Task<SecurityMasterTrustSnapshotDto?> GetTrustSnapshotAsync(Guid securityId, string? fundProfileId, CancellationToken ct = default)
            => Task.FromResult(SnapshotFactory?.Invoke(securityId, fundProfileId));

        public Task<ApiResponse<BulkResolveSecurityMasterConflictsResult>> BulkResolveConflictsAsync(BulkResolveSecurityMasterConflictsRequest request, CancellationToken ct = default)
        {
            LastBulkRequest = request;
            return Task.FromResult(ApiResponse<BulkResolveSecurityMasterConflictsResult>.Ok(BulkResolveResult));
        }
    }

    private sealed class StubSecurityMasterRuntimeStatus : ISecurityMasterRuntimeStatus
    {
        public StubSecurityMasterRuntimeStatus(
            bool isAvailable = true,
            string availabilityDescription = "Security Master runtime is available.")
        {
            IsAvailable = isAvailable;
            AvailabilityDescription = availabilityDescription;
        }

        public bool IsAvailable { get; }

        public string AvailabilityDescription { get; }
    }

    private sealed class StubSecurityMasterOperatorWorkflowClient : ISecurityMasterOperatorWorkflowClient
    {
        private readonly SecurityMasterIngestStatusResponse _status;
        private readonly List<SecurityMasterConflict> _conflicts;

        public StubSecurityMasterOperatorWorkflowClient(
            SecurityMasterIngestStatusResponse status,
            IReadOnlyList<SecurityMasterConflict> conflicts)
        {
            _status = status;
            _conflicts = conflicts.ToList();
        }

        public string? LastResolution { get; private set; }

        public string? LastResolvedBy { get; private set; }

        public string? LastReason { get; private set; }

        public Task<SecurityMasterIngestStatusResponse?> GetIngestStatusAsync(CancellationToken ct = default)
            => Task.FromResult<SecurityMasterIngestStatusResponse?>(_status);

        public Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterConflict>>(_conflicts.ToList());

        public Task<SecurityMasterConflict?> ResolveConflictAsync(Guid conflictId, string resolution, string resolvedBy, string? reason, CancellationToken ct = default)
        {
            LastResolution = resolution;
            LastResolvedBy = resolvedBy;
            LastReason = reason;

            var existing = _conflicts.FirstOrDefault(conflict => conflict.ConflictId == conflictId);
            if (existing is null)
            {
                return Task.FromResult<SecurityMasterConflict?>(null);
            }

            _conflicts.Remove(existing);
            return Task.FromResult<SecurityMasterConflict?>(existing with { Status = resolution });
        }
    }

    private static SecurityDetailDto CreateSecurityDetail(Guid securityId)
    {
        var emptyJson = JsonDocument.Parse("{}").RootElement.Clone();
        return new SecurityDetailDto(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Apple Inc.",
            Currency: "USD",
            CommonTerms: emptyJson,
            AssetSpecificTerms: emptyJson,
            Identifiers:
            [
                new SecurityIdentifierDto(
                    Kind: SecurityIdentifierKind.Ticker,
                    Value: "AAPL",
                    IsPrimary: true,
                    ValidFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
            ],
            Aliases: [],
            Version: 3,
            EffectiveFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null);
    }
}
#endif
