#if WINDOWS
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

            using var viewModel = new SecurityMasterViewModel(
                LoggingService.Instance,
                NotificationService.Instance,
                Mock.Of<ITradingParametersBackfillService>(),
                Mock.Of<ISecurityMasterImportService>(),
                new StubSecurityMasterRuntimeStatus(),
                workflowClient,
                navigation,
                queryService.Object,
                Mock.Of<ISmService>());

            await WaitForConditionAsync(() =>
                viewModel.OpenConflictCount == 1 &&
                viewModel.RuntimeStatusDetail.Contains("Last ingest completed", StringComparison.OrdinalIgnoreCase));

            viewModel.SelectedConflict.Should().NotBeNull();
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

    private sealed class StubSecurityMasterRuntimeStatus : ISecurityMasterRuntimeStatus
    {
        public bool IsAvailable => true;

        public string AvailabilityDescription => "Security Master runtime is available.";
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
}
#endif
