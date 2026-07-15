using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using FluentAssertions;
using Meridian.Contracts.Api.Quality;
using Meridian.Ui.Services.DataQuality;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// Characterization tests for the current JSON-to-model mapping behavior in DataQualityViewModel.
/// These pin the existing translation rules before transport and mapping logic move into dedicated services.
/// </summary>
public sealed class DataQualityViewModelCharacterizationTests
{
    [Fact]
    public void BuildAlertModel_MapsNumericSeverityToCurrentPresentationModel()
    {
        using var viewModel = CreateSubject();
        using var document = JsonDocument.Parse("""
            {
              "id": "alert-42",
              "symbol": "SPY",
              "type": "StaleData",
              "description": "No trades received",
              "severity": 3
            }
            """);

        var result = InvokePrivate<AlertModel?>(viewModel, "BuildAlertModel", document.RootElement);

        result.Should().NotBeNull();
        result!.Id.Should().Be("alert-42");
        result.Symbol.Should().Be("SPY");
        result.AlertType.Should().Be("StaleData");
        result.Message.Should().Be("No trades received");
        result.Severity.Should().Be("Critical");
        ((SolidColorBrush)result.SeverityBrush).Color.Should().Be(Color.FromRgb(244, 67, 54));
    }

    [Fact]
    public void BuildAnomalyModel_MapsEnumIndexesAndFormatsTimestamp()
    {
        using var viewModel = CreateSubject();
        using var document = JsonDocument.Parse("""
            {
              "symbol": "AAPL",
              "description": "Gap detected",
              "severity": 1,
              "type": 10,
              "detectedAt": "2026-03-20T14:15:00Z"
            }
            """);

        var result = InvokePrivate<AnomalyModel?>(viewModel, "BuildAnomalyModel", document.RootElement);

        result.Should().NotBeNull();
        result!.Symbol.Should().Be("AAPL");
        result.Description.Should().Be("Gap detected");
        result.Type.Should().Be("CrossedMarket");
        result.Timestamp.Should().Be("Mar 20 14:15");
        result.SeverityColor.Color.Should().Be(Color.FromRgb(255, 193, 7));
    }

    [Fact]
    public void BuildAlertModel_WhenPayloadIsNotObject_ReturnsNull()
    {
        using var viewModel = CreateSubject();
        using var document = JsonDocument.Parse("[]");

        var result = InvokePrivate<AlertModel?>(viewModel, "BuildAlertModel", document.RootElement);

        result.Should().BeNull();
    }

    [Fact]
    public void ApplySymbolFilter_WhenSearchMiss_HasClearRecoveryState()
    {
        using var viewModel = CreateSubject();
        viewModel.SymbolQuality.Add(new SymbolQualityModel { Symbol = "AAPL" });
        viewModel.SymbolQuality.Add(new SymbolQualityModel { Symbol = "MSFT" });

        viewModel.ApplySymbolFilter("QQQ");

        viewModel.FilteredSymbols.Should().BeEmpty();
        viewModel.HasNoSymbols.Should().BeTrue();
        viewModel.HasActiveSymbolFilter.Should().BeTrue();
        viewModel.SymbolFilterScopeText.Should().Be("Filtering monitored symbols by \"QQQ\".");
        viewModel.SymbolEmptyStateTitle.Should().Be("No monitored symbols match \"QQQ\".");
        viewModel.SymbolEmptyStateDetail.Should().Be("Clear the filter to return to the monitored-symbol quality list.");

        viewModel.ClearSymbolFilter();

        viewModel.FilteredSymbols.Select(symbol => symbol.Symbol).Should().Equal("AAPL", "MSFT");
        viewModel.HasNoSymbols.Should().BeFalse();
        viewModel.HasActiveSymbolFilter.Should().BeFalse();
        viewModel.SymbolFilterScopeText.Should().Be("Showing all monitored symbols.");
    }

