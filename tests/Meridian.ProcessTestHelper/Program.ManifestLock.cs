using System.Text.Json;
using Meridian.Contracts.Integrations;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Integrations;

namespace Meridian.ProcessTestHelper;

internal static partial class Program
{
    private static async Task<int> RetainManifestAndHoldLockAsync(IReadOnlyList<string> args)
    {
        RequireArgumentCount(args, 4);
        var manifest = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(args[2]).ConfigureAwait(false),
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationManifestDto)
            ?? throw new InvalidDataException("The candidate manifest is missing.");
        await new FileProviderIntegrationManifestStore(args[1])
            .SaveManifestVersionAsync(manifest).ConfigureAwait(false);

        var lockPath = Path.Combine(args[1], "_integrations", "manifest-locks",
            $"{Sha256Digest.ComputeUtf8(manifest.ManifestId)}.lock");
        await using var manifestLock = new FileStream(
            lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await File.WriteAllTextAsync(args[3], "retained-and-locked").ConfigureAwait(false);
        // The parent terminates the process between durable retention and pointer promotion.
        // An operating-system lock must remain exclusive until that termination releases it.
        await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        return 0;
    }
}
