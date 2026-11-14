using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Entities.FundStructure;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>Read-only legal and lifecycle observations over the native posted balance schedule.</summary>
public sealed class IntercompanyObligationMonitorService(
    IIntercompanyBalanceScheduleSource schedules,
    IIntercompanyAgreementService agreements,
    IEntityLifecycleService lifecycle,
    ILedgerJournalStore journals,
    IIntercompanyAgreementAccessAuthority access,
    TimeProvider? timeProvider = null) : IIntercompanyObligationMonitorService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<IntercompanyObligationMonitorDto> EvaluateAsync(IntercompanyAgreementCallContext context,
        string? companyId, IntercompanyObligationMonitorRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(context.TenantId) || context.TenantId.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(context.Actor) || string.IsNullOrWhiteSpace(companyId) ||
            companyId.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A concrete authenticated tenant, company and operator are required.");
        if (request.AsOf == default || request.ExpiryHorizonDays is < 0 or > 3660 || request.EntityId == Guid.Empty ||
            request.LedgerBookId == Guid.Empty || (request.LedgerBookId is null && string.IsNullOrWhiteSpace(request.FundProfileId)))
            throw new ArgumentException("Select a fund or book, an explicit as-of date and an expiry horizon between 0 and 3660 calendar days.");
        var evaluatedAt = _clock.GetUtcNow();
        try
        {
            var before = await CaptureAsync(context, companyId, request, ct).ConfigureAwait(false);
            var findings = BuildFindings(before, request);
            var after = await CaptureAsync(context, companyId, request, ct).ConfigureAwait(false);
            var fingerprint = Fingerprint(before.Versions);
            var stale = !Sha256Digest.FixedEquals(fingerprint, Fingerprint(after.Versions));
            if (stale)
                findings.Add(new("intercompany.monitor.stale", "Warning",
                    "The posted schedule, legal registry, entity lifecycle or source book configuration changed during evaluation; refresh the observation."));
            var complete = !stale && before.Schedule.Available && before.Schedule.IsMappingComplete &&
                findings.All(x => x.Code is not ("intercompany.lifecycle.unknown" or "intercompany.agreement.reference-unknown" or
                    "intercompany.agreement.reference-invalid"));
            var guarantees = before.Agreements.Values.Where(x => x.AgreementType == IntercompanyAgreementTypeDto.Guarantee &&
                x.LatestApprovedVersion is not null).OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.AgreementId).ToArray();
            return new(request.AsOf, request.ExpiryHorizonDays, evaluatedAt,
                stale ? "Stale" : !before.Schedule.Available ? "Unavailable" : complete ? "Current" : "Partial", complete,
                fingerprint, before.Versions, findings.OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.EntityId)
                    .ThenBy(x => x.AgreementId).ThenBy(x => x.JournalEntryId).ToArray(), before.Schedule, guarantees);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or
            TimeoutException or NotSupportedException or JsonException or Npgsql.NpgsqlException)
        {
            // Do not disclose a rejected party, book, external review or underlying provider error.
            return new(request.AsOf, request.ExpiryHorizonDays, evaluatedAt, "Unavailable", false, "", [],
                [new("intercompany.monitor.unavailable", "Warning",
                    "Intercompany obligation monitoring could not establish complete authorized source authority. Coverage and lifecycle remain unknown.")], null, []);
        }
    }

    private async Task<Observation> CaptureAsync(IntercompanyAgreementCallContext context, string companyId,
        IntercompanyObligationMonitorRequestDto request, CancellationToken ct)
    {
        var schedule = await schedules.GetAsync(context.TenantId, companyId, request.FundProfileId,
            request.LedgerBookId, request.AsOf, ct).ConfigureAwait(false);
        if (!string.Equals(schedule.TenantId, context.TenantId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(schedule.CompanyId, companyId, StringComparison.OrdinalIgnoreCase) || schedule.AsOf != request.AsOf ||
            (request.LedgerBookId is not null && schedule.LedgerBookId != request.LedgerBookId) ||
            (request.FundProfileId is not null && !string.Equals(schedule.FundProfileId, request.FundProfileId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The native balance schedule returned a different source scope.");
        var initialBooks = new List<LedgerBookRecord>();
        if (request.LedgerBookId is { } bookId)
        {
            var book = await journals.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The selected source book is unavailable.");
            initialBooks.Add(book);
            // A group book can have guarantees before any source journal exists. Retain its entity books,
            // while every entity is still checked against the operator's scoped access authority below.
            if (book.FundStructureNodeKind != FundStructureNodeKindDto.Entity)
                initialBooks.AddRange(await journals.ListLedgerBooksAsync(book.FundProfileId,
                    fundStructureNodeKind: FundStructureNodeKindDto.Entity, ct: ct).ConfigureAwait(false));
        }
        else
            initialBooks.AddRange(await journals.ListLedgerBooksAsync(request.FundProfileId, ct: ct).ConfigureAwait(false));
        var entities = initialBooks.Where(x => x.FundStructureNodeKind == FundStructureNodeKindDto.Entity)
            .Select(x => x.FundStructureNodeId).Concat(schedule.Rows.Select(x => x.EntityId))
            .Concat(schedule.Rows.Where(x => x.CounterpartyEntityId.HasValue).Select(x => x.CounterpartyEntityId!.Value))
            .Distinct().Order().ToArray();
        if (request.EntityId is { } requestedEntity && !entities.Contains(requestedEntity))
            throw new InvalidOperationException("The requested entity does not belong to the selected book/fund observation scope.");
        if (entities.Any(x => x == Guid.Empty) || (entities.Length > 0 &&
            !await access.CanAccessEntitiesAsync(context, entities, ct).ConfigureAwait(false)))
            throw new UnauthorizedAccessException("Monitor entity population is outside the operator's scoped grants.");
        var legal = new Dictionary<Guid, IntercompanyAgreementDetailDto>();
        foreach (var entity in entities)
        {
            string? cursor = null;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                var page = await agreements.ListAsync(context, new(EntityId: entity, AsOf: request.AsOf,
                    Cursor: cursor, PageSize: 100), ct).ConfigureAwait(false);
                foreach (var detail in page.Items)
                {
                    if (detail.PartyOneId != entity && detail.PartyTwoId != entity &&
                        !detail.Revisions.Any(x => x.Terms.PartyOneId == entity || x.Terms.PartyTwoId == entity) &&
                        !detail.Proposals.Any(x => x.Draft.PartyOneId == entity || x.Draft.PartyTwoId == entity))
                        throw new InvalidDataException("Legal registry query returned an unrelated agreement.");
                    if (legal.TryGetValue(detail.AgreementId, out var earlier) &&
                        earlier.AggregateVersion != detail.AggregateVersion)
                        throw new InvalidOperationException("The legal registry changed while its pages were read.");
                    legal[detail.AgreementId] = detail;
                }
                cursor = page.NextCursor;
                if (cursor is not null && !visited.Add(cursor))
                    throw new InvalidDataException("Legal registry pagination did not advance.");
            } while (cursor is not null);
        }
        foreach (var agreementId in schedule.Rows.Where(x => x.IntercompanyAgreementId.HasValue)
            .Select(x => x.IntercompanyAgreementId!.Value).Distinct())
        {
            if (!legal.ContainsKey(agreementId))
            {
                var detail = await agreements.GetAsync(context, agreementId, request.AsOf, ct: ct).ConfigureAwait(false);
                if (detail is not null)
                    legal[agreementId] = detail;
            }
        }
        var disclosedParties = legal.Values.SelectMany(x => new[] { x.PartyOneId, x.PartyTwoId }).Distinct().ToArray();
        if (disclosedParties.Length > 0 && !await access.CanAccessEntitiesAsync(context, disclosedParties, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Legal disclosure includes an inaccessible party.");
        var observedEntities = entities.Concat(disclosedParties).Distinct().Order().ToArray();
        var timelines = new Dictionary<Guid, EntityLifecycleRevisionDto?>();
        var coverageEnds = new List<AgreementCoverageEnd>();
        var horizon = request.AsOf.DayNumber > DateOnly.MaxValue.DayNumber - request.ExpiryHorizonDays
            ? DateOnly.MaxValue : request.AsOf.AddDays(request.ExpiryHorizonDays);
        var versions = new List<IntercompanyObligationSourceVersionDto>
        {
            new("balance-schedule", request.LedgerBookId?.ToString("D") ?? request.FundProfileId ?? "scope",
                schedule.Available ? "available" : "unavailable", ScheduleHash(schedule))
        };
        foreach (var book in initialBooks.DistinctBy(x => x.LedgerBookId).OrderBy(x => x.LedgerBookId))
            versions.Add(new("ledger-book", book.LedgerBookId.ToString("D"), book.UpdatedAt.ToString("O", CultureInfo.InvariantCulture),
                Sha256Digest.ComputeUtf8($"{book.FundProfileId}\n{book.FundStructureNodeId:D}\n{book.FundStructureNodeKind}\n{book.BaseCurrency}\n{book.AccountingBasis}\n{book.AccountingPolicyId}\n{book.AccountingPolicyVersion}")));
        foreach (var detail in legal.Values.OrderBy(x => x.AgreementId))
        {
            versions.Add(new("legal-agreement", detail.AgreementId.ToString("D"), detail.AggregateVersion.ToString(CultureInfo.InvariantCulture),
                Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(detail,
                    IntercompanyObligationMonitorJsonContext.Default.IntercompanyAgreementDetailDto))));
            if (await FindCoverageEndAsync(context, detail, request.AsOf, horizon, ct).ConfigureAwait(false) is { } coverageEnd)
                coverageEnds.Add(coverageEnd);
        }
        foreach (var entity in observedEntities)
        {
            var revision = await lifecycle.GetAsync(entity, ct: ct).ConfigureAwait(false);
            if (revision is not null && (revision.EntityId != entity ||
                !string.Equals(revision.TenantId, context.TenantId, StringComparison.OrdinalIgnoreCase) ||
                !Sha256Digest.FixedEquals(EntityLifecycleRevisionIntegrity.Seal(revision).ContentHash, revision.ContentHash)))
                throw new InvalidDataException("Retained lifecycle revision does not match its source identity and content.");
            timelines.Add(entity, revision);
            versions.Add(new("entity-lifecycle", entity.ToString("D"), revision?.Revision.ToString(CultureInfo.InvariantCulture) ?? "unknown",
                revision?.ContentHash ?? Sha256Digest.ComputeUtf8("unknown")));
        }
        return new(schedule, legal, timelines, coverageEnds, versions.OrderBy(x => x.SourceKind, StringComparer.Ordinal)
            .ThenBy(x => x.SourceId, StringComparer.Ordinal).ToArray());
    }

    private async Task<AgreementCoverageEnd?> FindCoverageEndAsync(IntercompanyAgreementCallContext context,
        IntercompanyAgreementDetailDto detail, DateOnly asOf, DateOnly horizon, CancellationToken ct)
    {
        if (!detail.LatestApprovedVersion.HasValue || detail.EffectiveStatus != IntercompanyAgreementStatusDto.Active ||
            detail.ApplicableRevision is not { } active)
            return null;
        // Ask the native legal projection at every retained change boundary. A later reactivation
        // cannot erase an intervening gap, and a superseded revision's raw expiry grants no finding.
        var boundaries = detail.Revisions.SelectMany(revision => revision.Action == IntercompanyAgreementActionDto.Terminate
                ? new DateOnly?[] { revision.Terms.TerminationEffectiveDate }
                : [revision.Terms.EffectiveFrom, revision.Terms.EffectiveTo])
            .Where(date => date.HasValue && date.Value > asOf && date.Value <= horizon)
            .Select(date => date!.Value).Distinct().Order();
        foreach (var boundary in boundaries)
        {
            var projected = await agreements.GetAsync(context, detail.AgreementId, boundary, ct: ct).ConfigureAwait(false);
            if (projected is null || projected.AgreementId != detail.AgreementId || projected.AsOf != boundary ||
                projected.AggregateVersion != detail.AggregateVersion)
                throw new InvalidOperationException("The legal registry changed or could not establish the requested effective chronology.");
            if (projected.EffectiveStatus != IntercompanyAgreementStatusDto.Active)
                return new(detail.AgreementId, active.AgreementVersion, boundary);
            active = projected.ApplicableRevision
                ?? throw new InvalidOperationException("Active legal coverage has no retained approved revision.");
        }
        return null;
    }

    private static List<IntercompanyObligationFindingDto> BuildFindings(Observation observation,
        IntercompanyObligationMonitorRequestDto request)
    {
        var findings = new List<IntercompanyObligationFindingDto>();
        var schedule = observation.Schedule;
        foreach (var issue in schedule.Issues)
            findings.Add(new("intercompany.schedule." + issue.Code, "Warning", issue.Message,
                LedgerBookId: issue.LedgerBookId, JournalEntryId: issue.JournalEntryId,
                DrillThrough: issue.JournalEntryId is { } journal
                    ? JournalRoute(journal, issue.LedgerBookId) : null));
        if (!schedule.Available)
            findings.Add(new("intercompany.schedule.unavailable", "Warning",
                "The posted intercompany schedule is unavailable; balance coverage is unknown."));
        foreach (var coverageEnd in observation.CoverageEnds)
        {
            var detail = observation.Agreements[coverageEnd.AgreementId];
            findings.Add(new("intercompany.agreement.expiring", "Warning",
                $"{detail.Name} legal coverage ends on {coverageEnd.Date:yyyy-MM-dd}.", AgreementId: detail.AgreementId,
                AgreementVersion: coverageEnd.AgreementVersion, DrillThrough: AgreementRoute(detail.AgreementId)));
        }
        foreach (var row in schedule.Rows)
        {
            if (!row.MappingVerified)
                findings.Add(new("intercompany.coverage.unmapped", "Warning",
                    "Posted intercompany facts lack exact reviewed agreement, account or party attribution; use the existing legacy attribution workflow.",
                    row.EntityId, row.IntercompanyAgreementId, row.IntercompanyAgreementVersion, row.LedgerBookId,
                    row.JournalEntryIds.FirstOrDefault() is var journal && journal != Guid.Empty ? journal : null,
                    "/accounting/entity-operations"));
            if (row.IntercompanyAgreementId is not { } agreementId ||
                row.IntercompanyAgreementVersion is not { } agreementVersion ||
                string.IsNullOrWhiteSpace(row.IntercompanyAgreementPayloadHash) ||
                !observation.Agreements.TryGetValue(agreementId, out var detail))
            {
                findings.Add(new("intercompany.agreement.reference-unknown", "Warning",
                    "An exact approved native agreement revision cannot be established for this posted balance; legal coverage is unknown.",
                    row.EntityId, LedgerBookId: row.LedgerBookId));
                continue;
            }
            var legalRevision = detail.Revisions.SingleOrDefault(x => x.AgreementVersion == agreementVersion &&
                Sha256Digest.FixedEquals(x.PayloadHash, row.IntercompanyAgreementPayloadHash));
            if (legalRevision is null || detail.AgreementType == IntercompanyAgreementTypeDto.Guarantee ||
                legalRevision.Action == IntercompanyAgreementActionDto.Terminate ||
                !SameParties(row, legalRevision.Terms) || legalRevision.Terms.Currency != row.TransactionCurrency)
                findings.Add(new("intercompany.agreement.reference-invalid", "Warning",
                    "Retained legal identity, approved revision, currency or party evidence does not match this posted balance.",
                    row.EntityId, agreementId, agreementVersion, row.LedgerBookId, DrillThrough: AgreementRoute(agreementId)));
            if (row.TransactionSignedBalance != 0 && detail.EffectiveStatus is IntercompanyAgreementStatusDto.Expired or IntercompanyAgreementStatusDto.Terminated)
                findings.Add(new(detail.EffectiveStatus == IntercompanyAgreementStatusDto.Expired
                        ? "intercompany.agreement.expired-exposure" : "intercompany.agreement.terminated-exposure", "Warning",
                    $"{detail.Name} is {detail.EffectiveStatus.ToString().ToLowerInvariant()} with retained posted exposure.",
                    row.EntityId, agreementId, agreementVersion, row.LedgerBookId, DrillThrough: AgreementRoute(agreementId)));
        }
        var effectiveAt = new DateTimeOffset(request.AsOf.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        foreach (var (entity, revision) in observation.Lifecycle)
        {
            var result = EntityLifecyclePolicy.Evaluate(revision, effectiveAt, EntityLifecycleOperationDto.Read, false);
            if (result.Status is null)
                findings.Add(new("intercompany.lifecycle.unknown", "Warning",
                    "No retained lifecycle window supports this entity on the selected date; earlier history remains unknown.", entity));
            else if (result.Status is LegalEntityLifecycleStatusDto.WindingDown or LegalEntityLifecycleStatusDto.Dissolved or LegalEntityLifecycleStatusDto.Merged)
                findings.Add(new("intercompany.lifecycle.exposure", "Warning",
                    $"Entity lifecycle is {result.Status}; inspect its retained agreements, guarantees and posted exposure.", entity));
        }
        return findings.Distinct().ToList();
    }

    private static bool SameParties(IntercompanyBalanceScheduleRowDto row, IntercompanyAgreementDraftDto draft) =>
        row.CounterpartyEntityId is { } counterparty &&
        ((draft.PartyOneId == row.EntityId && draft.PartyTwoId == counterparty) ||
         (draft.PartyTwoId == row.EntityId && draft.PartyOneId == counterparty));
    private static string AgreementRoute(Guid id) => $"/accounting/entity-operations?agreementId={id:D}";
    private static string JournalRoute(Guid id, Guid? book) => $"/accounting/journal-entries/detail?journalEntryId={id:D}" +
        (book is null ? "" : $"&ledgerBookId={book:D}");
    private static string ScheduleHash(IntercompanyBalanceScheduleDto schedule) => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(schedule,
        IntercompanyBalanceScheduleJsonContext.Default.IntercompanyBalanceScheduleDto));
    private static string Fingerprint(IReadOnlyList<IntercompanyObligationSourceVersionDto> versions) => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(versions,
        IntercompanyObligationMonitorJsonContext.Default.IReadOnlyListIntercompanyObligationSourceVersionDto));
    private sealed record Observation(IntercompanyBalanceScheduleDto Schedule,
        IReadOnlyDictionary<Guid, IntercompanyAgreementDetailDto> Agreements,
        IReadOnlyDictionary<Guid, EntityLifecycleRevisionDto?> Lifecycle,
        IReadOnlyList<AgreementCoverageEnd> CoverageEnds,
        IReadOnlyList<IntercompanyObligationSourceVersionDto> Versions);
    private sealed record AgreementCoverageEnd(Guid AgreementId, int AgreementVersion, DateOnly Date);
}
