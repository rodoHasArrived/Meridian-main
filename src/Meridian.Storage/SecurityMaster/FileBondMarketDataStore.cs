using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Archival;

namespace Meridian.Storage.SecurityMaster;

/// <summary>Atomic host-local observation history, partitioned by authenticated tenant and locked across processes.</summary>
public sealed class FileBondMarketDataStore(string dataRoot) : IBondMarketDataStore
{
    private readonly string _directory = Path.Combine(Path.GetFullPath(dataRoot), "security-master", "bond-market-observations");

    public async Task<BondMarketImportResult> AppendAsync(string tenantId, IReadOnlyList<BondMarketObservationDto> observations, CancellationToken ct = default)
    {
        var path = TenantPath(tenantId);
        ArgumentNullException.ThrowIfNull(observations);
        var proposed = observations.ToArray();
        foreach (var observation in proposed)
            BondMarketDataRules.ValidateRetained(observation);
        Directory.CreateDirectory(_directory);
        await using var lease = await AcquireAsync(path + ".lock", ct).ConfigureAwait(false);
        var retained = (await ReadFileAsync(path, tenantId, ct).ConfigureAwait(false)).ToList();
        var selected = new List<BondMarketObservationDto>(proposed.Length);
        var appended = 0;
        foreach (var observation in proposed)
        {
            var sameRevision = retained.SingleOrDefault(row => BondMarketDataRules.SourceRevisionKey(row.Input) == BondMarketDataRules.SourceRevisionKey(observation.Input));
            if (sameRevision is not null)
            {
                if (!Sha256Digest.FixedEquals(sameRevision.ContentSha256, observation.ContentSha256))
                    throw new InvalidOperationException("A source observation revision already exists with different content; retain a new source revision.");
                selected.Add(sameRevision);
                continue;
            }
            if (retained.Any(row => row.ObservationId == observation.ObservationId))
                throw new InvalidOperationException("The observation identity is already used by another source revision.");
            var previous = retained.Where(row => BondMarketDataRules.SourceKey(row.Input) == BondMarketDataRules.SourceKey(observation.Input))
                .MaxBy(row => row.Input.Revision);
            if (previous is not null && (previous.Input.Revision >= observation.Input.Revision
                || previous.Input.Kind != observation.Input.Kind || previous.Input.EconomicDate != observation.Input.EconomicDate
                || previous.Input.SecurityId != observation.Input.SecurityId || previous.Input.SeriesKey != observation.Input.SeriesKey
                || previous.RecordedAtUtc > observation.RecordedAtUtc))
                throw new InvalidOperationException("A source correction must advance its revision and preserve its series, security, category and economic date.");
            retained.Add(observation);
            selected.Add(observation);
            appended++;
        }
        // Validate the complete batch before the single durable publication; a rejected row writes nothing.
        if (appended > 0)
        {
            var rows = retained.ToArray();
            var envelope = new BondMarketStoreEnvelope(1, tenantId, RowsHash(rows), rows);
            await AtomicFileWriter.WriteAsync(path, JsonSerializer.Serialize(envelope, BondMarketStoreJsonContext.Default.BondMarketStoreEnvelope), ct).ConfigureAwait(false);
        }
        return new(selected, appended);
    }

    public Task<IReadOnlyList<BondMarketObservationDto>> ReadAsync(string tenantId, CancellationToken ct = default)
        => ReadFileAsync(TenantPath(tenantId), tenantId, ct);

    private string TenantPath(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (tenantId != tenantId.Trim())
            throw new ArgumentException("Tenant identity must be canonical.", nameof(tenantId));
        return Path.Combine(_directory, Sha256Digest.ComputeUtf8(tenantId) + ".json");
    }

    private static async Task<IReadOnlyList<BondMarketObservationDto>> ReadFileAsync(string path, string tenantId, CancellationToken ct)
    {
        if (!File.Exists(path))
            return [];
        // Atomic replace permits concurrent readers; sharing delete is required on Windows.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var envelope = await JsonSerializer.DeserializeAsync(stream, BondMarketStoreJsonContext.Default.BondMarketStoreEnvelope, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Retained bond market observations are unreadable.");
        if (envelope.SchemaVersion != 1 || envelope.TenantId != tenantId || envelope.Rows is null
            || !Sha256Digest.FixedEquals(RowsHash(envelope.Rows), envelope.ContentSha256))
            throw new InvalidDataException("Retained bond market observation partition or content hash is invalid.");
        foreach (var row in envelope.Rows)
            BondMarketDataRules.ValidateRetained(row);
        if (envelope.Rows.GroupBy(row => BondMarketDataRules.SourceRevisionKey(row.Input)).Any(group => group.Count() != 1)
            || envelope.Rows.Select(row => row.ObservationId).Distinct().Count() != envelope.Rows.Length)
            throw new InvalidDataException("Retained market observations contain conflicting source revision identities.");
        return envelope.Rows;
    }

    private static string RowsHash(BondMarketObservationDto[] rows)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(rows, BondMarketDataJsonContext.Default.BondMarketObservationDtoArray));

    private static async Task<FileStream> AcquireAsync(string path, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(30))
            { await Task.Delay(30, ct).ConfigureAwait(false); }
        }
    }
}

internal sealed record BondMarketStoreEnvelope(int SchemaVersion, string TenantId, string ContentSha256, BondMarketObservationDto[] Rows);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, UseStringEnumConverter = true)]
[JsonSerializable(typeof(BondMarketStoreEnvelope))]
internal partial class BondMarketStoreJsonContext : JsonSerializerContext;
