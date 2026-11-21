using Meridian.Contracts.Configuration;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ProviderConnectionLifecycleService
{
    /// <summary>Checks draft credentials without saving them or recording a saved verification.</summary>
    public async Task<ProviderCredentialDraftVerificationResultDto> VerifyDraftAsync(
        string providerId, ProviderCredentialDraftRequestDto request, CancellationToken ct = default)
    {
        var descriptor = RequireDescriptor(providerId);
        var draft = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string environment;
        string? savedGeneration = null;
        var enforceLegacyOwnership = _ownershipScope is null;
        using (await AcquireOwnershipLeaseAsync(descriptor.ProviderId, enforceLegacyOwnership, ct).ConfigureAwait(false))
        {
            if (!ProviderCredentialProbe.Supports(descriptor.ProviderId))
                return new(descriptor.ProviderId, ProviderCredentialDraftStatusDto.Unavailable,
                    "This provider does not offer draft verification through Meridian.");

            IReadOnlyDictionary<string, string?> fields;
            try
            {
                fields = NormalizeCredentialFields(descriptor, request.Credentials ?? new Dictionary<string, string?>());
            }
            catch (ProviderCredentialValidationException)
            {
                return ProviderCredentialProbe.Rejected(descriptor.ProviderId, "The draft contains unsupported credential fields.");
            }

            foreach (var (field, value) in fields)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    draft[field] = value.Trim();
            }
            if (draft.Count == 0)
                return ProviderCredentialProbe.Rejected(descriptor.ProviderId, "Enter a draft credential before testing.");

            environment = _ownershipScope?.Environment ?? descriptor.NormalizeEnvironment(request.Environment);
            if (_ownershipScope is not null && !string.IsNullOrWhiteSpace(request.Environment) &&
                descriptor.NormalizeEnvironment(request.Environment) != environment)
                return ProviderCredentialProbe.Rejected(descriptor.ProviderId, "The selected environment differs from the owned connection.");

            // Owned editors preserve blank fields on save. Test the same candidate, using only
            // the retained values of the selected account. A provider-wide test uses its draft.
            if (_ownershipScope is { } scope && descriptor.RequiredFields.Any(field => field.Required && !draft.ContainsKey(field.Name)))
            {
                var owned = _configStore.CoreStore.LoadRequired().ProviderConnections!.Connections!
                    .Single(connection => connection.ConnectionId == scope.ConnectionId);
                if (!owned.Enabled)
                    return ProviderCredentialProbe.Rejected(descriptor.ProviderId, "Enter a complete draft to test a disabled connection.");
                var saved = await ((IScopedProviderCredentialStore)_rootCredentialStore)
                    .ReadScopedAsync(descriptor.ProviderId, scope, ct).ConfigureAwait(false);
                if (saved is not null && !string.IsNullOrWhiteSpace(saved.CredentialGeneration))
                {
                    savedGeneration = saved.CredentialGeneration;
                    foreach (var (field, value) in saved.Credentials)
                        draft.TryAdd(field, value);
                }
            }
        }

        var result = await new ProviderCredentialProbe(_httpClientFactory)
            .VerifyAsync(descriptor.ProviderId, draft, environment, _ownershipScope?.ExternalAccountId, ct)
            .ConfigureAwait(false);
        using var currentOwnership = await AcquireOwnershipLeaseAsync(descriptor.ProviderId,
            enforceLegacyOwnership || savedGeneration is not null, ct).ConfigureAwait(false);
        if (savedGeneration is not null)
        {
            var current = await ((IScopedProviderCredentialStore)_rootCredentialStore)
                .ReadScopedAsync(descriptor.ProviderId, _ownershipScope!, ct).ConfigureAwait(false);
            if (current?.CredentialGeneration != savedGeneration)
                return new(descriptor.ProviderId, ProviderCredentialDraftStatusDto.Unavailable,
                    "Saved credentials changed during this test. Test the draft against the current connection.");
        }
        return result;
    }
}
