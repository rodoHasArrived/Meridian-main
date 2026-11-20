using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Core.IO;
using Meridian.Core.Logging;

namespace Meridian.Infrastructure.Adapters.Core;

internal sealed class ProviderRateLimitRollupStore(string dataRoot, IAtomicFileWriter writer)
{
    private static readonly Serilog.ILogger Log = LoggingSetup.ForContext<ProviderRateLimitRollupStore>();
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(2);
    private readonly RootedPathGuard _pathGuard = new(dataRoot);
    private int _writeFailed;

    public void Record(string provider, DateOnly utcDay, bool rotated)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return;
        try
        {
            var path = _pathGuard.ResolvePath("_meta", "provider-rate-limit-rollups.json");
            var lockPath = _pathGuard.ResolvePath("_meta", "provider-rate-limit-rollups.json.lock");
            using var lease = ExclusiveFileLock.Acquire(lockPath, LockTimeout);
            _pathGuard.EnsurePath(path);
            var cutoff = utcDay.AddDays(-6);
            var retained = Load(path).Where(row => row.DateUtc >= cutoff).ToList();
            var providerId = provider.Trim().ToLowerInvariant();
            var index = retained.FindIndex(row => row.ProviderId == providerId && row.DateUtc == utcDay);
            if (index == -1)
            {
                index = retained.Count;
                retained.Add(new(providerId, utcDay, 0, 0));
            }
            var existing = retained[index];
            retained[index] = rotated
                ? existing with { RotationCount = existing.RotationCount + 1 }
                : existing with { ThrottleCount = existing.ThrottleCount + 1 };
            writer.Write(path, JsonSerializer.Serialize(retained));
        }
        catch (Exception error) when (IsStorageFailure(error))
        {
            // Evidence loss must remain observable without interrupting provider fallback.
            if (Interlocked.Exchange(ref _writeFailed, 1) == 0)
                Log.Warning(error, "Could not retain provider rate-limit daily observations.");
        }
    }

    public bool TryRead(string provider, DateOnly utcDay, out IReadOnlyList<ProviderRateLimitDailyRollupDto> rollups)
    {
        rollups = [];
        try
        {
            var path = _pathGuard.ResolvePath("_meta", "provider-rate-limit-rollups.json");
            var cutoff = utcDay.AddDays(-6);
            rollups = Load(path).Where(row => row.DateUtc >= cutoff && row.DateUtc <= utcDay &&
                    string.Equals(row.ProviderId, provider, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(row => row.DateUtc)
                .Select(row => new ProviderRateLimitDailyRollupDto(row.DateUtc, row.ThrottleCount, row.RotationCount))
                .ToArray();
            return Volatile.Read(ref _writeFailed) == 0;
        }
        catch (Exception error) when (IsStorageFailure(error))
        {
            return false;
        }
    }

    private static List<Rollup> Load(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var rows = JsonSerializer.Deserialize<List<Rollup>>(stream);
            if (rows is null || rows.Any(row => row is null || string.IsNullOrWhiteSpace(row.ProviderId)
                || row.ThrottleCount < 0 || row.RotationCount < 0))
                throw new InvalidDataException("Provider rate-limit rollups must contain valid observations.");
            return rows;
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    private static bool IsStorageFailure(Exception error)
        => error is IOException or InvalidDataException or UnauthorizedAccessException
            or TimeoutException or JsonException or InvalidOperationException;

    private sealed record Rollup(string ProviderId, DateOnly DateUtc, long ThrottleCount, long RotationCount);
}
