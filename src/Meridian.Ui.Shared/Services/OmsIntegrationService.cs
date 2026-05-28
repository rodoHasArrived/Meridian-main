using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Ui.Shared.Services;

public sealed class OmsIntegrationService
{
    private const int MaxTrackedMessages = 10_000;
    private const int MaxAuditEntries = 20_000;

    private readonly ConcurrentDictionary<string, OmsInboundMessage> _messages = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<OmsIntegrationAuditEntry> _audit = new();

    public OmsIngestionResult Ingest(OmsInboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var dedupKey = string.IsNullOrWhiteSpace(message.DeduplicationKey) ? ComputeKey(message) : message.DeduplicationKey.Trim();
        var normalized = message with { DeduplicationKey = dedupKey };
        if (_messages.TryAdd(dedupKey, normalized))
        {
            TrimMessagesIfRequired();
            EnqueueAudit(new OmsIntegrationAuditEntry(DateTimeOffset.UtcNow, dedupKey, "ingest", message.SourceSystem, message.CorrelationId));
            return new OmsIngestionResult(dedupKey, false, "Accepted.");
        }

        EnqueueAudit(new OmsIntegrationAuditEntry(DateTimeOffset.UtcNow, dedupKey, "replay", message.SourceSystem, message.CorrelationId));
        return new OmsIngestionResult(dedupKey, true, "Replay-safe duplicate ignored.");
    }

    public IReadOnlyCollection<OmsInboundMessage> Snapshot() => _messages.Values.ToArray();
    public IReadOnlyCollection<OmsIntegrationAuditEntry> AuditTrail(int take = 200) => _audit.Reverse().Take(Math.Max(1, take)).ToArray();

    public OmsSyncConflictResult ResolveSyncConflict(OmsSyncRequest request)
    {
        var winner = request.PullRecord.UpdatedAtUtc >= request.PushRecord.UpdatedAtUtc ? request.PullRecord : request.PushRecord;
        var mode = request.Mode.Equals("push", StringComparison.OrdinalIgnoreCase) ? "push" : "pull";
        EnqueueAudit(new OmsIntegrationAuditEntry(DateTimeOffset.UtcNow, request.EntityId, "sync-resolve", "excel-bridge", request.CorrelationId));
        return new OmsSyncConflictResult(mode, winner, "timestamp-precedence");
    }

    private void EnqueueAudit(OmsIntegrationAuditEntry entry)
    {
        _audit.Enqueue(entry);
        while (_audit.Count > MaxAuditEntries && _audit.TryDequeue(out _))
        {
        }
    }

    private void TrimMessagesIfRequired()
    {
        if (_messages.Count <= MaxTrackedMessages)
        {
            return;
        }

        foreach (var key in _messages.Keys)
        {
            if (_messages.Count <= MaxTrackedMessages)
            {
                break;
            }

            _messages.TryRemove(key, out _);
        }
    }

    private static string ComputeKey(OmsInboundMessage msg)
    {
        var raw = $"{msg.SourceSystem}|{msg.ExternalOrderId}|{msg.EventType}|{msg.EventTimestampUtc:O}|{msg.PayloadHash}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}

public sealed record OmsInboundMessage(
    string SourceSystem,
    string ExternalOrderId,
    string EventType,
    DateTimeOffset EventTimestampUtc,
    string PayloadHash,
    string? DeduplicationKey,
    string CorrelationId);

public sealed record OmsOutboundContract(
    string MeridianOrderId,
    string ExternalOrderId,
    string Status,
    DateTimeOffset UpdatedAtUtc,
    string Version);

public sealed record OmsIngestionResult(string DeduplicationKey, bool ReplayDetected, string Detail);

public sealed record OmsAdapterDiagnostics(string Adapter, string Channel, int RetryCount, string LastStatus, DateTimeOffset CheckedAtUtc);

public sealed record OmsSyncRecord(string EntityId, DateTimeOffset UpdatedAtUtc, IReadOnlyDictionary<string, string> Fields);

public sealed record OmsSyncRequest(string Mode, string EntityId, string CorrelationId, OmsSyncRecord PullRecord, OmsSyncRecord PushRecord);

public sealed record OmsSyncConflictResult(string Mode, OmsSyncRecord Winner, string Policy);

public sealed record OmsIntegrationAuditEntry(DateTimeOffset TimestampUtc, string ScopeId, string Action, string Actor, string CorrelationId);
