namespace Meridian.Infrastructure.Contracts;

/// <summary>
/// Marks a class as implementing a specific Architectural Decision Record (ADR).
/// Used for runtime verification that implementations satisfy documented contracts.
/// </summary>
/// <example>
/// <code>
/// [ImplementsAdr("ADR-001", "Provider Abstraction Pattern")]
/// public sealed class AlpacaMarketDataClient : IMarketDataClient
/// {
///     // Implementation
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class ImplementsAdrAttribute : Attribute
{
    /// <summary>
    /// The ADR identifier (e.g., "ADR-001").
    /// </summary>
    public string AdrId { get; }

    /// <summary>
    /// Optional description of how this type implements the ADR.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Path to the ADR document relative to the docs/adr directory.
    /// </summary>
    public string DocumentPath => $"docs/adr/{AdrId.ToLowerInvariant().Replace("adr-", "")}-*.md";

    /// <summary>
    /// Creates a new ImplementsAdrAttribute.
    /// </summary>
    /// <param name="adrId">The ADR identifier (e.g., "ADR-001").</param>
    /// <param name="description">Optional description.</param>
    public ImplementsAdrAttribute(string adrId, string? description = null)
    {
        AdrId = adrId ?? throw new ArgumentNullException(nameof(adrId));
        Description = description;
    }
}

/// <summary>
/// Marks a method or property as a documented contract that must be implemented.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false)]
public sealed class DocumentedContractAttribute : Attribute
{
    /// <summary>
    /// The contract name for documentation generation.
    /// </summary>
    public string ContractName { get; }

    /// <summary>
    /// Description of the contract requirements.
    /// </summary>
    public string? Requirements { get; set; }

    /// <summary>
    /// Creates a new DocumentedContractAttribute.
    /// </summary>
    /// <param name="contractName">The contract name.</param>
    public DocumentedContractAttribute(string contractName)
    {
        ContractName = contractName ?? throw new ArgumentNullException(nameof(contractName));
    }
}

/// <summary>
/// Metadata extracted from ImplementsAdrAttribute for verification.
/// </summary>
public sealed record AdrImplementation(
    string AdrId,
    string? Description,
    Type ImplementationType,
    string TypeName)
{
    /// <summary>
    /// Creates metadata from an ImplementsAdrAttribute and its type.
    /// </summary>
    public static AdrImplementation FromAttribute(ImplementsAdrAttribute attr, Type type)
    {
        return new AdrImplementation(
            attr.AdrId,
            attr.Description,
            type,
            type.FullName ?? type.Name);
    }
}

/// <summary>
/// Extension methods for ADR verification.
/// </summary>
public static class AdrVerificationExtensions
{
    /// <summary>
    /// Gets all ADR implementations from a type.
    /// </summary>
    public static IEnumerable<AdrImplementation> GetAdrImplementations(this Type type)
    {
        return Attribute.GetCustomAttributes(type, typeof(ImplementsAdrAttribute))
            .Cast<ImplementsAdrAttribute>()
            .Select(attr => AdrImplementation.FromAttribute(attr, type));
    }

    /// <summary>
    /// Checks if a type implements a specific ADR.
    /// </summary>
    public static bool ImplementsAdr(this Type type, string adrId)
    {
        return type.GetAdrImplementations().Any(a => a.AdrId.Equals(adrId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all types implementing a specific ADR from an assembly.
    /// </summary>
    public static IEnumerable<AdrImplementation> GetAdrImplementations(
        this System.Reflection.Assembly assembly,
        string? adrIdFilter = null)
    {
        var types = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface);

        foreach (var type in types)
        {
            foreach (var impl in type.GetAdrImplementations())
            {
                if (adrIdFilter == null || impl.AdrId.Equals(adrIdFilter, StringComparison.OrdinalIgnoreCase))
                {
                    yield return impl;
                }
            }
        }
    }
}
