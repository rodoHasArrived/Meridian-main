using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ProviderConnectionLifecycleService
{
    /// <summary>Updates host-wide Alpaca market data settings with verified account access for SIP.</summary>
    public async Task<ConfigurationChangeStatus> SetAlpacaFeedAsync(
        AlpacaFeedUpdateDto update, string? tenantId, CancellationToken ct = default)
    {
        var feed = update.Feed?.Trim().ToLowerInvariant();
        if (feed is not ("iex" or "sip" or "delayed_sip"))
            throw new ArgumentException("Select IEX, SIP, or delayed SIP.");
        var sourceId = update.SourceId?.Trim();
        if (sourceId is "")
            throw new ArgumentException("Select an existing Alpaca data source.");

        if (feed != "sip")
            return await _configStore.UpdateWithChangeStatusAsync(config => AlpacaFeedConfiguration.WithFeed(config, feed, sourceId), ct)
                .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(tenantId))
            throw new UnauthorizedAccessException("Alpaca credential ownership could not be established.");

        var core = _configStore.CoreStore;
        AlpacaSipCredential authority;
        using (await core.AcquireWriteLeaseAsync(ct).ConfigureAwait(false))
        {
            var config = core.LoadRequired();
            // Validate the named target before contacting the vendor.
            _ = AlpacaFeedConfiguration.WithFeed(config, feed, sourceId);
            authority = await ReadAlpacaSipCredentialAsync(config, tenantId, ct).ConfigureAwait(false);
        }

        var probe = new ProviderCredentialProbe(_httpClientFactory);
        var account = await probe.VerifyAsync("alpaca", authority.Read.Credentials, authority.Scope.Environment,
            authority.Scope.ExternalAccountId, ct).ConfigureAwait(false);
        if (account.Status != ProviderCredentialDraftStatusDto.Verified)
            throw new AlpacaFeedVerificationException(account.Message);
        var entitlement = await probe.VerifySipAsync(authority.Read.Credentials, ct).ConfigureAwait(false);
        if (entitlement.Status != ProviderCredentialDraftStatusDto.Verified)
            throw new AlpacaFeedVerificationException(entitlement.Message);

        var (_, status) = await core.UpdateRequiredCheckedWithChangeStatusAsync(async (current, token) =>
        {
            var checkedAuthority = await ReadAlpacaSipCredentialAsync(current, tenantId, token).ConfigureAwait(false);
            if (checkedAuthority.Binding != authority.Binding || checkedAuthority.Scope != authority.Scope ||
                checkedAuthority.Read.CredentialGeneration != authority.Read.CredentialGeneration)
                throw new ProviderCredentialConflictException("Alpaca credentials changed during SIP verification. Retry with the current connection.");
            var next = AlpacaFeedConfiguration.WithFeed(current, feed, sourceId);
            UiConfigurationSaveGuard.Validate(current, next, confirmDataSemantics: false, wholeDocument: false, verifiedAlpacaSip: true);
            return (next, true);
        }, ct).ConfigureAwait(false);
        return status;
    }

    // Call under the configuration lease used by owned credential writes and binding changes.
    private async Task<AlpacaSipCredential> ReadAlpacaSipCredentialAsync(AppConfig config, string tenantId, CancellationToken ct)
    {
        ProviderCredentialScope? scope;
        try
        {
            scope = ProviderRuntimeCredentialService.ResolveScope(config, "alpaca");
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException)
        {
            throw MissingAlpacaSipCredential();
        }
        if (scope is null || _rootCredentialStore is not IScopedProviderCredentialStore scopedStore)
            throw MissingAlpacaSipCredential();
        if (!string.Equals(scope.TenantId, tenantId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Alpaca credential ownership could not be established.");
        var binding = config.ProviderConnections!.RuntimeCredentials!.Single(candidate =>
            ProviderCredentialCatalog.NormalizeProviderId(candidate.ProviderId) == "alpaca");
        var read = await scopedStore.ReadScopedAsync("alpaca", scope, ct).ConfigureAwait(false);
        if (read is null || string.IsNullOrWhiteSpace(read.CredentialGeneration))
            throw MissingAlpacaSipCredential();
        return new(binding, scope, read);
    }

    private static AlpacaFeedVerificationException MissingAlpacaSipCredential()
        => new("Select an owned Alpaca connection with saved credentials as the host runtime credential before selecting SIP.");

    private sealed record AlpacaSipCredential(ProviderRuntimeCredentialBinding Binding, ProviderCredentialScope Scope,
        ProviderCredentialReadResult Read);
}

internal sealed class AlpacaFeedVerificationException(string message) : InvalidOperationException(message);
