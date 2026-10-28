using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Storage.Calendars;
using Meridian.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace Meridian.Tests.Ui;

/// <summary>
/// Pins UiServer's logging (ADR-025 rollout): every category still stops below Warning through the
/// default providers, and the host-start inventory lines alone (the Security Master terms capability
/// line and each loaded calendar's line) also reach the process's Serilog logger, from Information,
/// so a workstation-mode host writes them to its log file. Nothing else reaches that logger, even
/// when the configuration lowers the default level.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UiServerLoggingTests : IDisposable
{
    private const string HostedServiceName = "SecurityMasterTermsCapabilityHostedService";
    private const string CapabilityCategory = "Meridian.Application.Composition.SecurityMasterTermsCapabilityHostedService";
    private const string CalendarCategory = "Meridian.Storage.Calendars.FileBusinessDayCalendarSource";

    private readonly TestArtifactDirectory _artifacts = TestArtifactDirectory.Create(nameof(UiServerLoggingTests));

    public void Dispose() => _artifacts.Dispose();

    [Fact]
    public void TheInventoryCategories_AreTheLoggerCategoriesOfTheCapabilityServiceAndTheCalendarSource()
    {
        UiServer.StartupInventoryLogCategories.Should().Equal(CapabilityCategory, CalendarCategory);
    }

    [Fact]
    public async Task HostStart_WritesTheCapabilityAndCalendarLinesToSerilog_AndOtherCategoriesStillStopBelowWarning()
    {
        WriteZzTestCalendar();
        var serilog = new CollectingSink();
        var defaults = new CapturingLoggerProvider();
        await using var provider = BuildHost(serilog, defaults, configuration: null);

        await StartCapabilityServiceAsync(provider);
        var factory = provider.GetRequiredService<ILoggerFactory>();
        LogFromOtherCategories(factory);

        var events = serilog.Events.ToArray();
        events.Select(static e => (SourceContext(e), e.Level)).Should().Equal(
            (CalendarCategory, LogEventLevel.Information),
            (CapabilityCategory, LogEventLevel.Information));
        events[0].MessageTemplate.Text.Should().StartWith("Loaded business-day calendar {CalendarId} version {CalendarVersion}");
        events[0].Properties["CalendarId"].Should().Be(new ScalarValue("ZZTEST"));
        events[1].MessageTemplate.Text.Should().StartWith("Security Master terms capability: declared mapping {DeclaredMappingVersion}");

        // The default providers keep the Warning floor for every category, these two included.
        defaults.Entries.Should().Equal(
            new CapturedLogEntry("Meridian.UiServer", LogLevel.Warning, "UiServer warning"),
            new CapturedLogEntry("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning, "ASP.NET Core warning"));

        factory.CreateLogger(CapabilityCategory).IsEnabled(LogLevel.Information).Should().BeTrue();
        factory.CreateLogger(CalendarCategory).IsEnabled(LogLevel.Information).Should().BeTrue();
        factory.CreateLogger(CalendarCategory).IsEnabled(LogLevel.Debug).Should().BeFalse();
        factory.CreateLogger("Meridian.UiServer").IsEnabled(LogLevel.Information).Should().BeFalse();
        factory.CreateLogger("Meridian.UiServer").IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public async Task AConfiguredDefaultLevel_ReachesTheDefaultProviders_ButNotSerilog()
    {
        // Logging__LogLevel__Default=Information (as the container deployments set it) lowers the
        // default providers' floor as before; the Serilog provider's own rules still admit only the
        // inventory categories.
        WriteZzTestCalendar();
        var serilog = new CollectingSink();
        var defaults = new CapturingLoggerProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Information" })
            .Build();
        await using var provider = BuildHost(serilog, defaults, configuration);

        await StartCapabilityServiceAsync(provider);
        LogFromOtherCategories(provider.GetRequiredService<ILoggerFactory>());

        serilog.Events.Select(SourceContext).Should().Equal(CalendarCategory, CapabilityCategory);
        defaults.Entries.Select(static entry => entry.Category).Should().Contain(["Meridian.UiServer", "Microsoft.AspNetCore.Hosting.Diagnostics"]);
        defaults.Entries.Should().Contain(static entry => entry.Category == "Meridian.UiServer" && entry.Level == LogLevel.Information);
    }

    private ServiceProvider BuildHost(CollectingSink serilog, CapturingLoggerProvider defaults, IConfiguration? configuration)
    {
        var logger = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(serilog).CreateLogger();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (configuration is not null)
            {
                logging.AddConfiguration(configuration.GetSection("Logging"));
            }

            // Stands in for the console, debug and event-source providers WebApplication adds.
            logging.AddProvider(defaults);
            UiServer.ConfigureLogging(logging, logger);
        });
        services.AddSecurityMasterTermsCapability(_ => _artifacts.RootPath);
        return services.BuildServiceProvider();
    }

    private static void LogFromOtherCategories(ILoggerFactory factory)
    {
        var uiServer = factory.CreateLogger("Meridian.UiServer");
        var aspNetCore = factory.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics");
        uiServer.LogInformation("UiServer information");
        uiServer.LogWarning("UiServer warning");
        aspNetCore.LogInformation("ASP.NET Core information");
        aspNetCore.LogWarning("ASP.NET Core warning");
    }

    private static async Task StartCapabilityServiceAsync(IServiceProvider provider)
    {
        var hosted = provider.GetServices<IHostedService>()
            .Should().ContainSingle(static service => service.GetType().Name == HostedServiceName).Subject;
        await hosted.StartAsync(CancellationToken.None);
        await hosted.StopAsync(CancellationToken.None);
    }

    private void WriteZzTestCalendar()
    {
        var directory = Path.Combine(_artifacts.RootPath, FileBusinessDayCalendarSource.DirectoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "ZZTEST.json"), """
            { "calendarId": "ZZTEST", "version": 1, "weekendDays": ["Saturday","Sunday"],
              "coverage": { "from": "2026-01-01", "to": "2026-12-31" }, "holidays": ["2026-01-02"] }
            """);
    }

    private static string? SourceContext(LogEvent logEvent)
        => logEvent.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string category }
            ? category
            : null;

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyCollection<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }

    private sealed record CapturedLogEntry(string Category, LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

        public IReadOnlyCollection<CapturedLogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Enqueue(new CapturedLogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}
