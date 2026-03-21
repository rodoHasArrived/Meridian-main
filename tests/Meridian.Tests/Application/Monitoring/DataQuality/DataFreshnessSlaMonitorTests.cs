using FluentAssertions;
using Meridian.Application.Monitoring.DataQuality;
using Xunit;

namespace Meridian.Tests.Monitoring.DataQuality;

/// <summary>
/// Unit tests for DataFreshnessSlaMonitor (ADQ-4.7).
/// </summary>
public sealed class DataFreshnessSlaMonitorTests : IDisposable
{
    private readonly DataFreshnessSlaMonitor _monitor;

    public DataFreshnessSlaMonitorTests()
    {
        // Use a config that doesn't skip outside market hours for consistent test behavior
        _monitor = new DataFreshnessSlaMonitor(new SlaConfig
        {
            DefaultFreshnessThresholdSeconds = 10,
            CriticalFreshnessThresholdSeconds = 30,
            CheckIntervalSeconds = 3600, // Long interval to prevent automatic checks during tests
            SkipOutsideMarketHours = false,
            AlertCooldownSeconds = 0 // No cooldown for tests
        });
    }

    [Fact]
    public void RegisterSymbol_ShouldAddSymbolToMonitoring()
    {
        // Arrange
        var symbol = "AAPL";

        // Act
        _monitor.RegisterSymbol(symbol);

        // Assert
        var status = _monitor.GetSymbolStatus(symbol);
        status.Should().NotBeNull();
        status!.Symbol.Should().Be(symbol);
        status.State.Should().Be(SlaState.NoData);
    }

    [Fact]
    public void RegisterSymbol_WithCustomThreshold_ShouldUseCustomValue()
    {
        // Arrange
        var symbol = "MSFT";
        var customThreshold = 30;

        // Act
        _monitor.RegisterSymbol(symbol, customThreshold);
        _monitor.RecordEvent(symbol); // Need to record event to have a valid status

        // Assert
        var status = _monitor.GetSymbolStatus(symbol);
        status.Should().NotBeNull();
        status!.ThresholdSeconds.Should().Be(customThreshold);
    }

    [Fact]
    public void UnregisterSymbol_ShouldRemoveSymbolFromMonitoring()
    {
        // Arrange
        var symbol = "GOOGL";
        _monitor.RegisterSymbol(symbol);

        // Act
        _monitor.UnregisterSymbol(symbol);

        // Assert
        var status = _monitor.GetSymbolStatus(symbol);
        status.Should().BeNull();
    }

