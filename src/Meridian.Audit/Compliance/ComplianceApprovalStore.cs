using System.Text.Json;
using Meridian.Storage;
using Meridian.Storage.Archival;

namespace Meridian.Audit.Compliance;

/// <summary>
/// Resolves approval evidence from an authority-owned store. A request-supplied identifier is only
/// a lookup key; policy evaluation still verifies the stored action and object binding.
/// </summary>
public interface IComplianceApprovalResolver
{
    ComplianceApprovalRequestRecord? Resolve(string approvalRequestId);
}

public interface IComplianceApprovalStore : IComplianceApprovalResolver
{
    ComplianceApprovalRequestRecord CreateRequest(
        ActorContext requester,
        ComplianceApprovalRequestCommand command);

    ComplianceApprovalRequestRecord RecordDecision(
        string approvalRequestId,
        ActorContext approver,
        bool approved);
}

/// <summary>
/// Durable, single-writer approval authority for compliance step-up decisions. Mutations are
/// persisted atomically before becoming visible to policy evaluation.
/// </summary>
public sealed class FileComplianceApprovalStore : IComplianceApprovalStore
{
    private static readonly TimeSpan DefaultApprovalLifetime = TimeSpan.FromHours(24);

    private readonly object _gate = new();
    private readonly string _path;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _approvalLifetime;
    private Dictionary<string, ComplianceApprovalRequestRecord> _requests;

    public FileComplianceApprovalStore(StorageOptions storageOptions)
        : this(Path.Combine(
            (storageOptions ?? throw new ArgumentNullException(nameof(storageOptions))).RootPath,
            "compliance",
            "approvals",
            "approval-requests.json"))
    {
    }

    public FileComplianceApprovalStore(
        string path,
        TimeProvider? timeProvider = null,
        TimeSpan? approvalLifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _approvalLifetime = approvalLifetime ?? DefaultApprovalLifetime;
        if (_approvalLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(approvalLifetime),
                "Approval lifetime must be greater than zero.");
        }

        _requests = Load();
    }

    public ComplianceApprovalRequestRecord CreateRequest(
        ActorContext requester,
        ComplianceApprovalRequestCommand command)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(command);
        ValidateAuthenticatedActor(requester.ActorId, nameof(requester));
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ObjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);

        var now = _timeProvider.GetUtcNow();
        var record = new ComplianceApprovalRequestRecord(
            ApprovalRequestId: $"compliance-approval-{Guid.NewGuid():N}",
            Action: command.Action,
            ObjectType: command.ObjectType.Trim(),
            ObjectId: command.ObjectId.Trim(),
            EntityId: NormalizeOptional(command.EntityId),
            CorrelationId: command.CorrelationId.Trim(),
            RequestedByActorId: requester.ActorId.Trim(),
            RequestedAtUtc: now,
            ExpiresAtUtc: now.Add(_approvalLifetime),
            Decisions: []);

        lock (_gate)
        {
            var next = new Dictionary<string, ComplianceApprovalRequestRecord>(
                _requests,
                StringComparer.Ordinal)
            {
                [record.ApprovalRequestId] = record
            };
            Persist(next);
            _requests = next;
            return Copy(record);
        }
    }

    public ComplianceApprovalRequestRecord RecordDecision(
        string approvalRequestId,
        ActorContext approver,
        bool approved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalRequestId);
        ArgumentNullException.ThrowIfNull(approver);
        ValidateAuthenticatedActor(approver.ActorId, nameof(approver));

        lock (_gate)
        {
            if (!_requests.TryGetValue(approvalRequestId.Trim(), out var request))
            {
                throw new KeyNotFoundException(
                    $"Compliance approval request '{approvalRequestId}' was not found.");
            }

            var now = _timeProvider.GetUtcNow();
            if (request.ExpiresAtUtc <= now)
            {
                throw new InvalidOperationException(
                    $"Compliance approval request '{approvalRequestId}' has expired.");
            }

            if (string.Equals(
                    request.RequestedByActorId,
                    approver.ActorId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The requesting actor cannot approve their own compliance action.");
            }

            var existing = request.Decisions.FirstOrDefault(decision =>
                string.Equals(
                    decision.ApprovedByActorId,
                    approver.ActorId,
                    StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (existing.Approved != approved)
                {
                    throw new InvalidOperationException(
                        "An actor cannot replace an authoritative compliance decision in place.");
                }

                return Copy(request);
            }

            var decision = new ComplianceApprovalDecisionRecord(
                ApprovalId: $"compliance-decision-{Guid.NewGuid():N}",
                ApprovedByActorId: approver.ActorId.Trim(),
                Approved: approved,
                DecidedAtUtc: now);
            var updated = request with
            {
                Decisions = request.Decisions
                    .Append(decision)
                    .OrderBy(item => item.DecidedAtUtc)
                    .ThenBy(item => item.ApprovalId, StringComparer.Ordinal)
                    .ToArray()
            };
            var next = new Dictionary<string, ComplianceApprovalRequestRecord>(
                _requests,
                StringComparer.Ordinal)
            {
                [request.ApprovalRequestId] = updated
            };
            Persist(next);
            _requests = next;
            return Copy(updated);
        }
    }

    public ComplianceApprovalRequestRecord? Resolve(string approvalRequestId)
    {
        if (string.IsNullOrWhiteSpace(approvalRequestId))
        {
            return null;
        }

        lock (_gate)
        {
            return _requests.TryGetValue(approvalRequestId.Trim(), out var request)
                ? Copy(request)
                : null;
        }
    }

    private Dictionary<string, ComplianceApprovalRequestRecord> Load()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, ComplianceApprovalRequestRecord>(StringComparer.Ordinal);
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize(
                File.ReadAllText(_path),
                ComplianceApprovalJsonContext.Default.ComplianceApprovalSnapshot)
                ?? throw new InvalidDataException("The compliance approval snapshot is empty.");
            if (snapshot.Requests.Any(request =>
                    string.IsNullOrWhiteSpace(request.ApprovalRequestId) ||
                    string.IsNullOrWhiteSpace(request.RequestedByActorId) ||
                    string.IsNullOrWhiteSpace(request.ObjectType) ||
                    string.IsNullOrWhiteSpace(request.ObjectId)))
            {
                throw new InvalidDataException(
                    "The compliance approval snapshot contains an unbound record.");
            }

            return snapshot.Requests.ToDictionary(
                request => request.ApprovalRequestId,
                Copy,
                StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException(
                $"The compliance approval snapshot at '{_path}' is corrupt; step-up policy evaluation is blocked.",
                ex);
        }
    }

    private void Persist(IReadOnlyDictionary<string, ComplianceApprovalRequestRecord> requests)
    {
        var snapshot = new ComplianceApprovalSnapshot(
            requests.Values
                .OrderBy(request => request.RequestedAtUtc)
                .ThenBy(request => request.ApprovalRequestId, StringComparer.Ordinal)
                .Select(Copy)
                .ToArray());
        var json = JsonSerializer.Serialize(
            snapshot,
            ComplianceApprovalJsonContext.Default.ComplianceApprovalSnapshot);
        AtomicFileWriter.Write(_path, json, CancellationToken.None);
    }

    private static ComplianceApprovalRequestRecord Copy(ComplianceApprovalRequestRecord request)
        => request with { Decisions = request.Decisions.ToArray() };

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateAuthenticatedActor(string actorId, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId, parameterName);
        if (string.Equals(actorId.Trim(), "anonymous", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "Compliance approval evidence requires an authenticated actor.");
        }
    }
}
