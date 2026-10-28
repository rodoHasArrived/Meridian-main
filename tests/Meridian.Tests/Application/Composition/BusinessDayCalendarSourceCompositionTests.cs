using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage;
using Meridian.Storage.Calendars;
using Meridian.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Tests.Application.Composition;

/// <summary>
/// Pins the composed business-day calendar source (ADR-025): every host that runs the storage
/// feature resolves <see cref="IBusinessDayCalendarSource"/> to the file source over
/// <c>&lt;dataRoot&gt;/calendars</c>, and the Security Master cash-flow service receives that source.
/// </summary>
[Collection("Sequential")]
public sealed class BusinessDayCalendarSourceCompositionTests : IDisposable
{
    private readonly TestArtifactDirectory _artifacts = TestArtifactDirectory.Create(nameof(BusinessDayCalendarSourceCompositionTests));

    public void Dispose() => _artifacts.Dispose();

    [Fact]
    public void Register_ResolvesTheFileSourceOverTheDataRoot()
    {
        using var provider = BuildProvider(securityMasterConfigured: false);

        var source = provider.GetRequiredService<IBusinessDayCalendarSource>();

        var fileSource = source.Should().BeOfType<FileBusinessDayCalendarSource>().Subject;
        fileSource.CalendarDirectory.Should().Be(Path.Combine(_artifacts.RootPath, "calendars"));
        fileSource.Calendars.Should().BeEmpty();
        source.TryGet("WEEKENDS", out _).Should().BeTrue();
        provider.GetRequiredService<IBusinessDayCalendarSource>().Should().BeSameAs(source, "the source loads once");
    }

    [Fact]
    public void Register_WithOneValidFile_TryGetFindsIt()
    {
        var directory = Path.Combine(_artifacts.RootPath, "calendars");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "ZZTEST.json"), """
            { "calendarId": "ZZTEST", "version": 1, "weekendDays": ["Saturday","Sunday"],
              "coverage": { "from": "2026-01-01", "to": "2026-12-31" }, "holidays": ["2026-01-02"] }
            """);
        using var provider = BuildProvider(securityMasterConfigured: false);

        var source = provider.GetRequiredService<IBusinessDayCalendarSource>();

        source.TryGet("ZZTEST", out var calendar).Should().BeTrue();
        calendar!.IsBusinessDay(new DateOnly(2026, 1, 2)).Should().BeFalse();
    }

    [Fact]
    public void Register_WhenSecurityMasterIsConfigured_HandsTheSourceToTheCashFlowService()
    {
        using var provider = BuildProvider(securityMasterConfigured: true);

        var service = provider.GetRequiredService<ISecurityMasterCashFlowService>();

        service.Should().BeOfType<SecurityMasterCashFlowService>().Which.CalendarSource
            .Should().BeSameAs(provider.GetRequiredService<IBusinessDayCalendarSource>());
    }

    private ServiceProvider BuildProvider(bool securityMasterConfigured)
    {
        var settings = new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Test",
            ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true",
            [SecurityMasterStartup.ConnectionStringVariable] = securityMasterConfigured
                ? "Host=unused;Database=unused;Username=test;Password=test"
                : null
        };
        var services = new ServiceCollection();
        services.AddLogging();
        new StorageFeatureRegistration().Register(services, CompositionOptions.Minimal with
        {
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build()
        });
        services.Replace(ServiceDescriptor.Singleton(new StorageOptions { RootPath = _artifacts.RootPath }));
        return services.BuildServiceProvider();
    }
}
