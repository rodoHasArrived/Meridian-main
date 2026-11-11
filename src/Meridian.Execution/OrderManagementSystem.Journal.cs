using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Execution.Services;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    internal bool IsClientOrderIdUnavailable(string id) => _recoveryJournal.IsDispatched(id)
        || _orders.ContainsKey(id);

    private string JournalScope => _gateway is IBrokerageConnectionState connection
        ? connection.ScopeIdentity ?? throw new InvalidOperationException("Verified broker account/environment is required for journal retention.")
        : $"local-paper:{_gateway.GatewayId}";

    private async Task RecordJournalIntentAsync(OrderRequest request, string kind, CancellationToken ct)
    {
        if (_journal is null)
            return;
        var id = request.ClientOrderId ?? throw new InvalidOperationException("Journal intent requires an assigned client order ID.");
        var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(request, ExecutionJsonContext.Default.OrderRequest));
        var reportingAssetClass = kind == "Dispatch" && _instrumentCoverage is not null
            && await _instrumentCoverage.IsUsdEquityAsync(request, ct).ConfigureAwait(false) ? "USD_US_EQUITY" : null;
        await _journal.AppendAsync(new ExecutionJournalEntry(1, $"{kind}:{id}:{hash}", JournalScope,
            request.FundAccountId, id, kind, DateTimeOffset.UtcNow, Request: request,
            Reference: kind == "Dispatch" ? _marketReferences?.GetReference(request.Symbol, request.Side) : null,
            ReportingAssetClass: reportingAssetClass), ct).ConfigureAwait(false);
    }

    private async Task RecordJournalReportAsync(ExecutionReport report, OrderState? state, CancellationToken ct)
    {
        if (_journal is null)
            return;
        try
        {
            var id = state?.OrderId ?? report.ClientOrderId ?? report.OrderId;
            var scope = JournalScope;
            var original = (await _journal.ReadAsync(ct: ct).ConfigureAwait(false))
                .FirstOrDefault(entry => entry.Scope == scope && entry.OrderId == id && entry.Request is not null);
            var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(report, ExecutionJsonContext.Default.ExecutionReport));
            await _journal.AppendAsync(new ExecutionJournalEntry(1, $"report:{hash}", scope,
                state?.FundAccountId ?? original?.AccountId, id, report.ReportType.ToString(), DateTimeOffset.UtcNow,
                Report: report), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var failure = new ExecutionReportDeliveryException(report, $"Journal retention failed after gateway acknowledgement: {ex.Message}");
            MarkTerminalReportPumpFailure(failure, "Execution journal retention failed; order admission is halted pending recovery.");
            throw failure;
        }
    }

    private async Task RecordJournalStateAsync(OrderState state, CancellationToken ct)
    {
        if (_journal is null)
            return;
        var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(state, ExecutionJsonContext.Default.OrderState));
        await _journal.AppendAsync(new(1, $"state:{hash}", JournalScope, state.FundAccountId,
            state.OrderId, "OrderState", DateTimeOffset.UtcNow, State: state), ct).ConfigureAwait(false);
    }

    private Task RecordJournalOperationAsync(OrderState state, string kind, CancellationToken ct,
        OrderModification? modification = null, string? detail = null) => _journal is null ? Task.CompletedTask
        : _journal.AppendAsync(new(1, $"operation:{Guid.NewGuid():N}", JournalScope, state.FundAccountId,
            state.OrderId, kind, DateTimeOffset.UtcNow, Detail: detail, Modification: modification), ct);
}
