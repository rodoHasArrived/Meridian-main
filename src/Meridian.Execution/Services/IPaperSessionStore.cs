using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;

namespace Meridian.Execution.Services;

/// <summary>
/// Serialisable snapshot of a paper session's metadata.
/// Written atomically to <c>session.json</c> on every lifecycle change.
/// </summary>
public sealed record PersistedSessionRecord(
    string SessionId,
    string StrategyId,
    string? StrategyName,
    decimal InitialCash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    bool IsActive,
    List<string> Symbols);

/// <summary>
/// Lightweight serialisation DTO for a <see cref="Meridian.Ledger.LedgerAccount"/>.
/// </summary>
public sealed record PersistedLedgerAccountDto(
    string Name,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId);

/// <summary>
/// Lightweight serialisation DTO for a single debit/credit line in a journal entry.
/// </summary>
public sealed record PersistedLedgerLineDto(
    Guid EntryId,
    Guid JournalEntryId,
    DateTimeOffset Timestamp,
    PersistedLedgerAccountDto Account,
    decimal Debit,
    decimal Credit,
    string Description);

/// <summary>
/// Lightweight serialisation DTO for a complete journal entry plus its optional audit metadata.
/// Written one per line to <c>ledger.jsonl</c>.
/// </summary>
public sealed record PersistedJournalEntryDto(
    Guid JournalEntryId,
    DateTimeOffset Timestamp,
    string Description,
    IReadOnlyList<PersistedLedgerLineDto> Lines,
    string? ActivityType = null,
    string? Symbol = null,
    Guid? SecurityId = null,
    Guid? OrderId = null,
    string? LedgerView = null,
    string? StrategyId = null);

/// <summary>
/// Versioned durable envelope for one paper-session fill.  <see cref="FillId"/> is the
/// idempotency key and <see cref="CanonicalHash"/> detects a conflicting reuse of that key.
/// <see cref="IsApplied"/> is reconstructed from the store's durable apply acknowledgements;
/// it is not part of the immutable fill claim itself.
/// </summary>
public sealed record PaperSessionFillRecord(
    int SchemaVersion,
    Guid FillId,
    string CanonicalHash,
    ExecutionReport Fill,
    DateTimeOffset RecordedAt,
    bool IsApplied = false)
{
    /// <summary>Current on-disk envelope schema.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Creates a validated current-version envelope for <paramref name="fill"/>.</summary>
    public static PaperSessionFillRecord Create(
        Guid fillId,
        ExecutionReport fill,
        DateTimeOffset? recordedAt = null,
        bool isApplied = false)
    {
        if (fillId == Guid.Empty)
            throw new ArgumentException("A paper-session fill requires a non-empty FillId.", nameof(fillId));

        ArgumentNullException.ThrowIfNull(fill);
        var expectedFillId = ComputeCanonicalFillId(fill);
        if (fillId != expectedFillId)
        {
            throw new ArgumentException(
                $"FillId '{fillId:D}' does not match canonical FillId '{expectedFillId:D}'.",
                nameof(fillId));
        }

        return new PaperSessionFillRecord(
            CurrentSchemaVersion,
            fillId,
            ComputeCanonicalHash(fill),
            fill,
            recordedAt ?? DateTimeOffset.UtcNow,
            isApplied);
    }

    /// <summary>
    /// Creates an envelope whose FillId is derived only from stable execution-report content.
    /// Account scope and cumulative portfolio state are deliberately excluded so every producer
    /// uses the same paper-session idempotency key for the same fill.
    /// </summary>
    public static PaperSessionFillRecord CreateCanonical(
        ExecutionReport fill,
        DateTimeOffset? recordedAt = null,
        bool isApplied = false)
    {
        ArgumentNullException.ThrowIfNull(fill);
        return Create(ComputeCanonicalFillId(fill), fill, recordedAt, isApplied);
    }

    /// <summary>
    /// Computes the canonical, account-independent identity of an execution fill.
    /// The full payload hash remains a separate conflict detector so changed non-identity
    /// content cannot silently reuse this id.
    /// </summary>
    public static Guid ComputeCanonicalFillId(ExecutionReport fill)
    {
        ArgumentNullException.ThrowIfNull(fill);

        var canonicalIdentity = string.Join(
            "|",
            EncodeIdentityPart(fill.OrderId),
            EncodeIdentityPart(fill.ClientOrderId),
            EncodeIdentityPart(fill.GatewayOrderId),
            EncodeIdentityPart(fill.Symbol),
            ((int)fill.Side).ToString(CultureInfo.InvariantCulture),
            FormatDecimal(fill.FilledQuantity),
            fill.FillPrice is { } fillPrice ? FormatDecimal(fillPrice) : "-",
            FormatDecimal(fill.Commission ?? 0m),
            fill.Timestamp.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentity));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Computes the stable content hash used to distinguish retry from conflict.</summary>
    public static string ComputeCanonicalHash(ExecutionReport fill)
    {
        ArgumentNullException.ThrowIfNull(fill);
        var json = JsonSerializer.SerializeToUtf8Bytes(fill, ExecutionJsonContext.Default.ExecutionReport);
        return Convert.ToHexStringLower(SHA256.HashData(json));
    }

    /// <summary>Validates the envelope version, identity, and canonical content hash.</summary>
    public PaperSessionFillRecord Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported paper-session fill schema version {SchemaVersion}; expected {CurrentSchemaVersion}.");
        }

        if (FillId == Guid.Empty)
            throw new InvalidDataException("A persisted paper-session fill has an empty FillId.");
        if (string.IsNullOrWhiteSpace(CanonicalHash))
            throw new InvalidDataException($"Persisted paper-session fill '{FillId:D}' has no canonical hash.");

        var expectedHash = ComputeCanonicalHash(Fill);
        if (!string.Equals(CanonicalHash, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Persisted paper-session fill '{FillId:D}' failed canonical hash validation.");
        }

        var expectedFillId = ComputeCanonicalFillId(Fill);
        if (FillId != expectedFillId)
        {
            throw new InvalidDataException(
                $"Persisted paper-session fill '{FillId:D}' does not match canonical FillId '{expectedFillId:D}'.");
        }

        return this;
    }

    private static string EncodeIdentityPart(string? value)
        => value is null
            ? "-"
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string FormatDecimal(decimal value)
        => value.ToString("G29", CultureInfo.InvariantCulture);
}

