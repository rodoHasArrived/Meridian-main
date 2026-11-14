using System.Text.Json;
using Meridian.Contracts.Integrations;

namespace Meridian.Tests.Application.Integrations;

internal static class ProviderIntegrationCompletionFixture
{
    internal static async Task<ProviderIntegrationSyncRunDto> RetainEmptySourceAsync(
        IProviderIntegrationManifestStore store,
        ProviderIntegrationManifestDto manifest,
        ProviderConnectionDto connection,
        string runId = "complete-empty-run",
        DateTimeOffset? startedAt = null,
        bool retainRaw = true)
    {
        var reference = ProviderIntegrationManifestIdentity.Create(manifest);
        var capturedAt = startedAt ?? DateTimeOffset.Parse("2026-06-16T12:30:00Z");
        var run = new ProviderIntegrationSyncRunDto(runId, manifest.ManifestId, connection.ConnectionId,
            manifest.ProviderId, ProviderCapabilityKindDto.Positions, "positions", capturedAt, capturedAt,
            ProviderIntegrationProcessingStatusDto.Validated, 0, 0, 0, "payload-" + runId, [])
        {
            ManifestReference = reference,
            OriginalManifestReference = reference
        };
        if (retainRaw)
        {
            await store.SaveRawPayloadAsync(new(run.RawPayloadId!, manifest.ProviderId, connection.ConnectionId,
                run.Capability, run.EndpointKey, run.SyncRunId, capturedAt,
                new Dictionary<string, string> { ["statusCode"] = "200" },
                JsonSerializer.SerializeToElement(new { positions = Array.Empty<object>() }),
                $"{manifest.ManifestId}:v{manifest.ManifestVersion}", ProviderIntegrationProcessingStatusDto.Received)
            {
                ManifestReference = reference,
                OriginalManifestReference = reference
            });
        }
        await store.SaveSyncRunAsync(run);
        return run;
    }
}
