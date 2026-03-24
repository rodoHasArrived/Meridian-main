using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using FluentAssertions;
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

    private static DataQualityViewModel CreateSubject() =>
        new(StatusService.Instance, LoggingService.Instance, NotificationService.Instance);

    private static T InvokePrivate<T>(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull($"expected private method {methodName} to exist");
        return (T)method!.Invoke(instance, args)!;
    }
}
