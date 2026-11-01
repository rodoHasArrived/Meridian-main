using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage;
using Meridian.Storage.Calendars;
using Meridian.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Meridian.Tests.Application.Composition;

/// <summary>
/// Pins the eager Security Master terms capability line (ADR-025): the hosted service that
/// <c>AddSecurityMasterTermsCapability</c> registers, for the storage feature and for the WPF desktop's
/// own graph alike, loads the business-day calendar source at host start, so every node logs its
/// calendar lines and one capability line naming the declared-mapping version, the schedule kernel
/// version, the classes it reads ahead of what it writes, and the loaded calendars, without any
/// projection request resolving the cash-flow service.
/// </summary>
[Collection("Sequential")]
public sealed class SecurityMasterTermsCapabilityStartupTests : IDisposable
{
    private const string HostedServiceName = "SecurityMasterTermsCapabilityHostedService";
    private const string CapabilityPrefix = "Security Master terms capability: ";

    // Pinned on purpose: a release that changes a class's read or write terms version changes this line.
    private const string ExpectedTermsVersions = "Bond r4/w3, CertificateOfDeposit r4/w3, DirectLoan r2/w1, StructuredCredit r5/w4, Swap r3/w1";

    private readonly TestArtifactDirectory _artifacts = TestArtifactDirectory.Create(nameof(SecurityMasterTermsCapabilityStartupTests));

    public void Dispose() => _artifacts.Dispose();

    [Fact]
    public async Task HostStart_WithNoCalendarDirectory_LogsTheCapabilityLineWithNoCalendars()
    {
        var (provider, logs, cashFlowResolutions) = BuildHost();
        await using var _ = provider;

        await StartCapabilityServiceAsync(provider);

        cashFlowResolutions.Count.Should().Be(0, "the capability line must not wait for a projection request");
        logs.Entries.Should().NotContain(static entry => entry.Level >= LogLevel.Warning);
        logs.Entries.Should().ContainSingle(static entry => entry.Message.StartsWith(CapabilityPrefix, StringComparison.Ordinal))
            .Which.Message.Should().Be(
                CapabilityPrefix
                + "declared mapping security-master-declared-schedule-v1, schedule kernel contractual-schedule-kernel-v1, "
                + "terms versions " + ExpectedTermsVersions + ", business-day calendars 0: none");
    }

