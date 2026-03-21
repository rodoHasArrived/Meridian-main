namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Marks a public property on an <see cref="IBacktestStrategy"/> implementation as a user-configurable
/// parameter. The backtesting UI discovers these via reflection to render a dynamic parameter form.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class StrategyParameterAttribute(string displayName, string? description = null) : Attribute
{
    /// <summary>Human-readable label shown in the UI.</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>Optional longer description / tooltip text.</summary>
    public string? Description { get; } = description;
}
