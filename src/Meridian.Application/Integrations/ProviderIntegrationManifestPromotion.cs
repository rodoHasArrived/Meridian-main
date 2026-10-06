using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

internal static class ProviderIntegrationManifestPromotion
{
    internal static async Task<ProviderIntegrationManifestDto> SelectAvailableVersionAsync(
        IProviderIntegrationManifestStore store,
        ProviderIntegrationManifestDto candidate,
        CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var retained = await store.GetManifestVersionAsync(candidate.ManifestId, candidate.ManifestVersion, ct).ConfigureAwait(false);
            if (retained is null || ProviderIntegrationManifestIdentity.Matches(
                    ProviderIntegrationManifestIdentity.Create(retained), ProviderIntegrationManifestIdentity.Create(candidate)))
            {
                return candidate;
            }

            // An interrupted promotion may leave a different immutable candidate behind.
            // Preserve it and allocate another revision; the expected-current CAS is unchanged.
            candidate = candidate with { ManifestVersion = checked(candidate.ManifestVersion + 1) };
        }
    }

    internal static async Task SaveAsync(
        IProviderIntegrationManifestStore store,
        ProviderIntegrationManifestDto candidate,
        ProviderIntegrationManifestReferenceDto? expected,
        CancellationToken ct)
    {
        await store.SaveManifestVersionAsync(candidate, ct).ConfigureAwait(false);
        if (!await store.CompareExchangeCurrentManifestAsync(
                candidate.ManifestId, expected, ProviderIntegrationManifestIdentity.Create(candidate), ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The provider integration manifest changed. Reload the current version before saving again.");
        }
    }

    internal static void ValidateExpected(
        ProviderIntegrationManifestDto? current,
        ProviderIntegrationManifestReferenceDto? expected)
    {
        if (expected is not null &&
            (current is null || !ProviderIntegrationManifestIdentity.Matches(
                expected, ProviderIntegrationManifestIdentity.Create(current))))
        {
            throw new InvalidOperationException(
                "The provider integration manifest changed. Reload the current version before saving again.");
        }
    }
}
