using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Archival;

namespace Meridian.Storage.FixedIncome;

/// <summary>Atomic, process-serialized transaction source revisions in exact tenant/company/account/book partitions.</summary>
public sealed class FileBondPerformanceTransactionStore(string dataRoot) : IBondPerformanceTransactionPopulationStore
{
    private readonly string _root = Path.Combine(Path.GetFullPath(dataRoot), "fixed-income", "performance-transactions");

    public async Task<BondPerformanceTransactionPopulationDto> AppendAsync(BondPerformanceTransactionPopulationDto population, CancellationToken ct = default)
    {
        BondPerformanceTransactionRules.Validate(population);
        Directory.CreateDirectory(_root);
        var path = PathFor(population.Scope);
        await using var lease = await Lease(path + ".lock", ct).ConfigureAwait(false);
        var existing = (await ReadAsync(population.Scope, ct).ConfigureAwait(false)).ToArray();
        var same = existing.SingleOrDefault(row => row.PopulationId == population.PopulationId);
        if (same is not null)
        {
            if (JsonSerializer.Serialize(same.Document, BondPerformanceTransactionsJsonContext.Default.BondPerformanceTransactionsDocument)
                != JsonSerializer.Serialize(population.Document, BondPerformanceTransactionsJsonContext.Default.BondPerformanceTransactionsDocument)
                || same.Evidence != population.Evidence)
                throw new InvalidOperationException("Retained transaction source identity already has different economics or review evidence.");
            return same;
        }
        var prior = existing.Where(row => row.Document.SourceDomain == population.Document.SourceDomain
                && row.Document.SourceEntityId == population.Document.SourceEntityId)
            .OrderBy(row => row.Document.Revision).LastOrDefault();
        if (prior is not null && (prior.Document.Revision >= population.Document.Revision
            || prior.Document.PeriodStart != population.Document.PeriodStart || prior.Document.PeriodEnd != population.Document.PeriodEnd))
            throw new InvalidOperationException("A revised transaction source must advance its revision without moving its economic coverage window.");
        var rows = existing.Append(population).ToArray();
        var envelope = new BondPerformanceTransactionEnvelope(1, Hash(rows), rows);
        await AtomicFileWriter.WriteAsync(path, JsonSerializer.Serialize(envelope,
            BondPerformanceTransactionStoreJsonContext.Default.BondPerformanceTransactionEnvelope), ct).ConfigureAwait(false);
        return population;
    }

    public async Task<IReadOnlyList<BondPerformanceTransactionPopulationDto>> ReadAsync(AccountCashForecastScopeDto scope, CancellationToken ct = default)
    {
        var path = PathFor(scope);
        if (!File.Exists(path))
            return [];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var envelope = await JsonSerializer.DeserializeAsync(stream,
            BondPerformanceTransactionStoreJsonContext.Default.BondPerformanceTransactionEnvelope, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Retained transaction population file is unreadable.");
        if (envelope.SchemaVersion != 1 || envelope.Rows is null || !Sha256Digest.FixedEquals(Hash(envelope.Rows), envelope.ContentSha256)
            || envelope.Rows.Any(row => row.Scope != scope) || envelope.Rows.Select(row => row.PopulationId).Distinct().Count() != envelope.Rows.Length)
            throw new InvalidDataException("Retained transaction population scope, checksum or identities are invalid.");
        foreach (var row in envelope.Rows)
            BondPerformanceTransactionRules.Validate(row);
        return envelope.Rows;
    }

    private string PathFor(AccountCashForecastScopeDto scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(scope.TenantId) || string.IsNullOrWhiteSpace(scope.CompanyId)
            || string.IsNullOrWhiteSpace(scope.AccountId) || string.IsNullOrWhiteSpace(scope.BookId))
            throw new ArgumentException("An exact transaction population scope is required.");
        return Path.Combine(_root, Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[] {
            scope.TenantId, scope.CompanyId, scope.AccountId, scope.BookId, scope.BaseCurrency })) + ".json");
    }

    private static string Hash(BondPerformanceTransactionPopulationDto[] rows)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(rows, BondPerformanceTransactionsJsonContext.Default.BondPerformanceTransactionPopulationDtoArray));

    private static async Task<FileStream> Lease(string path, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (started.Elapsed < TimeSpan.FromSeconds(30)) { await Task.Delay(30, ct).ConfigureAwait(false); }
        }
    }
}

internal sealed record BondPerformanceTransactionEnvelope(int SchemaVersion, string ContentSha256, BondPerformanceTransactionPopulationDto[] Rows);
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BondPerformanceTransactionEnvelope))]
internal partial class BondPerformanceTransactionStoreJsonContext : JsonSerializerContext;
