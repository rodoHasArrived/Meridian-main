using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Meridian.Ui.Shared.Contracts.Integrations;

using Meridian.Contracts.Integrity;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Compatibility facade for older tests and hosts. New OMS/EMS API handling lives behind
/// <see cref="IOmsIntegrationApiHandler"/> and is registered from Meridian.Ui.Services.
/// </summary>
public sealed class OmsIntegrationService : IOmsIntegrationApiHandler
{
    private readonly InMemoryOmsIntegrationApiHandler _inner = new();

    public OmsIngestionResult Ingest(OmsInboundMessage message, OmsRequestSignatureInput? signature = null)
        => _inner.Ingest(message, signature);

    public IReadOnlyCollection<OmsInboundMessage> Snapshot() => _inner.Snapshot();

    public IReadOnlyCollection<OmsAdapterDiagnostics> AdapterDiagnostics() => _inner.AdapterDiagnostics();

    public OmsSyncConflictResult ResolveSyncConflict(OmsSyncRequest request, OmsRequestSignatureInput? signature = null)
        => _inner.ResolveSyncConflict(request, signature);

    public IReadOnlyCollection<OmsIntegrationAuditEntry> AuditTrail(int take = 200) => _inner.AuditTrail(take);

    public OmsRequestSignatureResult VerifyRequestSignature(OmsRequestSignatureInput signature)
        => _inner.VerifyRequestSignature(signature);

    public OmsKeyRotationResult RotateSigningKey(OmsKeyRotationRequest request) => _inner.RotateSigningKey(request);
}
internal sealed class InMemoryOmsIntegrationApiHandler : IOmsIntegrationApiHandler
{
    private const int MaxTrackedMessages = 10_000;
    private const int MaxAuditEntries = 20_000;

