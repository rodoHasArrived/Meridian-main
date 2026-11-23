using System.Text.Json;
using Meridian.Contracts.Operations;
using Meridian.Execution.Events;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;

namespace Meridian.Execution.Services;

/// <summary>Reads recorded paper order and simulated handoff evidence for an authorized run.</summary>
public sealed class PaperOrderEvidenceReader(IOperationalCaseHistoryStore history)
{
    public async Task<IReadOnlyList<PaperOrderEvidence>> ReadForRunAsync(
        string runId, Guid accountId, string version, CancellationToken ct)
    {
        var orders = await history.ReadAsync(new OperationalCaseHistoryQuery { CaseType = OrderIntentJournal.CaseType }, ct)
            .ConfigureAwait(false);
        var resultsByCase = orders.ToLookup(static order => order.CaseId, StringComparer.Ordinal);
        var owned = orders.Where(record => record.EventType == "IntentClaimed" &&
            record.Data.GetValueOrDefault("runId") == runId &&
            record.Data.GetValueOrDefault("strategyVersionSha256") == version &&
            record.Data.GetValueOrDefault("fundAccountId") == accountId.ToString("D"));
        var result = new List<PaperOrderEvidence>();
        foreach (var claim in owned)
        {
            var orderId = claim.Data["orderId"];
            var receipts = await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = orderId,
                CaseType = OrderIntentJournal.PaperFillCaseType
            }, ct).ConfigureAwait(false);
            var fills = receipts
                .Where(receipt => receipt.Data.GetValueOrDefault("financialAccountId") == accountId.ToString("D"))
                .Select(static receipt => new SimulatedFillHandoff(
                    JsonSerializer.Deserialize<TradeExecutedEvent>(receipt.Data["fill"]) ??
                        throw new InvalidDataException("The simulated handoff receipt has no fill."),
                    receipt.RecordHashSha256))
                .ToArray();
            result.Add(new PaperOrderEvidence(
                Guid.Parse(claim.Data["intentId"]),
                orderId,
                OrderIntentJournal.ReadResult(resultsByCase[claim.CaseId].ToArray()),
                fills));
        }
        return result;
    }
}

public sealed record PaperOrderEvidence(Guid IntentId, string OrderId, OrderResult? Admission,
    IReadOnlyList<SimulatedFillHandoff> Handoffs);

public sealed record SimulatedFillHandoff(TradeExecutedEvent Fill, string ReceiptSha256);
