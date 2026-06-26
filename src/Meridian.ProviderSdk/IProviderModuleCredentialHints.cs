namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Optional interface implemented by provider modules to advertise which credential keys they accept.
/// When implemented, the UI uses this list to render a typed credential form instead of a generic
/// key/value editor.
/// </summary>
public interface IProviderModuleCredentialHints
{
    /// <summary>
    /// Ordered list of credential key names expected by this module (e.g. ["keyId", "secretKey"]).
    /// Keys must be consistent with the keys passed via <see cref="ProviderModuleContext.Credentials"/>.
    /// </summary>
    IReadOnlyList<string> CredentialKeyHints { get; }
}