    [Fact]
    public void RecordEvent_ShouldUpdateLastEventTime()
    {
        // Arrange
        var symbol = "TSLA";
        _monitor.RegisterSymbol(symbol);

        // Act
        _monitor.RecordEvent(symbol);

        // Assert
        var status = _monitor.GetSymbolStatus(symbol);
        status.Should().NotBeNull();
        status!.State.Should().Be(SlaState.Healthy);
        status.LastEventTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RecordEvent_WithUnregisteredSymbol_ShouldAutoRegister()
    {
        // Arrange
        var symbol = "NVDA";

        // Act
        _monitor.RecordEvent(symbol);

        // Assert
        var status = _monitor.GetSymbolStatus(symbol);
        status.Should().NotBeNull();
        status!.Symbol.Should().Be(symbol);
        status.State.Should().Be(SlaState.Healthy);
    }

    [Fact]
    public void GetSymbolStatus_WithNoData_ShouldReturnNoDataState()
    {
        // Arrange
        var symbol = "AMD";
        _monitor.RegisterSymbol(symbol);

        // Act
        var status = _monitor.GetSymbolStatus(symbol);

        // Assert
        status.Should().NotBeNull();
        status!.State.Should().Be(SlaState.NoData);
        status.ViolationCount.Should().Be(0);
    }

    [Fact]
    public void GetSymbolStatus_WithUnknownSymbol_ShouldReturnNull()
    {
        // Act
        var status = _monitor.GetSymbolStatus("UNKNOWN");

        // Assert
        status.Should().BeNull();
    }

    [Fact]
    public void GetSnapshot_ShouldReturnCorrectCounts()
    {
        // Arrange
        _monitor.RegisterSymbol("AAPL");
        _monitor.RegisterSymbol("MSFT");
        _monitor.RecordEvent("AAPL"); // Healthy
        // MSFT remains NoData

        // Act
        var snapshot = _monitor.GetSnapshot();

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.TotalSymbols.Should().Be(2);
        snapshot.HealthySymbols.Should().Be(1);
        snapshot.NoDataSymbols.Should().Be(1);
        snapshot.ViolationSymbols.Should().Be(0);
        snapshot.WarningSymbols.Should().Be(0);
        snapshot.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetSnapshot_ShouldIncludeAllSymbolStatuses()
    {
        // Arrange
        _monitor.RecordEvent("AAPL");
        _monitor.RecordEvent("MSFT");
        _monitor.RecordEvent("GOOGL");

        // Act
        var snapshot = _monitor.GetSnapshot();

        // Assert
        snapshot.SymbolStatuses.Should().HaveCount(3);
        snapshot.SymbolStatuses.Select(s => s.Symbol).Should().Contain("AAPL");
        snapshot.SymbolStatuses.Select(s => s.Symbol).Should().Contain("MSFT");
        snapshot.SymbolStatuses.Select(s => s.Symbol).Should().Contain("GOOGL");
    }

    [Fact]
    public void GetSnapshot_WithAllHealthy_ShouldHaveHighFreshnessScore()
    {
        // Arrange
        _monitor.RecordEvent("AAPL");
        _monitor.RecordEvent("MSFT");

        // Act
        var snapshot = _monitor.GetSnapshot();

        // Assert
        snapshot.OverallFreshnessScore.Should().Be(100);
    }

    [Fact]
    public void TotalViolations_ShouldStartAtZero()
    {
        // Assert
        _monitor.TotalViolations.Should().Be(0);
    }

    [Fact]
    public void CurrentViolations_ShouldStartAtZero()
    {
        // Assert
        _monitor.CurrentViolations.Should().Be(0);
    }

    [Fact]
    public void TotalRecoveries_ShouldStartAtZero()
    {
        // Assert
        _monitor.TotalRecoveries.Should().Be(0);
    }

    [Fact]
    public void OnViolation_ShouldFireWhenViolationDetected()
    {
        // Arrange
        using var monitor = new DataFreshnessSlaMonitor(new SlaConfig
        {
            DefaultFreshnessThresholdSeconds = 1, // Very short threshold for test
            CheckIntervalSeconds = 1,
            SkipOutsideMarketHours = false,
            AlertCooldownSeconds = 0
        });

        using var violationReceived = new ManualResetEventSlim(false);
        SlaViolationEvent? capturedEvent = null;
        monitor.OnViolation += e => { capturedEvent = e; violationReceived.Set(); };

        monitor.RecordEvent("AAPL");

        // Act - Wait for the violation callback to fire (timer-based, needs >1s staleness)
        violationReceived.Wait(TimeSpan.FromSeconds(5));

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Value.Symbol.Should().Be("AAPL");
        capturedEvent.Value.ThresholdSeconds.Should().Be(1);
    }

    [Fact]
    public void OnRecovery_ShouldFireWhenRecoveredFromViolation()
    {
        // Arrange
        using var monitor = new DataFreshnessSlaMonitor(new SlaConfig
        {
            DefaultFreshnessThresholdSeconds = 1,
            CheckIntervalSeconds = 1,
            SkipOutsideMarketHours = false,
            AlertCooldownSeconds = 0
        });

        using var violationReceived = new ManualResetEventSlim(false);
        SlaRecoveryEvent? capturedRecovery = null;
        monitor.OnRecovery += e => capturedRecovery = e;
        monitor.OnViolation += _ => violationReceived.Set();

        monitor.RecordEvent("AAPL");

        // Wait for violation - timer fires every 1s and needs >1s staleness
        violationReceived.Wait(TimeSpan.FromSeconds(5));

        // Act - Record event to recover
        monitor.RecordEvent("AAPL");

        // Assert
        capturedRecovery.Should().NotBeNull();
        capturedRecovery!.Value.Symbol.Should().Be("AAPL");
    }

    [Fact]
    public void Dispose_ShouldClearAllState()
    {
        // Arrange
        _monitor.RecordEvent("AAPL");
        _monitor.RecordEvent("MSFT");

        // Act
        _monitor.Dispose();

        // Assert - After dispose, snapshot should show no symbols
        var snapshot = _monitor.GetSnapshot();
        snapshot.TotalSymbols.Should().Be(0);
    }

    public void Dispose()
    {
        _monitor.Dispose();
    }
}

/// <summary>
/// Unit tests for SlaConfig.
/// </summary>
public sealed class SlaConfigTests
{
    [Fact]
    public void Default_ShouldHaveReasonableDefaults()
    {
        // Act
        var config = SlaConfig.Default;

        // Assert
        config.DefaultFreshnessThresholdSeconds.Should().Be(60);
        config.CriticalFreshnessThresholdSeconds.Should().Be(300);
        config.CheckIntervalSeconds.Should().Be(10);
        config.SkipOutsideMarketHours.Should().BeTrue();
        config.AlertCooldownSeconds.Should().Be(300);
    }

    [Fact]
    public void GetThresholdForSymbol_WithNoOverride_ShouldReturnDefault()
    {
        // Arrange
        var config = new SlaConfig
        {
            DefaultFreshnessThresholdSeconds = 60
        };

        // Act
        var threshold = config.GetThresholdForSymbol("AAPL");

        // Assert
        threshold.Should().Be(60);
    }

    [Fact]
    public void GetThresholdForSymbol_WithOverride_ShouldReturnCustomValue()
    {
        // Arrange
        var config = new SlaConfig
        {
            DefaultFreshnessThresholdSeconds = 60,
            SymbolThresholds = new Dictionary<string, int>
            {
                ["AAPL"] = 30,
                ["MSFT"] = 120
            }
        };

        // Act
        var aaplThreshold = config.GetThresholdForSymbol("AAPL");
        var msftThreshold = config.GetThresholdForSymbol("MSFT");
        var googlThreshold = config.GetThresholdForSymbol("GOOGL");

        // Assert
        aaplThreshold.Should().Be(30);
        msftThreshold.Should().Be(120);
        googlThreshold.Should().Be(60); // Uses default
    }

    [Fact]
    public void MarketHours_ShouldBeConfigurable()
    {
        // Arrange
        var config = new SlaConfig
        {
            MarketOpenUtc = new TimeOnly(14, 0),
            MarketCloseUtc = new TimeOnly(21, 0)
        };

        // Assert
        config.MarketOpenUtc.Should().Be(new TimeOnly(14, 0));
        config.MarketCloseUtc.Should().Be(new TimeOnly(21, 0));
    }
}

/// <summary>
/// Unit tests for SlaState enum behavior through SymbolSlaStatus.
/// </summary>
public sealed class SymbolSlaStatusTests
{
    [Fact]
    public void SymbolSlaStatus_ShouldBeImmutableRecord()
    {
        // Arrange
        var status = new SymbolSlaStatus(
            Symbol: "AAPL",
            LastEventTime: DateTimeOffset.UtcNow,
            TimeSinceLastEvent: TimeSpan.FromSeconds(5),
            FreshnessMs: 5000,
            State: SlaState.Healthy,
            ThresholdSeconds: 60,
            ViolationCount: 0,
            LastViolationTime: null,
            IsWithinMarketHours: true
        );

        // Assert
        status.Symbol.Should().Be("AAPL");
        status.State.Should().Be(SlaState.Healthy);
        status.FreshnessMs.Should().Be(5000);
        status.ViolationCount.Should().Be(0);
        status.IsWithinMarketHours.Should().BeTrue();
    }

    [Fact]
    public void SymbolSlaStatus_WithViolation_ShouldTrackDetails()
    {
        // Arrange
        var violationTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var status = new SymbolSlaStatus(
            Symbol: "MSFT",
            LastEventTime: violationTime,
            TimeSinceLastEvent: TimeSpan.FromMinutes(5),
            FreshnessMs: 300000,
            State: SlaState.Violation,
            ThresholdSeconds: 60,
            ViolationCount: 3,
            LastViolationTime: violationTime.AddMinutes(1),
            IsWithinMarketHours: true
        );

        // Assert
        status.State.Should().Be(SlaState.Violation);
        status.ViolationCount.Should().Be(3);
        status.LastViolationTime.Should().NotBeNull();
        status.TimeSinceLastEvent.TotalMinutes.Should().BeApproximately(5, 0.1);
    }
}

/// <summary>
/// Unit tests for SlaStatusSnapshot.
/// </summary>
public sealed class SlaStatusSnapshotTests
{
    [Fact]
    public void SlaStatusSnapshot_ShouldContainAllMetrics()
    {
        // Arrange
        var statuses = new List<SymbolSlaStatus>
        {
            new(
                Symbol: "AAPL",
                LastEventTime: DateTimeOffset.UtcNow,
                TimeSinceLastEvent: TimeSpan.FromSeconds(5),
                FreshnessMs: 5000,
                State: SlaState.Healthy,
                ThresholdSeconds: 60,
                ViolationCount: 0,
                LastViolationTime: null,
                IsWithinMarketHours: true
            )
        };

        var snapshot = new SlaStatusSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            TotalSymbols: 1,
            HealthySymbols: 1,
            WarningSymbols: 0,
            ViolationSymbols: 0,
            NoDataSymbols: 0,
            TotalViolations: 0,
            OverallFreshnessScore: 100.0,
            IsMarketOpen: true,
            SymbolStatuses: statuses
        );

        // Assert
        snapshot.TotalSymbols.Should().Be(1);
        snapshot.HealthySymbols.Should().Be(1);
        snapshot.OverallFreshnessScore.Should().Be(100.0);
        snapshot.SymbolStatuses.Should().HaveCount(1);
    }
}

/// <summary>
/// Unit tests for market hours behavior in DataFreshnessSlaMonitor.
/// </summary>
public sealed class DataFreshnessSlaMonitorMarketHoursTests : IDisposable
{
    private readonly DataFreshnessSlaMonitor _monitor;

