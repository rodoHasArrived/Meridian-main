using System.Text.Json;
using Meridian.FinancialOperations.Ledger;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.DirectLending;

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
                await _commandService.RequestProjectionAsync(
                    envelope.LoanId,
                    envelope.EffectiveDate,
                    new DirectLendingCommandMetadataDto(envelope.CommandId, envelope.CorrelationId, envelope.SourceEventId, envelope.SourceSystem, ReplayFlag: true),
                    ct).ConfigureAwait(false);
                break;

            case "direct-lending.journal.requested":
                await ProcessJournalAsync(envelope, ct).ConfigureAwait(false);
                break;

            case "direct-lending.reconciliation.requested":
                await _commandService.ReconcileAsync(
                    envelope.LoanId,
                    new DirectLendingCommandMetadataDto(envelope.CommandId, envelope.CorrelationId, envelope.SourceEventId, envelope.SourceSystem, ReplayFlag: true),
                    ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task ProcessJournalAsync(DirectLendingOutboxEnvelope envelope, CancellationToken ct)
    {
        var history = await _queryService.GetHistoryAsync(envelope.LoanId, ct).ConfigureAwait(false);
        var sourceEvent = history.FirstOrDefault(item => item.EventId == envelope.SourceEventId);
        if (sourceEvent is null)
        {
            return;
        }

        var existing = await _queryService.GetJournalsAsync(envelope.LoanId, ct).ConfigureAwait(false);
        if (existing.Any(item => item.SourceEventId == envelope.SourceEventId))
        {
            return;
        }

        var contract = await _queryService.GetLoanAsync(envelope.LoanId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return;
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
        var description = sourceEvent.EventType;
        switch (sourceEvent.EventType)
        {
            case "loan.drawdown-booked":
                var drawdownAmount = GetRequiredDecimal(payload.RootElement, "amount", "Amount");
                description = "Drawdown funding";
                lines.Add(new JournalLineDto(Guid.NewGuid(), 1, "LoanPrincipal", drawdownAmount, 0m, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                lines.Add(new JournalLineDto(Guid.NewGuid(), 2, "Cash", 0m, drawdownAmount, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                break;

            case "loan.daily-accrual-posted":
                var interest = GetRequiredDecimal(payload.RootElement, "interestAmount", "InterestAmount");
                var commitmentFee = GetRequiredDecimal(payload.RootElement, "commitmentFeeAmount", "CommitmentFeeAmount");
                var penalty = GetDecimal(payload.RootElement, "penaltyAmount", "PenaltyAmount");
                var pikInterest = GetDecimal(payload.RootElement, "pikInterestAmount", "PikInterestAmount");
                description = "Daily accrual";
                if (interest > 0m)
                {
                    lines.Add(new JournalLineDto(Guid.NewGuid(), lines.Count + 1, "AccruedInterestReceivable", interest, 0m, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                    lines.Add(new JournalLineDto(Guid.NewGuid(), lines.Count + 1, "InterestIncome", 0m, interest, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                }

                // PIK interest capitalizes into loan principal instead of a cash receivable.
                if (pikInterest > 0m)
                {
                    lines.Add(new JournalLineDto(Guid.NewGuid(), lines.Count + 1, "LoanPrincipal", pikInterest, 0m, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                    lines.Add(new JournalLineDto(Guid.NewGuid(), lines.Count + 1, "InterestIncome", 0m, pikInterest, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                }

                if (commitmentFee > 0m)
                {
                    lines.Add(new JournalLineDto(Guid.NewGuid(), lines.Count + 1, "CommitmentFeeReceivable", commitmentFee, 0m, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                    lines.Add(new JournalLineDto(Guid.NewGuid(), lines.Count + 1, "CommitmentFeeIncome", 0m, commitmentFee, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                }

                if (penalty > 0m)
                {
                    lines.Add(new JournalLineDto(Guid.NewGuid(), lines.Count + 1, "PenaltyReceivable", penalty, 0m, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                    lines.Add(new JournalLineDto(Guid.NewGuid(), lines.Count + 1, "PenaltyIncome", 0m, penalty, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                }
                break;

            case "loan.mixed-payment-applied":
                var paymentAmount = GetRequiredDecimal(payload.RootElement, "amount", "Amount");
                description = "Mixed payment";
                lines.Add(new JournalLineDto(Guid.NewGuid(), 1, "Cash", paymentAmount, 0m, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                lines.Add(new JournalLineDto(Guid.NewGuid(), 2, "LoanAndAccrualsClearing", 0m, paymentAmount, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                break;

            case "loan.fee-assessed":
                var feeAmount = GetRequiredDecimal(payload.RootElement, "amount", "Amount");
                description = "Fee assessment";
                lines.Add(new JournalLineDto(Guid.NewGuid(), 1, "FeeReceivable", feeAmount, 0m, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                lines.Add(new JournalLineDto(Guid.NewGuid(), 2, "FeeIncome", 0m, feeAmount, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                break;

            case "loan.write-off-applied":
                var writeOffAmount = GetRequiredDecimal(payload.RootElement, "appliedAmount", "AppliedAmount", "amount", "Amount");
                description = "Write-off";
                lines.Add(new JournalLineDto(Guid.NewGuid(), 1, "WriteOffExpense", writeOffAmount, 0m, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                lines.Add(new JournalLineDto(Guid.NewGuid(), 2, "LoanPrincipal", 0m, writeOffAmount, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                break;

            case "loan.prepayment-penalty-charged":
                var prepaymentPenaltyAmount = GetRequiredDecimal(payload.RootElement, "penaltyAmount", "PenaltyAmount");
                description = "Prepayment penalty";
                if (prepaymentPenaltyAmount > 0m)
                {
                    lines.Add(new JournalLineDto(Guid.NewGuid(), 1, "PenaltyReceivable", prepaymentPenaltyAmount, 0m, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                    lines.Add(new JournalLineDto(Guid.NewGuid(), 2, "PenaltyIncome", 0m, prepaymentPenaltyAmount, contract.CurrentTerms.BaseCurrency, lineDimensionsJson));
                }
                break;
        }

        if (lines.Count == 0)
        {
            return;
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

    private static decimal GetDecimal(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDecimal();
            }
        }

        return 0m;
    }

    private static decimal GetRequiredDecimal(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDecimal();
            }
        }

        throw new InvalidOperationException($"Direct lending outbox payload is missing numeric property '{propertyNames[0]}'.");
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
