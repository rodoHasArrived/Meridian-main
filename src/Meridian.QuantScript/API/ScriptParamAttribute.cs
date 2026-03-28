namespace Meridian.QuantScript.API;

/// <summary>
/// Marks a script field or property as a configurable parameter surfaced in the QuantScript sidebar.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ScriptParamAttribute : Attribute
{
    public ScriptParamAttribute(string name)
    {
        Name = name;
    }

    /// <summary>Display name shown in the Parameters panel.</summary>
    public string Name { get; }

    /// <summary>Default value when no override is supplied.</summary>
    public object? Default { get; set; }

    /// <summary>Optional minimum value for numeric types.</summary>
    public double Min { get; set; } = double.MinValue;

    /// <summary>Optional maximum value for numeric types.</summary>
    public double Max { get; set; } = double.MaxValue;

    /// <summary>Optional tooltip shown in the Parameters panel.</summary>
    public string? Description { get; set; }
}
