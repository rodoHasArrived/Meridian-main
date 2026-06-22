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

            viewModel.SearchCommand.CanExecute(null).Should().BeTrue();

            await viewModel.SearchCommand.ExecuteAsync(null);

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
            viewModel.SearchCommand.CanExecute(null).Should().BeTrue();
            viewModel.ClearSearchCommand.CanExecute(null).Should().BeTrue();

            viewModel.ClearSearchCommand.Execute(null);

            viewModel.SearchQuery.Should().BeEmpty();
            viewModel.Results.Should().BeEmpty();
            viewModel.SelectedSecurity.Should().BeNull();
            viewModel.StatusText.Should().Be("Enter a query and press Search.");
            viewModel.IsSearchRecoveryVisible.Should().BeFalse();
            viewModel.SearchCommand.CanExecute(null).Should().BeFalse();
            viewModel.ClearSearchCommand.CanExecute(null).Should().BeFalse();
        });
    }

    [Fact]
    public void SearchCommand_ShouldTrackQueryAvailability()
    {
        WpfTestThread.Run(() =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            using var viewModel = CreateViewModel(
                navigation,
                new StubWorkstationSecurityMasterApiClient());

            viewModel.SearchCommand.CanExecute(null).Should().BeFalse();

            viewModel.SearchQuery = "msft";

            viewModel.HasSearchQuery.Should().BeTrue();
            viewModel.SearchCommand.CanExecute(null).Should().BeTrue();

            viewModel.SearchQuery = " ";

            viewModel.HasSearchQuery.Should().BeFalse();
            viewModel.SearchCommand.CanExecute(null).Should().BeFalse();
        });
    }

    [Fact]
    public void SearchWorkspaceFilters_ShouldNarrowLoadedResults_AndExposeMappingGaps()
    {
        WpfTestThread.Run(() =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            using var viewModel = CreateViewModel(
                navigation,
                new StubWorkstationSecurityMasterApiClient());

            var equity = CreateTrustSnapshot(Guid.Parse("12121212-1111-1111-1111-111111111111")).Security with
            {
                Classification = CreateTrustSnapshot(Guid.Parse("12121212-1111-1111-1111-111111111111")).Security.Classification with
                {
                    MatchedProvider = "polygon",
                    MatchedIdentifierKind = "Ticker",
                    MatchedIdentifierValue = "AAPL"
                }
            };
            var bondBase = CreateTrustSnapshot(Guid.Parse("23232323-2222-2222-2222-222222222222")).Security;
            var bond = bondBase with
            {
                DisplayName = "UST 10Y",
                Classification = bondBase.Classification with
                {
                    AssetClass = "Treasury",
                    PrimaryIdentifierKind = "Cusip",
                    PrimaryIdentifierValue = "91282CJZ5",
                    MatchedProvider = "bloomberg",
                    MatchedIdentifierKind = "Cusip",
                    MatchedIdentifierValue = "91282CJZ5"
                },
                EconomicDefinition = bondBase.EconomicDefinition with
                {
                    SubType = "Treasury",
                    AssetFamily = "Rates"
                }
            };
            var unmappedBase = CreateTrustSnapshot(Guid.Parse("34343434-3333-3333-3333-333333333333")).Security;
            var unmapped = unmappedBase with
            {
                DisplayName = "Internal Direct Loan",
                Classification = unmappedBase.Classification with
                {
                    AssetClass = "Direct Loan",
                    PrimaryIdentifierKind = "LoanId",
                    PrimaryIdentifierValue = "DL-001"
                },
                EconomicDefinition = unmappedBase.EconomicDefinition with
                {
                    SubType = "Unitranche",
                    AssetFamily = "Private Credit"
                }
            };

            viewModel.Results.Add(equity);
            viewModel.Results.Add(bond);
            viewModel.Results.Add(unmapped);
            viewModel.SelectedSecurity = equity;

            viewModel.FilteredResults.Should().HaveCount(3);
            viewModel.MappingHealthSummaryText.Should().Contain("2 mapped");
            viewModel.AssetClassFilterOptions.Should().Contain("All asset classes");
            viewModel.AssetClassFilterOptions.Should().Contain("Direct Loan");
            viewModel.AssetClassFilterOptions.Should().Contain("Equity");
            viewModel.AssetClassFilterOptions.Should().Contain("Treasury");
            viewModel.ProviderFilterOptions.Should().Contain("All providers");
            viewModel.ProviderFilterOptions.Should().Contain("bloomberg");
            viewModel.ProviderFilterOptions.Should().Contain("polygon");

            viewModel.SelectedAssetClassFilter = "Treasury";

            viewModel.FilteredResults.Should().ContainSingle().Which.SecurityId.Should().Be(bond.SecurityId);
            viewModel.SelectedSecurity.Should().BeNull();
            viewModel.SearchFilterSummaryText.Should().Contain("asset class: Treasury");

            viewModel.SelectedAssetClassFilter = "All asset classes";
            viewModel.ShowMappingGapsOnly = true;

            viewModel.FilteredResults.Should().ContainSingle().Which.SecurityId.Should().Be(unmapped.SecurityId);
            viewModel.SearchFilterSummaryText.Should().Contain("mapping gaps only");

            viewModel.ResetSearchFiltersCommand.Execute(null);

            viewModel.ShowMappingGapsOnly.Should().BeFalse();
            viewModel.SelectedAssetClassFilter.Should().Be("All asset classes");
            viewModel.SelectedProviderFilter.Should().Be("All providers");
            viewModel.FilteredResults.Should().HaveCount(3);
        });
    }

    [Fact]
    public void SecurityMasterPageSource_BindsSearchRecoveryAction()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SecurityMasterViewModel.cs"));

        xaml.Should().Contain("SecurityMasterClearSearchButton");
        xaml.Should().Contain("SecurityMasterSearchButton");
        xaml.Should().Contain("SecurityMasterSearchRecoveryCard");
        xaml.Should().Contain("SecurityMasterSearchRecoveryClearButton");
        xaml.Should().Contain("SecurityMasterFilterWorkbench");
        xaml.Should().Contain("SecurityMasterAssetClassFilter");
        xaml.Should().Contain("SecurityMasterProviderFilter");
        xaml.Should().Contain("SecurityMasterMappingGapFilter");
        xaml.Should().Contain("SecurityMasterResetFiltersButton");
        xaml.Should().Contain("ItemsSource=\"{Binding FilteredResults}\"");
        xaml.Should().Contain("<KeyBinding Key=\"Enter\"");
        xaml.Should().Contain("Command=\"{Binding SearchCommand}\"");
        xaml.Should().NotContain("Search_Click");
        xaml.Should().NotContain("SearchBox_KeyDown");
        xaml.Should().Contain("{Binding IsSearchRecoveryVisible");
        xaml.Should().Contain("{Binding SearchRecoveryTitle}");
        xaml.Should().Contain("{Binding SearchRecoveryDetail}");
        xaml.Should().Contain("{Binding ClearSearchCommand}");
        viewModel.Should().Contain("SearchCommand = new AsyncRelayCommand(ct => SearchAsync(ct), CanSearch);");
        viewModel.Should().Contain("private bool CanSearch()");
        viewModel.Should().Contain("ClearSearchCommand = new RelayCommand(OnClearSearch, CanClearSearch);");
        viewModel.Should().Contain("ResetSearchFiltersCommand = new RelayCommand(ResetSearchWorkspaceFilters, () => HasActiveSearchWorkspaceFilters);");
        viewModel.Should().Contain("private void OnClearSearch()");
    }

    [Fact]
    public void SecurityMasterWorkflowSection_ShouldOwnImportPollingAndBadgeStateWithAdapterBindings()
    {
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SecurityMasterViewModel.cs"));
        var sections = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SecurityMasterViewModel.Sections.cs"));

        sections.Should().Contain("internal sealed class SecurityMasterWorkflowSectionViewModel");
        sections.Should().Contain("public int ImportTotal { get; set; }");
        sections.Should().Contain("public int ImportProcessed { get; set; }");
        sections.Should().Contain("public bool IsImporting { get; set; }");
        sections.Should().Contain("public string WorkflowStatusText { get; set; }");
        sections.Should().Contain("public string WorkflowRetrievedAtText { get; set; }");
        sections.Should().Contain("public int OpenConflictCount { get; set; }");
        viewModel.Should().Contain("private readonly SecurityMasterWorkflowSectionViewModel _workflowSection = new();");
        viewModel.Should().Contain("get => _workflowSection.ImportTotal;");
        viewModel.Should().Contain("get => _workflowSection.IsImporting;");
        viewModel.Should().Contain("get => _workflowSection.WorkflowStatusText;");
        viewModel.Should().Contain("get => _workflowSection.OpenConflictCount;");
        viewModel.Should().Contain("SetSectionProperty(_workflowSection.OpenConflictCount, value, next => _workflowSection.OpenConflictCount = next)");
        viewModel.Should().NotContain("private int _openConflictCount;");
        viewModel.Should().NotContain("private bool _isImporting;");
        viewModel.Should().NotContain("private string _workflowStatusText");
    }

    [Fact]
    public void SecurityMasterSearchSection_ShouldOwnSearchFilterAndRecoveryStateWithAdapterBindings()
    {
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SecurityMasterViewModel.cs"));
        var sections = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SecurityMasterViewModel.Sections.cs"));

        sections.Should().Contain("public string SearchQuery { get; set; } = string.Empty;");
        sections.Should().Contain("public bool ActiveOnly { get; set; } = true;");
        sections.Should().Contain("public string SelectedAssetClassFilter { get; set; }");
        sections.Should().Contain("public string SelectedProviderFilter { get; set; }");
        sections.Should().Contain("public bool ShowMappingGapsOnly { get; set; }");
        sections.Should().Contain("public bool IsLoading { get; set; }");
        sections.Should().Contain("public string StatusText { get; set; } = \"Enter a query and press Search.\";");
        sections.Should().Contain("public bool HasSearchAttempted { get; set; }");
        viewModel.Should().Contain("private readonly SecurityMasterSearchSectionViewModel _searchSection = new(AllAssetClassesFilterLabel, AllProvidersFilterLabel);");
        viewModel.Should().Contain("get => _searchSection.SearchQuery;");
        viewModel.Should().Contain("get => _searchSection.SelectedAssetClassFilter;");
        viewModel.Should().Contain("get => _searchSection.IsLoading;");
        viewModel.Should().Contain("get => _searchSection.StatusText;");
        viewModel.Should().Contain("_searchSection.HasSearchAttempted = true;");
        viewModel.Should().NotContain("private string _searchQuery");
        viewModel.Should().NotContain("private bool _activeOnly");
        viewModel.Should().NotContain("private bool _hasSearchAttempted");
    }

    [Fact]
    public void SecurityMasterPrintSection_ShouldOwnCorporateActionFormStateWithAdapterBindings()
    {
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SecurityMasterViewModel.cs"));
        var sections = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SecurityMasterViewModel.Sections.cs"));

        sections.Should().Contain("public bool IsRecordCorpActionVisible { get; set; }");
        sections.Should().Contain("public string CorpActType { get; set; } = \"Dividend\";");
        sections.Should().Contain("public string CorpActExDate { get; set; } = string.Empty;");
        sections.Should().Contain("public decimal CorpActAmount { get; set; }");
        sections.Should().Contain("public string CorpActCurrency { get; set; } = \"USD\";");
        viewModel.Should().Contain("get => _printSection.IsRecordCorpActionVisible;");
        viewModel.Should().Contain("get => _printSection.CorpActType;");
        viewModel.Should().Contain("get => _printSection.CorpActExDate;");
        viewModel.Should().Contain("get => _printSection.CorpActAmount;");
        viewModel.Should().Contain("get => _printSection.CorpActCurrency;");
        viewModel.Should().NotContain("private bool _isRecordCorpActionVisible");
        viewModel.Should().NotContain("private string _corpActType");
        viewModel.Should().NotContain("private string _corpActExDate");
        viewModel.Should().NotContain("private decimal _corpActAmount");
        viewModel.Should().NotContain("private string _corpActCurrency");
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

    [Fact]
    public void LoadSelectedTrustSnapshotAsync_ShouldProjectScheduleAndLotModelIntoCoverageAndEvidence()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var securityId = Guid.Parse("abababab-1111-1111-1111-111111111111");
            var snapshotClient = new StubWorkstationSecurityMasterApiClient
            {
                SnapshotFactory = (_, _) => CreateTrustSnapshot(securityId)
            };

            using var viewModel = CreateViewModel(navigation, snapshotClient);

            await viewModel.LoadSelectedTrustSnapshotAsync(securityId);

            viewModel.CompanyCoverageFields.Should().Contain(field =>
                field.Label == "Schedule model" &&
                field.Value.Contains("Schedule book includes 2 cash-flow events", StringComparison.OrdinalIgnoreCase));
            viewModel.CompanyCoverageFields.Should().Contain(field =>
                field.Label == "Lot model" &&
                field.Value.Contains("factor-adjusted exposure", StringComparison.OrdinalIgnoreCase));
            viewModel.PrintEvidenceItems.Should().Contain(item =>
                item.Title == "Schedule model" &&
                item.Destination.Contains("Schedule source", StringComparison.OrdinalIgnoreCase));
            viewModel.PrintEvidenceItems.Should().Contain(item =>
                item.Title == "Lot model" &&
                item.Destination.Contains("Lot/open-position guidance", StringComparison.OrdinalIgnoreCase));
            viewModel.ScheduleBookStatusText.Should().Contain("2 schedule events");
            viewModel.ScheduleBookFields.Should().Contain(field =>
                field.Label == "Summary" &&
                field.Value.Contains("Schedule book includes 2 cash-flow events", StringComparison.OrdinalIgnoreCase));
            viewModel.ScheduleBookEvents.Should().HaveCount(2);
            viewModel.ScheduleBookFactorHistory.Should().ContainSingle();
            viewModel.ScheduleBookProvenanceHistory.Should().ContainSingle();
            viewModel.OpenLotReadModelStatusText.Should().Contain("1 open lot");
            viewModel.OpenLotReadModelFields.Should().Contain(field =>
                field.Label == "Quantity model" &&
                field.Value == "FactorAdjustedFace");
            viewModel.OpenLotRows.Should().ContainSingle().Which.LotId.Should().Be("lot-1");
            viewModel.OpenLotProvenanceHistory.Should().ContainSingle();
            viewModel.ValidationIssuesStatusText.Should().Contain("blocking issue");
            viewModel.ValidationIssues.Should().ContainSingle().Which.Code.Should().Be("SM_IDENTIFIER_DUPLICATE_ACTIVE");
            viewModel.ChangeHistoryStatusText.Should().Contain("structured change entr");
            viewModel.ChangeHistoryItems.Should().ContainSingle().Which.ChangedFieldsSummary.Should().Contain("Display name");
        });
    }

    [Fact]
    public void LoadSelectedTrustSnapshotAsync_ShouldLoadInstrumentPassportFromFallbackEndpoint()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var securityId = Guid.Parse("acacacac-1111-1111-1111-111111111111");
            var snapshot = CreateTrustSnapshot(securityId);
            var passport = CreateInstrumentPassport(snapshot);
            var snapshotClient = new StubWorkstationSecurityMasterApiClient
            {
                SnapshotFactory = (_, _) => snapshot,
                PassportFactory = (_, _) => passport
            };

            using var viewModel = CreateViewModel(navigation, snapshotClient);

            await viewModel.LoadSelectedTrustSnapshotAsync(securityId);

            snapshotClient.PassportRequestCount.Should().Be(1);
            snapshotClient.LastPassportSecurityId.Should().Be(securityId);
            viewModel.SelectedTrustSnapshot.Should().Be(snapshot);
            viewModel.SelectedInstrumentPassport.Should().Be(passport);
            viewModel.InstrumentPassportErrorText.Should().BeEmpty();
            viewModel.InstrumentPassportSummaryText.Should().Contain("Apple Inc.");
            viewModel.InstrumentPassportProviderConfidenceText.Should().Contain("1 active / 1 total");
            viewModel.InstrumentPassportFields.Should().Contain(field =>
                field.Label == "Provider confidence" &&
                field.Value == "1 active / 1 total");
            viewModel.InstrumentPassportFields.Should().Contain(field =>
                field.Label == "Primary provider" &&
                field.Value.Contains("Bloomberg", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void LoadSelectedTrustSnapshotAsync_WhenInstrumentPassportEndpointFails_ShouldPreserveTrustSnapshot()
    {
        WpfTestThread.Run(async () =>
        {
            var navigation = NavigationService.Instance;
            navigation.ResetForTests();
            navigation.Initialize(new Frame());

            var securityId = Guid.Parse("adadadad-1111-1111-1111-111111111111");
            var snapshot = CreateTrustSnapshot(securityId);
            var snapshotClient = new StubWorkstationSecurityMasterApiClient
            {
                SnapshotFactory = (_, _) => snapshot,
                PassportFactory = (_, _) => throw new InvalidOperationException("Passport endpoint offline")
            };

            using var viewModel = CreateViewModel(navigation, snapshotClient);

            await viewModel.LoadSelectedTrustSnapshotAsync(securityId);

            snapshotClient.PassportRequestCount.Should().Be(1);
            viewModel.SelectedTrustSnapshot.Should().Be(snapshot);
            viewModel.TrustSnapshotErrorText.Should().BeEmpty();
            viewModel.SelectedInstrumentPassport.Should().BeNull();
            viewModel.InstrumentPassportFields.Should().BeEmpty();
            viewModel.InstrumentPassportErrorText.Should().Be("Instrument passport failed to load.");
            viewModel.StatusText.Should().Contain("instrument passport evidence failed to load");
        });
    }

    [Fact]
    public void SecurityMasterPageSource_BindsScheduleBookAndOpenLotReadModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SecurityMasterViewModel.cs"));

        xaml.Should().Contain("SecurityMasterScheduleBookPanel");
        xaml.Should().Contain("SecurityMasterOpenLotReadModelPanel");
        xaml.Should().Contain("SecurityMasterValidationIssuesGrid");
        xaml.Should().Contain("SecurityMasterChangeHistoryGrid");
        xaml.Should().Contain("SecurityMasterScheduleBookEventsGrid");
        xaml.Should().Contain("SecurityMasterOpenLotRowsGrid");
        xaml.Should().Contain("{Binding ValidationIssuesStatusText}");
        xaml.Should().Contain("{Binding ChangeHistoryStatusText}");
        xaml.Should().Contain("ItemsSource=\"{Binding ValidationIssues}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding ChangeHistoryItems}\"");
        xaml.Should().Contain("{Binding ScheduleBookStatusText}");
        xaml.Should().Contain("{Binding OpenLotReadModelStatusText}");
        xaml.Should().Contain("ItemsSource=\"{Binding ScheduleBookEvents}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding OpenLotRows}\"");
        viewModel.Should().Contain("ObservableCollection<SecurityValidationIssueDto> ValidationIssues");
        viewModel.Should().Contain("ObservableCollection<SecurityMasterChangeHistoryItemDto> ChangeHistoryItems");
        viewModel.Should().Contain("ObservableCollection<SecurityMasterScheduleEventDto> ScheduleBookEvents");
        viewModel.Should().Contain("ObservableCollection<SecurityMasterOpenLotDto> OpenLotRows");
        viewModel.Should().Contain("PopulateValidationPresentation(snapshot);");
        viewModel.Should().Contain("PopulateChangeHistoryPresentation(snapshot);");
        viewModel.Should().Contain("PopulateScheduleBookPresentation(snapshot);");
        viewModel.Should().Contain("PopulateOpenLotReadModelPresentation(snapshot);");
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
                            : "Golden copy is trusted for downstream Accounting and Reporting workflows.",
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
            RetrievedAtUtc: new DateTimeOffset(2026, 4, 20, 10, 5, 0, TimeSpan.Zero))
        {
            ValidationReport = new SecurityValidationReportDto(
                SecurityId: securityId,
                Scope: "Security",
                EvaluatedAtUtc: new DateTimeOffset(2026, 4, 20, 10, 5, 0, TimeSpan.Zero),
                HasBlockingIssues: true,
                CriticalIssueCount: 0,
                ErrorIssueCount: 1,
                Issues:
                [
                    new SecurityValidationIssueDto(
                        Severity: SecurityValidationSeverityDto.Error,
                        Code: "SM_IDENTIFIER_DUPLICATE_ACTIVE",
                        Title: "Duplicate active identifier exists on the record",
                        Message: "The record contains duplicate active identifier 'Ticker|AAPL|'.",
                        AffectedFields: ["identifiers"],
                        SuggestedAction: "Expire duplicate identifiers or consolidate them into a single active identifier.",
                        EvidenceLinks: [])
                ]),
            ChangeHistory =
            [
                new SecurityMasterChangeHistoryItemDto(
                    ChangeId: "4:SecurityAmended",
                    StreamVersion: 4,
                    EventType: "SecurityAmended",
                    ChangedAtUtc: new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero),
                    EffectiveAtUtc: new DateTimeOffset(2026, 4, 20, 9, 30, 0, TimeSpan.Zero),
                    Actor: "workflow.bot",
                    Origin: "Vendor",
                    SourceSystem: "golden-edm",
                    SourceRecordId: "EDM-123",
                    Reason: "golden-copy refresh",
                    Summary: "Security Amended updated display name for 'Apple Inc.'.",
                    ChangedFields: ["Display name"],
                    ChangedFieldsSummary: "Display name"),
            ],
            ScheduleSummary = new SecurityMasterScheduleSummaryDto(
                SupportsCashflowSchedule: true,
                SupportsFactorHistory: true,
                HasEconomicScheduleTerms: true,
                CurrentFactor: 0.9825m,
                CurrentFactorDate: new DateOnly(2026, 4, 19),
                NextLifecycleDate: new DateOnly(2031, 12, 15),
                SourceSummary: "Schedules follow golden-edm record EDM-123 as of 2026-04-20 09:30 UTC.",
                Summary: "Factor-aware cash-flow support is available. Current factor 0.9825 as of 2026-04-19."),
            LotModel = new SecurityMasterLotModelDto(
                QuantityModel: "FactorAdjustedFace",
                LotSize: 1m,
                ContractMultiplier: null,
                UsesFaceValue: true,
                SupportsFactorAdjustedExposure: true,
                RequiresResolvedSecurityId: true,
                Summary: "Lots should reconcile by current face using factor-adjusted exposure (lot size 1)."),
            ScheduleBook = new SecurityMasterScheduleBookDto(
                SupportsCashflowSchedule: true,
                SupportsFactorHistory: true,
                HasEconomicScheduleTerms: true,
                Currency: "USD",
                CurrentFactor: 0.9825m,
                CurrentFactorDate: new DateOnly(2026, 4, 19),
                NextLifecycleDate: new DateOnly(2031, 12, 15),
                SourceSummary: "Schedules follow golden-edm record EDM-123 as of 2026-04-20 09:30 UTC.",
                Summary: "Schedule book includes 2 cash-flow events with factor history from golden-edm.",
                Events:
                [
                    new SecurityMasterScheduleEventDto(
                        EventId: "sched-coupon-2026-06",
                        EventType: "Coupon",
                        EffectiveDate: new DateOnly(2026, 6, 15),
                        PayDate: new DateOnly(2026, 6, 15),
                        AccrualStartDate: new DateOnly(2025, 12, 15),
                        AccrualEndDate: new DateOnly(2026, 6, 15),
                        ExpectedAmount: 29375m,
                        ActualAmount: null,
                        VarianceAmount: null,
                        FactorStart: 1m,
                        FactorEnd: 0.9825m,
                        Currency: "USD",
                        PostingStatus: "Forecast",
                        SourceSystem: "golden-edm",
                        SourceRecordId: "EDM-123",
                        SourceAsOfUtc: new DateTimeOffset(2026, 4, 20, 9, 30, 0, TimeSpan.Zero),
                        SourceUpdatedBy: "workflow.bot",
                        SourceReason: "Projected coupon from trusted schedule source.",
                        IsDerivedFromEconomicTerms: true,
                        IsCurrentProjection: true),
                    new SecurityMasterScheduleEventDto(
                        EventId: "sched-maturity-2031-12",
                        EventType: "Maturity",
                        EffectiveDate: new DateOnly(2031, 12, 15),
                        PayDate: new DateOnly(2031, 12, 15),
                        AccrualStartDate: new DateOnly(2031, 6, 15),
                        AccrualEndDate: new DateOnly(2031, 12, 15),
                        ExpectedAmount: 529375m,
                        ActualAmount: null,
                        VarianceAmount: null,
                        FactorStart: 0.9825m,
                        FactorEnd: 0m,
                        Currency: "USD",
                        PostingStatus: "Pending",
                        SourceSystem: "golden-edm",
                        SourceRecordId: "EDM-123",
                        SourceAsOfUtc: new DateTimeOffset(2026, 4, 20, 9, 30, 0, TimeSpan.Zero),
                        SourceUpdatedBy: "workflow.bot",
                        SourceReason: "Final principal and coupon projection.",
                        IsDerivedFromEconomicTerms: true,
                        IsCurrentProjection: true)
                ],
                FactorHistory:
                [
                    new SecurityMasterFactorPointDto(
                        PointId: "factor-current",
                        EffectiveDate: new DateOnly(2026, 4, 19),
                        Factor: 0.9825m,
                        PreviousFactor: 1m,
                        SourceSystem: "golden-edm",
                        SourceRecordId: "EDM-123",
                        SourceAsOfUtc: new DateTimeOffset(2026, 4, 20, 9, 30, 0, TimeSpan.Zero),
                        SourceUpdatedBy: "workflow.bot",
                        SourceReason: "Current factor from trustee feed.",
                        IsCurrentFactor: true)
                ],
                ProvenanceHistory:
                [
                    new SecurityMasterScheduleProvenanceDto(
                        ProvenanceId: "schedule-provenance-1",
                        Category: "Schedule",
                        Summary: "Loaded from golden EDM schedule source.",
                        EffectiveDate: new DateOnly(2026, 6, 15),
                        SourceSystem: "golden-edm",
                        SourceRecordId: "EDM-123",
                        SourceAsOfUtc: new DateTimeOffset(2026, 4, 20, 9, 30, 0, TimeSpan.Zero),
                        SourceUpdatedBy: "workflow.bot",
                        SourceReason: "Trusted source carried the schedule projection.",
                        StreamVersion: 4,
                        EventType: "SecurityAmended")
                ]),
            OpenLotReadModel = new SecurityMasterOpenLotReadModelDto(
                QuantityModel: "FactorAdjustedFace",
                LotSize: 1m,
                ContractMultiplier: null,
                UsesFaceValue: true,
                SupportsFactorAdjustedExposure: true,
                RequiresResolvedSecurityId: true,
                CurrentFactor: 0.9825m,
                CurrentFactorDate: new DateOnly(2026, 4, 19),
                AsOfUtc: new DateTimeOffset(2026, 4, 20, 10, 5, 0, TimeSpan.Zero),
                Summary: "Open lots reconcile by current face using factor-adjusted exposure.",
                Lots:
                [
                    new SecurityMasterOpenLotDto(
                        SecurityId: securityId,
                        PortfolioId: "portfolio-alpha",
                        RunId: "run-42",
                        AccountScopeId: "acct-alpha",
                        AccountScopeDisplayName: "Fund Alpha - Main",
                        VehicleScopeId: null,
                        VehicleScopeDisplayName: null,
                        LotId: "lot-1",
                        Symbol: "AAPL",
                        TradeDate: new DateTimeOffset(2026, 4, 20, 9, 30, 0, TimeSpan.Zero),
                        SettleDate: new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero),
                        OriginalQuantity: 100000m,
                        CurrentQuantity: 95000m,
                        OriginalFace: 100000m,
                        CurrentFace: 95000m,
                        FactorAdjustedQuantity: 93337.5m,
                        FactorAdjustedFace: 93337.5m,
                        CostBasis: 99000m,
                        EntryPrice: 99m,
                        UnrealizedPnl: 1250m,
                        Currency: "USD",
                        LotStatus: "Open",
                        SourceSystem: "ledger",
                        SourceRecordId: "LOT-1",
                        AsOfUtc: new DateTimeOffset(2026, 4, 20, 10, 5, 0, TimeSpan.Zero),
                        SourceUpdatedBy: "workflow.bot",
                        SourceReason: "Latest ledger lot read model.",
                        IsLongTerm: false,
                        Notes: "Primary scoped lot.")
                ],
                ProvenanceHistory:
                [
                    new SecurityMasterOpenLotProvenanceDto(
                        ProvenanceId: "lot-provenance-1",
                        RunId: "run-42",
                        PortfolioId: "portfolio-alpha",
                        AccountScopeId: "acct-alpha",
                        AccountScopeDisplayName: "Fund Alpha - Main",
                        SourceSystem: "ledger",
                        SourceRecordId: "LOT-1",
                        AsOfUtc: new DateTimeOffset(2026, 4, 20, 10, 5, 0, TimeSpan.Zero),
                        Summary: "Lot sourced from latest ledger read model.")
                ])
        };
    }

    private static InstrumentPassportDto CreateInstrumentPassport(SecurityMasterTrustSnapshotDto snapshot)
        => new(
            SecurityId: snapshot.SecurityId,
            Identity: snapshot.Identity,
            EconomicDefinition: snapshot.EconomicDefinition,
            IdentifierSummary: new SecurityMasterIdentifierSummaryDto(
                PrimaryIdentifierKind: "Ticker",
                PrimaryIdentifierValue: "AAPL",
                ActiveIdentifierCount: 1,
                ActiveAliasCount: 0,
                ProviderMappingCount: 1,
                DistinctProviderCount: 1,
                HasPrimaryIdentifier: true,
                HasProviderMappings: true,
                Summary: "Primary identifiers are aligned.",
                ProviderMappings: []),
            ProviderMappings: [],
            LifecycleEvents: [],
            CorporateActions: snapshot.CorporateActions,
            Pricing: new InstrumentPassportPricingDto(
                Status: "Ready",
                Summary: "Trading parameters are active.",
                TradingParameters: null,
                LotSize: 1m,
                TickSize: 0.01m,
                ContractMultiplier: 1m,
                TradingHoursUtc: "13:30-20:00",
                CircuitBreakerThresholdPct: 10m),
            Usage: snapshot.DownstreamImpact,
            TrustPosture: snapshot.TrustPosture,
            RetrievedAtUtc: snapshot.RetrievedAtUtc)
        {
            ProviderConfidence =
            [
                new InstrumentPassportProviderConfidenceDto(
                    Provider: "Bloomberg",
                    ProviderSource: "blp-reference",
                    MappingKind: "Ticker",
                    Symbol: "AAPL US Equity",
                    NormalizedSymbol: "AAPL",
                    IsPrimary: true,
                    IsActive: true,
                    FreshnessAsOf: snapshot.RetrievedAtUtc.AddMinutes(-5),
                    FreshnessMinutes: 5,
                    ConfidenceScore: 0.87m,
                    ConfidenceReason: "Primary ticker matches the golden copy.",
                    IdentifierConflictIds: [],
                    IdentifierConflictSummaries: [],
                    OverrideHistory: [])
            ]
        };

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
            ImpactSummary: isBulkEligible ? "Low-risk scoped exposure only." : "Manual review required for downstream Accounting workflows.",
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
        public Func<Guid, string?, InstrumentPassportDto?>? PassportFactory { get; init; }
        public int PassportRequestCount { get; private set; }
        public Guid? LastPassportSecurityId { get; private set; }
        public string? LastPassportFundProfileId { get; private set; }

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

        public Task<InstrumentPassportDto?> GetInstrumentPassportAsync(Guid securityId, string? fundProfileId, CancellationToken ct = default)
        {
            PassportRequestCount++;
            LastPassportSecurityId = securityId;
            LastPassportFundProfileId = fundProfileId;
            return Task.FromResult(PassportFactory?.Invoke(securityId, fundProfileId));
        }

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
