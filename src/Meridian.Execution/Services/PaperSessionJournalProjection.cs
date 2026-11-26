using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Events;
using Meridian.Execution.Serialization;
using Meridian.Ledger;

namespace Meridian.Execution.Services;

/// <summary>Assigns durable identities to the postings from each replayed source effect.</summary>
internal sealed class PaperSessionJournalProjection
{
    private readonly string _sessionId;
    private readonly string _accountId;
    private readonly string _strategyId;
    private readonly LedgerPolicy _policy;
    private readonly IReadOnlyDictionary<Guid, PaperSessionFillRecord> _fills;
    private readonly List<JournalEntry> _postings = [];
    private readonly List<PaperSessionJournalBatch> _batches = [];

    public PaperSessionJournalProjection(string sessionId, string accountId, string strategyId, string currency,
        IReadOnlyList<PaperSessionFillRecord> fills)
    {
        _sessionId = sessionId;
        _accountId = accountId;
        _strategyId = strategyId;
        _policy = LedgerPolicy.Trading(currency);
        _fills = fills.ToDictionary(static fill => fill.FillId);
        Ledger = new Meridian.Ledger.Ledger(_policy);
        SourceLedger.AddPostingObserver(_postings.Add);
    }

    public Meridian.Ledger.Ledger SourceLedger { get; } = new();
    public Meridian.Ledger.Ledger Ledger { get; }
    public IReadOnlyList<PaperSessionJournalBatch> Batches => _batches;

    public void CaptureOpening(decimal initialCash, DateTimeOffset openingTimestamp) => Capture(
        _sessionId, PaperSessionJournalPurpose.OpeningCapital,
        Sha256Digest.ComputeUtf8(FormattableString.Invariant($"{initialCash:G29}:{openingTimestamp:O}")));

    public void CaptureFill(Guid fillId)
    {
        var fill = _fills[fillId];
        Capture(fillId.ToString("D"), PaperSessionJournalPurpose.Fill, fill.CanonicalHash, fill);
    }

    public void CaptureSettlement(PaperSessionSettlementRecord settlement) => Capture(
        settlement.Key, PaperSessionJournalPurpose.Settlement, Sha256Digest.ComputeUtf8(
            JsonSerializer.Serialize(settlement, ExecutionJsonContext.Default.PaperSessionSettlementRecord)),
        symbol: settlement.Symbol);

    public void CaptureFinancing(PaperFinancingCharge charge) => Capture(
        charge.SourceEventId.ToString("D"), PaperSessionJournalPurpose.Financing, Sha256Digest.ComputeUtf8(
            JsonSerializer.Serialize(charge, ExecutionJsonContext.Default.PaperFinancingCharge)), financing: charge);

    private void Capture(string sourceId, PaperSessionJournalPurpose purpose, string sourceHash,
        PaperSessionFillRecord? fill = null, string? symbol = null, PaperFinancingCharge? financing = null)
    {
        var batch = new PaperSessionJournalBatch(PaperSessionJournalBatch.CurrentSchemaVersion,
            _sessionId, _accountId, sourceId, purpose, sourceHash, _policy.FunctionalCurrency, [], financing);
        var entries = new List<JournalEntry>(_postings.Count);
        for (var index = 0; index < _postings.Count; index++)
        {
            var source = _postings[index];
            var journalId = PaperFinancingCharge.Identity("paper-session-entry", batch.BatchId.ToString("D"),
                index.ToString(CultureInfo.InvariantCulture));
            var tags = source.Metadata.Tags?.ToDictionary(static pair => pair.Key, static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            tags.Add("paperSessionSourceId", sourceId);
            tags.Add("paperSessionSourceHash", sourceHash);
            if (fill?.RunId is { } runId)
                tags.Add("runId", runId);
            if (fill is not null)
            {
                foreach (var pair in Meridian.Contracts.Operations.MarketDataInputEvidenceTags.ToTags(fill.Fill.MarketDataEvidence))
                    tags[pair.Key] = pair.Value;
                tags.Add("orderId", fill.Fill.ClientOrderId ?? fill.Fill.OrderId);
                if (fill.FinancialAccountId is { } owner)
                    tags.Add("executionAccountId", owner);
            }
            if (financing is not null)
            {
                tags.Add("sourceReference", financing.SourceReference);
                tags.Add("rationale", financing.Rationale);
                tags.Add("actorId", financing.ActorId);
            }

            var metadata = source.Metadata with
            {
                ActivityType = source.Metadata.ActivityType ?? purpose.ToString(),
                Symbol = fill?.Fill.Symbol ?? symbol ?? source.Metadata.Symbol,
                FillId = fill?.FillId ?? source.Metadata.FillId,
                FinancialAccountId = _accountId,
                StrategyId = fill?.StrategyId ?? _strategyId,
                LedgerBook = _sessionId,
                LedgerView = LedgerViewKind.Historical,
                EffectiveDate = source.Metadata.EffectiveDate ?? DateOnly.FromDateTime(source.Timestamp.UtcDateTime),
                IdempotencyKey = $"paper-session:{batch.BatchId:N}:{index.ToString(CultureInfo.InvariantCulture)}",
                Tags = tags
            };

            // Currency and chart binding are added before the IDs change. Rebinding the cash-flow
            // group tags to the new line IDs uses the same checked path as other draft rebuilds.
            var enriched = new JournalEntry(source.JournalEntryId, source.Timestamp, source.Description,
                source.Lines.Select(line => Copy(line, line.EntryId, source.JournalEntryId)).ToArray(), metadata);
            var rebuilt = new JournalEntry(journalId, source.Timestamp, source.Description,
                enriched.Lines.Select((line, position) => Copy(line,
                    PaperFinancingCharge.Identity("paper-session-line", journalId.ToString("D"), position.ToString(CultureInfo.InvariantCulture)),
                    journalId)).ToArray(), metadata);
            var entry = LedgerCashFlowAttribution.RemapForRebuiltLines(enriched, rebuilt);
            Ledger.Post(entry);
            entries.Add(entry);
        }
        _postings.Clear();
        _batches.Add(batch with { Entries = entries.AsReadOnly() });
    }

    private LedgerEntry Copy(LedgerEntry line, Guid lineId, Guid journalId) => new(lineId, journalId,
        line.Timestamp, line.Account, line.Debit, line.Credit, line.Description, line.Dimensions,
        line.Currency ?? _policy.FunctionalAmount(line.Debit, line.Credit))
    {
        ChartAccount = line.ChartAccount ?? _policy.Chart.GetPostingReference(line.Account),
        Memo = line.Memo
    };
}
