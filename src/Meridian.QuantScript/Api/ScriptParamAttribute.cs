namespace Meridian.QuantScript.Api;

/// <summary>
/// Marks a top-level variable declaration in a .csx script as a user-configurable parameter.
/// The UI reflects these to render a dynamic parameter form before execution.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class ScriptParamAttribute(string label) : Attribute
{
    public string Label { get; } = label;
    public object? Default { get; init; }
    public double Min { get; init; } = double.MinValue;
    public double Max { get; init; } = double.MaxValue;
    public string? Description { get; init; }
}
