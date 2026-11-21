using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

/// <summary>
/// A module-owned database initializer and the steps that must finish before it runs.
/// Initializers retain ownership of configuration checks, migrations, and readiness receipts.
/// </summary>
public sealed class DatabaseStartupStep
{
    private readonly Func<IServiceProvider, CancellationToken, ILogger?, Task> _initialize;

    public DatabaseStartupStep(
        string id,
        IReadOnlyCollection<string> dependencies,
        Func<IServiceProvider, CancellationToken, ILogger?, Task> initialize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(initialize);
        if (dependencies.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Database startup dependencies require an identifier.", nameof(dependencies));

        Id = id;
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
        _initialize = initialize;
    }

    public string Id { get; }
    public IReadOnlyList<string> Dependencies { get; }

    internal Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken, ILogger? logger)
        => _initialize(services, cancellationToken, logger);
}

/// <summary>
/// Validates and orders the final host's database initializers before any migrations run.
/// Execution is sequential and stops on the first failure or cancellation. Host lifecycle gates
/// determine when the plan runs and whether a successful startup can be reused.
/// </summary>
public sealed class DatabaseStartupPlan
{
    public DatabaseStartupPlan(IEnumerable<DatabaseStartupStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var pending = steps.ToList();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in pending)
        {
            ArgumentNullException.ThrowIfNull(step);
            if (!ids.Add(step.Id))
                throw new InvalidOperationException($"Duplicate database startup step '{step.Id}'.");
        }

        foreach (var step in pending)
        {
            foreach (var dependency in step.Dependencies)
            {
                if (!ids.Contains(dependency))
                    throw new InvalidOperationException(
                        $"Database startup step '{step.Id}' requires missing step '{dependency}'.");
            }
        }

        var completed = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<DatabaseStartupStep>(pending.Count);
        while (pending.Count > 0)
        {
            var index = pending.FindIndex(step => step.Dependencies.All(completed.Contains));
            if (index < 0)
                throw new InvalidOperationException(
                    $"Database startup dependency cycle among: {string.Join(", ", pending.Select(step => step.Id))}.");

            var step = pending[index];
            pending.RemoveAt(index);
            ordered.Add(step);
            completed.Add(step.Id);
        }

        Steps = ordered.AsReadOnly();
    }

    public IReadOnlyList<DatabaseStartupStep> Steps { get; }

    public async Task EnsureReadyAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var step in Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await step.InitializeAsync(services, cancellationToken, logger).ConfigureAwait(false);
        }
    }
}

public static class DatabaseStartupServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared core startup plan. Workstation-only readiness, including Reporting,
    /// remains owned by its host and runs after this plan. Registration does not run migrations.
    /// </summary>
    public static IServiceCollection AddDatabaseStartupPlan(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<DatabaseStartupPlan>();
        services.AddDatabaseStartupStep(LedgerStartup.StartupStep);
        services.AddDatabaseStartupStep(SecurityMasterStartup.StartupStep);
        services.AddDatabaseStartupStep(DirectLendingStartup.StartupStep);
        services.AddDatabaseStartupStep(AssetOperationsStartup.StartupStep);
        services.AddDatabaseStartupStep(FundAccountsStartup.StartupStep);
        services.AddDatabaseStartupStep(FundStructureStartup.StartupStep);
        services.AddDatabaseStartupStep(BankingStartup.StartupStep);
        services.AddDatabaseStartupStep(MoneyMarketStartup.StartupStep);
        return services;
    }

    /// <summary>
    /// Adds an initializer. Registering the same instance again is harmless; conflicting owners
    /// of an identifier are rejected. Factory registrations are also validated by the final plan.
    /// </summary>
    public static IServiceCollection AddDatabaseStartupStep(this IServiceCollection services, DatabaseStartupStep step)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(step);
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(DatabaseStartupStep))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<DatabaseStartupStep>()
            .FirstOrDefault(candidate => string.Equals(candidate.Id, step.Id, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (!ReferenceEquals(existing, step))
                throw new InvalidOperationException($"Duplicate database startup step '{step.Id}'.");
            return services;
        }

        services.AddSingleton(step);
        return services;
    }
}
