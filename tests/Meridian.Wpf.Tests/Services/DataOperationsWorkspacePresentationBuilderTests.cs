using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Session;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;
using ExportFormatModel = Meridian.Ui.Services.ExportFormat;
using ExportJobModel = Meridian.Ui.Services.ExportJob;
using ExportJobRunModel = Meridian.Ui.Services.ExportJobRun;
using NotificationHistoryItemModel = Meridian.Ui.Services.Services.NotificationHistoryItem;
using ProviderInfoModel = Meridian.Ui.Services.Services.ProviderInfo;
using StatusProviderInfoModel = Meridian.Ui.Services.Services.StatusProviderInfo;

namespace Meridian.Wpf.Tests.Services;

public sealed class DataOperationsWorkspacePresentationBuilderTests
{
    [Fact]
    public void Build_WithLiveOperationalTelemetry_ProjectsProviderBackfillStorageAndExportState()
    {
        var retrievedAt = new DateTimeOffset(2026, 04, 16, 14, 30, 00, TimeSpan.Zero);
        var data = new DataOperationsWorkspaceData
        {
            ScopeLabel = "US Equities · Production",
            ScopeSummary = "Route providers, backfills, storage, and export jobs for US equities without leaving the shell.",
            RetrievedAt = retrievedAt,
            Notifications =
            [
                new NotificationHistoryItemModel
                {
                    Title = "Storage review",
                    Message = "Storage is aligned for the next package handoff.",
                    Type = NotificationType.Info,
                    Timestamp = retrievedAt.AddMinutes(-30).UtcDateTime,
                    IsRead = true
                }
            ],
            Providers =
            [
                new ProviderInfoModel { ProviderId = "polygon", DisplayName = "Polygon", Description = "Primary equities feed" },
                new ProviderInfoModel { ProviderId = "alpaca", DisplayName = "Alpaca", Description = "Fallback equities feed" }
            ],
            ProviderStatus = new StatusProviderInfoModel
            {
                ActiveProvider = "Polygon",
                IsConnected = true,
                ConnectionCount = 2,
                AvailableProviders = ["Polygon", "Alpaca"]
            },
            BackfillHealth = new BackfillHealthResponse
            {
                IsHealthy = true,
                Providers = new Dictionary<string, BackfillProviderHealth>
                {
                    ["Polygon"] = new() { IsAvailable = true, LatencyMs = 42 },
                    ["Alpaca"] = new() { IsAvailable = true, LatencyMs = 55 }
                }
            },
            LastBackfillStatus = new BackfillResultDto
            {
                Success = true,
                Provider = "Polygon",
                BarsWritten = 125000,
                StartedUtc = retrievedAt.AddHours(-5),
                CompletedUtc = retrievedAt.AddHours(-4)
            },
            BackfillExecutions =
            [
                new BackfillExecution
                {
                    Id = "exec-01",
                    Status = "Completed",
                    StartedAt = retrievedAt.AddHours(-4).UtcDateTime,
                    CompletedAt = retrievedAt.AddHours(-3).UtcDateTime,
                    SymbolsProcessed = 12,
                    BarsDownloaded = 125000
                }
            ],
            ResumableJobs =
            [
                new BackfillCheckpoint
                {
                    JobId = "job-01",
                    Provider = "Polygon",
                    FromDate = retrievedAt.AddDays(-7).UtcDateTime,
                    ToDate = retrievedAt.AddDays(-1).UtcDateTime,
                    CreatedAt = retrievedAt.AddMinutes(-90).UtcDateTime,
                    Status = CheckpointStatus.InProgress,
                    SymbolCheckpoints =
                    [
                        new SymbolCheckpoint { Symbol = "AAPL", Status = SymbolCheckpointStatus.Pending },
                        new SymbolCheckpoint { Symbol = "MSFT", Status = SymbolCheckpointStatus.Completed },
                        new SymbolCheckpoint { Symbol = "NVDA", Status = SymbolCheckpointStatus.Failed }
                    ]
                },
                new BackfillCheckpoint
                {
                    JobId = "job-02",
                    Provider = "Alpaca",
                    FromDate = retrievedAt.AddDays(-14).UtcDateTime,
                    ToDate = retrievedAt.AddDays(-8).UtcDateTime,
                    CreatedAt = retrievedAt.AddHours(-2).UtcDateTime,
                    Status = CheckpointStatus.Failed,
                    SymbolCheckpoints =
                    [
                        new SymbolCheckpoint { Symbol = "SPY", Status = SymbolCheckpointStatus.Failed },
                        new SymbolCheckpoint { Symbol = "QQQ", Status = SymbolCheckpointStatus.Completed }
                    ]
                }
            ],
            BackfillSchedules =
            [
                new BackfillSchedule
                {
                    Id = "sched-01",
                    Name = "Nightly gap fill",
                    IsEnabled = true,
                    CronExpression = "0 2 * * *"
                },
                new BackfillSchedule
                {
                    Id = "sched-02",
                    Name = "Weekend audit",
                    IsEnabled = false,
                    CronExpression = "0 3 * * 6"
                }
            ],
            StorageStats = new StorageStatsSummary
            {
                UsedPercentage = 61.2,
                TotalSymbols = 245,
                TotalFiles = 1800,
                NewestData = retrievedAt.AddMinutes(-5).UtcDateTime
            },
            StorageHealth = new StorageHealthReport
            {
                Status = "Healthy",
                CheckedAt = retrievedAt.UtcDateTime
            },
            ActiveSession = new CollectionSession
            {
                Id = "session-01",
                Name = "US Equities",
                Status = SessionStatus.Active,
                Provider = "Polygon",
                Symbols = ["AAPL", "MSFT", "NVDA"],
                Statistics = new CollectionSessionStatistics
                {
                    TotalEvents = 12345,
                    EventsPerSecond = 87.5f
                },
                StartedAt = retrievedAt.AddHours(-1).UtcDateTime,
                UpdatedAt = retrievedAt.AddMinutes(-2).UtcDateTime,
                CreatedAt = retrievedAt.AddHours(-1).UtcDateTime
            },
            Sessions =
            [
                new CollectionSession
                {
                    Id = "session-00",
                    Name = "Prior session",
                    Status = SessionStatus.Completed,
                    Provider = "Alpaca",
                    Symbols = ["SPY"],
                    UpdatedAt = retrievedAt.AddDays(-1).UtcDateTime,
                    CreatedAt = retrievedAt.AddDays(-1).UtcDateTime
                }
            ],
            ExportJobs =
            [
                new ExportJobModel
                {
                    Id = "export-01",
                    Name = "Daily EOD",
                    SourcePath = @"C:\data",
                    DestinationPath = @"C:\exports",
                    Format = ExportFormatModel.Csv,
                    CreatedAt = retrievedAt.AddDays(-1).UtcDateTime,
                    LastRunAt = retrievedAt.AddMinutes(-10).UtcDateTime,
                    Status = ExportJobStatus.Running,
                    RunHistory =
                    {
                        new ExportJobRunModel
                        {
                            StartedAt = retrievedAt.AddHours(-12).UtcDateTime,
                            CompletedAt = retrievedAt.AddHours(-11).UtcDateTime,
                            Success = true,
                            FilesExported = 24,
                            BytesExported = 4096
                        }
                    }
                }
            ]
        };

        var presentation = DataOperationsWorkspacePresentationBuilder.Build(data);

        presentation.Context.PrimaryScopeValue.Should().Be("US Equities · Production");
        presentation.Context.FreshnessValue.Should().Contain("US Equities active");
        presentation.Context.ReviewStateValue.Should().Be("2 resumable job(s)");
        presentation.Context.CriticalValue.Should().Be("No urgent blockers");
        presentation.QueueScopeBadgeText.Should().Be("US Equities · Production");
        presentation.QueueSummaryText.Should().Contain("2 resumable backfill job(s)").And.Contain("3 remaining symbol(s)");
        presentation.HeroState.FocusText.Should().Be("Historical coverage");
        presentation.HeroState.BadgeText.Should().Be("Attention");
        presentation.HeroState.HandoffTitleText.Should().Be("Resume staged backfills before the next operator handoff");
        presentation.HeroState.PrimaryActionId.Should().Be("Backfill");
        presentation.HeroState.SecondaryActionId.Should().Be("Schedules");
        presentation.HeroState.TargetText.Should().Be("Target: Backfill");
        presentation.HeroMetrics.Select(metric => metric.Label).Should().ContainInOrder("Providers", "Backfill", "Storage");
        presentation.HeroMetrics.Select(metric => metric.Value).Should().ContainInOrder("2/2 ready", "3 pending", "61% used");
        presentation.HeroMetrics.Select(metric => metric.Tone).Should().ContainInOrder("Success", "Warning", "Info");

        presentation.SummaryProvidersText.Should().Be("2/2 ready");
        presentation.SummaryBackfillText.Should().Be("3 pending");
        presentation.SummaryStorageText.Should().Be("61% used");

        presentation.CommandGroup.PrimaryCommands.Select(command => command.Id)
            .Should().ContainInOrder("ProviderHealth", "Backfill", "DataExport");
        presentation.CommandGroup.SecondaryCommands.Select(command => command.Id)
            .Should().Contain(["Provider", "Symbols", "Storage", "CollectionSessions", "Schedules", "PackageManager"]);

        presentation.ProviderQueueItems.Should().ContainSingle();
        presentation.ProviderQueueItems[0].StatusLabel.Should().Be("Healthy");
        presentation.ProviderQueueItems[0].CountLabel.Should().Be("2/2 ready");
        presentation.ProviderQueueItems[0].PrimaryActionId.Should().Be("ProviderHealth");

        presentation.BackfillQueueItems.Should().HaveCount(2);
        presentation.BackfillQueueItems[0].StatusLabel.Should().Be("Active");
        presentation.BackfillQueueItems[0].CountLabel.Should().Be("3 symbol(s) pending");
        presentation.BackfillQueueItems[0].SecondaryActionId.Should().Be("Schedules");
        presentation.BackfillQueueItems[1].Title.Should().Be("Collection sessions");
        presentation.BackfillQueueItems[1].PrimaryActionId.Should().Be("CollectionSessions");

        presentation.StorageQueueItems.Should().HaveCount(2);
        presentation.StorageQueueItems[0].StatusLabel.Should().Be("Healthy");
        presentation.StorageQueueItems[0].CountLabel.Should().Be("61% used");
        presentation.StorageQueueItems[1].Title.Should().Be("Export jobs");
        presentation.StorageQueueItems[1].StatusLabel.Should().Be("Running");
        presentation.StorageQueueItems[1].CountLabel.Should().Be("1 active");
        presentation.StorageQueueItems[1].PrimaryActionId.Should().Be("DataExport");

        presentation.RecentOperations.Should().HaveCount(3);
        presentation.RecentOperations.Select(item => item.ActionId)
            .Should().BeEquivalentTo(["CollectionSessions", "Backfill", "DataExport"]);
    }

