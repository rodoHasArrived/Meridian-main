using System.Globalization;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Storage.Calendars;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Composition;

/// <summary>
/// Resolves the business-day calendar source at host start, so its per-calendar and refused-file
/// lines are logged when the node starts rather than on the first projection request (the cash-flow
/// service is its only other consumer). Configured writers first check the persisted deployment
/// capability floor; unconfigured graphs never resolve its persistence reader. It then logs one capability line: the declared-mapping and
/// schedule-kernel versions, each asset class whose read and write terms versions differ, and the
/// loaded calendars, so a node's build is visible from its logs. Registered through
/// <see cref="SecurityMasterTermsCapabilityServiceCollectionExtensions.AddSecurityMasterTermsCapability"/>.
/// </summary>
internal sealed class SecurityMasterTermsCapabilityHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SecurityMasterTermsCapabilityHostedService> _logger;

    public SecurityMasterTermsCapabilityHostedService(
        IServiceProvider services,
        ILogger<SecurityMasterTermsCapabilityHostedService>? logger = null)
    {
        _services = services;
        _logger = logger ?? NullLogger<SecurityMasterTermsCapabilityHostedService>.Instance;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _services.GetRequiredService<Meridian.Application.SecurityMaster.ISecurityMasterTermsCapabilityVerifier>()
            .VerifyAsync(cancellationToken).ConfigureAwait(false);
        var source = _services.GetRequiredService<IBusinessDayCalendarSource>();

        var termsVersions = SecurityAssetClassCatalog.All
            .Where(static descriptor => descriptor.ReadsTermsThrough != descriptor.TermsVersion)
            .OrderBy(static descriptor => descriptor.AssetClass, StringComparer.Ordinal)
            .Select(static descriptor => string.Create(
                CultureInfo.InvariantCulture,
                $"{descriptor.AssetClass} r{descriptor.ReadsTermsThrough}/w{descriptor.TermsVersion}"))
            .ToArray();

        // Only the file source lists what it loaded (never the built-in WEEKENDS, whose id a file
        // cannot claim); another source's count is unknown, not zero.
        var calendars = (source as FileBusinessDayCalendarSource)?.Calendars
            .Select(static calendar => string.Create(
                CultureInfo.InvariantCulture,
                $"{calendar.CalendarId} v{calendar.Version} {calendar.ContentHash}"))
            .ToArray();

        _logger.LogInformation(
            "Security Master terms capability: declared mapping {DeclaredMappingVersion}, schedule kernel {KernelVersion}, terms versions {TermsVersions}, business-day calendars {CalendarCount}: {Calendars}",
            SecurityVersionedTerms.DeclaredScheduleMappingVersion,
            ScheduleGenerator.KernelVersion,
            termsVersions.Length == 0 ? "none" : string.Join(", ", termsVersions),
            calendars?.Length,
            calendars is null
                ? "not listed by " + source.GetType().Name
                : calendars.Length == 0 ? "none" : string.Join(", ", calendars));
    }

    internal static bool WritesDeclaredTerms()
        => Meridian.Application.SecurityMaster.SecurityMasterTermsCapabilityVerifier.WritesDeclaredTerms();

    internal static void EnsureCompatible(SecurityMasterTermsCapabilityState state, bool writesDeclaredTerms, ILogger logger)
        => Meridian.Application.SecurityMaster.SecurityMasterTermsCapabilityVerifier.EnsureCompatible(state, writesDeclaredTerms, logger);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public static class SecurityMasterTermsCapabilityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the business-day calendar source for declared schedule conventions (ADR-025), read
    /// from files under <c>&lt;dataRoot&gt;/calendars</c> once at host start, and the hosted service
    /// that checks the persisted minimum capability for configured writers and logs the Security
    /// Master terms capability line. An unknown calendar id never falls back to
    /// weekends only. Idempotent; <c>StorageFeatureRegistration</c> calls this for every host composed
    /// through <c>AddMarketDataServices</c>, and the WPF desktop, which composes its own graph and
    /// still writes Security Master terms in-process, calls it directly.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="dataRoot">Resolves the host's data root when the calendar source is first resolved.</param>
    public static IServiceCollection AddSecurityMasterTermsCapability(
        this IServiceCollection services,
        Func<IServiceProvider, string> dataRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dataRoot);

        services.TryAddSingleton<IBusinessDayCalendarSource>(sp => FileBusinessDayCalendarSource.Load(
            dataRoot(sp),
            sp.GetService<ILogger<FileBusinessDayCalendarSource>>()));
        services.TryAddSingleton<ISecurityMasterTermsCapabilityStore>(sp =>
            new PostgresSecurityMasterTermsCapabilityStore(sp.GetRequiredService<SecurityMasterOptions>()));
        services.TryAddSingleton<Meridian.Application.SecurityMaster.ISecurityMasterTermsCapabilityVerifier>(sp =>
            new Meridian.Application.SecurityMaster.SecurityMasterTermsCapabilityVerifier(sp,
                sp.GetService<ILogger<SecurityMasterTermsCapabilityHostedService>>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SecurityMasterTermsCapabilityHostedService>());
        return services;
    }
}