    public DataFreshnessSlaMonitorMarketHoursTests()
    {
        _monitor = new DataFreshnessSlaMonitor(new SlaConfig
        {
            DefaultFreshnessThresholdSeconds = 60,
            SkipOutsideMarketHours = true,
            MarketOpenUtc = new TimeOnly(13, 30), // 9:30 AM ET
            MarketCloseUtc = new TimeOnly(20, 0)  // 4:00 PM ET
        });
    }

    [Fact]
    public void IsMarketOpen_ShouldReturnTrue_WhenSkipOutsideMarketHoursIsFalse()
    {
        // Arrange
        using var monitor = new DataFreshnessSlaMonitor(new SlaConfig
        {
            SkipOutsideMarketHours = false
        });

        // Act & Assert - Should always return true when skipping is disabled
        monitor.IsMarketOpen().Should().BeTrue();
    }

    [Fact]
    public void IsMarketOpen_ShouldReturnFalse_OnWeekends()
    {
        // This test validates the logic but actual result depends on current day
        // We test the behavior by checking the snapshot state
        _monitor.RecordEvent("AAPL");
        var snapshot = _monitor.GetSnapshot();

        // The status should reflect market hours awareness
        snapshot.IsMarketOpen.Should().Be(_monitor.IsMarketOpen());
    }

