namespace Meridian.Infrastructure.Contracts;

/// <summary>
/// Provides resolved credential values to a provider instance.
/// Obtain an instance via <see cref="AttributeCredentialResolver.ForType"/>.
/// </summary>
public interface ICredentialContext
{
    /// <summary>
    /// Whether this context owns the complete credential record, including missing or removed
    /// fields. Authoritative records must not be overlaid with module, config or environment values.
    /// </summary>
    bool IsAuthoritative => false;

    /// <summary>The environment bound to the selected credential record, when supplied.</summary>
    string? CredentialEnvironment => null;

    /// <summary>
    /// Returns the resolved credential value for the given logical <paramref name="name"/>,
    /// or <see langword="null"/> when the credential is not configured.
    /// </summary>
    /// <param name="name">
    /// The logical credential name as declared in <see cref="RequiresCredentialAttribute.Name"/>.
    /// </param>
    string? Get(string name);

    /// <summary>Returns <see langword="true"/> when the credential for <paramref name="name"/> is non-empty.</summary>
    bool IsConfigured(string name);
}
