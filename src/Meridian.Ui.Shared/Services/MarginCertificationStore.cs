using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Services;

public sealed class MarginCertificationStore(string dataRoot)
{
    private readonly string _path = Path.Combine(dataRoot, "accounting", "margin-certifications.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<IReadOnlyList<MarginCertificationResultDto>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MarginCertificationResultDto> UpsertAsync(
        MarginCertificationResultDto certification,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var records = (await ReadCoreAsync(ct).ConfigureAwait(false)).ToList();
            records.RemoveAll(item =>
                string.Equals(item.ProviderId, certification.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.AccountId, certification.AccountId, StringComparison.OrdinalIgnoreCase) &&
                item.AsOf == certification.AsOf);
            records.Add(certification);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                records.OrderBy(static item => item.ProviderId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static item => item.AccountId, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(static item => item.AsOf),
                JsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await AtomicFileWriter.WriteAsync(_path, bytes, ct).ConfigureAwait(false);
            return certification;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<MarginCertificationResultDto>> ReadCoreAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return [];
        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<List<MarginCertificationResultDto>>(stream, JsonOptions, ct).ConfigureAwait(false)
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }
}
