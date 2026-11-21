using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;

namespace Meridian.Execution.OrderManagement;

/// <summary>Claims a paper order before dispatch and retains its first known outcome.</summary>
internal sealed class OrderIntentJournal(IOperationalCaseHistoryStore history)
{
    internal const string CaseType = "paper-order-intent";
    public const string PaperFillCaseType = "paper-order-fill-handoff";

    internal async Task RetainFillAsync(TradeExecutedEvent fill)
    {
        var payload = JsonSerializer.Serialize(fill);
        await history.AppendAsync(new OperationalCaseHistoryAppendRequest
        {
            CaseId = fill.OrderId,
            CaseType = PaperFillCaseType,
            HistoryEventId = $"paper-order-fill:{fill.FillId:N}",
            EventType = "SimulatedFillAccepted",
            OccurredAtUtc = fill.OccurredAt.ToUniversalTime(),
            ActorId = "order-management-system",
            Reason = "Retained a simulated accounting handoff for reconciliation.",
            CorrelationId = fill.OrderId,
            InputHashSha256 = Sha256Digest.ComputeUtf8(payload),
            Data = new Dictionary<string, string>
            {
                ["fillId"] = fill.FillId.ToString("D"),
                ["financialAccountId"] = fill.FinancialAccountId ?? "",
                ["dataProvenance"] = DataProvenance.Simulated.Token(),
                ["fill"] = payload
            }
        }, CancellationToken.None).ConfigureAwait(false);
    }

    internal static string CaseId(OrderIntent intent) =>
        "paper-order-intent:" + Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(
            new { TenantId = intent.TenantId.Trim(), CompanyId = intent.CompanyId.Trim(), intent.FundAccountId, intent.IntentId }));

    internal ValueTask<IReadOnlyList<OperationalCaseHistoryRecord>> ReadAsync(string caseId, CancellationToken ct) =>
        history.ReadAsync(new OperationalCaseHistoryQuery { CaseType = CaseType, CaseId = caseId }, ct);

    internal async Task<bool> HasIntentForRunAsync(string runId, CancellationToken ct)
    {
        var records = await history.ReadAsync(new OperationalCaseHistoryQuery { CaseType = CaseType }, ct).ConfigureAwait(false);
        return records.Any(record => record.EventType == "IntentClaimed" &&
            string.Equals(record.Data.GetValueOrDefault("runId"), runId, StringComparison.Ordinal));
    }

    internal async Task<bool> TryClaimAsync(
        string caseId, string inputHash, OrderIntent intent, string orderId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["expectedPreviousCaseSequence"] = "0",
            ["claimOwner"] = Guid.NewGuid().ToString("N"),
            ["intentId"] = intent.IntentId.ToString("D"),
            ["orderId"] = orderId,
            ["origin"] = intent.StrategyAuthority is null ? "Operator" : "Strategy",
            ["runId"] = intent.StrategyAuthority?.RunId ?? "",
            ["strategyVersionSha256"] = intent.StrategyAuthority?.StrategyVersionSha256 ?? "",
            ["sessionId"] = intent.StrategyAuthority?.PaperSessionId ?? "",
            ["fundAccountId"] = intent.FundAccountId.ToString("D")
        };

        try
        {
            await history.AppendAsync(new OperationalCaseHistoryAppendRequest
            {
                CaseId = caseId,
                CaseType = CaseType,
                HistoryEventId = $"{caseId}:claim",
                EventType = "IntentClaimed",
                OccurredAtUtc = now,
                ActorId = intent.Actor,
                Reason = "Claimed this scoped paper intent before admission.",
                CorrelationId = orderId,
                InputHashSha256 = inputHash,
                Data = data
            }, ct).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException)
        {
            if ((await ReadAsync(caseId, ct).ConfigureAwait(false)).Count > 0)
                return false;
            throw;
        }
    }

    internal async Task RetainResultAsync(string caseId, string inputHash, OrderIntent intent, OrderResult result)
    {
        await history.AppendAsync(new OperationalCaseHistoryAppendRequest
        {
            CaseId = caseId,
            CaseType = CaseType,
            HistoryEventId = $"{caseId}:result",
            EventType = "IntentResult",
            OccurredAtUtc = DateTimeOffset.UtcNow,
            ActorId = intent.Actor,
            Reason = "Retained the paper intent admission result.",
            CorrelationId = result.OrderId,
            InputHashSha256 = inputHash,
            Data = new Dictionary<string, string> { ["result"] = JsonSerializer.Serialize(result) }
        }, CancellationToken.None).ConfigureAwait(false);
    }

    internal static OrderResult? ReadResult(IReadOnlyList<OperationalCaseHistoryRecord> records)
    {
        var result = records.LastOrDefault(record => record.EventType == "IntentResult");
        return result is null ? null : JsonSerializer.Deserialize<OrderResult>(result.Data["result"]);
    }
}