/// <summary>Outcome of attempting to durably claim a paper-session FillId.</summary>
public enum PaperSessionFillAppendStatus : byte
{
    Added = 0,
    ExistingSame = 1,
    Conflict = 2
}

/// <summary>Idempotent durable-fill append result.</summary>
public sealed record PaperSessionFillAppendResult(
    PaperSessionFillAppendStatus Status,
    string? ExistingCanonicalHash = null);

/// <summary>
/// Storage abstraction for paper trading session persistence.
/// Implementations may store session data in-memory, on the local file system,
/// or in any other durable backend.
/// </summary>
public interface IPaperSessionStore
{
    /// <summary>Creates or replaces the session metadata record.</summary>
    Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default);

    /// <summary>Appends a single fill event to the session's immutable fill log.</summary>
    Task AppendFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default);

    /// <summary>
    /// Durably and atomically claims <paramref name="record"/> before the caller applies it to
    /// portfolio state. Implementations must serialize concurrent claims for one FillId and must
    /// not complete until the claim is crash-durable. The default fails closed so a legacy store
    /// cannot silently substitute a read-then-append sequence.
    /// </summary>
    Task<PaperSessionFillAppendResult> TryAppendFillAsync(
        string sessionId,
        PaperSessionFillRecord record,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            $"Paper-session store '{GetType().FullName}' does not implement the required atomic durable fill-claim contract.");

    /// <summary>
    /// Durably and idempotently marks a claimed fill as applied. Implementations must reject an
    /// unknown FillId or a canonical-hash mismatch and must not complete until the acknowledgement
    /// is crash-durable. The default fails closed instead of silently acknowledging nothing.
    /// </summary>
    Task MarkFillAppliedAsync(
        string sessionId,
        Guid fillId,
        string canonicalHash,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            $"Paper-session store '{GetType().FullName}' does not implement the required durable fill-acknowledgement contract.");

    /// <summary>Appends an order-state snapshot to the session's order history log.</summary>
    Task AppendOrderUpdateAsync(string sessionId, OrderState order, CancellationToken ct = default);

    /// <summary>
    /// Writes the complete ledger journal for <paramref name="sessionId"/> to a JSONL sidecar
    /// file (<c>ledger.jsonl</c>).  Replaces any previously-written ledger for the same session.
    /// </summary>
    Task SaveLedgerJournalAsync(
        string sessionId,
        IReadOnlyList<PersistedJournalEntryDto> entries,
        CancellationToken ct = default);

    /// <summary>Returns metadata records for every persisted session.</summary>
    Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(CancellationToken ct = default);

    /// <summary>Returns all recorded fill events for <paramref name="sessionId"/>, in order.</summary>
    Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Returns versioned fill records including durable apply state.  The default adapter treats
    /// legacy raw fills as already acknowledged while deriving their account-independent canonical
    /// identity from the retained report content. New writes still require explicit atomic claim
    /// and acknowledgement implementations above.
    /// </summary>
    async Task<IReadOnlyList<PaperSessionFillRecord>> LoadFillRecordsAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        var fills = await LoadFillsAsync(sessionId, ct).ConfigureAwait(false);
        return fills
            .Select(fill => PaperSessionFillRecord.CreateCanonical(
                fill,
                fill.Timestamp,
                isApplied: true))
            .ToArray();
    }

    /// <summary>Returns the order-state history for <paramref name="sessionId"/>, in order.</summary>
    Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Returns the persisted ledger journal entries for <paramref name="sessionId"/>.
    /// Returns an empty list when no ledger has been saved for this session.
    /// </summary>
    Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(
        string sessionId,
        CancellationToken ct = default);
}
