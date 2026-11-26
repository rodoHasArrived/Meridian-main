using System.Text.Json;
using Meridian.FinancialOperations.Ledger;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.DirectLending;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.DirectLending;

/// <summary>
/// Processes retained direct-lending outbox work through command, query, and accounting services.
/// </summary>
public sealed class DirectLendingOutboxDispatcher : BackgroundService
{
    private const int MinimumOutboxBatchSize = 1;
    private const int MaximumOutboxBatchSize = 5000;
    private const int MinimumOutboxPollIntervalSeconds = 1;
    private const int MaximumOutboxPollIntervalSeconds = 3600;

    private readonly IDirectLendingOperationsStore _operationsStore;
    private readonly IDirectLendingCommandService _commandService;
    private readonly IDirectLendingQueryService _queryService;
    private readonly IAccountingPolicyService _accountingPolicyService;
    private readonly int _outboxBatchSize;
    private readonly TimeSpan _outboxPollInterval;
    private readonly ILogger<DirectLendingOutboxDispatcher> _logger;

    public DirectLendingOutboxDispatcher(
        IDirectLendingOperationsStore operationsStore,
        IDirectLendingCommandService commandService,
        IDirectLendingQueryService queryService,
        DirectLendingOptions options,
        ILogger<DirectLendingOutboxDispatcher> logger,
        IAccountingPolicyService? accountingPolicyService = null)
    {
        _operationsStore = operationsStore;
        _commandService = commandService;
        _queryService = queryService;
        _outboxBatchSize = NormalizeOutboxBatchSize(options.OutboxBatchSize);
        _outboxPollInterval = NormalizeOutboxPollInterval(options.OutboxPollIntervalSeconds);
        _logger = logger;
        _accountingPolicyService = accountingPolicyService ?? new AccountingPolicyService();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await _operationsStore.GetPendingOutboxMessagesAsync(_outboxBatchSize, stoppingToken).ConfigureAwait(false);
                if (messages.Count == 0)
                {
                    await Task.Delay(_outboxPollInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var message in messages)
                {
                    try
                    {
                        await ProcessAsync(message, stoppingToken).ConfigureAwait(false);
                        await _operationsStore.MarkOutboxProcessedAsync(message.OutboxMessageId, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Direct lending outbox processing failed for {MessageId} ({Topic}).", message.OutboxMessageId, message.Topic);
                        await _operationsStore.MarkOutboxFailedAsync(message.OutboxMessageId, ex.Message, stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Direct lending outbox polling failed; retrying in {DelaySeconds} seconds.",
                    _outboxPollInterval.TotalSeconds);
                try
                {
                    await Task.Delay(_outboxPollInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    public static int NormalizeOutboxBatchSize(int configuredBatchSize)
        => Math.Clamp(configuredBatchSize, MinimumOutboxBatchSize, MaximumOutboxBatchSize);

    public static TimeSpan NormalizeOutboxPollInterval(int configuredPollIntervalSeconds)
        => TimeSpan.FromSeconds(Math.Clamp(
            configuredPollIntervalSeconds,
            MinimumOutboxPollIntervalSeconds,
            MaximumOutboxPollIntervalSeconds));

    private async Task ProcessAsync(DirectLendingOutboxMessage message, CancellationToken ct)
    {
        var envelope = JsonSerializer.Deserialize<DirectLendingOutboxEnvelope>(message.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Unable to deserialize direct lending outbox payload for {message.OutboxMessageId}.");

        switch (message.Topic)
        {
            case "direct-lending.projection.requested":
                var projection = await _commandService.RequestProjectionAsync(
                    envelope.LoanId,
                    envelope.EffectiveDate,
                    new DirectLendingCommandMetadataDto(envelope.CommandId, envelope.CorrelationId, envelope.SourceEventId, envelope.SourceSystem, ReplayFlag: true),
                    ct).ConfigureAwait(false);
                if (projection.Error is { } projectionError)
                    throw new DirectLendingCommandException(projectionError);
                break;

            case "direct-lending.journal.requested":
                await ProcessJournalAsync(envelope, ct).ConfigureAwait(false);
                break;

            case "direct-lending.reconciliation.requested":
                var reconciliation = await _commandService.ReconcileAsync(
                    envelope.LoanId,
                    new DirectLendingCommandMetadataDto(envelope.CommandId, envelope.CorrelationId, envelope.SourceEventId, envelope.SourceSystem, ReplayFlag: true),
                    ct).ConfigureAwait(false);
                if (reconciliation.Error is { } reconciliationError)
                    throw new DirectLendingCommandException(reconciliationError);
                break;

            case "direct-lending.asset-operations.requested":
                var publication = await _commandService.PublishAssetOperationsAsync(envelope.LoanId,
                    new DirectLendingCommandMetadataDto(envelope.CommandId, envelope.CorrelationId,
                        envelope.SourceEventId, envelope.SourceSystem, ReplayFlag: true), ct).ConfigureAwait(false);
                if (publication.Error is { } publicationError)
                    throw new DirectLendingCommandException(publicationError);
                break;

            default:
                throw new InvalidOperationException($"Unsupported direct lending outbox topic '{message.Topic}'.");
        }
    }

    private async Task ProcessJournalAsync(DirectLendingOutboxEnvelope envelope, CancellationToken ct)
    {
        var history = await _queryService.GetHistoryAsync(envelope.LoanId, ct).ConfigureAwait(false);
        var sourceEvent = history.FirstOrDefault(item => item.EventId == envelope.SourceEventId);
        if (sourceEvent is null)
        {
            throw new InvalidOperationException($"Journal source event '{envelope.SourceEventId}' is unavailable; retain the delivery for recovery.");
        }

        var existing = await _queryService.GetJournalsAsync(envelope.LoanId, ct).ConfigureAwait(false);
        if (existing.Any(item => item.SourceEventId == envelope.SourceEventId))
        {
            return;
        }

        var contract = await _queryService.GetLoanAsync(envelope.LoanId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            throw new InvalidOperationException($"Journal loan '{envelope.LoanId}' is unavailable; retain the delivery for recovery.");
        }

        var accountingDate = envelope.EffectiveDate ?? contract.EffectiveDate;
        var policy = await _accountingPolicyService
            .ResolvePolicyAsync(new AccountingPolicyQuery(AccountingBasisKindDto.Primary, accountingDate), ct)
            .ConfigureAwait(false);
        var lineDimensionsJson = JsonSerializer.Serialize(new
        {
            accountingBasis = policy.AccountingBasis.ToString(),
            accountingPolicyId = policy.PolicyId,
            accountingPolicyVersion = policy.Version,
            ruleId = sourceEvent.EventType,
            ruleVersion = policy.Version,
            sourceEventId = envelope.SourceEventId
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var payload = JsonDocument.Parse(sourceEvent.PayloadJson);
        var lines = new List<JournalLineDto>();
        var description = sourceEvent.EventType switch
        {
            "loan.drawdown-booked" => "Drawdown funding",
            "loan.daily-accrual-posted" => "Daily accrual",
            "loan.daily-accrual-reversed" => "Daily accrual reversal",
            "loan.pik-capitalized" => "PIK capitalization",
            "loan.principal-payment-applied" => "Principal receipt",
            "loan.mixed-payment-applied" => "Mixed payment",
            "loan.unapplied-cash-applied" => "Unapplied cash applied",
            "loan.unapplied-cash-refunded" => "Unapplied cash refund",
            "loan.fee-assessed" => "Fee assessment",
            "loan.write-off-applied" => "Write-off",
            "loan.prepayment-penalty-charged" => "Prepayment penalty",
            "loan.discount-premium-amortized" or "DiscountPremiumAmortized" => "Discount/premium amortization",
            "loan.restructured" or "LoanRestructured" => "Restructuring",
            _ => sourceEvent.EventType
        };
        foreach (var line in DirectLendingJournalLines.ForEvent(
            sourceEvent.EventType,
            payload.RootElement,
            envelope.LoanId.ToString("D")))
        {
            // ForEvent retains the role and resolves its historical name/type in the one registry.
            DirectLendingJournalLines.RequiredRole(line);
            lines.Add(new JournalLineDto(
                Guid.NewGuid(),
                lines.Count + 1,
                line.Account,
                line.Debit,
                line.Credit,
                contract.CurrentTerms.BaseCurrency,
                lineDimensionsJson));
        }

        if (lines.Count == 0)
        {
            return;
        }

        // Fail closed: a drafted journal that does not balance (or carries a negative amount) is
        // never saved; the delivery is retained for recovery instead.
        if (lines.Any(line => line.DebitAmount < 0m || line.CreditAmount < 0m))
        {
            throw new LedgerValidationException(
                $"Direct lending journal draft for event '{envelope.SourceEventId}' ({sourceEvent.EventType}) contains a negative amount.");
        }

        var totalDebits = lines.Sum(line => line.DebitAmount);
        var totalCredits = lines.Sum(line => line.CreditAmount);
        if (totalDebits != totalCredits)
        {
            throw new LedgerValidationException(
                $"Direct lending journal draft for event '{envelope.SourceEventId}' ({sourceEvent.EventType}) does not balance: debits {totalDebits}, credits {totalCredits}.");
        }

        var entry = new JournalEntryDto(
            Guid.NewGuid(),
            envelope.LoanId,
            accountingDate,
            accountingDate,
            envelope.SourceEventId,
            sourceEvent.EventType,
            policy.AccountingBasis.ToString(),
            description,
            DateTimeOffset.UtcNow,
            null,
            JournalEntryStatus.Draft,
            lines);

        await _operationsStore.SaveJournalEntryAsync(entry, ct).ConfigureAwait(false);
    }

    private sealed record DirectLendingOutboxEnvelope(
        Guid LoanId,
        Guid SourceEventId,
        string EventType,
        DateOnly? EffectiveDate,
        long ServicingRevision,
        Guid? CommandId,
        Guid? CorrelationId,
        Guid? CausationId,
        string? SourceSystem);
}