    [Fact]
    public void SymbolQualityTable_ShouldExposeDenseTerminalColumns()
    {
        using var viewModel = CreateSubject();

        viewModel.SymbolQualityTable.Title.Should().Be("Data quality by symbol");
        viewModel.SymbolQualityTable.Rows.Should().BeSameAs(viewModel.FilteredSymbols);
        viewModel.SymbolQualityTable.Columns.Select(static column => column.Header).Should().ContainInOrder(
            "Symbol",
            "Score",
            "Grade",
            "Status",
            "Issues",
            "Last Update");
    }

    [Fact]
    public void SelectedSymbolQuality_ShouldOwnDrilldownStateAndCompareAvailability()
    {
        using var viewModel = CreateSubject();
        var symbol = new SymbolQualityModel
        {
            Symbol = "SPY",
            Score = 98.1,
            ScoreFormatted = "98.1%",
            Grade = "A",
            Status = "Healthy",
            Issues = "None",
            LastUpdate = DateTimeOffset.UtcNow,
            LastUpdateFormatted = "Now",
            Presentation = new DataQualitySymbolPresentation
            {
                Symbol = "SPY",
                Score = 98.1,
                ScoreFormatted = "98.1%",
                Status = "Healthy"
            }
        };

        viewModel.SelectedSymbolQuality = symbol;

        viewModel.CanCompareSelectedSymbol.Should().BeTrue();
        viewModel.IsDrilldownVisible.Should().BeTrue();
        viewModel.DrilldownSymbolHeader.Should().Contain("SPY");

        viewModel.SelectedSymbolQuality = null;

        viewModel.CanCompareSelectedSymbol.Should().BeFalse();
        viewModel.IsDrilldownVisible.Should().BeFalse();
    }

    [Fact]
    public void ApplySymbolFilter_WhenSelectedRowIsFilteredOut_ShouldClearDrilldown()
    {
        using var viewModel = CreateSubject();
        var selected = new SymbolQualityModel
        {
            Symbol = "AAPL",
            Score = 99.0,
            ScoreFormatted = "99.0%",
            Grade = "A",
            Status = "Healthy",
            Presentation = new DataQualitySymbolPresentation
            {
                Symbol = "AAPL",
                Score = 99.0,
                ScoreFormatted = "99.0%",
                Status = "Healthy"
            }
        };
        viewModel.SymbolQuality.Add(selected);
        viewModel.SymbolQuality.Add(new SymbolQualityModel { Symbol = "MSFT" });
        viewModel.ApplySymbolFilter(string.Empty);
        viewModel.SelectedSymbolQuality = selected;

        viewModel.ApplySymbolFilter("MSFT");

        viewModel.SelectedSymbolQuality.Should().BeNull();
        viewModel.CanCompareSelectedSymbol.Should().BeFalse();
        viewModel.IsDrilldownVisible.Should().BeFalse();
        viewModel.FilteredSymbols.Should().ContainSingle(symbol => symbol.Symbol == "MSFT");
    }

    [Fact]
    public void ApplySymbolFilter_WhenLibraryIsEmpty_KeepsSetupEmptyState()
    {
        using var viewModel = CreateSubject();

        viewModel.ApplySymbolFilter("SPY");

        viewModel.FilteredSymbols.Should().BeEmpty();
        viewModel.HasNoSymbols.Should().BeTrue();
        viewModel.HasActiveSymbolFilter.Should().BeTrue();
        viewModel.SymbolEmptyStateTitle.Should().Be("No symbols are currently being monitored.");
        viewModel.SymbolEmptyStateDetail.Should().Be("Clear the filter or add symbols from the workspace before running quality checks.");

        viewModel.ClearSymbolFilter();

        viewModel.HasActiveSymbolFilter.Should().BeFalse();
        viewModel.SymbolEmptyStateDetail.Should().Be("Add symbols from the workspace before running quality checks.");
    }

