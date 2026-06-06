using System.Text.Json;

namespace Meridian.Platform.FundOperationsPersistence;

public sealed class FileShadowProjectionWriter : IShadowProjectionWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _rootPath;
    public FileShadowProjectionWriter(FundOperationsDomain domain, string rootPath)
    {
        Domain = domain;
        _rootPath = rootPath;
    }

    public FundOperationsDomain Domain { get; }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar })
            .ToHashSet();

        var sanitizedChars = value
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
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
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, ct).ConfigureAwait(false);
    }
}
