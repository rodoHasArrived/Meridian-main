using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using static Meridian.Execution.OrderManagement.OrderRiskSizingPolicy;
using static Meridian.Execution.OrderManagement.OrderAmendmentBook;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    internal PendingOrderAmendment? GetPendingAmendmentForRecovery(string orderId) =>
        _amendmentBook.GetPending(orderId);

    private async Task<PendingOrderAmendment> BeginOrderAmendmentAsync(
        string orderId, OrderState original, OrderModification modification, CancellationToken ct)
    {
        await _brokerageRecoveryReportGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var proposed = original with
            {
                Quantity = modification.NewQuantity ?? original.Quantity,
                LimitPrice = modification.NewLimitPrice ?? original.LimitPrice,
                StopPrice = modification.NewStopPrice ?? original.StopPrice,
                RoutedNotional = ResolveAmendedRoutedNotional(original, modification)
            };
            _brokerIdentities.TryGetActiveId(orderId, out var brokerId);
            // Persist the preassigned replacement reference and prior chain before dispatch,
            // while the book retains snapshot reference identity across later candidate enrichment.
            var pending = _amendmentBook.Begin(orderId, original, proposed, brokerId, modification.NewTrail,
                modification.ReplacementClientOrderId,
                GetBrokerOrderAliases(orderId)
                    .Where(alias => !string.Equals(alias, brokerId, StringComparison.Ordinal)
                        && !string.Equals(alias, orderId, StringComparison.Ordinal))
                    .ToArray());
            var current = _orders[orderId];
            _orders[orderId] = ApplyAmendmentTerms(current, original);
            try
            {
                if (IsTerminal(current.Status))
                    throw new InvalidOperationException("Order became terminal before amendment dispatch.");
                SavePendingAmendment(orderId);
            }
            catch
            {
                _amendmentBook.Remove(orderId);
                throw;
            }
            return pending;
        }
        finally
        {
            _brokerageRecoveryReportGate.Release();
        }
    }

    private async Task AbandonOrderAmendmentAsync(string orderId, PendingOrderAmendment amendment)
    {
        await _brokerageRecoveryReportGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_amendmentBook.RemoveExpected(orderId, amendment))
            {
                _orders[orderId] = ApplyAmendmentTerms(_orders[orderId], amendment.Original);
                SavePendingAmendment(orderId);
            }
        }
        finally
        {
            _brokerageRecoveryReportGate.Release();
        }
    }

    /// <summary>
    /// Applies a gateway's pending amendment report atomically with the check that the amendment
    /// is still unresolved. An authoritative Modified/ModificationRejected report on the live
    /// stream may resolve it at any moment; checking and marking recovery outside the report gate
    /// would tell the caller a confirmed amendment is uncertain and invite a duplicate retry.
    /// </summary>
    /// <returns>null while the outcome remains pending; true when it resolved to the proposed
    /// terms; false when it resolved to the original terms.</returns>
    private async Task<bool?> ProcessPendingAmendmentReportAsync(
        string orderId, PendingOrderAmendment amendment, ExecutionReport report)
    {
        await _brokerageRecoveryReportGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_amendmentBook.IsSameAttempt(orderId, amendment))
            {
                await ProcessGatewayReportCoreAsync(report, CancellationToken.None).ConfigureAwait(false);
                if (_amendmentBook.IsSameAttempt(orderId, amendment))
                {
                    MarkBrokerageRecoveryRequired(orderId);
                    return null;
                }
            }

            // The book retains broker outcome evidence for indistinguishable trail-only terms.
            return _amendmentBook.ResolveOutcome(orderId, amendment);
        }
        finally
        {
            _brokerageRecoveryReportGate.Release();
            TrimRetainedOrdersIfNeeded();
        }
    }

    private void SavePendingAmendment(string orderId)
    {
        if (!_recoveryJournal.IsDispatched(orderId))
            return;
        var pending = GetPendingAmendmentForRecovery(orderId);
        _brokerIdentities.TryGetActiveId(orderId, out var brokerId);
        _recoveryJournal.SavePendingAmendment(_orders[orderId], brokerId, GetBrokerOrderAliases(orderId), pending);
    }

    private PendingOrderAmendment? PrepareAmendmentReport(
        string orderId, ref ExecutionReport report, decimal? locallyAuthorizedQuantity)
    {
        var transition = _amendmentBook.PrepareReport(orderId, report, locallyAuthorizedQuantity);
        report = transition.Report;
        if (transition.UpdatedState is { } updated)
        {
            _orders[orderId] = updated;
            _amendmentBook.RecordOutcome(transition.ResolvedAmendment!, transition.Accepted);
        }
        return transition.ResolvedAmendment;
    }

    private IReadOnlyList<OrderState> GetAmendmentExposureReservations() =>
        _amendmentBook.GetExposureReservations();
}
