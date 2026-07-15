using System.Text.Json;

namespace Meridian.Platform.FundOperationsPersistence;

public sealed class FileShadowProjectionWriter : IShadowProjectionWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<char> InvalidPathChars = Path.GetInvalidFileNameChars()
        .Concat(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar })
        .ToHashSet();

    private readonly string _rootPath;
    public FileShadowProjectionWriter(FundOperationsDomain domain, string rootPath)
    {
        Domain = domain;
        _rootPath = rootPath;
    }

    public FundOperationsDomain Domain { get; }

    private static string SanitizePathSegment(string value)
    {
        var sanitizedChars = value
            .Select(ch => InvalidPathChars.Contains(ch) ? '_' : ch)
            .ToArray();

        var sanitized = new string(sanitizedChars);

        while (sanitized.Contains("..", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("..", "_", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }

    public async Task WriteAsync(string projectionName, string entityKey, object payload, CancellationToken ct = default)
    {
        var safeProjection = SanitizePathSegment(projectionName);
        var safeEntity = SanitizePathSegment(entityKey);
        var folder = Path.Combine(_rootPath, Domain.ToString(), safeProjection);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{safeEntity}.json");

        // Write to a temp file and atomically move it into place so a crash mid-write cannot
        // leave a torn projection file, honouring the repository's atomic-write guardrail.
        var tempPath = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                // FlushAsync only reaches the OS cache; force the bytes to physical disk before the
                // rename so a crash right after File.Move cannot surface a zero/partial file.
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemp(tempPath);
            throw;
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // Best-effort cleanup; a leftover temp file is harmless and swept on next write.
        }
    }
}
