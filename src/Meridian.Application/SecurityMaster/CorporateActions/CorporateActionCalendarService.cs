using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster.CorporateActions;

/// <summary>Evidence-backed operational dates; never submits elections or changes financial terms.</summary>
public sealed class CorporateActionCalendarService(
    ICorporateActionOperationsService operations, ICorporateActionOperationsStore? store = null)
{
    private ICorporateActionOperationsStore Store => store ?? throw new CorporateActionPersistenceUnavailableException("Durable corporate-action storage is not configured.");

    public async Task<CorporateActionCaseDeadlinesDto> GetDeadlinesAsync(Guid caseId, string tenant, string company, CancellationToken ct = default)
    {
        var processingCase = await RequireCaseAsync(caseId, tenant, company, ct).ConfigureAwait(false);
        var evidence = await Store.ListDeadlineEvidenceAsync(caseId, tenant, company, ct).ConfigureAwait(false);
        var mutable = !CorporateActionCaseStates.IsContentFrozen(processingCase.State);
        return new CorporateActionCaseDeadlinesDto(processingCase, ProjectHistory(evidence), mutable,
            mutable ? null : "This case is content-frozen; use its governed reopen or restatement transition before recording a deadline.");
    }

    public Task<CorporateActionEvidenceMutationResultDto> RecordAsync(RecordCorporateActionDeadlineRequestDto request,
        string actor, string? correlationId, CancellationToken ct = default)
    {
        ValidateCapture(request.Deadline, request.EvidenceReference, request.EvidenceHash, request.Rationale);
        return operations.AddEvidenceAsync(new AddCorporateActionEvidenceRequestDto(request.CaseId, request.ExpectedVersion,
            request.IdempotencyKey, request.TenantId, request.CompanyId, CorporateActionDeadlinePolicy.EvidenceKind,
            request.EvidenceReference, actor, EvidenceHash: request.EvidenceHash, Description: request.Rationale,
            Metadata: JsonSerializer.SerializeToElement(new CorporateActionDeadlineEvidenceDto(1, request.Deadline), CorporateActionDeadlinePolicy.JsonOptions),
            CorrelationId: correlationId, ScopeAssertion: request.ScopeAssertion), ct);
    }

    public async Task<CorporateActionDeadlineImportPreviewDto> PreviewImportAsync(CorporateActionDeadlineImportRequestDto request, CancellationToken ct = default)
    {
        var processingCase = await RequireCaseAsync(request.CaseId, request.TenantId, request.CompanyId, ct).ConfigureAwait(false);
        var preview = ParseImport(request);
        var errors = preview.Errors.ToList();
        if (processingCase.Version != request.ExpectedVersion)
            errors.Add("The case version changed; refresh before previewing this import.");
        if (CorporateActionCaseStates.IsContentFrozen(processingCase.State))
            errors.Add("The case is content-frozen; use its governed reopen/restatement workflow.");
        try
        {
            CorporateActionCaseAccountingPolicy.EnsureScopeAssertionMatches(request.ScopeAssertion ?? new(request.TenantId, request.CompanyId), processingCase.Scope);
        }
        catch (CorporateActionOperationException exception) { errors.Add(exception.Message); }
        var history = ProjectHistory(await Store.ListDeadlineEvidenceAsync(request.CaseId, request.TenantId, request.CompanyId, ct).ConfigureAwait(false));
        foreach (var row in preview.Rows)
        {
            var current = history.FirstOrDefault(e => e.IsCurrent && e.Deadline.SeriesId == row.Deadline.SeriesId);
            if ((CorporateActionDeadlinePolicy.ValidateApplicability(processingCase.Scope, row.Deadline) ??
                CorporateActionDeadlinePolicy.ValidateAppend(row.Deadline, current?.EvidenceId, current?.Deadline)) is { } error)
                errors.Add($"Row {row.Row}: {error}");
        }
        return preview with { Errors = errors };
    }

    public async Task<CorporateActionDeadlineImportResultDto> ImportAsync(CorporateActionDeadlineImportRequestDto request,
        string actor, string? correlationId, CancellationToken ct = default)
    {
        var preview = ParseImport(request);
        if (preview.Errors.Count > 0)
            throw new CorporateActionValidationException(string.Join(" ", preview.Errors));
        if (!Sha256Digest.FixedEquals(preview.PreviewHash, request.PreviewHash ?? string.Empty))
            throw new CorporateActionValidationException("The CSV or import scope changed; preview this exact import again.");
        var results = new List<CorporateActionEvidenceMutationResultDto>();
        foreach (var row in preview.Rows)
        {
            try
            {
                var result = await RecordAsync(new RecordCorporateActionDeadlineRequestDto(request.CaseId,
                    request.ExpectedVersion + results.Count, $"{request.IdempotencyKey}:row:{row.Row}", request.TenantId,
                    request.CompanyId, row.Deadline, row.EvidenceReference, row.EvidenceHash, row.Rationale, request.ScopeAssertion), actor, correlationId, ct).ConfigureAwait(false);
                results.Add(result);
            }
            catch (CorporateActionOperationException exception)
            {
                // Each row is an independent durable command. Replay resumes the exact original
                // import and its row keys; successfully committed rows are never rolled back.
                return new CorporateActionDeadlineImportResultDto(results.Count, false, results, row.Row, exception.Message);
            }
        }
        return new CorporateActionDeadlineImportResultDto(results.Count, true, results, null, null);
    }

    public async Task<CorporateActionCalendarPageDto> GetCalendarAsync(string tenant, string company,
        DateOnly from, DateOnly to, string? account = null, string? custody = null, string? cursor = null,
        int take = 250, CancellationToken ct = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
            throw new CorporateActionValidationException("Select a calendar range of at most 367 days.");
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(company))
            throw new CorporateActionScopeMismatchException("Tenant and company scope are required.");
        if (take is < 1 or > 500)
            throw new CorporateActionValidationException("Calendar page size must be between 1 and 500.");
        var snapshot = await Store.ReadCalendarAsync(tenant, company, from, to, account, custody, ct).ConfigureAwait(false);
        var items = ProjectCalendar(snapshot, from, to, account, custody);
        var revision = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(items, CorporateActionDeadlinePolicy.JsonOptions));
        var query = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new { tenant, company, from, to, account, custody }));
        var offset = 0;
        if (cursor is not null)
        {
            CalendarCursor? decoded;
            try
            { decoded = cursor.Length > 2048 ? null : JsonSerializer.Deserialize<CalendarCursor>(Convert.FromBase64String(cursor)); }
            catch (Exception ex) when (ex is JsonException or FormatException) { decoded = null; }
            if (decoded is null || decoded.Query != query || decoded.Offset < 0 || decoded.Offset > items.Count)
                throw new CorporateActionValidationException("The calendar cursor does not match this scope/range; refresh the calendar.");
            if (decoded.Revision != revision)
                throw new CorporateActionStateConflictException(Guid.Empty, "The retained calendar changed between pages; refresh the range before continuing.");
            offset = decoded.Offset;
        }
        var page = items.Skip(offset).Take(take).ToArray();
        var next = offset + page.Length < items.Count
            ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new CalendarCursor(query, revision, offset + page.Length))) : null;
        return new CorporateActionCalendarPageDto(from, to, DateTimeOffset.UtcNow, revision, page, next, items.Count,
            "Retained source proposals and processing cases in the authenticated scope; this is not a complete market-event feed. Coverage is partial until all pages are loaded.");
    }

    public static IReadOnlyList<CorporateActionDeadlineHistoryDto> ProjectHistory(IReadOnlyList<CorporateActionEvidenceDto> evidence)
    {
        var values = evidence.Select(e => (Evidence: e, Value: CorporateActionDeadlinePolicy.Read(e.Metadata))).ToArray();
        var latest = values.GroupBy(e => e.Value.SeriesId).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Evidence.CaseVersion).First().Evidence.EvidenceId);
        return values.Select(e => new CorporateActionDeadlineHistoryDto(e.Evidence.EvidenceId, e.Value,
            e.Evidence.EvidenceReference, e.Evidence.EvidenceHash, e.Evidence.Description, e.Evidence.CaseVersion,
            e.Evidence.RecordedBy, e.Evidence.RecordedAtUtc, latest[e.Value.SeriesId] == e.Evidence.EvidenceId))
            .OrderByDescending(e => e.CaseVersion).ToArray();
    }

    public static IReadOnlyList<CorporateActionCalendarEventDto> ProjectCalendar(CorporateActionCalendarReadSet snapshot,
        DateOnly from, DateOnly to, string? account, string? custody)
    {
        var result = new List<CorporateActionCalendarEventDto>();
        foreach (var proposal in snapshot.Proposals)
        {
            var cases = snapshot.Cases.Where(c => c.ProposalId == proposal.ProposalId).ToArray();
            var references = cases.Select(c => new CorporateActionCalendarCaseReferenceDto(c.CaseId, c.Version, c.State, c.Scope)).OrderBy(c => c.CaseId).ToArray();
            var ticker = proposal.DisplayMetadata?.Ticker ?? proposal.SecurityId.ToString();
            void AddDate(string kind, DateOnly? date)
            {
                if (date is { } value && value >= from && value <= to)
                    result.Add(new CorporateActionCalendarEventDto($"{proposal.ProposalId:N}:{kind}", proposal.SecurityId,
                        ticker, proposal.ProposedAction.EventType, proposal.ProposalId, kind, value, null, references, proposal.ProviderIdentity, proposal.State));
            }
            AddDate("ExDate", proposal.ProposedAction.ExDate);
            AddDate("RecordDate", proposal.ProposedAction.RecordDate);
            AddDate("PaymentDate", proposal.ProposedAction.PayDate);
            foreach (var cutoff in CorporateActionElectionCalendar.Read(proposal.ProposedAction.Payload).Cutoffs)
            {
                var date = DateOnly.FromDateTime(cutoff.DeadlineAt.DateTime);
                if (date < from || date > to)
                    continue;
                result.Add(new CorporateActionCalendarEventDto($"{proposal.ProposalId:N}:source-election:{Uri.EscapeDataString(cutoff.Id)}",
                    proposal.SecurityId, ticker, proposal.ProposedAction.EventType, proposal.ProposalId, "ElectionDeadline",
                    date, cutoff.DeadlineAt, references, proposal.ProviderIdentity, proposal.State, SourceCutoff: cutoff));
            }
            foreach (var processingCase in cases)
                foreach (var record in ProjectHistory(snapshot.Evidence.Where(e => e.CaseId == processingCase.CaseId).ToArray()).Where(e => e.IsCurrent && !e.Deadline.Withdrawn))
                {
                    var deadline = record.Deadline;
                    if (account is not null && !string.IsNullOrWhiteSpace(deadline.FinancialAccountId) && deadline.FinancialAccountId != account ||
                        custody is not null && !string.IsNullOrWhiteSpace(deadline.CustodyAccountId) && deadline.CustodyAccountId != custody)
                        continue;
                    var date = deadline.Date ?? DateOnly.FromDateTime(deadline.At!.Value.DateTime);
                    if (date < from || date > to)
                        continue;
                    result.Add(new CorporateActionCalendarEventDto($"{processingCase.CaseId:N}:{deadline.SeriesId:N}", proposal.SecurityId,
                        ticker, proposal.ProposedAction.EventType, proposal.ProposalId, deadline.Kind, date, deadline.At,
                        [new CorporateActionCalendarCaseReferenceDto(processingCase.CaseId, processingCase.Version, processingCase.State, processingCase.Scope)],
                        proposal.ProviderIdentity, proposal.State, record));
                }
        }
        return result.OrderBy(e => e.Date).ThenBy(e => e.SecurityId).ThenBy(e => e.EventId, StringComparer.Ordinal).ToArray();
    }

    public static CorporateActionDeadlineImportPreviewDto ParseImport(CorporateActionDeadlineImportRequestDto request)
    {
        var errors = new List<string>();
        var rows = new List<CorporateActionDeadlineImportRowDto>();
        if (request.Csv is null || Encoding.UTF8.GetByteCount(request.Csv) > 262144)
            throw new CorporateActionValidationException("CSV imports must be at most 256 KiB.");
        if (request.CaseId == Guid.Empty || request.ExpectedVersion <= 0)
            errors.Add("A durable case ID and positive expected case version are required.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || Encoding.UTF8.GetByteCount(request.IdempotencyKey) > 220)
            errors.Add("Use a nonblank import idempotency key of at most 220 UTF-8 bytes.");
        var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(request with { PreviewHash = null }, CorporateActionDeadlinePolicy.JsonOptions));
        IReadOnlyList<string[]> parsed;
        try
        { parsed = ParseCsv(request.Csv.TrimStart('\uFEFF')); }
        catch (FormatException ex) { return new CorporateActionDeadlineImportPreviewDto(hash, request.ExpectedVersion, [], [ex.Message]); }
        if (parsed.Count == 0 || string.Join(',', parsed[0]) != CorporateActionDeadlinePolicy.CsvHeader)
            return new CorporateActionDeadlineImportPreviewDto(hash, request.ExpectedVersion, [], [$"Use the canonical CSV header: {CorporateActionDeadlinePolicy.CsvHeader}"]);
        if (parsed.Count > 101)
            errors.Add("Import at most 100 deadline rows at a time.");
        var series = new HashSet<Guid>();
        for (var index = 1; index < Math.Min(parsed.Count, 101); index++)
        {
            var cells = parsed[index];
            try
            {
                if (cells.Length != 14)
                    throw new FormatException("Expected 14 CSV columns.");
                if (!Guid.TryParse(cells[0], out var id) || id == Guid.Empty)
                    throw new FormatException("seriesId must be a UUID.");
                if (!series.Add(id))
                    throw new FormatException("Each deadline series may occur once in an import.");
                DateOnly? date = string.IsNullOrEmpty(cells[3]) ? null : DateOnly.ParseExact(cells[3], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                DateTimeOffset? at = string.IsNullOrEmpty(cells[4]) ? null : CorporateActionDeadlinePolicy.TryParseInstant(cells[4], out var instant)
                    ? instant : throw new FormatException("at requires an ISO timestamp with an explicit offset.");
                Guid? previous = string.IsNullOrEmpty(cells[11]) ? null : Guid.Parse(cells[11]);
                var withdrawn = string.IsNullOrEmpty(cells[12]) ? false : bool.Parse(cells[12]);
                var value = new CorporateActionElectionDeadlineDto(id, cells[1], cells[2], date, at,
                    Empty(cells[5]), Empty(cells[6]), Empty(cells[7]), previous, withdrawn, cells[13]);
                ValidateCapture(value, cells[8], cells[9], cells[10]);
                rows.Add(new CorporateActionDeadlineImportRowDto(index + 1, value, cells[8], cells[9], cells[10]));
            }
            catch (Exception ex) when (ex is FormatException or CorporateActionValidationException)
            { errors.Add($"Row {index + 1}: {ex.Message}"); }
        }
        if (rows.Count == 0 && errors.Count == 0)
            errors.Add("The CSV has no deadline records.");
        return new CorporateActionDeadlineImportPreviewDto(hash, request.ExpectedVersion, rows, errors);
    }

    private static IReadOnlyList<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var closedQuote = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; closedQuote = true; } }
                else
                    cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0 && !closedQuote)
                quoted = true;
            else if (c == ',' || c is '\r' or '\n')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                closedQuote = false;
                if (c != ',')
                { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; if (cells.Any(x => x.Length > 0)) rows.Add(cells.ToArray()); cells.Clear(); }
            }
            else if (closedQuote || c == '"')
                throw new FormatException("CSV quotes are malformed.");
            else
                cell.Append(c);
        }
        if (quoted)
            throw new FormatException("CSV has an unterminated quoted field.");
        if (cell.Length > 0 || cells.Count > 0 || closedQuote)
        { cells.Add(cell.ToString()); if (cells.Any(x => x.Length > 0)) rows.Add(cells.ToArray()); }
        return rows;
    }

    private static string? Empty(string value) => value.Length == 0 ? null : value;
    private static void ValidateCapture(CorporateActionElectionDeadlineDto value, string reference, string hash, string rationale)
    {
        if (CorporateActionDeadlinePolicy.ValidateCapture(value, reference, hash, rationale) is { } error)
            throw new CorporateActionValidationException(error);
    }
    private async Task<CorporateActionProcessingCaseDto> RequireCaseAsync(Guid id, string tenant, string company, CancellationToken ct)
        => await operations.GetCaseAsync(id, tenant, company, ct).ConfigureAwait(false)
            ?? throw new CorporateActionNotFoundException("Corporate-action processing case", id);
    private sealed record CalendarCursor(string Query, string Revision, int Offset);
}
