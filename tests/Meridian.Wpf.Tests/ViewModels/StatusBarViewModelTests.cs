using System.Windows.Media;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

/// <summary>
/// Unit tests for the pure formatting/derivation helpers on
/// <see cref="StatusBarViewModel"/>. The view model itself relies on
/// <c>Application.Current.Dispatcher</c>, so the live refresh loop is not
/// exercised here; these tests cover the two pieces that are easy to break
/// without UI: throughput formatting and status derivation.
/// </summary>
public sealed class StatusBarViewModelTests
{
    // ── FormatThroughput ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "0 ev/s")]
    [InlineData(1, "1 ev/s")]
    [InlineData(999, "999 ev/s")]
    [InlineData(1_000, "1.0K ev/s")]
    [InlineData(1_499, "1.5K ev/s")]
    [InlineData(12_345, "12.3K ev/s")]
    [InlineData(999_999, "1000.0K ev/s")]
    [InlineData(1_000_000, "1.0M ev/s")]
    [InlineData(5_500_000, "5.5M ev/s")]
    public void FormatThroughput_FormatsAcrossTiers(double eventsPerSecond, string expected)
    {
        StatusBarViewModel.FormatThroughput(eventsPerSecond).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FormatThroughput_HandlesNonsenseInputs(double bogus)
    {
        StatusBarViewModel.FormatThroughput(bogus).Should().Be("0 ev/s");
    }

    // ── DeriveBackendStatus ───────────────────────────────────────────────

    [Fact]
    public void DeriveBackendStatus_WhenStatusIsNull_ReportsDisconnected()
    {
        var (status, brush) = StatusBarViewModel.DeriveBackendStatus(status: null, dropRate: 0);
        status.Should().Be("Disconnected");
        brush.Should().NotBeSameAs(Brushes.Transparent);
    }

    [Fact]
    public void DeriveBackendStatus_WhenStatusIsDisconnected_ReportsDisconnected()
    {
        var status = new StatusResponse { IsConnected = false };
        var (label, _) = StatusBarViewModel.DeriveBackendStatus(status, dropRate: 0);
        label.Should().Be("Disconnected");
    }

    [Fact]
    public void DeriveBackendStatus_WhenConnectedAndDropRateBelowThreshold_ReportsConnected()
    {
        var status = new StatusResponse { IsConnected = true };
        var (label, _) = StatusBarViewModel.DeriveBackendStatus(
            status, dropRate: StatusBarViewModel.DegradedDropRateThreshold);
        label.Should().Be("Connected");
    }

    [Fact]
    public void DeriveBackendStatus_WhenConnectedAndDropRateAboveThreshold_ReportsDegraded()
    {
        var status = new StatusResponse { IsConnected = true };
        var (label, _) = StatusBarViewModel.DeriveBackendStatus(
            status, dropRate: StatusBarViewModel.DegradedDropRateThreshold + 0.001);
        label.Should().Be("Degraded");
    }
}
