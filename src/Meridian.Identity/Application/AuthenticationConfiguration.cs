using Microsoft.Extensions.Configuration;

namespace Meridian.Identity;

/// <summary>
/// Host-owned authentication settings. Register an instance to isolate a host's credentials and
/// authentication posture from process settings. Without one, existing hosts retain live environment
/// reads, including credential and API-key rotation.
/// </summary>
public sealed class AuthenticationConfiguration
{
    private readonly Func<string, string?> _read;
    private readonly Action<string, string?> _write;

    public AuthenticationConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _read = name => configuration[name];
        _write = (name, value) => configuration[name] = value;
    }

    private AuthenticationConfiguration()
    {
        _read = Environment.GetEnvironmentVariable;
        _write = Environment.SetEnvironmentVariable;
    }

    /// <summary>Missing host settings remain absent; they never fall back to the process.</summary>
    public string? this[string name]
    {
        get => _read(name);
        set => _write(name, value);
    }

    /// <summary>Compatibility adapter for hosts that use process startup configuration.</summary>
    public static AuthenticationConfiguration FromEnvironment() => new();
}