    [Fact]
    public async Task RefreshAndRepairGap_UsesInjectedCompositeServicesAndRetainsExactRemediationIdentity()
    {
        var apiClient = Substitute.For<IDataQualityApiClient>();
        var presentationService = Substitute.For<IDataQualityPresentationService>();
        var observedAt = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new DataQualityPresentationSnapshot
        {
            IsAvailable = true,
            IsPartial = true,
            DashboardVersion = "quality-v42",
            OverallScore = 81.25,
            OverallScoreText = "81.3",
            OverallGradeText = "B",
            StatusText = "Partial evidence",
            Symbols =
            [
                new DataQualitySymbolPresentation
                {
                    Symbol = "AAPL",
                    Score = 81.25,
                    ScoreFormatted = "81.3%",
                    Grade = "B",
                    Status = "Warning",
                    Issues = "1 open gap",
                    LastUpdate = observedAt,
                    LastUpdateFormatted = "Now",
                    Components =
                    [
                        new QualityComponentResponse("StoredCompleteness", "Stored completeness", 0.4, 98.5, "Available", observedAt, 0, "Complete"),
                        new QualityComponentResponse("StreamingFreshness", "Streaming freshness", 0.35, null, "Unavailable", null, 0, "Feed offline"),
                        new QualityComponentResponse("AdapterGapIntegrity", "Adapter gap integrity", 0.25, 62.0, "Partial", observedAt, 1, "One gap")
                    ],
                    GapCount = 1
                }
            ],
            Gaps =
            [
                new DataQualityGapPresentation
                {
                    GapId = "gap-stable-17",
                    Symbol = "AAPL",
                    Provider = "polygon",
                    Description = "Missing minute bars",
                    Duration = "15m",
                    DashboardVersion = "quality-v42",
                    CanRepair = true
                }
            ]
        };
        presentationService
            .GetSnapshotAsync("7d", Arg.Any<CancellationToken>())
            .Returns(snapshot);
        apiClient
            .RepairGapAsync(
                "AAPL",
                Arg.Is<QualityGapRemediationRequest>(request =>
                    request.GapId == "gap-stable-17" && request.DashboardVersion == "quality-v42"),
                Arg.Any<CancellationToken>())
            .Returns(new QualityGapRemediationResponse(
                "gap-stable-17",
                "AAPL",
                "Completed",
                "polygon",
                new DateOnly(2026, 7, 14),
                new DateOnly(2026, 7, 15),
                "repair-17",
                "Repair queued"));

        using var viewModel = new DataQualityViewModel(
            StatusService.Instance,
            LoggingService.Instance,
            NotificationService.Instance,
            apiClient,
            presentationService);

        await viewModel.RefreshAsync();

        viewModel.StatusText.Should().Be("Partial evidence");
        viewModel.SymbolQuality.Should().ContainSingle();
        viewModel.SymbolQuality[0].StoredCompletenessText.Should().Be("98.5");
        viewModel.SymbolQuality[0].StreamingFreshnessText.Should().Be("--");
        viewModel.SymbolQuality[0].AdapterIntegrityText.Should().Be("62.0");
        viewModel.Gaps.Should().ContainSingle(gap =>
            gap.GapId == "gap-stable-17" && gap.DashboardVersion == "quality-v42");

        (await viewModel.RepairGapAsync("gap-stable-17")).Should().BeTrue();
        viewModel.Gaps.Should().BeEmpty();
        await apiClient.Received(1).RepairGapAsync(
            "AAPL",
            Arg.Is<QualityGapRemediationRequest>(request =>
                request.GapId == "gap-stable-17" && request.DashboardVersion == "quality-v42"),
            Arg.Any<CancellationToken>());
    }

    private static DataQualityViewModel CreateSubject()
    {
        var apiClient = Substitute.For<IDataQualityApiClient>();
        var presentationService = new DataQualityPresentationService(apiClient);
        return new DataQualityViewModel(
            StatusService.Instance,
            LoggingService.Instance,
            NotificationService.Instance,
            apiClient,
            presentationService);
    }

    private static T InvokePrivate<T>(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull($"expected private method {methodName} to exist");
        return (T)method!.Invoke(instance, args)!;
    }
}
