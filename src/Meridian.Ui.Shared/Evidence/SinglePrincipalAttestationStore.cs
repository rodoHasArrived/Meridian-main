using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Core.IO;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>Server-derived authority; this type is never bound from an HTTP request body.</summary>
public sealed record SinglePrincipalAttestationAuthority(
    string Actor,
    OperationsActionOriginDto Origin,
    bool CanRead,
    bool CanAdministerDeclaration = false,
    bool CanAttest = false,
    bool CanRevoke = false,
    bool CanModifyTarget = false);

public sealed class SinglePrincipalAttestationException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Scoped, default-off storage for non-posting evidence acknowledgments. Declarations, assertions,
/// revocations and their audit events share one atomic snapshot and a cross-process lease.
/// No method creates an ordinary approval, changes evidence review status or grants domain authority.
/// </summary>
public sealed partial class SinglePrincipalAttestationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RootedPathGuard _guard;
    private readonly string _directory;
    private readonly bool _policyEnabled;
    private readonly Func<SinglePrincipalAttestationScopeDto, SinglePrincipalAttestationAuthority,
        RecordPreparerAttestationRequestDto, CancellationToken, Task<SinglePrincipalAttestationSubjectSnapshotDto?>> _resolveSubject;
    private readonly Func<string, string, CancellationToken, Task> _write;

    public static string PolicyHash { get; } = Digest(
        SinglePrincipalAttestationPolicy.Version + "\n" +
        SinglePrincipalAttestationPolicy.EvidenceAcknowledgment + "\n" +
        SinglePrincipalAttestationPolicy.Confirmation + "\nNo approval, posting, waiver, certification or release authority.");

    public SinglePrincipalAttestationStore(
        string dataRoot,
        Func<SinglePrincipalAttestationScopeDto, SinglePrincipalAttestationAuthority,
            RecordPreparerAttestationRequestDto, CancellationToken, Task<SinglePrincipalAttestationSubjectSnapshotDto?>> resolveSubject,
        bool policyEnabled = false,
        Func<string, string, CancellationToken, Task>? stateWriter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(resolveSubject);
        _guard = new RootedPathGuard(dataRoot);
        _directory = _guard.ResolvePath("single-principal-attestations");
        _resolveSubject = resolveSubject;
        _policyEnabled = policyEnabled;
        _write = stateWriter ?? ((path, content, ct) => AtomicFileWriter.WriteAsync(path, content, ct));
    }

    public async Task<SinglePrincipalAttestationStateDto> GetAsync(
        SinglePrincipalAttestationScopeDto scope,
        SinglePrincipalAttestationAuthority authority,
        CancellationToken ct = default)
    {
        RequireRead(authority);
        var normalized = NormalizeScope(scope);
        await using var lease = await AcquireAsync(normalized, ct).ConfigureAwait(false);
        return Project(await ReadAsync(normalized, ct).ConfigureAwait(false));
    }

    public async Task<SinglePrincipalDeclarationDto> ChangeDeclarationAsync(
        SinglePrincipalAttestationScopeDto scope,
        SinglePrincipalAttestationAuthority authority,
        ChangeSinglePrincipalDeclarationRequestDto request,
        CancellationToken ct = default)
    {
        RequireHuman(authority);
        var normalized = NormalizeScope(scope);
        RequireCommand(request.CommandId, request.Reason);
        var fingerprint = Fingerprint("declaration", authority, request);
        await using var lease = await AcquireAsync(normalized, ct).ConfigureAwait(false);
        var state = await ReadAsync(normalized, ct).ConfigureAwait(false);
        if (Replay<SinglePrincipalDeclarationDto>(state, request.CommandId, fingerprint) is { } completed)
            return completed;
        if (!authority.CanAdministerDeclaration)
            throw Denied("declaration_permission", "A separate declaration-administration grant is required.");
        var current = state.Declarations.LastOrDefault();
        if (request.ExpectedRevision != (current?.Revision ?? 0))
            throw Denied("stale_declaration", "The declaration changed; refresh before confirming.");
        if (request.PolicyVersion != SinglePrincipalAttestationPolicy.Version)
            throw Denied("unsupported_policy", "The confirmed attestation policy is unsupported.");
        if (request.Enabled && !_policyEnabled)
            throw Denied("policy_disabled", "The evidence-acknowledgment policy is disabled on this host.");
        if (string.IsNullOrWhiteSpace(request.PrincipalId))
            throw Denied("principal_required", "A declaration must name its bound human principal.");
        // The first slice declares the authenticated human's own principal, never another account.
        if (!SameActor(request.PrincipalId, authority.Actor))
            throw Denied("principal_mismatch", "The declaration must bind the authenticated human principal.");
        var now = DateTimeOffset.UtcNow;
        var declaration = new SinglePrincipalDeclarationDto(
            current?.DeclarationId ?? Guid.NewGuid(), checked((current?.Revision ?? 0) + 1), normalized,
            request.Enabled, authority.Actor.Trim(), SinglePrincipalAttestationPolicy.Version, PolicyHash,
            authority.Actor.Trim(), now, request.Reason);
        var next = state with { Declarations = [.. state.Declarations, declaration] };
        next = Complete(next, request.CommandId, fingerprint, declaration,
            request.Enabled ? "DeclarationEnabled" : "DeclarationWithdrawn", authority.Actor, request.Reason, now);
        await SaveAsync(next, ct).ConfigureAwait(false);
        return declaration;
    }

    public async Task<PreparerAttestationReceiptDto> AttestAsync(
        SinglePrincipalAttestationScopeDto scope,
        SinglePrincipalAttestationAuthority authority,
        RecordPreparerAttestationRequestDto request,
        CancellationToken ct = default)
    {
        RequireHuman(authority);
        var normalized = NormalizeScope(scope);
        RequireCommand(request.CommandId, request.Reason);
        var fingerprint = Fingerprint("attest", authority, request);
        await using var lease = await AcquireAsync(normalized, ct).ConfigureAwait(false);
        var state = await ReadAsync(normalized, ct).ConfigureAwait(false);
        if (Replay<PreparerAttestationReceiptDto>(state, request.CommandId, fingerprint) is { } completed)
            return completed;
        if (!_policyEnabled)
            throw Denied("policy_disabled", "The evidence-acknowledgment policy is disabled on this host.");
        if (!authority.CanAttest || !authority.CanModifyTarget)
            throw Denied("attestation_permission", "A domain attestation grant and target-modification permission are required.");
        if (request.ActionId != SinglePrincipalAttestationPolicy.EvidenceAcknowledgment)
            throw Denied("excluded_action", "Only retained evidence acknowledgment is currently registered; this action grants no attestation authority.");
        var declaration = state.Declarations.LastOrDefault();
        if (declaration is not { Enabled: true } || !SameActor(declaration.PrincipalId, authority.Actor))
            throw Denied("declaration_required", "An enabled declaration bound to this human principal is required.");
        if (request.ExpectedDeclarationRevision != declaration.Revision)
            throw Denied("stale_declaration", "The declaration changed; refresh before confirming.");
        if (declaration.PolicyVersion != SinglePrincipalAttestationPolicy.Version || declaration.PolicyHash != PolicyHash)
            throw Denied("unsupported_policy", "The retained declaration targets a different policy.");
        if (request.Confirmation != SinglePrincipalAttestationPolicy.Confirmation)
            throw Denied("confirmation_required", "Confirm the exact preparer responsibility and independent-review disclosure.");
        var subject = await _resolveSubject(normalized, authority, request, ct).ConfigureAwait(false);
        if (subject is null || !subject.Eligible || subject.Checks.Count == 0 ||
            string.IsNullOrWhiteSpace(subject.SourceArtifactId) || string.IsNullOrWhiteSpace(subject.SourceHash) ||
            string.IsNullOrWhiteSpace(subject.SourceSubjectKind) || string.IsNullOrWhiteSpace(subject.SourceSubjectId) ||
            !ValidSubjectSnapshot(subject.SubjectSnapshotJson, subject.Hash))
            throw Denied("unsupported_subject", "The scoped retained evidence is unavailable, unsupported or has unresolved checks.");
        if (subject.SubjectKind != request.SubjectKind || subject.SubjectId != request.SubjectId ||
            subject.Revision != request.SubjectRevision || subject.Hash != request.SubjectHash)
            throw Denied("stale_subject", "The evidence or reviewed operands changed; inspect and confirm the current revision.");
        var now = DateTimeOffset.UtcNow;
        // ACK creates a NEW assertion task owned by the authenticated human. It does not purport
        // to complete somebody else's task or to replace the external source creator/importer.
        var receipt = new PreparerAttestationReceiptDto(
            Guid.NewGuid(), request.CommandId, request.CommandId, SinglePrincipalAttestationPolicy.SchemaVersion,
            SinglePrincipalAttestationPolicy.DecisionKind, SinglePrincipalAttestationPolicy.Disclosure,
            normalized, request.ActionId, subject.SubjectKind, subject.SubjectId, subject.Revision, subject.Hash,
            subject.SourceArtifactId, subject.SourceHash, subject.SourceActor, subject.SourceProvenance,
            authority.Actor.Trim(), authority.Actor.Trim(), authority.Origin, declaration.DeclarationId,
            declaration.Revision, declaration.PolicyVersion, declaration.PolicyHash, now,
            request.Reason, request.Confirmation, subject.Checks.ToArray(), subject.SourceSubjectKind, subject.SourceSubjectId,
            subject.SubjectSnapshotJson);
        var next = state with { Receipts = [.. state.Receipts, receipt] };
        next = Complete(next, request.CommandId, fingerprint, receipt, "PreparerAssertionRecorded",
            authority.Actor, request.Reason, now, receipt.ReceiptId);
        await SaveAsync(next, ct).ConfigureAwait(false);
        return receipt;
    }

    public async Task<PreparerAttestationRevocationDto> RevokeAsync(
        SinglePrincipalAttestationScopeDto scope,
        SinglePrincipalAttestationAuthority authority,
        RevokePreparerAttestationRequestDto request,
        CancellationToken ct = default)
    {
        RequireHuman(authority);
        var normalized = NormalizeScope(scope);
        RequireCommand(request.CommandId, request.Reason);
        var fingerprint = Fingerprint("revoke", authority, request);
        await using var lease = await AcquireAsync(normalized, ct).ConfigureAwait(false);
        var state = await ReadAsync(normalized, ct).ConfigureAwait(false);
        if (Replay<PreparerAttestationRevocationDto>(state, request.CommandId, fingerprint) is { } completed)
            return completed;
        var receipt = state.Receipts.SingleOrDefault(item => item.ReceiptId == request.ReceiptId)
            ?? throw Denied("receipt_not_found", "The receipt was not found in this scope.");
        if (!authority.CanModifyTarget || (!SameActor(receipt.AttestedBy, authority.Actor) && !authority.CanRevoke))
            throw Denied("revocation_permission", "Only the bound attester or a specifically authorized governance actor may revoke this assertion.");
        if (request.ExpectedReceiptRevision != 1 || state.Revocations.Any(item => item.ReceiptId == request.ReceiptId))
            throw Denied("stale_receipt", "The assertion changed or was already revoked.");
        var now = DateTimeOffset.UtcNow;
        var revocation = new PreparerAttestationRevocationDto(
            request.CommandId, request.ReceiptId, 2, authority.Actor.Trim(), now, request.Reason);
        var next = state with { Revocations = [.. state.Revocations, revocation] };
        next = Complete(next, request.CommandId, fingerprint, revocation, "PreparerAssertionRevoked",
            authority.Actor, request.Reason, now, receipt.ReceiptId);
        await SaveAsync(next, ct).ConfigureAwait(false);
        return revocation;
    }
}
