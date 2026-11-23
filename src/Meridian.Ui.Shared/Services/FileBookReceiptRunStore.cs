using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Core.IO;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Services;

public interface IBookReceiptRunStore
{
    ValueTask<IAsyncDisposable> AcquireAsync(BookReceiptScopeDto scope, CancellationToken ct = default);
    Task<IReadOnlyList<BookReceiptRunDto>> ReadAsync(BookReceiptScopeDto scope, CancellationToken ct = default);
    Task SaveAsync(BookReceiptRunDto run, CancellationToken ct = default);
}

/// <summary>Append-only observations, integrity checked on read and serialized under an exact-book process lease.</summary>
public sealed class FileBookReceiptRunStore(string dataRoot) : IBookReceiptRunStore
{
    private readonly RootedPathGuard _guard = new(dataRoot);

    public async ValueTask<IAsyncDisposable> AcquireAsync(BookReceiptScopeDto scope, CancellationToken ct = default)
    {
        var directory = DirectoryFor(scope);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "capture.lock");
        _guard.EnsurePath(path);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<BookReceiptRunDto>> ReadAsync(BookReceiptScopeDto scope, CancellationToken ct = default)
    {
        var directory = DirectoryFor(scope);
        if (!Directory.Exists(directory))
            return [];
        var result = new List<BookReceiptRunDto>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            _guard.EnsurePath(path);
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize(bytes, BookReceiptJsonContext.Default.BookReceiptRunEnvelope)
                ?? throw new InvalidDataException("A retained receipt run is unreadable.");
            var payload = JsonSerializer.SerializeToUtf8Bytes(envelope.Run, BookReceiptJsonContext.Default.BookReceiptRunDto);
            if (envelope.ContentHash != Sha256Digest.Compute(payload) || envelope.Run.Scope != scope ||
                Path.GetFileNameWithoutExtension(path) != envelope.Run.RunId)
                throw new InvalidDataException("A retained receipt run failed integrity or ownership verification.");
            result.Add(envelope.Run);
        }
        return result.OrderBy(static run => run.Sequence).ThenBy(static run => run.RunId, StringComparer.Ordinal).ToArray();
    }

    public async Task SaveAsync(BookReceiptRunDto run, CancellationToken ct = default)
    {
        var directory = DirectoryFor(run.Scope);
        if (run.RunId.Length != 64 || run.RunId.Any(static character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A receipt run requires a content-derived identifier.", nameof(run));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, run.RunId + ".json");
        _guard.EnsurePath(path);
        var payload = JsonSerializer.SerializeToUtf8Bytes(run, BookReceiptJsonContext.Default.BookReceiptRunDto);
        var envelope = new BookReceiptRunEnvelope(Sha256Digest.Compute(payload), run);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, BookReceiptJsonContext.Default.BookReceiptRunEnvelope);
        if (File.Exists(path))
        {
            var retained = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            if (!retained.AsSpan().SequenceEqual(bytes))
                throw new InvalidOperationException("Retained receipt runs are immutable.");
            return;
        }
        await AtomicFileWriter.WriteAsync(path, bytes, ct).ConfigureAwait(false);
    }

    private string DirectoryFor(BookReceiptScopeDto scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.CompanyId);
        if (scope.LedgerBookId == Guid.Empty)
            throw new ArgumentException("Receipt runs require an exact ledger book.", nameof(scope));
        var identity = JsonSerializer.Serialize(scope, BookReceiptJsonContext.Default.BookReceiptScopeDto);
        return _guard.ResolvePath("book-receipts", Sha256Digest.ComputeUtf8(identity));
    }
}

internal sealed record BookReceiptRunEnvelope(string ContentHash, BookReceiptRunDto Run);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BookReceiptRunEnvelope))]
[JsonSerializable(typeof(BookReceiptRunDto))]
[JsonSerializable(typeof(BookReceiptScopeDto))]
[JsonSerializable(typeof(BookReceiptSourceSnapshotDto))]
[JsonSerializable(typeof(BookReceiptExpectationDto))]
[JsonSerializable(typeof(BookReceiptDto))]
[JsonSerializable(typeof(BookReceiptPostingDto))]
[JsonSerializable(typeof(BookReceiptQueueItemDto[]))]
internal sealed partial class BookReceiptJsonContext : JsonSerializerContext;
