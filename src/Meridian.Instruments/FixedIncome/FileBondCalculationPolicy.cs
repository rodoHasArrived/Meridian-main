using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.Archival;

namespace Meridian.Instruments.FixedIncome;

/// <summary>Durable live-calculation cutover gate, separate from the cash-flow dependency graph.</summary>
public sealed class FileBondCalculationPolicy : IBondCalculationPolicy
{
    private readonly string _path;
    private readonly IBusinessDayCalendarSource? _calendars;
    private string EnabledMarkerPath => _path + ".enabled";
    private string HistoryPath => _path + ".history";

    public FileBondCalculationPolicy(string path, IBusinessDayCalendarSource? calendars = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _calendars = calendars;
    }

    public async Task<string?> GetBlockerAsync(SecurityDetailDto security, CancellationToken ct = default)
    {
        if (!string.Equals(security.AssetClass, "Bond", StringComparison.OrdinalIgnoreCase))
            return null;
        BondMigrationCutoverReceiptDto? receipt;
        try
        {
            receipt = await ReadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return "Bond cutover state is unavailable; live calculation is blocked until its retained receipt is restored.";
        }
        if (receipt is null)
            return null;
        var blocked = receipt.ExplicitBlocks.FirstOrDefault(entry => entry.SecurityId == security.SecurityId);
        if (blocked is not null)
            return $"Bond is explicitly blocked by the reviewed cutover: {blocked.Reason}. Release requires a reviewed amendment against the current receipt hash and a fresh complete inventory.";
        var resolution = ScheduleSpecFactory.Resolve(security, StructuredCashFlowTermsResolver.Resolve(security), _calendars);
        return DeclarationBlocker(resolution);
    }

    internal static string? DeclarationBlocker(DeclaredScheduleResolution resolution)
        => resolution.Status switch
        {
            DeclaredScheduleStatus.Declared when resolution.Conventions.CouponRateUnit is not ("Percent" or "Fraction")
                || resolution.Conventions.CouponCashBasis is not ("Level" or "DayCount")
                => "Bond cutover requires explicitly reviewed couponRateUnit and couponCashBasis; legacy inference is retired.",
            DeclaredScheduleStatus.Declared => null,
            DeclaredScheduleStatus.Refused => $"Bond cutover requires supported declared conventions: {resolution.Refusal?.Message}",
            _ => "Bond cutover requires reviewed explicit schedule conventions; legacy live calculation is retired."
        };

    public async Task<BondMigrationCutoverReceiptDto?> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path))
        {
            if (File.Exists(EnabledMarkerPath))
                throw new InvalidDataException("Retained bond cutover receipt is missing after cutover was enabled.");
            return null;
        }
        var json = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
        var hash = Sha256Digest.Compute(System.Text.Encoding.UTF8.GetBytes(json));
        if (!File.Exists(EnabledMarkerPath)
            || !Sha256Digest.FixedEquals((await File.ReadAllTextAsync(EnabledMarkerPath, ct).ConfigureAwait(false)).Trim(), hash))
            throw new InvalidDataException("Bond cutover receipt does not match the retained head digest.");
        var archive = Path.Combine(HistoryPath, hash + ".json");
        if (!File.Exists(archive) || await File.ReadAllTextAsync(archive, ct).ConfigureAwait(false) != json)
            throw new InvalidDataException("Bond cutover receipt does not match its immutable retained history.");
        var receipt = JsonSerializer.Deserialize(json, BondMigrationJsonContext.Default.BondMigrationCutoverReceiptDto)
            ?? throw new InvalidDataException("Bond cutover receipt is empty.");
        if (receipt.ModelVersion != BondMigrationService.ModelVersion || string.IsNullOrWhiteSpace(receipt.Reviewer)
            || string.IsNullOrWhiteSpace(receipt.InventorySha256) || receipt.ExplicitBlocks is null)
            throw new InvalidDataException("Bond cutover receipt is incomplete or unsupported.");
        var previousHash = receipt.PreviousReceiptSha256;
        var seen = new HashSet<string>(StringComparer.Ordinal) { hash };
        while (previousHash is not null)
        {
            if (!Sha256Digest.IsCanonical(previousHash) || !seen.Add(previousHash) || seen.Count > 10_000)
                throw new InvalidDataException("Bond cutover history link is invalid or exceeds supported history bounds.");
            var previousPath = Path.Combine(HistoryPath, previousHash + ".json");
            var previousJson = await File.ReadAllTextAsync(previousPath, ct).ConfigureAwait(false);
            if (!Sha256Digest.FixedEquals(previousHash, Sha256Digest.ComputeUtf8(previousJson)))
                throw new InvalidDataException("Bond cutover history content does not match its retained hash.");
            var previous = JsonSerializer.Deserialize(previousJson, BondMigrationJsonContext.Default.BondMigrationCutoverReceiptDto)
                ?? throw new InvalidDataException("Bond cutover history receipt is empty.");
            if (previous.ModelVersion != BondMigrationService.ModelVersion)
                throw new InvalidDataException("Bond cutover history model is unsupported.");
            previousHash = previous.PreviousReceiptSha256;
        }
        return receipt;
    }

    internal static string ReceiptHash(BondMigrationCutoverReceiptDto receipt)
        => Sha256Digest.Compute(System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(receipt, BondMigrationJsonContext.Default.BondMigrationCutoverReceiptDto)));

    internal async Task WriteAsync(BondMigrationCutoverReceiptDto receipt, string? expectedPreviousHash, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Serializes cooperative writers across service instances/processes sharing this path.
        await using var writerLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
        var json = JsonSerializer.Serialize(receipt, BondMigrationJsonContext.Default.BondMigrationCutoverReceiptDto);
        var previous = await ReadAsync(ct).ConfigureAwait(false);
        if (previous is null ? expectedPreviousHash is not null
            : expectedPreviousHash is null || !Sha256Digest.FixedEquals(expectedPreviousHash, ReceiptHash(previous)))
            throw new InvalidOperationException("Bond cutover receipt changed; review an amendment against its current receipt hash.");
        // Each receipt's exact bytes are retained before advancing the head. The head digest advances
        // first, then the current receipt is atomically replaced. An interruption between them blocks
        // live calculations across restarts until the reviewed receipt is restored from its archive.
        // Keeping this independent head also detects rollback to an older, otherwise valid receipt.
        Directory.CreateDirectory(HistoryPath);
        var archive = Path.Combine(HistoryPath, ReceiptHash(receipt) + ".json");
        if (File.Exists(archive))
        {
            if (await File.ReadAllTextAsync(archive, ct).ConfigureAwait(false) != json)
                throw new InvalidDataException("Retained cutover history does not match its content hash.");
        }
        else
            await AtomicFileWriter.WriteAsync(archive, json, ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(EnabledMarkerPath, ReceiptHash(receipt), ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(_path, json, ct).ConfigureAwait(false);
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BondMigrationCutoverReceiptDto))]
[JsonSerializable(typeof(BondMigrationPreviewDto))]
[JsonSerializable(typeof(BondMigrationInventoryRowDto[]))]
internal sealed partial class BondMigrationJsonContext : JsonSerializerContext;