    [Fact]
    public void Build_WithoutTelemetry_FallsBackToActionableEmptyStates()
    {
        var presentation = DataOperationsWorkspacePresentationBuilder.Build(new DataOperationsWorkspaceData
        {
            ScopeLabel = "Provider and storage health",
            ScopeSummary = "Provider health, backfill priority, storage follow-up, and export delivery stay in one fixed shell.",
            RetrievedAt = new DateTimeOffset(2026, 04, 16, 14, 30, 00, TimeSpan.Zero)
        });

        presentation.Context.FreshnessValue.Should().Be("Awaiting live telemetry");
        presentation.Context.ReviewStateValue.Should().Be("Awaiting queue");
        presentation.Context.CriticalValue.Should().Be("No urgent blockers");
        presentation.QueueScopeBadgeText.Should().Be("Provider and storage health");
        presentation.HeroState.FocusText.Should().Be("Provider routing");
        presentation.HeroState.BadgeText.Should().Be("Degraded");
        presentation.HeroState.HandoffTitleText.Should().Be("Refresh provider telemetry before routing new queue work");
        presentation.HeroState.PrimaryActionId.Should().Be("Retry");
        presentation.HeroState.SecondaryActionId.Should().Be("Diagnostics");
        presentation.HeroState.TargetText.Should().Be("Target: Refresh current shell");
        presentation.HeroMetrics.Select(metric => metric.Value).Should().ContainInOrder("No providers", "No active backfill", "No data");
        presentation.HeroMetrics.Select(metric => metric.Tone).Should().ContainInOrder("Warning", "Neutral", "Neutral");

        presentation.SummaryProvidersText.Should().Be("No providers");
        presentation.SummaryBackfillText.Should().Be("No active backfill");
        presentation.SummaryStorageText.Should().Be("No data");

        presentation.ProviderQueueItems.Should().ContainSingle();
        presentation.ProviderQueueItems[0].StatusLabel.Should().Be("Unavailable");
        presentation.ProviderQueueItems[0].CountLabel.Should().Be("No telemetry");

        presentation.BackfillQueueItems.Should().HaveCount(2);
        presentation.BackfillQueueItems[0].StatusLabel.Should().Be("Idle");
        presentation.BackfillQueueItems[0].CountLabel.Should().Be("No queued work");
        presentation.BackfillQueueItems[1].StatusLabel.Should().Be("Idle");

        presentation.StorageQueueItems.Should().HaveCount(2);
        presentation.StorageQueueItems[0].StatusLabel.Should().Be("Unavailable");
        presentation.StorageQueueItems[0].CountLabel.Should().Be("No stats");
        presentation.StorageQueueItems[1].StatusLabel.Should().Be("Idle");
        presentation.StorageQueueItems[1].CountLabel.Should().Be("No jobs");

        presentation.RecentOperations.Should().HaveCount(3);
        presentation.RecentOperations.Select(item => item.ActionId)
            .Should().ContainInOrder("ProviderHealth", "Backfill", "DataExport");
    }

