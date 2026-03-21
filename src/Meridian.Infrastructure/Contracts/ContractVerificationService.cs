using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Contracts;

/// <summary>
/// Service that verifies implementations satisfy documented ADR contracts at runtime.
/// Run during application startup to catch contract violations early.
/// </summary>
public sealed class ContractVerificationService
{
    private readonly ILogger<ContractVerificationService> _logger;
    private readonly List<ContractViolation> _violations = new();

    public ContractVerificationService(ILogger<ContractVerificationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets all recorded violations from the last verification run.
    /// </summary>
    public IReadOnlyList<ContractViolation> Violations => _violations.AsReadOnly();

    /// <summary>
    /// Verifies all ADR implementations in the specified assemblies.
    /// </summary>
    /// <param name="assemblies">Assemblies to scan for implementations.</param>
    /// <returns>True if all contracts are satisfied, false otherwise.</returns>
    public bool VerifyContracts(params Assembly[] assemblies)
    {
        _violations.Clear();
        var allImplementations = new List<AdrImplementation>();

        foreach (var assembly in assemblies)
        {
            var implementations = assembly.GetAdrImplementations().ToList();
            allImplementations.AddRange(implementations);

            _logger.LogInformation(
                "Found {Count} ADR implementations in {Assembly}",
                implementations.Count,
                assembly.GetName().Name);
        }

        // Group by ADR
        var byAdr = allImplementations.GroupBy(i => i.AdrId).ToList();

        foreach (var group in byAdr)
        {
            _logger.LogDebug(
                "ADR {AdrId}: {Count} implementations",
                group.Key,
                group.Count());

            // Verify each implementation
            foreach (var impl in group)
            {
                VerifyImplementation(impl);
            }
        }

        // Verify required ADRs have implementations
        VerifyRequiredAdrs(byAdr.Select(g => g.Key).ToHashSet());

        if (_violations.Count > 0)
        {
            _logger.LogWarning(
                "Contract verification found {Count} violations",
                _violations.Count);

            foreach (var violation in _violations)
            {
                _logger.LogWarning(
                    "Contract violation in {Type}: {Message}",
                    violation.TypeName,
                    violation.Message);
            }

            return false;
        }

        _logger.LogInformation("All contract verifications passed");
        return true;
    }

    /// <summary>
    /// Verifies a single implementation satisfies its documented contract.
    /// </summary>
    private void VerifyImplementation(AdrImplementation impl)
    {
        var type = impl.ImplementationType;

        // ADR-001: Provider implementations must implement correct interfaces
        if (impl.AdrId == "ADR-001")
        {
            VerifyProviderAbstraction(type);
        }

        // ADR-004: Async methods must accept CancellationToken
        if (impl.AdrId == "ADR-004")
        {
            VerifyAsyncPatterns(type);
        }

        // ADR-005: Data sources must have DataSourceAttribute
        if (impl.AdrId == "ADR-005")
        {
            VerifyDataSourceAttribute(type);
        }
    }

    private void VerifyProviderAbstraction(Type type)
    {
        // Check if type implements IMarketDataClient or IHistoricalDataProvider
        var interfaces = type.GetInterfaces();
        var hasMarketData = interfaces.Any(i => i.Name == "IMarketDataClient");
        var hasHistorical = interfaces.Any(i => i.Name == "IHistoricalDataProvider");
        var hasDataSource = interfaces.Any(i => i.Name.Contains("DataSource"));

        if (!hasMarketData && !hasHistorical && !hasDataSource)
        {
            _violations.Add(new ContractViolation(
                "ADR-001",
                type.FullName ?? type.Name,
                "Type marked with [ImplementsAdr(\"ADR-001\")] must implement IMarketDataClient, IHistoricalDataProvider, or IDataSource"));
        }
    }

    private void VerifyAsyncPatterns(Type type)
    {
        var asyncMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.ReturnType.Name.StartsWith("Task") ||
                       m.ReturnType.Name.StartsWith("ValueTask") ||
                       m.ReturnType.Name.Contains("IAsyncEnumerable"))
            .ToList();

        foreach (var method in asyncMethods)
        {
            var parameters = method.GetParameters();
            var hasCancellation = parameters.Any(p => p.ParameterType == typeof(CancellationToken));

            if (!hasCancellation && !method.Name.StartsWith("Dispose"))
            {
                _violations.Add(new ContractViolation(
                    "ADR-004",
                    type.FullName ?? type.Name,
                    $"Async method '{method.Name}' must accept CancellationToken parameter"));
            }
        }
    }

    private void VerifyDataSourceAttribute(Type type)
    {
        var hasAttribute = Attribute.GetCustomAttribute(type, typeof(DataSources.DataSourceAttribute)) != null;

        if (!hasAttribute)
        {
            _violations.Add(new ContractViolation(
                "ADR-005",
                type.FullName ?? type.Name,
                "Type marked with [ImplementsAdr(\"ADR-005\")] must have [DataSource] attribute"));
        }
    }

    private void VerifyRequiredAdrs(HashSet<string> implementedAdrs)
    {
        // These ADRs should have at least one implementation
        var requiredAdrs = new[] { "ADR-001", "ADR-004" };

        foreach (var adr in requiredAdrs)
        {
            if (!implementedAdrs.Contains(adr))
            {
                _logger.LogDebug(
                    "No implementations found for {AdrId} (may be expected in test assemblies)",
                    adr);
            }
        }
    }
}

/// <summary>
/// Represents a contract violation found during verification.
/// </summary>
public sealed record ContractViolation(
    string AdrId,
    string TypeName,
    string Message)
{
    public override string ToString() => $"[{AdrId}] {TypeName}: {Message}";
}
