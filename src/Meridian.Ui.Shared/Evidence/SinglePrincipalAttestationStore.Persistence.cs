using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Core.IO;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class SinglePrincipalAttestationStore
{
    private sealed record CompletedCommand(Guid CommandId, string Fingerprint, JsonElement Result);
    private sealed record Snapshot(
        int SchemaVersion,
        SinglePrincipalAttestationScopeDto Scope,
        SinglePrincipalDeclarationDto[] Declarations,
        PreparerAttestationReceiptDto[] Receipts,
        PreparerAttestationRevocationDto[] Revocations,
        SinglePrincipalAttestationAuditEventDto[] AuditTrail,
        CompletedCommand[] Commands);
    private sealed record Envelope(Snapshot Payload, string Hash);

    private SinglePrincipalAttestationStateDto Project(Snapshot snapshot) => new(
        snapshot.Scope, _policyEnabled || _draftChecksEnabled, ActivePolicyVersion, PolicyHashFor(ActivePolicyVersion),
        SinglePrincipalAttestationPolicy.Disclosure, SinglePrincipalAttestationPolicy.Confirmation,
        EnabledActions(),
        snapshot.Declarations.LastOrDefault(), snapshot.Declarations, snapshot.Receipts,
        snapshot.Revocations, snapshot.AuditTrail);

    private static Snapshot Empty(SinglePrincipalAttestationScopeDto scope) => new(
        SinglePrincipalAttestationPolicy.SchemaVersion, scope, [], [], [], [], []);

    private IReadOnlyList<string> EnabledActions()
    {
        var actions = new List<string>();
        if (_policyEnabled) actions.Add(SinglePrincipalAttestationPolicy.EvidenceAcknowledgment);
        if (_draftChecksEnabled) actions.Add(SinglePrincipalAttestationPolicy.DraftCheck);
        return actions;
    }

    private static string PolicyHashFor(string version) => version switch
    {
        SinglePrincipalAttestationPolicy.Version => PolicyHash,
        SinglePrincipalAttestationPolicy.DraftVersion => DraftPolicyHash,
        _ => string.Empty
    };

    private static bool IsKnownPolicy(string version, string hash) =>
        !string.IsNullOrEmpty(hash) && PolicyHashFor(version) == hash;

    private static bool IsRegisteredAction(string action) => action is
        SinglePrincipalAttestationPolicy.EvidenceAcknowledgment or SinglePrincipalAttestationPolicy.DraftCheck;

    private static bool PolicyAllowsAction(string version, string action) =>
        action == SinglePrincipalAttestationPolicy.EvidenceAcknowledgment
        || version == SinglePrincipalAttestationPolicy.DraftVersion && action == SinglePrincipalAttestationPolicy.DraftCheck;

    private static string ConfirmationFor(string action) => action == SinglePrincipalAttestationPolicy.DraftCheck
        ? SinglePrincipalAttestationPolicy.DraftConfirmation : SinglePrincipalAttestationPolicy.Confirmation;

    private static void RequireRead(SinglePrincipalAttestationAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!authority.CanRead || string.IsNullOrWhiteSpace(authority.Actor))
            throw Denied("read_permission", "Current permission to read scoped preparer assertions is required.");
    }

    private static void RequireHuman(SinglePrincipalAttestationAuthority authority)
    {
        RequireRead(authority);
        if (authority.Origin != OperationsActionOriginDto.HumanOperator)
            throw Denied("human_required", "Only an authenticated interactive human can declare, attest or revoke.");
    }

    private static void RequireCommand(Guid commandId, string reason)
    {
        if (commandId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 4000)
            throw Denied("invalid_command", "A stable command ID and a reason of 1–4,000 characters are required.");
    }

    private static SinglePrincipalAttestationScopeDto NormalizeScope(SinglePrincipalAttestationScopeDto scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(scope.TenantId) || string.IsNullOrWhiteSpace(scope.CompanyId) ||
            scope.TenantId.Length > 200 || scope.CompanyId.Length > 200 || scope.LedgerBookId == Guid.Empty)
            throw Denied("invalid_scope", "An authenticated tenant/company and a valid optional book scope are required.");
        return scope with { TenantId = scope.TenantId.Trim().ToLowerInvariant(), CompanyId = scope.CompanyId.Trim().ToLowerInvariant() };
    }

    private static bool SameActor(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static SinglePrincipalAttestationException Denied(string code, string message) => new(code, message);
    private static string Digest(string value) => Sha256Digest.ComputeUtf8(value);

    private static bool ValidSubjectSnapshot(string? snapshotJson, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson) || !Sha256Digest.IsCanonical(expectedHash)
            || !Sha256Digest.FixedEquals(Sha256Digest.ComputeUtf8(snapshotJson), expectedHash))
            return false;
        try
        {
            using var snapshot = JsonDocument.Parse(snapshotJson);
            return snapshot.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Fingerprint<T>(string kind, SinglePrincipalAttestationAuthority authority, T request) =>
        Digest(JsonSerializer.Serialize(new { kind, actor = authority.Actor.Trim().ToLowerInvariant(), authority.Origin, request }, JsonOptions));

    private static T? Replay<T>(Snapshot state, Guid commandId, string fingerprint) where T : class
    {
        var prior = state.Commands.SingleOrDefault(item => item.CommandId == commandId);
        if (prior is null)
            return null;
        if (prior.Fingerprint != fingerprint)
            throw Denied("command_conflict", "This command ID already records a different request or actor.");
        return prior.Result.Deserialize<T>(JsonOptions)
            ?? throw Denied("state_unavailable", "The retained command result is unavailable.");
    }

    private static Snapshot Complete<T>(Snapshot state, Guid commandId, string fingerprint, T result,
        string action, string actor, string reason, DateTimeOffset now, Guid? receiptId = null) => state with
    {
        Commands = [.. state.Commands, new(commandId, fingerprint, JsonSerializer.SerializeToElement(result, JsonOptions))],
        AuditTrail = [.. state.AuditTrail, new(commandId, checked(state.AuditTrail.Length + 1), action, actor.Trim(), now, reason, receiptId)]
    };

    private string StatePath(SinglePrincipalAttestationScopeDto scope)
    {
        var key = Digest(JsonSerializer.Serialize(scope, JsonOptions));
        var path = Path.Combine(_directory, key + ".json");
        _guard.EnsurePath(path);
        return path;
    }

    private async Task<FileStream> AcquireAsync(SinglePrincipalAttestationScopeDto scope, CancellationToken ct)
    {
        _guard.EnsurePath(_directory);
        Directory.CreateDirectory(_directory);
        _guard.EnsurePath(_directory);
        var path = StatePath(scope) + ".lock";
        _guard.EnsurePath(path);
        return await ExclusiveFileLock.AcquireAsync(path, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
    }

    private async Task<Snapshot> ReadAsync(SinglePrincipalAttestationScopeDto scope, CancellationToken ct)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(StatePath(scope), ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return Empty(scope);
        }
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(content, JsonOptions);
            if (envelope?.Payload is not { } state || string.IsNullOrWhiteSpace(envelope.Hash) ||
                Digest(JsonSerializer.Serialize(state, JsonOptions)) != envelope.Hash || !ValidSnapshot(state, scope))
                throw Denied("state_unavailable", "Attestation history failed integrity or schema validation; no authority is available.");
            return state;
        }
        catch (JsonException)
        {
            throw Denied("state_unavailable", "Attestation history is unreadable; no authority is available.");
        }
    }

    private static bool ValidSnapshot(Snapshot state, SinglePrincipalAttestationScopeDto scope)
    {
        if (state.SchemaVersion != SinglePrincipalAttestationPolicy.SchemaVersion || state.Scope != scope ||
            state.Declarations is null || state.Receipts is null || state.Revocations is null || state.AuditTrail is null || state.Commands is null)
            return false;
        if (state.Commands.Any(item => item is null || item.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(item.Fingerprint)) ||
            state.Commands.Select(item => item.CommandId).Distinct().Count() != state.Commands.Length ||
            state.AuditTrail.Length != state.Commands.Length ||
            state.AuditTrail.Length != state.Declarations.Length + state.Receipts.Length + state.Revocations.Length ||
            state.AuditTrail.Where((item, index) => item is null || item.Sequence != index + 1 || string.IsNullOrWhiteSpace(item.Actor) || string.IsNullOrWhiteSpace(item.Reason)).Any() ||
            !state.AuditTrail.Select(item => item.CommandId).SequenceEqual(state.Commands.Select(item => item.CommandId)))
            return false;
        if (state.Declarations.Where((item, index) => item is null || item.Scope != scope || item.DeclarationId == Guid.Empty ||
                item.Revision != index + 1 || !IsKnownPolicy(item.PolicyVersion, item.PolicyHash) ||
                string.IsNullOrWhiteSpace(item.PrincipalId) || !SameActor(item.PrincipalId, item.ChangedBy) ||
                (index > 0 && item.DeclarationId != state.Declarations[0].DeclarationId)).Any())
            return false;
        if (state.Receipts.Any(item => item is null || item.Scope != scope || item.ReceiptId == Guid.Empty || item.CommandId == Guid.Empty ||
                item.AssertionId != item.CommandId || item.SchemaVersion != SinglePrincipalAttestationPolicy.SchemaVersion ||
                item.DecisionKind != SinglePrincipalAttestationPolicy.DecisionKind || item.Disclosure != SinglePrincipalAttestationPolicy.Disclosure ||
                !IsKnownPolicy(item.PolicyVersion, item.PolicyHash) || !IsRegisteredAction(item.ActionId) || !PolicyAllowsAction(item.PolicyVersion, item.ActionId)
                || item.Origin != OperationsActionOriginDto.HumanOperator ||
                item.Confirmation != ConfirmationFor(item.ActionId) || !SameActor(item.PreparedBy, item.AttestedBy) ||
                string.IsNullOrWhiteSpace(item.SubjectId) || string.IsNullOrWhiteSpace(item.SubjectHash) ||
                string.IsNullOrWhiteSpace(item.SubjectRevision) || string.IsNullOrWhiteSpace(item.SourceArtifactId) ||
                string.IsNullOrWhiteSpace(item.SourceHash) || string.IsNullOrWhiteSpace(item.SourceProvenance) ||
                string.IsNullOrWhiteSpace(item.SourceSubjectKind) || string.IsNullOrWhiteSpace(item.SourceSubjectId) || item.Checks is not { Count: > 0 } ||
                !ValidSubjectSnapshot(item.SubjectSnapshotJson, item.SubjectHash) ||
                !state.Declarations.Any(declaration => declaration.DeclarationId == item.DeclarationId && declaration.Revision == item.DeclarationRevision &&
                    declaration.Enabled && declaration.PolicyVersion == item.PolicyVersion && declaration.PolicyHash == item.PolicyHash
                    && SameActor(declaration.PrincipalId, item.AttestedBy))))
            return false;
        if (state.Receipts.Select(item => item.ReceiptId).Distinct().Count() != state.Receipts.Length ||
            state.Receipts.Any(item => !state.Commands.Any(command => command.CommandId == item.CommandId)) ||
            state.Revocations.Select(item => item?.ReceiptId).Distinct().Count() != state.Revocations.Length ||
            state.Revocations.Any(item => item is null || item.Revision != 2 || item.CommandId == Guid.Empty ||
                string.IsNullOrWhiteSpace(item.Actor) || string.IsNullOrWhiteSpace(item.Reason) ||
                !state.Receipts.Any(receipt => receipt.ReceiptId == item.ReceiptId) ||
                !state.Commands.Any(command => command.CommandId == item.CommandId)))
            return false;
        return true;
    }

    private Task SaveAsync(Snapshot state, CancellationToken ct)
    {
        if (!ValidSnapshot(state, state.Scope))
            throw Denied("state_unavailable", "The complete assertion and audit state could not be validated.");
        var hash = Digest(JsonSerializer.Serialize(state, JsonOptions));
        return _write(StatePath(state.Scope), JsonSerializer.Serialize(new Envelope(state, hash), JsonOptions), ct);
    }
}
