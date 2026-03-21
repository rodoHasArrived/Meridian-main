using Meridian.Application.Logging;
using Meridian.Application.Monitoring.Core;
using Meridian.Infrastructure.Contracts;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Service categories for organization and discovery.
/// </summary>
public enum ServiceCategory : byte
{
    /// <summary>Configuration and setup services.</summary>
    Setup,

    /// <summary>Validation and preflight check services.</summary>
    Validation,

    /// <summary>Diagnostic and error handling services.</summary>
    Diagnostics,

    /// <summary>Application lifecycle services.</summary>
    Lifecycle,

    /// <summary>Data query and manipulation services.</summary>
    Data,

    /// <summary>Trading calendar services.</summary>
    Calendar,

    /// <summary>Reporting and progress display services.</summary>
    Reporting
}

/// <summary>
/// Service metadata for discovery and documentation.
/// </summary>
/// <param name="Name">Service name.</param>
/// <param name="Category">Service category.</param>
/// <param name="Description">Service description.</param>
/// <param name="ServiceType">Service implementation type.</param>
/// <param name="IsAsync">Whether service operations are async.</param>
public sealed record ServiceInfo(
    string Name,
    ServiceCategory Category,
    string Description,
    Type ServiceType,
    bool IsAsync = true);

/// <summary>
/// Centralized registry for application services providing discovery,
/// categorization, and dependency resolution capabilities.
/// </summary>
/// <remarks>
/// The service registry provides:
/// - Service discovery by category
/// - Service metadata for documentation
/// - Centralized access to common services
/// - Health check aggregation for services
///
/// Services are organized into logical categories:
/// - Setup: ConfigurationWizard, AutoConfigurationService, ConfigEnvironmentOverride
/// - Validation: CredentialValidationService, ConnectivityTestService, PreflightChecker
/// - Diagnostics: DiagnosticBundleService, ErrorTracker, FriendlyErrorFormatter
/// - Lifecycle: GracefulShutdownService, StartupSummary
/// - Data: HistoricalDataQueryService, DryRunService, SampleDataGenerator
/// - Calendar: TradingCalendar
/// - Reporting: ProgressDisplayService, DailySummaryWebhook, ApiDocumentationService
/// </remarks>
[ImplementsAdr("ADR-001", "Centralized service registry for organization and discovery")]
public sealed class ServiceRegistry : IDisposable, IAsyncDisposable
{
    private readonly Dictionary<string, ServiceInfo> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, object> _instances = new();
    private readonly ILogger _log;
    private bool _disposed;

    public ServiceRegistry(ILogger? log = null)
    {
        _log = log ?? LoggingSetup.ForContext<ServiceRegistry>();
    }

    /// <summary>
    /// Registers a service with metadata.
    /// </summary>
    /// <typeparam name="T">Service type.</typeparam>
    /// <param name="instance">Service instance.</param>
    /// <param name="category">Service category.</param>
    /// <param name="description">Service description.</param>
    public void Register<T>(T instance, ServiceCategory category, string description) where T : class
    {
        var type = typeof(T);
        var name = type.Name;

        _services[name] = new ServiceInfo(name, category, description, type);
        _instances[type] = instance;

        _log.Debug("Registered service: {ServiceName} in category {Category}", name, category);
    }

    /// <summary>
    /// Gets a service by type.
    /// </summary>
    /// <typeparam name="T">Service type.</typeparam>
    /// <returns>Service instance or null if not registered.</returns>
    public T? Get<T>() where T : class
    {
        return _instances.TryGetValue(typeof(T), out var instance) ? instance as T : null;
    }

    /// <summary>
    /// Gets all services in a category.
    /// </summary>
    /// <param name="category">Category to filter by.</param>
    /// <returns>Services in the category.</returns>
    public IReadOnlyList<ServiceInfo> GetByCategory(ServiceCategory category)
    {
        return _services.Values.Where(s => s.Category == category).ToList();
    }

    /// <summary>
    /// Gets all registered services.
    /// </summary>
    /// <returns>All registered services.</returns>
    public IReadOnlyList<ServiceInfo> GetAll()
    {
        return _services.Values.ToList();
    }

    /// <summary>
    /// Gets a summary of registered services by category.
    /// </summary>
    /// <returns>Dictionary of category to service count.</returns>
    public IReadOnlyDictionary<ServiceCategory, int> GetSummary()
    {
        return _services.Values
            .GroupBy(s => s.Category)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var instance in _instances.Values)
        {
            try
            {
                if (instance is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "Error disposing service {ServiceType}", instance.GetType().Name);
            }
        }

