using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Serialization;

namespace Meridian.Ui.Shared.Services;

public sealed record PreparePeriodAdjustmentRequest(
    Guid LedgerBookId,
    Guid SourceJournalEntryId,
    Guid TargetPeriodId,
    DateOnly AccountingDate,
    string IntentId,
    string Reason,
    bool ReversePeriodEndMark = false);

/// <summary>
/// Prepares a source-linked inverse in an explicitly selected open period. Source journals and
/// their closed workbench records remain immutable; independent approval and posting are separate.
/// Delta mark accounting remains the default. Reversing a period-end mark is an explicit policy.
/// </summary>
public sealed class PeriodAdjustmentPreparationService(
    ILedgerJournalStore store,
    AutomatedJournalDraftIntakeService intake,
    IAutomatedJournalScheduleStore? schedules = null)
{
    internal const string SourceTag = "prior-period.adjustment-source";

    public async Task<AutomatedJournalDraftIntakeResult> PrepareAsync(
        PreparePeriodAdjustmentRequest request, string actor, string tenantId, string companyId,
        CancellationToken ct = default)
    {
        var prepared = await BuildAsync(request, actor, tenantId, companyId, ct).ConfigureAwait(false);
        return await intake.IntakeDraftsAsync(new AutomatedJournalPreparedDraftIntakeRequest(
            prepared.Book.FundProfileId, prepared.Book.BaseCurrency, [prepared.Draft], actor, prepared.Book.LedgerBookId,
            prepared.Target.PeriodId.ToString("D"), EntityId: prepared.EntityId, TenantId: tenantId, CompanyId: companyId,
            AccountingBasis: prepared.Book.AccountingBasis), ct).ConfigureAwait(false);
    }

    /// <summary>Retains an opt-in, one-time next-period mark reversal for the existing scheduler.</summary>
    public async Task<AutomatedJournalScheduleWorkItem> ScheduleMarkReversalAsync(
        PreparePeriodAdjustmentRequest request, string actor, string tenantId, string companyId,
        CancellationToken ct = default)
    {
        if (schedules is null)
            throw new InvalidOperationException("The durable automated journal schedule store is unavailable.");
        if (!request.ReversePeriodEndMark)
            throw new ArgumentException("Only explicit period-end mark reversal policy can be scheduled.");
        var prepared = await BuildAsync(request, actor, tenantId, companyId, ct).ConfigureAwait(false);
        var identity = $"period-mark-reversal:{request.LedgerBookId:N}:{request.SourceJournalEntryId:N}:{Uri.EscapeDataString(request.IntentId.Trim())}";
        var existing = await schedules.GetAsync(identity, ct).ConfigureAwait(false);
        var normalizedRequest = request with { IntentId = request.IntentId.Trim(), Reason = request.Reason.Trim() };
        if (existing is not null)
        {
            if (existing.PeriodAdjustment != normalizedRequest || existing.TenantId != tenantId || existing.CompanyId != companyId)
                throw new InvalidOperationException("This mark reversal schedule already retains different source, target, scope or policy terms.");
            if (existing.State is Meridian.Contracts.Workstation.AutomatedJournalScheduleStateDto.Failed
                or Meridian.Contracts.Workstation.AutomatedJournalScheduleStateDto.Blocked)
            {
                // The worker deliberately reuses its execution key to recover the same draft.
                // Archive the prior outcome under a distinct receipt before that key runs again.
                var priorFailure = existing.RunHistory.LastOrDefault(history =>
                    history.HistoryKind == AutomatedJournalScheduleHistoryKind.Execution &&
                    history.State is Meridian.Contracts.Workstation.AutomatedJournalScheduleStateDto.Failed
                        or Meridian.Contracts.Workstation.AutomatedJournalScheduleStateDto.Blocked);
                var history = priorFailure is null ? existing.RunHistory : existing.RunHistory.Append(priorFailure with
                {
                    RunKey = $"{priorFailure.RunKey}:retained-failure:v{existing.Version}",
                    HistoryKind = AutomatedJournalScheduleHistoryKind.Rearm,
                    Actor = actor,
                    PreviousVersion = existing.Version,
                    ResultVersion = checked(existing.Version + 1)
                }).ToArray();
                return await schedules.SaveAsync(existing with
                {
                    State = Meridian.Contracts.Workstation.AutomatedJournalScheduleStateDto.Scheduled,
                    LastScheduledForUtc = null,
                    LastConfiguredBy = actor,
                    LastSummary = "Explicit retry of the same retained period-end reversal policy.",
                    Blockers = [],
                    RunHistory = history
                }, ct).ConfigureAwait(false);
            }
            return existing;
        }
        var item = new AutomatedJournalScheduleWorkItem(identity, AutomatedJournalScheduleKind.PeriodEndMarkReversal,
            prepared.Book.FundProfileId, prepared.Book.LedgerBookId, prepared.SourcePeriod.PeriodId.ToString("D"),
            prepared.Draft.Lines.Select(line => line.dimensions?.EntityId).FirstOrDefault(x => x is not null)
                ?? $"book:{prepared.Book.LedgerBookId:D}",
            prepared.Book.BaseCurrency, prepared.SourcePeriod.StartDate, prepared.SourcePeriod.EndDate,
            request.AccountingDate, TimeOnly.MinValue, "UTC", actor, TenantId: tenantId, CompanyId: companyId,
            RecurrenceEnabled: false, CreatedBy: actor, LastConfiguredBy: actor)
        { PeriodAdjustment = normalizedRequest };
        return await schedules.SaveAsync(item, ct).ConfigureAwait(false);
    }

    private async Task<PreparedPeriodAdjustment> BuildAsync(
        PreparePeriodAdjustmentRequest request, string actor, string tenantId, string companyId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.LedgerBookId == Guid.Empty || request.SourceJournalEntryId == Guid.Empty
            || request.TargetPeriodId == Guid.Empty || string.IsNullOrWhiteSpace(request.IntentId)
            || string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(actor)
            || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
            throw new ArgumentException("Book, source journal, target period, intent, reason and authenticated scope are required.");
        var book = await store.GetLedgerBookAsync(request.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The selected ledger book is unavailable.");
        var sourceRecords = await store.QueryAsync(new LedgerJournalEntryQuery(
            LedgerBookId: request.LedgerBookId, JournalEntryId: request.SourceJournalEntryId), ct).ConfigureAwait(false);
        if (sourceRecords.Count != 1)
            throw new InvalidOperationException("Exactly one retained source journal must exist in the selected book.");
        var source = sourceRecords[0];
        var sourcePeriod = await store.GetPeriodAsync(source.PeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained source period is unavailable.");
        var target = await store.GetPeriodAsync(request.TargetPeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The explicitly selected target period is unavailable.");
        if (sourcePeriod.LedgerBookId != book.LedgerBookId || sourcePeriod.Status != "HardClosed"
            || target.LedgerBookId != book.LedgerBookId || target.Status != "Open"
            || request.AccountingDate < target.StartDate || request.AccountingDate > target.EndDate
            || target.StartDate <= sourcePeriod.EndDate)
            throw new InvalidOperationException("A correction requires a hard-closed source and a later open period in the same book, with a valid target accounting date.");
        if (!source.Entry.IsBalanced)
            throw new InvalidOperationException("The retained source journal is not balanced.");
        if (source.Entry.Lines.Any(line => line.Currency is null || !line.Currency.IsFunctionalCurrency
            || !string.Equals(line.Currency.FunctionalCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This preparation path supports explicit native book-currency lines only; foreign-currency corrections require the currency-preserving governed journal path.");
        if (request.ReversePeriodEndMark)
        {
            if ((!ValuationMarkEvidenceGuard.IsValuation(source.Entry.Metadata.IdempotencyKey)
                 && source.Entry.Metadata.ActivityType != "fair-value-mark")
                || source.Entry.Metadata.EffectiveDate != sourcePeriod.EndDate
                || request.AccountingDate != target.StartDate || target.StartDate != sourcePeriod.EndDate.AddDays(1))
                throw new InvalidOperationException("An opt-in mark reversal requires a retained period-end fair-value mark and the first day of the selected next period.");
            var entityIds = source.Entry.Lines.Select(line => line.Dimensions?.EntityId).Distinct().ToArray();
            if (entityIds.Length != 1)
                throw new InvalidOperationException("A period-end mark reversal requires one retained entity scope.");
            // Older valuation P&L accounts use the symbol as a fallback account scope. The
            // manual posting envelope can consequently name that fallback as FinancialAccountId.
            // The retained instrument leg owns the actual valued position's financial account.
            var instrumentLegs = source.Entry.Lines.Where(line => LedgerAccounts.UsesInstrumentSymbol(line.Account)).ToArray();
            var financialAccounts = (instrumentLegs.Length == 0 ? source.Entry.Lines : instrumentLegs)
                .Select(line => line.Dimensions?.AccountId ?? line.Account.FinancialAccountId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (financialAccounts.Length != 1)
                throw new InvalidOperationException("A period-end mark reversal requires one retained position financial account.");
            var reason = ValuationMarkEvidenceGuard.Validate(
                source.Entry.Metadata.Tags?.GetValueOrDefault(ValuationMarkEvidenceGuard.EvidenceTag),
                book.FundProfileId, sourcePeriod.EndDate, source.Entry.Metadata.SecurityId,
                financialAccounts[0], source.Entry.Metadata.Symbol,
                source.Entry.Metadata.Tags?.GetValueOrDefault(ValuationMarkEvidenceGuard.DigestTag), entityIds[0]);
            if (reason is not null)
                throw new InvalidOperationException($"Retained period-end mark evidence is invalid: {reason}");
        }
        var effective = source.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(source.Entry.Timestamp.UtcDateTime);
        var authority = new PriorPeriodAdjustmentSourceDto(source.Entry.JournalEntryId, source.PeriodId,
            effective, target.PeriodId, request.AccountingDate, request.IntentId.Trim(), request.Reason.Trim(), request.ReversePeriodEndMark);
        var key = FormattableString.Invariant($"prior-period|book={book.LedgerBookId:N}|source={source.Entry.JournalEntryId:N}|intent={Uri.EscapeDataString(authority.IntentId)}");
        var timestamp = new DateTimeOffset(request.AccountingDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var evidence = source.Entry.Metadata.EvidenceReferences.Append(new JournalEvidenceReference(
            $"period-adjustment:{authority.IntentId}",
            FormattableString.Invariant($"ledger://books/{book.LedgerBookId:D}/periods/{source.PeriodId:D}/journals/{source.Entry.JournalEntryId:D}?effective={effective:yyyy-MM-dd}&intent={Uri.EscapeDataString(authority.IntentId)}"),
            "prior-period-correction", "Meridian.Ledger", timestamp, actor,
            Description: authority.Reason)).ToArray();
        var description = $"Prior-period correction of {source.Entry.JournalEntryId:D}: {authority.Reason}";
        var journalEvent = new AutomatedJournalEvent(AutomatedJournalEventKind.PriorPeriodAdjustment,
            source.Entry.Metadata.Symbol ?? string.Empty, source.Entry.Lines.Sum(line => line.Debit), timestamp,
            FinancialAccountId: source.Entry.Metadata.FinancialAccountId, Description: description,
            SecurityId: source.Entry.Metadata.SecurityId, SourceEventId: source.Entry.JournalEntryId.ToString("D"),
            EffectiveDate: request.AccountingDate, IdempotencyKey: key, EvidenceReferences: evidence);
        var metadata = new JournalEntryMetadata(ActivityType: "prior-period-adjustment",
            Symbol: source.Entry.Metadata.Symbol, SecurityId: source.Entry.Metadata.SecurityId,
            FinancialAccountId: source.Entry.Metadata.FinancialAccountId, EffectiveDate: request.AccountingDate,
            IdempotencyKey: key, EvidenceReferences: evidence,
            Tags: new Dictionary<string, string>
            {
                [SourceTag] = JsonSerializer.Serialize(authority,
                PeriodAdjustmentJsonContext.Default.PriorPeriodAdjustmentSourceDto)
            });
        var draft = new AutomatedJournalDraft(journalEvent, description,
            source.Entry.Lines.Select(line => (line.Account, line.Credit, line.Debit, line.Dimensions)).ToArray(), metadata);
        var entities = source.Entry.Lines.Select(line => line.Dimensions?.EntityId).Distinct().ToArray();
        if (entities.Length != 1)
            throw new InvalidOperationException("This correction preparation path requires one retained source entity scope.");
        return new(book, sourcePeriod, target, draft, entities[0]);
    }

    private sealed record PreparedPeriodAdjustment(LedgerBookRecord Book, LedgerAccountingPeriod SourcePeriod,
        LedgerAccountingPeriod Target, AutomatedJournalDraft Draft, string? EntityId);
}
