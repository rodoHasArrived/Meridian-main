namespace Meridian.Application.Exceptions;

/// <summary>
/// Exception thrown when there are configuration errors
/// </summary>
public sealed class ConfigurationException : MeridianException
{
    public string? ConfigPath { get; }
    public string? FieldName { get; }

    public ConfigurationException(string message) : base(message)
    {
    }

    public ConfigurationException(string message, string? configPath = null, string? fieldName = null)
        : base(message)
    {
        ConfigPath = configPath;
        FieldName = fieldName;
    }

    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
