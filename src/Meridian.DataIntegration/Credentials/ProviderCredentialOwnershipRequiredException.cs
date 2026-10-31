namespace Meridian.DataIntegration.Credentials;

/// <summary>A provider-wide runtime cannot use credentials after an owned connection has been retained.</summary>
public sealed class ProviderCredentialOwnershipRequiredException() : InvalidOperationException(
    "Owned provider connections require a tenant-aware runtime. Use selected-connection verification and keep provider-wide activity stopped.");
