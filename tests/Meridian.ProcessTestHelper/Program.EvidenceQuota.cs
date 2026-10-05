using Meridian.Documents;

namespace Meridian.ProcessTestHelper;

internal static partial class Program
{
    private static async Task<int> ReserveEvidenceAndWaitAsync(IReadOnlyList<string> args)
    {
        RequireArgumentCount(args, 4);
        var publishBeforeCrash = bool.Parse(args[3]);
        var coordinator = new EvidenceStorageQuotaCoordinator(args[1], new EvidenceStorageQuotaOptions
        {
            MaxPackageBytes = 100,
            DefaultTenantBudgetBytes = 100,
            MinimumDiskHeadroomBytes = 0
        }, _ => 0, _ => long.MaxValue);
        await using var reservation = await coordinator.ReserveAsync("tenant", 80, 1).ConfigureAwait(false);
        var stage = Path.Combine(args[1], "stage", reservation.Id);
        Directory.CreateDirectory(stage);
        var artifact = Path.Combine(stage, "artifact.dat");
        await reservation.BeforeWriteAsync(20).ConfigureAwait(false);
        await File.WriteAllBytesAsync(artifact, Enumerable.Repeat((byte)0x5a, 20).ToArray()).ConfigureAwait(false);
        await reservation.AfterWriteAsync(20).ConfigureAwait(false);
        if (publishBeforeCrash)
        {
            await reservation.PublishAsync(async _ =>
            {
                File.Move(artifact, Path.Combine(args[1], "published.dat"));
                await File.WriteAllTextAsync(args[2], reservation.Id).ConfigureAwait(false);
                // Simulate death after index-last publication but before journal reconciliation.
                await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        await File.WriteAllTextAsync(args[2], reservation.Id).ConfigureAwait(false);
        await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        return 0;
    }
}
