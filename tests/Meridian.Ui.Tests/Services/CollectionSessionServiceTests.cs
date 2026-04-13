using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Session;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="CollectionSessionService"/> and its associated models.
/// </summary>
public sealed class CollectionSessionServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        var instance = CollectionSessionService.Instance;
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = CollectionSessionService.Instance;
        var b = CollectionSessionService.Instance;
        a.Should().BeSameAs(b);
    }

    [Fact]
    public void Constructor_DoesNotLoadConfigWhenBuildingService()
    {
        using var fixture = new PathFixture("mdc-session-constructor");
        var configService = new ThrowingLoadConfigService(fixture.ConfigPath);

        _ = new CollectionSessionService(configService, NotificationService.Instance);

        configService.LoadConfigCallCount.Should().Be(0);
    }

    [Fact]
    public async Task LoadSessionsAsync_UsesResolvedDataRootForSessionsFilePath()
    {
        using var fixture = new PathFixture("mdc-session-path");
        var service = new CollectionSessionService(
            new FixedConfigService(fixture.ConfigPath, new AppConfigDto { DataRoot = "retained-data" }),
            NotificationService.Instance);

        await service.LoadSessionsAsync();

        var path = GetPrivateField<string?>(service, "_sessionsFilePath");

        path.Should().Be(Path.Combine(fixture.RootPath, "retained-data", "_sessions", "sessions.json"));
    }

    [Fact]
    public async Task LoadSessionsAsync_WhenConfigLoadFails_FallsBackToDefaultDataRoot()
    {
        using var fixture = new PathFixture("mdc-session-fallback");
        var configService = new ThrowingLoadConfigService(fixture.ConfigPath);
        var service = new CollectionSessionService(configService, NotificationService.Instance);

        await service.LoadSessionsAsync();

        var path = GetPrivateField<string?>(service, "_sessionsFilePath");
        path.Should().Be(Path.Combine(fixture.RootPath, "data", "_sessions", "sessions.json"));
        configService.LoadConfigCallCount.Should().Be(1);
    }

    // ── CollectionSession model (from Contracts) ────────────────────

    [Fact]
    public void CollectionSession_DefaultValues_ShouldBeCorrect()
    {
        var session = new CollectionSession();

        session.Id.Should().NotBeNullOrEmpty("Id is auto-generated as a Guid");
        session.Name.Should().BeEmpty();
        session.Description.Should().BeNull();
        session.Status.Should().Be(SessionStatus.Pending);
        session.StartedAt.Should().BeNull();
        session.EndedAt.Should().BeNull();
        session.Symbols.Should().NotBeNull().And.BeEmpty();
        session.EventTypes.Should().NotBeNull().And.BeEmpty();
        session.Provider.Should().BeNull();
        session.Tags.Should().BeNull();
        session.Notes.Should().BeNull();
        session.Statistics.Should().BeNull();
        session.QualityScore.Should().Be(0);
        session.ManifestPath.Should().BeNull();
    }

    [Fact]
    public void CollectionSession_Id_ShouldBeUniqueAcrossInstances()
    {
        var a = new CollectionSession();
        var b = new CollectionSession();

        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void CollectionSession_CreatedAt_ShouldBeCloseToUtcNow()
    {
        var before = DateTime.UtcNow;
        var session = new CollectionSession();
        var after = DateTime.UtcNow;

        session.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void CollectionSession_UpdatedAt_ShouldBeCloseToUtcNow()
    {
        var before = DateTime.UtcNow;
        var session = new CollectionSession();
        var after = DateTime.UtcNow;

        session.UpdatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ── CollectionSessionsConfig model ──────────────────────────────

    [Fact]
    public void CollectionSessionsConfig_DefaultValues_ShouldBeCorrect()
    {
        var config = new CollectionSessionsConfig();

        config.Sessions.Should().BeNull();
        config.ActiveSessionId.Should().BeNull();
        config.AutoCreateDailySessions.Should().BeTrue();
        config.SessionNamingPattern.Should().Be("{date}-{mode}");
        config.GenerateManifestOnComplete.Should().BeTrue();
        config.RetainSessionHistory.Should().Be(365);
    }

    // ── CollectionSessionStatistics model ───────────────────────────

    [Fact]
    public void CollectionSessionStatistics_DefaultValues_ShouldBeCorrect()
    {
        var stats = new CollectionSessionStatistics();

        stats.TotalEvents.Should().Be(0);
        stats.TradeEvents.Should().Be(0);
        stats.QuoteEvents.Should().Be(0);
        stats.DepthEvents.Should().Be(0);
        stats.BarEvents.Should().Be(0);
        stats.TotalBytes.Should().Be(0);
        stats.CompressedBytes.Should().Be(0);
        stats.FileCount.Should().Be(0);
        stats.GapsDetected.Should().Be(0);
        stats.GapsFilled.Should().Be(0);
        stats.SequenceErrors.Should().Be(0);
        stats.EventsPerSecond.Should().Be(0);
        stats.CompressionRatio.Should().Be(0);
    }

    // ── SessionStatus constants ─────────────────────────────────────

    [Fact]
    public void SessionStatus_Constants_ShouldBeCorrectValues()
    {
        SessionStatus.Pending.Should().Be("Pending");
        SessionStatus.Active.Should().Be("Active");
        SessionStatus.Paused.Should().Be("Paused");
        SessionStatus.Completed.Should().Be("Completed");
        SessionStatus.Failed.Should().Be("Failed");
    }

    // ── CollectionSessionEventArgs model ────────────────────────────

    [Fact]
    public void CollectionSessionEventArgs_DefaultValues_ShouldBeCorrect()
    {
        var args = new CollectionSessionEventArgs();

        args.Session.Should().BeNull();
    }

    // ── GenerateSessionSummary ──────────────────────────────────────

    [Fact]
    public void GenerateSessionSummary_WithMinimalSession_ShouldReturnFormattedText()
    {
        var service = CollectionSessionService.Instance;
        var session = new CollectionSession
        {
            Name = "test-session",
            Symbols = new[] { "SPY", "AAPL" },
            Statistics = new CollectionSessionStatistics
            {
                TotalEvents = 1000,
                TradeEvents = 600,
                QuoteEvents = 400
            }
        };

        var summary = service.GenerateSessionSummary(session);

        summary.Should().NotBeNullOrEmpty();
        summary.Should().Contain("test-session");
        summary.Should().Contain("1,000");
        summary.Should().Contain("600");
        summary.Should().Contain("400");
        summary.Should().Contain("Symbols Collected: 2");
    }

    [Fact]
    public void GenerateSessionSummary_WithNullStatistics_ShouldNotThrow()
    {
        var service = CollectionSessionService.Instance;
        var session = new CollectionSession
        {
            Name = "empty-session",
            Symbols = Array.Empty<string>()
        };

        var act = () => service.GenerateSessionSummary(session);

        act.Should().NotThrow();
    }

    // ── Events ──────────────────────────────────────────────────────

    [Fact]
    public void Events_ShouldBeSubscribable()
    {
        var service = CollectionSessionService.Instance;

        var sessionCreatedFired = false;
        service.SessionCreated += (_, _) => sessionCreatedFired = true;
        service.SessionCreated -= (_, _) => sessionCreatedFired = true;

        sessionCreatedFired.Should().BeFalse("no event was raised");
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.Should().NotBeNull();
        return (T)field!.GetValue(instance)!;
    }
}
