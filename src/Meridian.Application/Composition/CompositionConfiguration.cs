using Meridian.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition;

/// <summary>
/// Configuration owned by one composed host. Explicit configuration is authoritative, including
/// missing keys; hosts that omit it retain the process-environment startup contract.
/// </summary>
public sealed class CompositionConfiguration(IConfiguration? configuration = null)
{
    /// <summary>The explicit settings shared with other host configuration adapters, when supplied.</summary>
    public IConfiguration? HostConfiguration => configuration;

    public bool UsesEnvironment => configuration is null;

    public string? this[string name]
    {
        get => configuration is null ? Environment.GetEnvironmentVariable(name) : configuration[name];
        set
        {
            if (configuration is null)
                Environment.SetEnvironmentVariable(name, value);
            else
                configuration[name] = value;
        }
    }

    public string? GetConnectionString(string name)
    {
        var value = this[name];
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        var unified = this[MeridianDatabaseEnvironment.UnifiedVariable];
        return MeridianDatabaseEnvironment.PropagatedConnectionStringVariables.Contains(name)
            && !string.IsNullOrWhiteSpace(unified)
                ? MeridianDatabaseEnvironment.NormalizeToConnectionString(unified.Trim())
                : value;
    }

    public bool IsConfigured(string name) => !string.IsNullOrWhiteSpace(GetConnectionString(name));

    public string GetSchema(string name, string defaultValue)
        => string.IsNullOrWhiteSpace(this[name]) ? defaultValue : this[name]!;

    public bool GetBoolean(string name, bool defaultValue)
        => bool.TryParse(this[name], out var value) ? value : defaultValue;

    public int GetInt32(string name, int defaultValue)
        => int.TryParse(this[name], out var value) ? value : defaultValue;

    /// <summary>Reuses the host's registered authority when composing additional service groups.</summary>
    public static CompositionConfiguration Resolve(IServiceCollection services)
        => services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(CompositionConfiguration))
            ?.ImplementationInstance as CompositionConfiguration ?? new CompositionConfiguration();
}