    [Fact]
    public void Build_WithDisconnectedProvider_ShouldSurfaceDk1TrustRationaleInProviderQueue()
    {
        var presentation = DataOperationsWorkspacePresentationBuilder.Build(new DataOperationsWorkspaceData
        {
            ScopeLabel = "DK1 pilot providers",
            RetrievedAt = new DateTimeOffset(2026, 04, 24, 14, 30, 00, TimeSpan.Zero),
            Providers =
            [
                new ProviderInfoModel { ProviderId = "alpaca", DisplayName = "Alpaca", Description = "DK1 pilot streaming provider" }
            ],
            ProviderStatus = new StatusProviderInfoModel
            {
                ActiveProvider = "Alpaca",
                IsConnected = false,
                ConnectionCount = 1,
                AvailableProviders = ["Alpaca"]
            },
            BackfillHealth = new BackfillHealthResponse
            {
                IsHealthy = false,
                Providers = new Dictionary<string, BackfillProviderHealth>
                {
                    ["Alpaca"] = new() { IsAvailable = false, ErrorMessage = "connection closed" }
                }
            }
        });

        presentation.ProviderQueueItems.Should().ContainSingle();
        var providerQueue = presentation.ProviderQueueItems[0];
        providerQueue.StatusLabel.Should().Be("Offline");
        providerQueue.Detail.Should().Contain("Signal source: Provider quote/trade stream health telemetry");
        providerQueue.Detail.Should().Contain("Reason code: PROVIDER_STREAM_DEGRADED");
        providerQueue.Detail.Should().Contain("Recommended action: Verify provider connectivity");
        presentation.HeroState.FocusText.Should().Be("Provider routing");
        presentation.HeroState.BadgeText.Should().Be("Review");
        presentation.HeroState.HandoffTitleText.Should().Be("Stabilize provider health before the next historical or export handoff");
        presentation.HeroState.PrimaryActionId.Should().Be("ProviderHealth");
        presentation.HeroState.SecondaryActionId.Should().Be("Provider");
        presentation.HeroState.TargetText.Should().Be("Target: Provider Health");
    }