        _services.Clear();
        _instances.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var instance in _instances.Values)
        {
            try
            {
                if (instance is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (instance is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "Error disposing service {ServiceType}", instance.GetType().Name);
            }
        }

        _services.Clear();
        _instances.Clear();
    }
}

/// <summary>
/// Extension methods for service registration with common patterns.
/// </summary>
public static class ServiceRegistryExtensions
{
    /// <summary>
    /// Registers setup services (configuration, wizard, auto-config).
    /// </summary>
    public static ServiceRegistry RegisterSetupServices(
        this ServiceRegistry registry,
        ConfigurationWizard? wizard = null,
        AutoConfigurationService? autoConfig = null)
    {
        if (wizard != null)
            registry.Register(wizard, ServiceCategory.Setup, "Interactive configuration wizard for first-time setup");

        if (autoConfig != null)
            registry.Register(autoConfig, ServiceCategory.Setup, "Automatic configuration from environment variables");

        return registry;
    }

    /// <summary>
    /// Registers validation services (credentials, connectivity, preflight).
    /// </summary>
    public static ServiceRegistry RegisterValidationServices(
        this ServiceRegistry registry,
        CredentialValidationService? credentialValidator = null,
        ConnectivityTestService? connectivityTest = null,
        PreflightChecker? preflightChecker = null)
    {
        if (credentialValidator != null)
            registry.Register(credentialValidator, ServiceCategory.Validation, "Validates API credentials for all providers");

        if (connectivityTest != null)
            registry.Register(connectivityTest, ServiceCategory.Validation, "Tests network connectivity to provider endpoints");

        if (preflightChecker != null)
            registry.Register(preflightChecker, ServiceCategory.Validation, "Runs preflight checks before application startup");

        return registry;
    }

    /// <summary>
    /// Registers diagnostic services (error tracking, diagnostics bundle).
    /// </summary>
    public static ServiceRegistry RegisterDiagnosticServices(
        this ServiceRegistry registry,
        DiagnosticBundleService? diagnosticBundle = null,
        ErrorTracker? errorTracker = null)
    {
        if (diagnosticBundle != null)
            registry.Register(diagnosticBundle, ServiceCategory.Diagnostics, "Generates diagnostic bundles for troubleshooting");

        if (errorTracker != null)
            registry.Register(errorTracker, ServiceCategory.Diagnostics, "Tracks and categorizes runtime errors");

        return registry;
    }

    /// <summary>
    /// Registers lifecycle services (shutdown, startup summary).
    /// </summary>
    public static ServiceRegistry RegisterLifecycleServices(
        this ServiceRegistry registry,
        GracefulShutdownService? shutdownService = null)
    {
        if (shutdownService != null)
            registry.Register(shutdownService, ServiceCategory.Lifecycle, "Handles graceful application shutdown with flush coordination");

        return registry;
    }

    /// <summary>
    /// Registers data services (historical query, sample generator).
    /// </summary>
    public static ServiceRegistry RegisterDataServices(
        this ServiceRegistry registry,
        HistoricalDataQueryService? historicalQuery = null,
        SampleDataGenerator? sampleGenerator = null)
    {
        if (historicalQuery != null)
            registry.Register(historicalQuery, ServiceCategory.Data, "Queries historical market data from storage");

        if (sampleGenerator != null)
            registry.Register(sampleGenerator, ServiceCategory.Data, "Generates sample market data for testing");

        return registry;
    }

    /// <summary>
    /// Registers calendar services (trading calendar).
    /// </summary>
    public static ServiceRegistry RegisterCalendarServices(
        this ServiceRegistry registry,
        TradingCalendar? tradingCalendar = null)
    {
        if (tradingCalendar != null)
            registry.Register(tradingCalendar, ServiceCategory.Calendar, "Provides trading hours and holiday information");

        return registry;
    }

    /// <summary>
    /// Registers reporting services (progress, webhooks, documentation).
    /// </summary>
    public static ServiceRegistry RegisterReportingServices(
        this ServiceRegistry registry,
        ProgressDisplayService? progressDisplay = null,
        ApiDocumentationService? apiDocs = null)
    {
        if (progressDisplay != null)
            registry.Register(progressDisplay, ServiceCategory.Reporting, "Displays progress for long-running operations");

        if (apiDocs != null)
            registry.Register(apiDocs, ServiceCategory.Reporting, "Generates API documentation");

        return registry;
    }
}