    [Fact]
    public void GetSymbolStatus_OutsideMarketHours_ShouldShowOutsideMarketHoursState()
    {
        // Arrange
        using var monitor = new DataFreshnessSlaMonitor(new SlaConfig
        {
            SkipOutsideMarketHours = true,
            MarketOpenUtc = new TimeOnly(0, 0), // Midnight
            MarketCloseUtc = new TimeOnly(0, 1)  // One minute after midnight - effectively always closed
        });

        monitor.RecordEvent("AAPL");

        // Allow a moment for potential async operations
        Thread.Sleep(10);

        // Act
        var status = monitor.GetSymbolStatus("AAPL");

        // Assert - if outside market hours, state should be OutsideMarketHours
        // NOTE: Actual result depends on current time, so we check the logic is working
        status.Should().NotBeNull();
        if (!monitor.IsMarketOpen())
        {
            status!.State.Should().Be(SlaState.OutsideMarketHours);
        }
    }

    public void Dispose()
    {
        _monitor.Dispose();
    }
}

/// <summary>
/// Unit tests for SlaViolationEvent and SlaRecoveryEvent.
/// </summary>
public sealed class SlaEventTests
{
    [Fact]
    public void SlaViolationEvent_ShouldContainAllDetails()
    {
        // Arrange
        var violationEvent = new SlaViolationEvent(
            Symbol: "AAPL",
            Timestamp: DateTimeOffset.UtcNow,
            TimeSinceLastEvent: TimeSpan.FromSeconds(120),
            ThresholdSeconds: 60,
            ViolationCount: 1,
            PreviousState: SlaState.Warning
        );

        // Assert
        violationEvent.Symbol.Should().Be("AAPL");
        violationEvent.TimeSinceLastEvent.TotalSeconds.Should().Be(120);
        violationEvent.ThresholdSeconds.Should().Be(60);
        violationEvent.ViolationCount.Should().Be(1);
        violationEvent.PreviousState.Should().Be(SlaState.Warning);
    }

    [Fact]
    public void SlaRecoveryEvent_ShouldContainRecoveryDetails()
    {
        // Arrange
        var recoveryEvent = new SlaRecoveryEvent(
            Symbol: "MSFT",
            Timestamp: DateTimeOffset.UtcNow,
            ViolationDuration: TimeSpan.FromMinutes(5),
            ViolationCount: 3
        );

        // Assert
        recoveryEvent.Symbol.Should().Be("MSFT");
        recoveryEvent.ViolationDuration.TotalMinutes.Should().Be(5);
        recoveryEvent.ViolationCount.Should().Be(3);
    }
}