    [Fact]
    public async Task HostStart_WithOneCalendar_LogsItsLineThenTheCapabilityLine()
    {
        var directory = Path.Combine(_artifacts.RootPath, "calendars");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "ZZTEST.json"), """
            { "calendarId": "ZZTEST", "version": 3, "weekendDays": ["Saturday","Sunday"],
              "coverage": { "from": "2026-01-01", "to": "2026-12-31" }, "holidays": ["2026-01-02"] }
            """);
        var hash = new HolidayBusinessDayCalendar(
            "ZZTEST",
            3,
            [new DateOnly(2026, 1, 2)],
            [DayOfWeek.Saturday, DayOfWeek.Sunday],
            new CalendarCoverage(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))).ContentHash;
        var (provider, logs, cashFlowResolutions) = BuildHost();
        await using var _ = provider;

        await StartCapabilityServiceAsync(provider);

        cashFlowResolutions.Count.Should().Be(0);
        var entries = logs.Entries.ToArray();
        var calendarLine = Array.FindIndex(entries, static entry =>
            entry.Category == "Meridian.Storage.Calendars.FileBusinessDayCalendarSource"
            && entry.Level == LogLevel.Information
            && entry.Message.StartsWith("Loaded business-day calendar ZZTEST version 3 hash ", StringComparison.Ordinal));
        calendarLine.Should().BeGreaterThanOrEqualTo(0);
        entries[calendarLine].Message.Should().Contain(hash).And.Contain("coverage 2026-01-01 to 2026-12-31");

        var capabilityLine = Array.FindIndex(entries, static entry => entry.Message.StartsWith(CapabilityPrefix, StringComparison.Ordinal));
        capabilityLine.Should().BeGreaterThan(calendarLine, "the source loads before the capability line is written");
        entries[capabilityLine].Level.Should().Be(LogLevel.Information);
        entries[capabilityLine].Message.Should().EndWith(
            "terms versions " + ExpectedTermsVersions + ", business-day calendars 1: ZZTEST v3 " + hash);
    }

    [Fact]
    public void Register_AddsTheCapabilityServiceOnce()
    {
        var services = new ServiceCollection();
        var registration = new StorageFeatureRegistration();
        var options = CompositionOptions.Minimal with { Configuration = TestConfiguration() };

        registration.Register(services, options);
        registration.Register(services, options);

        services.Should().ContainSingle(static descriptor => descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType != null
            && descriptor.ImplementationType.Name == HostedServiceName);
        services.Should().ContainSingle(static descriptor => descriptor.ServiceType == typeof(IBusinessDayCalendarSource));
    }

    [Fact]
    public async Task AddSecurityMasterTermsCapability_WithoutTheStorageFeature_LogsTheCapabilityLineOverTheGivenRoot()
    {
        // The WPF desktop composes its own graph and never runs StorageFeatureRegistration, so it calls
        // the extension directly with its own data root. A second call keeps the first registration.
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddSecurityMasterTermsCapability(_ => _artifacts.RootPath);
        services.AddSecurityMasterTermsCapability(static _ => throw new InvalidOperationException("The first registration must win."));

        services.Should().ContainSingle(static descriptor => descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType != null
            && descriptor.ImplementationType.Name == HostedServiceName);
        services.Should().ContainSingle(static descriptor => descriptor.ServiceType == typeof(IBusinessDayCalendarSource));
        await using var provider = services.BuildServiceProvider();

        await StartCapabilityServiceAsync(provider);

        provider.GetRequiredService<IBusinessDayCalendarSource>().Should().BeOfType<FileBusinessDayCalendarSource>()
            .Which.CalendarDirectory.Should().Be(Path.Combine(_artifacts.RootPath, FileBusinessDayCalendarSource.DirectoryName));
        logs.Entries.Should().ContainSingle(static entry => entry.Message.StartsWith(CapabilityPrefix, StringComparison.Ordinal))
            .Which.Message.Should().Be(
                CapabilityPrefix
                + "declared mapping security-master-declared-schedule-v1, schedule kernel contractual-schedule-kernel-v1, "
                + "terms versions " + ExpectedTermsVersions + ", business-day calendars 0: none");
    }

    private (ServiceProvider Provider, CapturingLoggerProvider Logs, ResolutionCounter CashFlowResolutions) BuildHost()
    {
        var logs = new CapturingLoggerProvider();
        var cashFlowResolutions = new ResolutionCounter();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        new StorageFeatureRegistration().Register(services, CompositionOptions.Minimal with { Configuration = TestConfiguration() });
        services.Replace(ServiceDescriptor.Singleton(new StorageOptions { RootPath = _artifacts.RootPath }));

        // The last registration wins: any resolution of the cash-flow service during start is counted.
        services.AddSingleton<ISecurityMasterCashFlowService>(_ =>
        {
            cashFlowResolutions.Count++;
            return Substitute.For<ISecurityMasterCashFlowService>();
        });
        return (services.BuildServiceProvider(), logs, cashFlowResolutions);
    }

    private static async Task StartCapabilityServiceAsync(IServiceProvider provider)
    {
        // Start the exact hosted registration, as the host does, without starting unrelated storage workers.
        var hosted = provider.GetServices<IHostedService>()
            .Should().ContainSingle(static service => service.GetType().Name == HostedServiceName).Subject;
        await hosted.StartAsync(CancellationToken.None);
        await hosted.StopAsync(CancellationToken.None);
    }

    private static IConfiguration TestConfiguration()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Test",
            ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true"
        }).Build();

    private sealed class ResolutionCounter
    {
        public int Count { get; set; }
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
