namespace Meridian.Infrastructure.Contracts;

/// <summary>
/// Declares that a provider class requires a named credential at runtime.
/// Apply one attribute per credential; providers with multiple credentials
/// apply the attribute multiple times.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AttributeCredentialResolver"/> scans these attributes at startup to build a
/// per-provider <see cref="ICredentialContext"/>, eliminating the need to add a dedicated
/// method to the legacy per-provider credential resolver for every new provider.
/// </para>
/// <para>
/// The WPF <c>CredentialManagementPage</c> reads these attributes to render credential fields
/// dynamically; <see cref="DisplayName"/> and <see cref="Description"/> are used as UI labels.
/// </para>
/// <para><b>Example:</b></para>
/// <code>
/// [RequiresCredential("API_KEY",
///     EnvironmentVariables = new[] { "MYBROKER_API_KEY", "MYBROKER__APIKEY" },
///     DisplayName = "API Key",
///     Description = "Found in your MyBroker dashboard under Settings → API.")]
/// [RequiresCredential("SECRET_KEY",
///     EnvironmentVariables = new[] { "MYBROKER_SECRET_KEY" },
///     Optional = true,
///     DisplayName = "API Secret (optional)")]
/// public sealed class MyBrokerHistoricalDataProvider : BaseHistoricalDataProvider { ... }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresCredentialAttribute : Attribute
{
    /// <summary>Logical credential name used to look up the resolved value via <see cref="ICredentialContext.Get"/>.</summary>
    public string Name { get; }

    /// <summary>One or more environment variable names to try in order (first non-empty value wins).</summary>
    public string[] EnvironmentVariables { get; init; } = [];

    /// <summary>
    /// When <see langword="true"/> the provider continues to function without this credential
    /// (e.g. anonymous / free-tier endpoints). Defaults to <see langword="false"/>.
    /// </summary>
    public bool Optional { get; init; }

    /// <summary>Human-readable label shown in the credential management UI. Defaults to <see cref="Name"/>.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Optional hint shown in the credential management UI, e.g. "Found under Settings → API Keys".</summary>
    public string? Description { get; init; }

    /// <param name="name">Logical credential name, e.g. <c>"API_KEY"</c>. Must be unique within the provider class.</param>
    public RequiresCredentialAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }
}
