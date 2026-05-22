using System.Text.Json;

namespace Meridian.Application.FundOperationsPersistence;

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

    public async Task WriteAsync(string projectionName, string entityKey, object payload, CancellationToken ct = default)
    {
        var safeProjection = projectionName.Replace('/', '_');
        var safeEntity = entityKey.Replace('/', '_');
        var folder = Path.Combine(_rootPath, Domain.ToString(), safeProjection);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{safeEntity}.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, ct).ConfigureAwait(false);
    }
}
