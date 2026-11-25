using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>
/// Durable operator casework over the authorized obligation monitor. Observation and disposition
/// append operational history; they never amend agreements, approve journals, or change close gates.
/// </summary>
public sealed partial class IntercompanyAgreementWorkQueueService(
    IIntercompanyObligationMonitorService monitor,
    IOperationalCaseHistoryStore history,
    IIntercompanyAgreementService agreements,
    ILedgerJournalStore journals,
    IFundProfileTenancyRegistry ownership,
    IIntercompanyAgreementAccessAuthority access,
    TimeProvider? timeProvider = null) : IIntercompanyAgreementWorkQueueService
{
    private const string CaseType = "intercompany-agreement-work";
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<IntercompanyAgreementWorkQueueDto> GetAsync(IntercompanyAgreementCallContext context,
        string? companyId, IntercompanyObligationMonitorRequestDto scope, CancellationToken ct = default)
    {
        var capture = await CaptureAsync(context, companyId, scope, ct).ConfigureAwait(false);
        var items = capture.Records.GroupBy(record => record.CaseId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Project(group.ToArray(), capture), StringComparer.Ordinal);
        foreach (var candidate in capture.Candidates.Values)
            if (!items.ContainsKey(candidate.CaseId))
                items.Add(candidate.CaseId, candidate);
        return new(scope.AsOf, scope.ExpiryHorizonDays, capture.Observation.EvaluatedAtUtc,
            capture.Observation.Status, capture.Observation.IsComplete, capture.Fingerprint,
            items.Values.OrderBy(item => item.State == IntercompanyAgreementWorkItemStateDto.Resolved)
                .ThenBy(item => item.DueDate ?? DateOnly.MaxValue).ThenBy(item => item.CaseId, StringComparer.Ordinal).ToArray(),
            capture.Guarantees, capture.Observation.Status == "Unavailable"
                ? ["Current authorized source evidence is unavailable. Retained case details are withheld; no cases were resolved."]
                : capture.Observation.IsComplete ? []
                : ["The observation is incomplete or stale. Refresh and restore source authority before changing case history."])
        { LedgerBookId = capture.Book.LedgerBookId, FundProfileId = capture.Book.FundProfileId };
    }

    public async Task<IntercompanyAgreementWorkItemDto> ObserveAsync(IntercompanyAgreementCallContext context,
        string? companyId, IntercompanyObligationMonitorRequestDto scope,
        IntercompanyAgreementWorkQueueObserveRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCommand(request.CommandId, request.CaseId, request.ExpectedVersion, request.ExpectedRecordHash,
            request.ExpectedSourceFingerprint, request.Reason);
        var input = InputDigest(context, scope, "Observe", request.CommandId, request.CaseId,
            request.ExpectedVersion, request.ExpectedRecordHash, request.ExpectedSourceFingerprint, request.Reason);
        var capture = await CaptureAsync(context, companyId, scope, ct).ConfigureAwait(false);
        if (Replay(capture, context.Actor, request.CommandId, input) is { } replay)
            return replay;
        RequireCurrent(capture, request.ExpectedSourceFingerprint);
        if (!capture.Candidates.TryGetValue(request.CaseId, out var candidate))
            throw new IntercompanyAgreementWorkQueueConflictException("The selected finding is no longer observed in this scope. Refresh the work queue.");
        var previous = CaseRecords(capture, request.CaseId);
        RequireVersion(previous, request.ExpectedVersion, request.ExpectedRecordHash);
        var retained = previous.Length == 0 ? null : Project(previous, capture);
        var now = _clock.GetUtcNow();
        var snapshot = candidate with
        {
            IsTracked = true,
            State = retained?.State ?? IntercompanyAgreementWorkItemStateDto.Open,
            Owner = retained?.Owner,
            FirstSeenAtUtc = retained?.FirstSeenAtUtc ?? now,
            LastObservedAtUtc = now,
            LastObservedSourceFingerprint = capture.Fingerprint
        };
        await VerifySourceAsync(context, companyId, scope, capture, ct).ConfigureAwait(false);
        return await AppendAsync(capture, context.Actor, request.CommandId, input, "Observe", request.Reason,
            snapshot, previous, null, [], now, ct).ConfigureAwait(false);
    }

    public async Task<IntercompanyAgreementWorkItemDto> UpdateAsync(IntercompanyAgreementCallContext context,
        string? companyId, IntercompanyObligationMonitorRequestDto scope,
        IntercompanyAgreementWorkQueueUpdateRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCommand(request.CommandId, request.CaseId, request.ExpectedVersion, request.ExpectedRecordHash,
            request.ExpectedSourceFingerprint, request.Reason);
        if (!Enum.IsDefined(request.Action) || request.ExpectedVersion == 0)
            throw new ArgumentException("Select a supported action and an existing tracked case version.");
        var evidence = ValidateEvidence(request.EvidenceReferences);
        if (request.Note is { Length: > 4000 } || request.Owner is { Length: > 256 })
            throw new ArgumentException("Notes may contain at most 4000 characters and owner IDs at most 256.");
        if (request.Action == IntercompanyAgreementWorkItemActionDto.AddNote && string.IsNullOrWhiteSpace(request.Note) && evidence.Count == 0)
            throw new ArgumentException("A note or an evidence reference is required.");
        if (request.Action == IntercompanyAgreementWorkItemActionDto.Resolve && evidence.Count == 0)
            throw new ArgumentException("Resolution requires at least one supporting evidence reference.");
        var input = InputDigest(context, scope, request.Action.ToString(), request.CommandId, request.CaseId,
            request.ExpectedVersion, request.ExpectedRecordHash, request.ExpectedSourceFingerprint,
            request.Reason, request.Owner, request.Note, string.Join("", evidence.Select(Encode)));
        var capture = await CaptureAsync(context, companyId, scope, ct).ConfigureAwait(false);
        if (Replay(capture, context.Actor, request.CommandId, input) is { } replay)
            return replay;
        RequireCurrent(capture, request.ExpectedSourceFingerprint);
        var previous = CaseRecords(capture, request.CaseId);
        RequireVersion(previous, request.ExpectedVersion, request.ExpectedRecordHash);
        var retained = Project(previous, capture);
        var state = request.Action switch
        {
            IntercompanyAgreementWorkItemActionDto.AwaitReview when retained.State is IntercompanyAgreementWorkItemStateDto.Open
                or IntercompanyAgreementWorkItemStateDto.Reopened => IntercompanyAgreementWorkItemStateDto.AwaitingReview,
            IntercompanyAgreementWorkItemActionDto.Resolve when retained.State != IntercompanyAgreementWorkItemStateDto.Resolved
                => IntercompanyAgreementWorkItemStateDto.Resolved,
            IntercompanyAgreementWorkItemActionDto.Reopen when retained.State == IntercompanyAgreementWorkItemStateDto.Resolved
                => IntercompanyAgreementWorkItemStateDto.Reopened,
            IntercompanyAgreementWorkItemActionDto.AssignOwner or IntercompanyAgreementWorkItemActionDto.AddNote => retained.State,
            _ => throw new IntercompanyAgreementWorkQueueConflictException("This action is not valid for the retained case state. Refresh the work queue.")
        };
        var now = _clock.GetUtcNow();
        var snapshot = retained with
        {
            State = state,
            Owner = request.Action == IntercompanyAgreementWorkItemActionDto.AssignOwner
                ? string.IsNullOrWhiteSpace(request.Owner) ? null : request.Owner.Trim() : retained.Owner,
            LastObservedAtUtc = retained.IsCurrentlyObserved ? now : retained.LastObservedAtUtc,
            LastObservedSourceFingerprint = retained.IsCurrentlyObserved ? capture.Fingerprint : retained.LastObservedSourceFingerprint
        };
        await VerifySourceAsync(context, companyId, scope, capture, ct).ConfigureAwait(false);
        return await AppendAsync(capture, context.Actor, request.CommandId, input, request.Action.ToString(), request.Reason,
            snapshot, previous, request.Note?.Trim(), evidence, now, ct).ConfigureAwait(false);
    }

    private async Task<Capture> CaptureAsync(IntercompanyAgreementCallContext context, string? companyId,
        IntercompanyObligationMonitorRequestDto scope, CancellationToken ct)
    {
        var book = await AuthorizeBookAsync(context, companyId, scope, ct).ConfigureAwait(false);
        var normalized = scope with { FundProfileId = book.FundProfileId, LedgerBookId = book.LedgerBookId };
        var scopeKey = HashParts(context.TenantId.Trim().ToUpperInvariant(), companyId, book.LedgerBookId.ToString("D"));
        IntercompanyObligationMonitorDto observation;
        try { observation = await monitor.EvaluateAsync(context, companyId, normalized, ct).ConfigureAwait(false); }
        catch (Exception error) when (IsUnavailable(error)) { throw Unavailable(); }
        if (observation.AsOf != scope.AsOf || observation.ExpiryHorizonDays != scope.ExpiryHorizonDays)
            throw Unavailable();
        if (observation.Status == "Unavailable")
            return new(book, normalized, scopeKey, observation, "", [], new(StringComparer.Ordinal), [], new(), _clock.GetUtcNow());
        if (observation.Status is not ("Current" or "Partial" or "Stale") || !IsDigest(observation.SourceFingerprint))
            throw Unavailable();
        if (observation.BalanceSchedule is not { } schedule || schedule.LedgerBookId != book.LedgerBookId
            || schedule.FundProfileId != book.FundProfileId || schedule.AsOf != scope.AsOf
            || !string.Equals(schedule.TenantId, context.TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(schedule.CompanyId, companyId, StringComparison.Ordinal))
            throw Unavailable();
        var records = await ReadHistoryAsync(scopeKey, ct).ConfigureAwait(false);
        var retained = records.Select(ReadSnapshot).ToArray();
        var legalIds = observation.Findings.Where(finding => finding.AgreementId.HasValue).Select(finding => finding.AgreementId!.Value)
            .Concat(observation.Guarantees.Select(guarantee => guarantee.AgreementId))
            .Concat(retained.Select(item => item.AgreementId)).Distinct().Order().ToArray();
        var legal = new Dictionary<Guid, IntercompanyAgreementDetailDto>();
        foreach (var agreementId in legalIds)
        {
            IntercompanyAgreementDetailDto? detail;
            try { detail = await agreements.GetAsync(context, agreementId, scope.AsOf, ct: ct).ConfigureAwait(false); }
            catch (Exception error) when (IsUnavailable(error)) { throw Unavailable(); }
            if (detail is null || detail.AgreementId != agreementId || detail.AsOf != scope.AsOf)
                throw Unavailable();
            if (!await access.CanAccessEntitiesAsync(context, [detail.PartyOneId, detail.PartyTwoId], ct).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The work queue includes parties outside the current operator scope.");
            legal.Add(agreementId, detail);
        }
        // Older case messages can mention former parties. Current book ownership alone never
        // permits disclosure of those retained details after entity grants have been revoked.
        var retainedParties = records.SelectMany(record => new[] { ReadParty(record, "partyOneId"), ReadParty(record, "partyTwoId") })
            .Distinct().ToArray();
        if (retainedParties.Length > 0 && !await access.CanAccessEntitiesAsync(context, retainedParties, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Retained case history is outside the current operator scope.");
        var fingerprint = HashParts(scopeKey, scope.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            scope.ExpiryHorizonDays.ToString(CultureInfo.InvariantCulture), observation.SourceFingerprint,
            string.Join("", legal.OrderBy(item => item.Key).Select(item => Encode(SerializeLegal(item.Value)))));
        var candidates = new Dictionary<string, IntercompanyAgreementWorkItemDto>(StringComparer.Ordinal);
        foreach (var finding in observation.Findings.Where(finding => finding.AgreementId.HasValue)
            .OrderBy(finding => finding.Code, StringComparer.Ordinal).ThenBy(finding => finding.AgreementId))
        {
            var id = CaseId(scopeKey, finding.AgreementId!.Value, finding.Code, finding.DueDate);
            var candidate = new IntercompanyAgreementWorkItemDto(id, book.LedgerBookId, finding.AgreementId.Value,
                finding.AgreementVersion, finding.Code, finding.Severity, finding.Message, Suggest(finding.Code),
                finding.DueDate, scope.AsOf, true, false, IntercompanyAgreementWorkItemStateDto.Open, null,
                null, null, null, Overdue(finding.DueDate, scope.AsOf), "", 0, null, []);
            // Expiring and expired-exposure observations describe the same legal boundary.
            candidates.TryAdd(id, candidate);
        }
        var guarantees = observation.Guarantees.Select(item => legal[item.AgreementId])
            .Where(item => item.AgreementType == IntercompanyAgreementTypeDto.Guarantee
                && item.ApplicableRevision is { Action: not IntercompanyAgreementActionDto.Terminate } revision
                && revision.AgreementId == item.AgreementId && revision.Terms.AgreementType == IntercompanyAgreementTypeDto.Guarantee
                && revision.Terms.EffectiveFrom <= scope.AsOf && item.Revisions.Any(retainedRevision =>
                    retainedRevision.AgreementVersion == revision.AgreementVersion && retainedRevision.PayloadHash == revision.PayloadHash))
            .Select(item =>
            {
                var revision = item.ApplicableRevision!;
                var terms = revision.Terms;
                return new IntercompanyGuaranteeDisclosureDto(item.AgreementId, terms.Code, terms.Name,
                    revision.AgreementVersion, revision.PayloadHash, terms.PartyOneId, terms.PartyOneRole,
                    terms.PartyTwoId, terms.PartyTwoRole, terms.Currency,
                    terms.Terms?.StructuredTerms?.Guarantee?.BeneficiaryReference,
                    terms.Terms?.StructuredTerms?.Guarantee?.LimitAmount, terms.EffectiveFrom, terms.EffectiveTo,
                    // The canonical registry chooses the applicable terms even after expiry or
                    // termination. A future termination is not an effective as-of termination.
                    item.EffectiveStatus == IntercompanyAgreementStatusDto.Terminated
                        ? item.Revisions.Where(entry => entry.Action == IntercompanyAgreementActionDto.Terminate
                            && entry.Terms.TerminationEffectiveDate <= scope.AsOf)
                            .Select(entry => entry.Terms.TerminationEffectiveDate).Where(date => date.HasValue).Min()
                        : null,
                    item.EffectiveStatus, null, "Unavailable",
                    "No reviewed guaranteed-obligation linkage or measured exposure source is retained. The declared cap is not current exposure.");
            }).OrderBy(item => item.Code, StringComparer.Ordinal).ThenBy(item => item.AgreementId).ToArray();
        return new(book, normalized, scopeKey, observation, fingerprint, records, candidates, guarantees, legal, _clock.GetUtcNow());
    }

    private async Task<LedgerBookRecord> AuthorizeBookAsync(IntercompanyAgreementCallContext context,
        string? companyId, IntercompanyObligationMonitorRequestDto scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(context.TenantId) || context.TenantId.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(context.Actor) || string.IsNullOrWhiteSpace(companyId)
            || companyId.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
            || scope.LedgerBookId is not { } bookId || bookId == Guid.Empty || scope.AsOf == default
            || scope.ExpiryHorizonDays is < 0 or > 3660 || scope.EntityId == Guid.Empty)
            throw new ArgumentException("Select a concrete tenant, company, operator, ledger book, date and valid expiry horizon.");
        var book = await journals.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false);
        var owner = book is null ? null : await ownership.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
        if (book is null || owner is null || !owner.IsHeldBy(context.TenantId) || owner.CompanyId != companyId
            || scope.FundProfileId is not null && scope.FundProfileId != book.FundProfileId
            || scope.EntityId.HasValue && scope.EntityId != book.FundStructureNodeId)
            throw new UnauthorizedAccessException("The selected work queue scope is unavailable to this operator.");
        if (book.FundStructureNodeKind == FundStructureNodeKindDto.Entity
            && !await access.CanAccessEntitiesAsync(context, [book.FundStructureNodeId], ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The selected book entity is outside the current operator scope.");
        return book;
    }

    private async Task VerifySourceAsync(IntercompanyAgreementCallContext context, string? company,
        IntercompanyObligationMonitorRequestDto scope, Capture original, CancellationToken ct)
    {
        var current = await CaptureAsync(context, company, scope, ct).ConfigureAwait(false);
        RequireCurrent(current, original.Fingerprint);
    }

    private static void RequireCurrent(Capture capture, string expected)
    {
        if (capture.Observation.Status != "Current" || !capture.Observation.IsComplete)
            throw new IntercompanyAgreementWorkQueueUnavailableException("A complete current authorized observation is required before changing case history.");
        if (!Sha256Digest.FixedEquals(capture.Fingerprint, expected))
            throw new IntercompanyAgreementWorkQueueConflictException("Source authority changed after the work queue was read. Refresh before continuing.");
    }

    private static string FindingKind(string code) => code is "intercompany.agreement.expiring"
        or "intercompany.agreement.expired-exposure" or "intercompany.agreement.terminated-exposure" ? "legal-coverage-end" : code;
    private static string CaseId(string scopeKey, Guid agreement, string code, DateOnly? due) =>
        "ic-work:" + HashParts(scopeKey, agreement.ToString("D"), FindingKind(code), due?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    private static int? Overdue(DateOnly? due, DateOnly asOf) => due.HasValue ? Math.Max(0, asOf.DayNumber - due.Value.DayNumber) : null;
    private static string Suggest(string code) => code switch
    {
        "intercompany.agreement.expiring" => "Review renewal or draft an agreement amendment; plan a controlled wind-down if coverage will end.",
        "intercompany.agreement.expired-exposure" or "intercompany.agreement.terminated-exposure" =>
            "Review posted balances and arrange a controlled wind-down under applicable legal and accounting authority.",
        "intercompany.coverage.unmapped" => "Review the posted source and complete the existing governed attribution workflow.",
        _ => "Inspect the retained legal and accounting evidence and resolve the source finding through its governed workflow."
    };
    private static string SerializeLegal(IntercompanyAgreementDetailDto detail) => JsonSerializer.Serialize(detail,
        IntercompanyObligationMonitorJsonContext.Default.IntercompanyAgreementDetailDto);
    private static string Encode(string? value) => value is null ? "-1:" : value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
    private static string HashParts(params string?[] values) => Sha256Digest.ComputeUtf8(string.Concat(values.Select(Encode)));
    private static bool IsUnavailable(Exception error) => error is IOException or InvalidDataException or JsonException or TimeoutException or Npgsql.NpgsqlException;
    private static IntercompanyAgreementWorkQueueUnavailableException Unavailable() =>
        new("Intercompany work queue source or retained history could not be verified. Restore authority and refresh; no case decision was inferred.");

    private sealed record Capture(LedgerBookRecord Book, IntercompanyObligationMonitorRequestDto Scope,
        string ScopeKey, IntercompanyObligationMonitorDto Observation, string Fingerprint,
        IReadOnlyList<OperationalCaseHistoryRecord> Records, Dictionary<string, IntercompanyAgreementWorkItemDto> Candidates,
        IReadOnlyList<IntercompanyGuaranteeDisclosureDto> Guarantees,
        Dictionary<Guid, IntercompanyAgreementDetailDto> Legal, DateTimeOffset Now);
}
