using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    public const string ManualIntentOrderPrefix = "paper-intent-";

    public PaperOrderIntentScope? ScopedPaperBook =>
        _gateway is PaperTradingGateway && _portfolioState is Services.PaperTradingPortfolio { UsesCanonicalAccounting: false }
            ? _options.PaperIntentScope : null;
    public bool SupportsScopedPaperIntents => ScopedPaperBook is not null && _intentJournal is not null;

    public Task<bool> HasRetainedPaperIntentForRunAsync(string runId, CancellationToken ct = default) =>
        _intentJournal?.HasIntentForRunAsync(runId, ct) ?? Task.FromResult(false);

    private Task<OrderResult> SubmitWithoutIntentAsync(
        OrderRequest request, OrderSubmissionAuthority? authority, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? sessionId = null;
        request.Metadata?.TryGetValue("sessionId", out sessionId);
        var scopedBook = ScopedPaperBook;
        var isPaperIntent = request.IntentId.HasValue ||
            request.ClientOrderId?.StartsWith(ManualIntentOrderPrefix, StringComparison.OrdinalIgnoreCase) == true ||
            Services.PaperSessionPersistenceService.IsStrategyRunSessionId(sessionId);
        // Run shutdown owns strategy submission and report admission. In this bounded profile a
        // separate risk-desk command can release a manual ticket only.
        var isApprovedRelease = sessionId is null && authority?.Origin == OrderCommandOrigin.EscalationRelease &&
            scopedBook is not null && request.FundAccountId == scopedBook.FundAccountId &&
            request.IntentId.HasValue && request.ClientOrderId is { } orderId &&
            _approvalReservations.IsApprovedRelease(orderId, request);
        if ((scopedBook is not null || isPaperIntent) && !isApprovedRelease)
            return Task.FromResult(new OrderResult
            {
                OrderId = request.ClientOrderId ?? "blocked",
                Success = false,
                ErrorMessage = sessionId is not null && authority?.Origin == OrderCommandOrigin.EscalationRelease
                    ? "Parked Designer strategy orders require withdrawal in this paper profile."
                    : "Use the scoped paper intent command for this paper book."
            });

        return PlaceOrderSerializedAsync(request, authority, ct);
    }

    /// <inheritdoc />
    public async Task<OrderResult> SubmitIntentAsync(OrderIntent intent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var terms = intent.Terms;
        var authority = intent.StrategyAuthority;
        if (ScopedPaperBook is not { } book || _intentJournal is null)
            return BlockIntent(intent, "Scoped order intents require an in-process paper gateway and durable intent journal.");

        if (intent.IntentId == Guid.Empty || !book.CanAccess(intent.TenantId, intent.CompanyId, intent.FundAccountId) ||
            string.IsNullOrWhiteSpace(intent.Actor) || string.IsNullOrWhiteSpace(intent.TenantId) ||
            string.IsNullOrWhiteSpace(intent.CompanyId) || terms.FundAccountId != intent.FundAccountId)
            return BlockIntent(intent, "The order intent is missing authenticated account scope.");

        if (string.IsNullOrWhiteSpace(terms.Symbol) || terms.Type != OrderType.Market || terms.TimeInForce != TimeInForce.Day ||
            terms.Quantity != decimal.Truncate(terms.Quantity) || terms.OptionContract is not null ||
            terms.Legs is { Count: > 0 } || terms.LimitPrice is not null || terms.StopPrice is not null ||
            terms.TrailPrice is not null || terms.TrailPercent is not null || terms.PositionIntent is not null ||
            terms.SessionIntent is not (null or OrderSessionIntent.RegularOnly))
            return BlockIntent(intent, "Scoped paper intents support market/Day equity orders in whole shares.");

        if (authority is not null && (!Sha256Digest.IsCanonical(authority.StrategyVersionSha256) ||
                string.IsNullOrWhiteSpace(authority.ApprovalReference) || string.IsNullOrWhiteSpace(authority.RunId) ||
                string.IsNullOrWhiteSpace(terms.ClientOrderId) ||
                !string.Equals(terms.StrategyId, authority.StrategyId, StringComparison.Ordinal) ||
                authority.PaperSessionId != Services.PaperSessionPersistenceService.GetStrategyRunSessionId(authority.RunId) ||
                _sessionPersistence?.GetSession(authority.PaperSessionId)?.Summary is not { IsActive: true } session ||
                session.FundAccountId != intent.FundAccountId ||
                !string.Equals(session.StrategyId, authority.StrategyId, StringComparison.Ordinal)))
            return BlockIntent(intent, "The paper strategy has no matching reviewed run and active session.");

        using var operation = EnterOperation();
        var caseId = OrderIntentJournal.CaseId(intent);
        var orderId = authority is null
            ? ManualIntentOrderPrefix + caseId["paper-order-intent:".Length..]
            : terms.ClientOrderId!;
        var request = terms with
        {
            Symbol = terms.Symbol.Trim().ToUpperInvariant(),
            ClientOrderId = orderId,
            StrategyId = authority?.StrategyId,
            Metadata = null,
            IntentId = null
        };
        var inputHash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            intent.Actor,
            Origin = authority,
            Terms = request
        }));

        var records = await _intentJournal.ReadAsync(caseId, ct).ConfigureAwait(false);
        if (records.Count > 0 || !await _intentJournal.TryClaimAsync(caseId, inputHash, intent, orderId, ct).ConfigureAwait(false))
        {
            if (records.Count == 0)
                records = await _intentJournal.ReadAsync(caseId, ct).ConfigureAwait(false);
            if (records.Count == 0 || !Sha256Digest.FixedEquals(records[0].InputHashSha256, inputHash))
                return BlockIntent(intent, "This intent ID already belongs to different order terms or authority.",
                    OrderIntentStatus.Conflict);

            return OrderIntentJournal.ReadResult(records) ?? RequireIntentReconciliation(intent, orderId);
        }

        var grant = new ExecutionAccountGrant(intent.FundAccountId, intent.Actor,
            authority is null ? ExecutionAccountGrantSources.HttpPrincipal : ExecutionAccountGrantSources.StrategyRun,
            DateTimeOffset.UtcNow);
        var commandAuthority = authority is null
            ? OrderSubmissionAuthority.ForOperatorTicket(intent.Actor, grant, intent.IntentId.ToString("D"))
            : OrderSubmissionAuthority.ForStrategyRun(intent.Actor, grant, authority.RunId,
                strategyArtifactId: null, sessionId: authority.PaperSessionId);

        OrderResult result;
        try
        {
            var placed = await PlaceOrderSerializedAsync(
                    OrderAuthorityMetadata.Bind(request with { IntentId = intent.IntentId }, commandAuthority), commandAuthority, ct)
                .ConfigureAwait(false);
            var disposition = !placed.RequiresOrderReconciliation &&
                (placed.Success || placed.RequiresApproval || placed.OrderState?.Status is OrderStatus.Rejected)
                ? OrderIntentStatus.Retained
                : OrderIntentStatus.ReconciliationRequired;
            result = placed with { IntentId = intent.IntentId, IntentStatus = disposition };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Paper order {OrderId} needs reconciliation after its intent was claimed.", orderId);
            result = RequireIntentReconciliation(intent, orderId);
        }

        try
        {
            await _intentJournal.RetainResultAsync(caseId, inputHash, intent, result).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Could not retain the admission receipt for paper order {OrderId}.", orderId);
            return RequireIntentReconciliation(intent, orderId);
        }

        return result;
    }

    private OrderResult RequireIntentReconciliation(OrderIntent intent, string orderId) => new()
    {
        Success = false,
        OrderId = orderId,
        OrderState = GetOrder(orderId),
        RequiresOrderReconciliation = true,
        IntentId = intent.IntentId,
        IntentStatus = OrderIntentStatus.ReconciliationRequired,
        ErrorMessage = "The intent is retained. Verify the existing order outcome before submitting any replacement."
    };

    private static OrderResult BlockIntent(OrderIntent intent, string reason, string status = OrderIntentStatus.Blocked) => new()
    {
        Success = false,
        OrderId = "blocked",
        IntentId = intent.IntentId,
        IntentStatus = status,
        ErrorMessage = reason
    };
}
