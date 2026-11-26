using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Codes = Meridian.Contracts.Ledger.FiscalYearCloseBlockerCodes;

namespace Meridian.Storage.Ledger;

/// <summary>
/// The retained close snapshot read for one constituent period. <see cref="IntegrityFailed"/> is set
/// when the store refused to serve retained bytes that no longer match their hash or scope.
/// </summary>
public sealed record FiscalYearCloseRead(LedgerPeriodCloseSnapshot? Snapshot, bool IntegrityFailed = false);

/// <summary>
/// Builds the read-only fiscal-year close plan (W12-YEND-001 slice 1) from retained hard-close
/// snapshots. The final period's snapshot already retains the book's full journal population up to
/// the fiscal year end, so the plan never reads live journals that may have been restated since.
/// </summary>
/// <remarks>
/// Monthly hard closes already roll revenue and expense into retained earnings, so the year's
/// closing transfers are mostly journals already posted. The pre-closing trial balance therefore
/// removes the fiscal year's <see cref="LedgerPostingKindDto.ClosingEntry"/> journals, and reported
/// income is measured there rather than from a year-end projection that would read zero.
/// </remarks>
public static class FiscalYearCloseProjection
{
    private const string HardClosed = "HardClosed";

    public static FiscalYearClosePlanDto Build(
        Guid ledgerBookId,
        int fiscalYear,
        IReadOnlyList<LedgerAccountingPeriod> bookPeriods,
        IReadOnlyDictionary<Guid, FiscalYearCloseRead> snapshots,
        FiscalYearCloseBookEvidenceDto? bookEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(bookPeriods);
        ArgumentNullException.ThrowIfNull(snapshots);
        if (ledgerBookId == Guid.Empty)
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));

        var book = bookPeriods.Where(p => p.LedgerBookId == ledgerBookId).ToArray();
        var periods = book.Where(p => p.FiscalYear == fiscalYear)
            .OrderBy(p => p.StartDate).ThenBy(p => p.PeriodNo).ThenBy(p => p.PeriodId).ToArray();
        var blockers = new List<FiscalYearCloseBlockerDto>();
        if (periods.Length == 0)
        {
            blockers.Add(new(Codes.NoPeriods, $"Ledger book has no accounting periods for fiscal year {fiscalYear}."));
            return NotReady(ledgerBookId, fiscalYear, null, null, blockers, []);
        }

        var start = periods.Min(p => p.StartDate);
        var end = periods.Max(p => p.EndDate);
        // A missing first period cannot hide behind the earliest period found: when an earlier
        // fiscal year exists, this year must begin the day after that year's last period ends.
        if (book.Where(p => p.FiscalYear < fiscalYear).Select(p => (DateOnly?)p.EndDate).Max() is { } priorEnd
            && start.DayNumber != priorEnd.DayNumber + 1)
            blockers.Add(new(Codes.CoverageGap, "Fiscal-year opening does not immediately follow the prior fiscal year."));

        var coveredTo = periods[0].EndDate;
        foreach (var period in periods.Skip(1))
        {
            if (period.StartDate.DayNumber > coveredTo.DayNumber + 1)
                blockers.Add(new(Codes.CoverageGap,
                    $"No accounting period covers {coveredTo.AddDays(1):yyyy-MM-dd} to {period.StartDate.AddDays(-1):yyyy-MM-dd}; a missing period cannot be treated as empty."));
            else if (period.StartDate <= coveredTo)
                blockers.Add(new(Codes.CoverageGap, $"Period '{period.Label}' overlaps an earlier constituent period.", period.PeriodId));
            if (period.EndDate > coveredTo)
                coveredTo = period.EndDate;
        }

        var expectedEnd = ExpectedYearEnd(start);
        if (end != expectedEnd
            && blockers.All(blocker => blocker.Code != Codes.CoverageGap))
            blockers.Add(new(Codes.CoverageGap,
                $"Periods must cover the supported twelve-month fiscal year from {start:yyyy-MM-dd} through {expectedEnd:yyyy-MM-dd}."));

        foreach (var prior in book.Where(p => p.FiscalYear < fiscalYear && p.Status != HardClosed)
                     .OrderBy(p => p.StartDate).ThenBy(p => p.PeriodId))
            blockers.Add(new(Codes.PriorYearNotClosed,
                $"Opening history is not final: prior-year period '{prior.Label}' is {prior.Status}.", prior.PeriodId));

        var evidence = new List<FiscalYearClosePeriodEvidenceDto>(periods.Length);
        var payloads = new Dictionary<Guid, (LedgerPeriodCloseSnapshot Snapshot, LedgerPeriodCloseSnapshotPayload Payload)>();
        foreach (var period in periods)
        {
            snapshots.TryGetValue(period.PeriodId, out var read);
            var snapshot = read?.Snapshot;
            evidence.Add(new(period.PeriodId, period.PeriodNo, period.Label, period.StartDate, period.EndDate,
                period.Status, period.Version, snapshot?.PeriodVersion, snapshot?.ContentHashSha256));

            if (period.Status != HardClosed)
            {
                blockers.Add(new(Codes.PeriodNotHardClosed,
                    $"Period '{period.Label}' is {period.Status}; every constituent period must be hard-closed.", period.PeriodId));
                continue;
            }
            if (read?.IntegrityFailed == true)
            {
                blockers.Add(new(Codes.SnapshotIntegrity,
                    $"The retained close snapshot for '{period.Label}' failed its integrity check.", period.PeriodId));
                continue;
            }
            if (snapshot is null)
            {
                blockers.Add(new(Codes.SnapshotMissing,
                    $"Period '{period.Label}' has no retained close snapshot.", period.PeriodId));
                continue;
            }
            if (snapshot.LedgerBookId != ledgerBookId || snapshot.PeriodId != period.PeriodId || snapshot.PeriodVersion != period.Version)
            {
                blockers.Add(new(Codes.SnapshotStale,
                    $"The retained close snapshot for '{period.Label}' does not match the current close version.", period.PeriodId));
                continue;
            }
            try
            {
                var payload = snapshot.VerifyIntegrity();
                if (payload.Period.StartDate != period.StartDate || payload.Period.EndDate != period.EndDate
                    || payload.Period.FiscalYear != period.FiscalYear || payload.Period.PeriodNo != period.PeriodNo)
                    throw new InvalidDataException("Retained close period dates or calendar scope changed.");
                payloads[period.PeriodId] = (snapshot, payload);
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException)
            {
                blockers.Add(new(Codes.SnapshotIntegrity,
                    $"The retained close snapshot for '{period.Label}' failed its integrity check.", period.PeriodId));
            }
        }

        if (blockers.Count > 0)
            return NotReady(ledgerBookId, fiscalYear, start, end, blockers, evidence);

        var final = periods.OrderByDescending(p => p.EndDate).ThenByDescending(p => p.PeriodNo).ThenBy(p => p.PeriodId).First();
        var (finalSnapshot, finalPayload) = payloads[final.PeriodId];
        foreach (var period in periods.Where(p => p.PeriodId != final.PeriodId && payloads[p.PeriodId].Snapshot.ClosedAt > finalSnapshot.ClosedAt))
            blockers.Add(new(Codes.FinalSnapshotSuperseded,
                $"Period '{period.Label}' was closed again after the final period '{final.Label}'; re-close the final period so its snapshot includes that change.",
                period.PeriodId));
        foreach (var prior in book.Where(p => p.FiscalYear < fiscalYear && p.ClosedAt > finalSnapshot.ClosedAt))
            blockers.Add(new(Codes.FinalSnapshotSuperseded,
                $"Opening-history period '{prior.Label}' was re-closed after '{final.Label}'; re-close the final period to include that change.",
                prior.PeriodId));

        var periodYears = book.ToDictionary(p => p.PeriodId, p => p.FiscalYear);
        foreach (var record in finalPayload.Entries)
        {
            if (!periodYears.TryGetValue(record.PeriodId, out var year))
                blockers.Add(new(Codes.UnknownJournalPeriod,
                    $"Journal {record.Entry.JournalEntryId:D} belongs to a period outside this book.", record.PeriodId));
            else if (year > fiscalYear)
                blockers.Add(new(Codes.LaterYearJournal,
                    $"Journal {record.Entry.JournalEntryId:D} belongs to a later fiscal year but is dated inside this one.", record.PeriodId));
        }

        if (blockers.Count > 0)
            return NotReady(ledgerBookId, fiscalYear, start, end, blockers, evidence);

        bool InYear(LedgerJournalEntryRecord record) => periodYears[record.PeriodId] == fiscalYear;
        var entries = finalPayload.Entries;
        var yearClosing = GetClosingTransfers(entries.Where(InYear));
        var yearClosingIds = yearClosing.Select(e => e.Entry.JournalEntryId).ToHashSet();

        var actual = Accumulate(entries.SelectMany(e => e.Entry.Lines).Select(Line));
        var preClosing = Accumulate(entries.Where(e => !yearClosingIds.Contains(e.Entry.JournalEntryId))
            .SelectMany(e => e.Entry.Lines).Select(Line));
        var opening = Accumulate(entries.Where(e => !InYear(e)).SelectMany(e => e.Entry.Lines).Select(Line));
        var posted = Accumulate(yearClosing.SelectMany(e => e.Entry.Lines).Select(Line));

        var yearEnd = new DateTimeOffset(end.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var label = $"fiscal-year-{fiscalYear}";
        var periodIds = periods.Select(period => period.PeriodId.ToString("D")).ToArray();
        var residual = YearEndCloseProjector.Project(new YearEndCloseInput(
            label, yearEnd, ToBalances(actual), "fiscal-year-close-plan", periodIds, periodIds)).ClosingEntries;
        var proposed = Accumulate(residual.JournalLines.Select(l => (l.account, l.dimensions, l.debit, l.credit)));
        var postClosing = Accumulate(entries.SelectMany(e => e.Entry.Lines).Select(Line)
            .Concat(residual.JournalLines.Select(l => (l.account, l.dimensions, l.debit, l.credit))));
        var income = PeriodCloseProjector.Project(new PeriodCloseInput(label, yearEnd, ToBalances(preClosing), "fiscal-year-close-plan")).NetIncome;

        var preEquity = Equity(preClosing);
        var postEquity = Equity(postClosing);
        if (preClosing.Sum(r => r.DebitTotal) != preClosing.Sum(r => r.CreditTotal)
            || postClosing.Sum(r => r.DebitTotal) != postClosing.Sum(r => r.CreditTotal))
            blockers.Add(new(Codes.Unbalanced, "The retained fiscal-year journal population does not balance."));
        if (postClosing.Any(r => IsTemporary(r) && r.Balance != 0m) || postEquity != preEquity + income)
            blockers.Add(new(Codes.IncomeNotPreserved,
                "Closing transfers would not move exactly the year's reported income into equity."));
        if (blockers.Count > 0)
            return NotReady(ledgerBookId, fiscalYear, start, end, blockers, evidence);

        var plan = new FiscalYearClosePlanDto(
            ledgerBookId, fiscalYear, start, end, IsReady: true, Blockers: [], evidence,
            NonZero(preClosing), NonZero(posted), NonZero(proposed),
            postClosing.Where(r => r.Balance != 0m).ToArray(),
            postClosing.Where(r => !IsTemporary(r) && r.Balance != 0m).ToArray(),
            Normalize(income), Normalize(Equity(opening)), Normalize(preEquity), Normalize(postEquity),
            PlanHashSha256: null)
        { Book = bookEvidence };
        var canonical = JsonSerializer.Serialize(plan, FiscalYearClosePlanHashJsonContext.Default.FiscalYearClosePlanDto);
        return plan with { PlanHashSha256 = Sha256Digest.ComputeUtf8(canonical) };
    }

    private static FiscalYearClosePlanDto NotReady(
        Guid bookId, int fiscalYear, DateOnly? start, DateOnly? end,
        IReadOnlyList<FiscalYearCloseBlockerDto> blockers, IReadOnlyList<FiscalYearClosePeriodEvidenceDto> evidence)
        => new(bookId, fiscalYear, start, end, IsReady: false, blockers, evidence,
            [], [], [], [], [], 0m, 0m, 0m, 0m, PlanHashSha256: null);

    private static (LedgerAccount Account, LedgerLineDimensionSet? Dimensions, decimal Debit, decimal Credit) Line(LedgerEntry line)
        => (line.Account, line.Dimensions, line.Debit, line.Credit);

    private static IReadOnlyList<FiscalYearCloseBalanceDto> Accumulate(
        IEnumerable<(LedgerAccount Account, LedgerLineDimensionSet? Dimensions, decimal Debit, decimal Credit)> lines)
    {
        var rows = new Dictionary<string, (LedgerAccount Account, LedgerDimensionSetDto? Dimensions, decimal Debit, decimal Credit)>(StringComparer.Ordinal);
        foreach (var (account, dimensions, debit, credit) in lines)
        {
            var dto = LedgerDimensionMapper.ToDto(dimensions);
            // Instrument key: a SecurityId-keyed account is one row across ticker renames; a legacy
            // account keeps its ticker key (and therefore its retained row order and plan hash).
            var instrument = account.SecurityId is { } securityId ? "security:" + securityId.ToString("D") : account.Symbol;
            var key = string.Join('\u001f', account.AccountType, account.Name, instrument, account.FinancialAccountId, ScopeKey(dto));
            rows[key] = rows.TryGetValue(key, out var row)
                ? row with { Debit = row.Debit + debit, Credit = row.Credit + credit }
                : (account, dto, debit, credit);
        }

        return rows.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .Select(row => new FiscalYearCloseBalanceDto(row.Account.Name, row.Account.AccountType.ToString(),
                row.Account.Symbol, row.Account.FinancialAccountId, row.Dimensions,
                Normalize(row.Debit), Normalize(row.Credit),
                Normalize(Meridian.Ledger.Ledger.CalculateNetBalance(row.Account, row.Debit, row.Credit)),
                row.Account.SecurityId))
            .ToArray();
    }

    private static IReadOnlyList<PeriodCloseAccountBalance> ToBalances(IReadOnlyList<FiscalYearCloseBalanceDto> rows)
        => rows.Select(row => new PeriodCloseAccountBalance(
                new LedgerAccount(row.AccountName, Enum.Parse<LedgerAccountType>(row.AccountType), row.Symbol, row.FinancialAccountId, row.SecurityId),
                row.Balance, LedgerDimensionMapper.ToDomain(row.Dimensions)))
            .ToArray();

    private static IReadOnlyList<FiscalYearCloseBalanceDto> NonZero(IReadOnlyList<FiscalYearCloseBalanceDto> rows)
        => rows.Where(r => r.Balance != 0m || r.DebitTotal != 0m || r.CreditTotal != 0m).ToArray();

    private static bool IsTemporary(FiscalYearCloseBalanceDto row)
        => row.AccountType is nameof(LedgerAccountType.Revenue) or nameof(LedgerAccountType.Expense);

    private static decimal Equity(IReadOnlyList<FiscalYearCloseBalanceDto> rows)
        => rows.Where(r => r.AccountType == nameof(LedgerAccountType.Equity)).Sum(r => r.Balance);

    internal static IReadOnlyList<LedgerJournalEntryRecord> GetClosingTransfers(IEnumerable<LedgerJournalEntryRecord> entries)
    {
        var transfers = new Dictionary<Guid, LedgerJournalEntryRecord>();
        foreach (var entry in entries.OrderBy(entry => entry.GlobalSequence).ThenBy(entry => entry.Entry.JournalEntryId))
        {
            if (entry.PostingKind == LedgerPostingKindDto.ClosingEntry
                || (entry.PostingKind == LedgerPostingKindDto.Adjustment
                    && entry.SourceJournalEntryId is { } sourceId
                    && transfers.TryGetValue(sourceId, out var source)
                    && source.PeriodId == entry.PeriodId
                    && IntercompanyJournalCorrectionEvidence.IsExactInverse(entry.Entry, source.Entry)))
                transfers.Add(entry.Entry.JournalEntryId, entry);
        }
        return transfers.Values.ToArray();
    }

    private static DateOnly? ExpectedYearEnd(DateOnly start)
    {
        if (start.Year == DateOnly.MaxValue.Year)
            return start is { Month: 1, Day: 1 } ? DateOnly.MaxValue : null;
        var next = start.AddYears(1);
        return start is { Month: 2, Day: 29 } ? next : next.AddDays(-1);
    }

    private static string ScopeKey(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
            return string.Empty;
        var external = dimensions.ExternalGlDimensions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(dimensions with { ExternalGlDimensions = external },
            FiscalYearClosePlanHashJsonContext.Default.LedgerDimensionSetDto);
    }

    // Decimal scale must not change the plan hash: 100 and 100.00 are the same amount.
    private static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(FiscalYearClosePlanDto))]
[JsonSerializable(typeof(LedgerDimensionSetDto))]
internal sealed partial class FiscalYearClosePlanHashJsonContext : JsonSerializerContext;
