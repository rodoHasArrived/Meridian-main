using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Storage.Archival;

namespace Meridian.Execution.Events;

/// <summary>
/// Binds one exact trade-fill posting scope to the authoritative journal backend it posts to.
/// </summary>
/// <remarks>
/// The durable handoff store partitions by posting scope, not by backend, and it only replays
/// fills that are still pending. Switching <see cref="TradeFillLedgerStoreKind"/> for a scope
/// that already acknowledged fills would therefore leave those fills absent from the newly
/// selected journal and split one accounting scope across two stores. The binding is recorded
/// beside the scope's handoff data on first use and checked on every composition, so a switch
/// fails closed. A new book, period or aggregate (a new scope) can use either backend.
/// </remarks>
public static class TradeFillLedgerBackendBinding
{
    internal const string Schema = "meridian.trade-fill-ledger-backend.v1";
    internal const string FileName = "ledger-backend.json";

    /// <summary>
    /// Records <paramref name="store"/> for the scope on first use, or verifies that the scope was
    /// bound to the same backend. Must run before the scope's handoff store is opened, so existing
    /// handoff data reliably means the scope predates the binding.
    /// </summary>
    /// <exception cref="InvalidOperationException">The scope is bound to a different backend.</exception>
    /// <exception cref="InvalidDataException">The binding file is unreadable or belongs to another scope.</exception>
    public static void Ensure(TradeFillPostingStoreOptions options, TradeFillLedgerStoreKind store)
    {
        ArgumentNullException.ThrowIfNull(options);
        var directory = options.ScopeDirectory;
        var path = Path.Combine(directory, FileName);

        TradeFillLedgerBackendBindingRecord? bound = null;
        if (File.Exists(path))
        {
            try
            {
                bound = JsonSerializer.Deserialize(
                    File.ReadAllText(path),
                    TradeFillLedgerBackendBindingJsonContext.Default.TradeFillLedgerBackendBindingRecord);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Trade-fill ledger backend binding '{path}' is unreadable: {ex.Message}", ex);
            }

            if (bound is null || bound.Schema != Schema
                || !string.Equals(bound.PostingScope, options.PostingScope, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Trade-fill ledger backend binding '{path}' does not belong to scope '{options.PostingScope}'.");
            }
        }
        else if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            // Handoff data written before backends were selectable can only have posted to the
            // governed Postgres ledger, the sole backend that existed then.
            bound = new TradeFillLedgerBackendBindingRecord(Schema, options.PostingScope, TradeFillLedgerStoreKind.Postgres);
            Write(path, bound);
        }

        if (bound is null)
        {
            Write(path, new TradeFillLedgerBackendBindingRecord(Schema, options.PostingScope, store));
            return;
        }

        if (bound.Store != store)
        {
            throw new InvalidOperationException(
                $"Trade-fill posting scope '{options.PostingScope}' is bound to the {bound.Store} ledger store but " +
                $"{TradeFillLedgerPostingHostOptions.SectionKey}:Store is {store}. Fills already acknowledged by the " +
                $"{bound.Store} store would be missing from the {store} journal. Use a new book, period or aggregate " +
                "for the other store, or migrate the scope's journals explicitly.");
        }
    }

    private static void Write(string path, TradeFillLedgerBackendBindingRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFileWriter.Write(
            path,
            JsonSerializer.Serialize(record, TradeFillLedgerBackendBindingJsonContext.Default.TradeFillLedgerBackendBindingRecord));
    }
}

internal sealed record TradeFillLedgerBackendBindingRecord(
    string Schema,
    string PostingScope,
    TradeFillLedgerStoreKind Store);

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TradeFillLedgerBackendBindingRecord))]
internal sealed partial class TradeFillLedgerBackendBindingJsonContext : JsonSerializerContext;