    [Fact]
    public void Build_WithUnreadNotifications_UsesAlertCountAndRoutesRecentActions()
    {
        var retrievedAt = new DateTimeOffset(2026, 04, 16, 14, 30, 00, TimeSpan.Zero);
        var presentation = DataOperationsWorkspacePresentationBuilder.Build(new DataOperationsWorkspaceData
        {
            ScopeLabel = "Ops Desk",
            ScopeSummary = "Keep provider, storage, and export follow-up together.",
            RetrievedAt = retrievedAt,
            UnreadAlerts = 1,
            Notifications =
            [
                new NotificationHistoryItemModel
                {
                    Title = "Export delayed",
                    Message = "Export queue is blocked on downstream delivery.",
                    Type = NotificationType.Warning,
                    Timestamp = retrievedAt.AddMinutes(-5).UtcDateTime,
                    IsRead = false
                },
                new NotificationHistoryItemModel
                {
                    Title = "Storage review",
                    Message = "Storage review remains open for the archive tier.",
                    Type = NotificationType.Info,
                    Timestamp = retrievedAt.AddMinutes(-15).UtcDateTime,
                    IsRead = true
                }
            ]
        });

        presentation.Context.CriticalValue.Should().Be("1 unread alert(s)");
        presentation.QueueScopeBadgeText.Should().Be("Ops Desk · 1 alert-linked");
        presentation.RecentOperations.Should().HaveCount(2);
        presentation.RecentOperations[0].ActionId.Should().Be("DataExport");
        presentation.RecentOperations[0].ActionLabel.Should().Be("Open Data Export");
        presentation.RecentOperations[1].ActionId.Should().Be("Storage");
        presentation.RecentOperations[1].ActionLabel.Should().Be("Open Storage");
    }
}