    private static readonly OmsRetryPolicy FixRetryPolicy = new(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), 2.0, true);
    private static readonly OmsRetryPolicy FileTransferRetryPolicy = new(7, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5), 2.0, true);
    private static readonly OmsRetryPolicy ExcelRetryPolicy = new(3, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15), 1.5, false);

    private readonly ConcurrentDictionary<string, OmsInboundMessage> _messages = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<OmsIntegrationAuditEntry> _audit = new();
    private readonly ConcurrentDictionary<string, byte[]> _signingKeys = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public InMemoryOmsIntegrationApiHandler(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _signingKeys.TryAdd("default", Encoding.UTF8.GetBytes("meridian-local-integration-signing-key"));
    }

    public OmsIngestionResult Ingest(OmsInboundMessage message, OmsRequestSignatureInput? signature = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateSignatureIfPresent(signature);

        var dedupKey = string.IsNullOrWhiteSpace(message.DeduplicationKey) ? ComputeKey(message) : message.DeduplicationKey.Trim();
        var normalized = message with { DeduplicationKey = dedupKey };
        var signatureKeyId = signature?.KeyId;

        if (_messages.TryAdd(dedupKey, normalized))
        {
            TrimMessagesIfRequired();
            EnqueueAudit(new OmsIntegrationAuditEntry(
                _timeProvider.GetUtcNow(),
                dedupKey,
                "ingest.accepted",
                message.SourceSystem,
                message.CorrelationId,
                OmsIntegrationScope.Write,
                signatureKeyId,
                "Inbound OMS/EMS event accepted for replay-safe processing."));
            return new OmsIngestionResult(dedupKey, false, "Accepted.");
        }

        EnqueueAudit(new OmsIntegrationAuditEntry(
            _timeProvider.GetUtcNow(),
            dedupKey,
            "ingest.replay-ignored",
            message.SourceSystem,
            message.CorrelationId,
            OmsIntegrationScope.Write,
            signatureKeyId,
            "Duplicate deduplication key detected; existing canonical event was preserved."));
        return new OmsIngestionResult(dedupKey, true, "Replay-safe duplicate ignored.");
    }

    public IReadOnlyCollection<OmsInboundMessage> Snapshot() => _messages.Values.OrderBy(m => m.EventTimestampUtc).ToArray();

    public IReadOnlyCollection<OmsAdapterDiagnostics> AdapterDiagnostics()
    {
        var now = _timeProvider.GetUtcNow();
        return
        [
            new("fix", "tcp", 0, "healthy", now, new OmsAdapterBoundary(
                OmsAdapterKind.Fix,
                "fix",
                "FIX 4.x session boundary",
                OmsIntegrationDirection.Inbound,
                FixRetryPolicy,
                true,
                new HashSet<OmsIntegrationScope> { OmsIntegrationScope.Write, OmsIntegrationScope.Diagnostics })),
            new("sftp", "file-transfer", 1, "retrying", now, new OmsAdapterBoundary(
                OmsAdapterKind.Sftp,
                "sftp",
                "SFTP pull/drop boundary",
                OmsIntegrationDirection.Inbound,
                FileTransferRetryPolicy,
                true,
                new HashSet<OmsIntegrationScope> { OmsIntegrationScope.Write, OmsIntegrationScope.Diagnostics }),
                "Last poll failed; retry scheduled under exponential backoff.",
                now.Add(FileTransferRetryPolicy.InitialDelay)),
            new("file-drop", "local", 0, "healthy", now, new OmsAdapterBoundary(
                OmsAdapterKind.FileDrop,
                "file-drop",
                "Local file-drop watcher boundary",
                OmsIntegrationDirection.Inbound,
                FileTransferRetryPolicy,
                false,
                new HashSet<OmsIntegrationScope> { OmsIntegrationScope.Write, OmsIntegrationScope.Diagnostics })),
            new("excel-bridge", "workbook-sync", 0, "healthy", now, new OmsAdapterBoundary(
                OmsAdapterKind.ExcelBridge,
                "excel-bridge",
                "Excel pull/push synchronization boundary",
                OmsIntegrationDirection.Outbound,
                ExcelRetryPolicy,
                true,
                new HashSet<OmsIntegrationScope> { OmsIntegrationScope.Read, OmsIntegrationScope.Write }))
        ];
    }

    public OmsSyncConflictResult ResolveSyncConflict(OmsSyncRequest request, OmsRequestSignatureInput? signature = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSignatureIfPresent(signature);

        var mode = request.Mode.Equals("push", StringComparison.OrdinalIgnoreCase) ? OmsSyncMode.Push : OmsSyncMode.Pull;
        var winner = request.PullRecord.UpdatedAtUtc >= request.PushRecord.UpdatedAtUtc ? request.PullRecord : request.PushRecord;
        var reason = request.PullRecord.UpdatedAtUtc == request.PushRecord.UpdatedAtUtc
            ? "Timestamps tied; pull snapshot is canonical by deterministic tie-break."
            : "Newest UpdatedAtUtc wins across pull/push records.";

        EnqueueAudit(new OmsIntegrationAuditEntry(
            _timeProvider.GetUtcNow(),
            request.EntityId,
            "excel.sync-conflict-resolved",
            "excel-bridge",
            request.CorrelationId,
            OmsIntegrationScope.Write,
            signature?.KeyId,
            reason));

        return new OmsSyncConflictResult(mode.ToString().ToLowerInvariant(), winner, "timestamp-precedence", true, reason);
    }

    public IReadOnlyCollection<OmsIntegrationAuditEntry> AuditTrail(int take = 200)
        => _audit.Reverse().Take(Math.Max(1, take)).ToArray();

    public OmsRequestSignatureResult VerifyRequestSignature(OmsRequestSignatureInput signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        if (string.IsNullOrWhiteSpace(signature.KeyId) || string.IsNullOrWhiteSpace(signature.Signature))
        {
            return new OmsRequestSignatureResult(false, "Missing key id or signature.", signature.KeyId);
        }

        if (!_signingKeys.TryGetValue(signature.KeyId, out var key))
        {
            return new OmsRequestSignatureResult(false, "Unknown signing key.", signature.KeyId);
        }

        var expected = ComputeSignature(key, signature.TimestampUtc, signature.CanonicalPayload);
        var isValid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature.Signature));

        return new OmsRequestSignatureResult(isValid, isValid ? "Signature verified." : "Signature mismatch.", signature.KeyId);
    }

    public OmsKeyRotationResult RotateSigningKey(OmsKeyRotationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var material = Sha256Digest.ComputeBytesUtf8($"{request.KeyId}|{request.CorrelationId}|{request.EffectiveAtUtc:O}");
        _signingKeys.AddOrUpdate(request.KeyId, material, (_, _) => material);
        EnqueueAudit(new OmsIntegrationAuditEntry(
            _timeProvider.GetUtcNow(),
            request.KeyId,
            "auth.signing-key-rotated",
            request.RequestedBy,
            request.CorrelationId,
            OmsIntegrationScope.Admin,
            request.KeyId,
            "Signing key rotation hook completed."));
        return new OmsKeyRotationResult(request.KeyId, "rotated", request.EffectiveAtUtc, request.CorrelationId);
    }

    private void ValidateSignatureIfPresent(OmsRequestSignatureInput? signature)
    {
        if (signature is null)
        {
            return;
        }

        var result = VerifyRequestSignature(signature);
        if (!result.IsValid)
        {
            EnqueueAudit(new OmsIntegrationAuditEntry(
                _timeProvider.GetUtcNow(),
                signature.KeyId,
                "auth.signature-rejected",
                "request-signing",
                string.Empty,
                OmsIntegrationScope.Write,
                signature.KeyId,
                result.Detail));
            throw new InvalidOperationException(result.Detail);
        }
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
        var hash = Sha256Digest.ComputeBytesUtf8(raw);
        return Convert.ToHexString(hash);
    }

    public static string ComputeSignature(byte[] key, string timestampUtc, string canonicalPayload)
    {
        using var hmac = new HMACSHA256(key);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestampUtc}\n{canonicalPayload}"));
        return Convert.ToHexString(bytes);
    }
}
