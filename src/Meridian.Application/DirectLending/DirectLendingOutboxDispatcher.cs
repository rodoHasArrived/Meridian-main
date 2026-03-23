using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.DirectLending;

public sealed class DirectLendingOutboxDispatcher : BackgroundService
{
    private readonly IDirectLendingOperationsStore _operationsStore;
    private readonly IDirectLendingCommandService _commandService;
    private readonly IDirectLendingQueryService _queryService;
    private readonly DirectLendingOptions _options;
    private readonly ILogger<DirectLendingOutboxDispatcher> _logger;

    public DirectLendingOutboxDispatcher(
        IDirectLendingOperationsStore operationsStore,
        IDirectLendingCommandService commandService,
        IDirectLendingQueryService queryService,
        DirectLendingOptions options,
        ILogger<DirectLendingOutboxDispatcher> logger)
    {
        _operationsStore = operationsStore;
        _commandService = commandService;
        _queryService = queryService;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var messages = await _operationsStore.GetPendingOutboxMessagesAsync(_options.OutboxBatchSize, stoppingToken).ConfigureAwait(false);
            if (messages.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.OutboxPollIntervalSeconds), stoppingToken).ConfigureAwait(false);
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
    }

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

        using var payload = JsonDocument.Parse(sourceEvent.PayloadJson);
        var lines = new List<JournalLineDto>();
        var description = sourceEvent.EventType;
        switch (sourceEvent.EventType)
        {
            case "loan.drawdown-booked":
                var drawdownAmount = payload.RootElement.GetProperty("amount").GetDecimal();
                description = "Drawdown funding";
                lines.Add(new JournalLineDto(Guid.NewGuid(), 1, "LoanPrincipal", drawdownAmount, 0m, contract.CurrentTerms.BaseCurrency, null));
                lines.Add(new JournalLineDto(Guid.NewGuid(), 2, "Cash", 0m, drawdownAmount, contract.CurrentTerms.BaseCurrency, null));
                break;

            case "loan.daily-accrual-posted":
                var interest = payload.RootElement.GetProperty("interestAmount").GetDecimal();
                var commitmentFee = payload.RootElement.GetProperty("commitmentFeeAmount").GetDecimal();
                description = "Daily accrual";
                if (interest > 0m)
                {
                    lines.Add(new JournalLineDto(Guid.NewGuid(), lines.Count + 1, "AccruedInterestReceivable", interest, 0m, contract.CurrentTerms.BaseCurrency, null));
                    lines.Add(new JournalLineDto(Guid.NewGuid(), lines.Count + 1, "InterestIncome", 0m, interest, contract.CurrentTerms.BaseCurrency, null));
                }

                if (commitmentFee > 0m)
                {
                    lines.Add(new JournalLineDto(Guid.NewGuid(), lines.Count + 1, "CommitmentFeeReceivable", commitmentFee, 0m, contract.CurrentTerms.BaseCurrency, null));
                    lines.Add(new JournalLineDto(Guid.NewGuid(), lines.Count + 1, "CommitmentFeeIncome", 0m, commitmentFee, contract.CurrentTerms.BaseCurrency, null));
                }
                break;

            case "loan.mixed-payment-applied":
                var paymentAmount = payload.RootElement.GetProperty("amount").GetDecimal();
                description = "Mixed payment";
                lines.Add(new JournalLineDto(Guid.NewGuid(), 1, "Cash", paymentAmount, 0m, contract.CurrentTerms.BaseCurrency, null));
                lines.Add(new JournalLineDto(Guid.NewGuid(), 2, "LoanAndAccrualsClearing", 0m, paymentAmount, contract.CurrentTerms.BaseCurrency, null));
                break;

            case "loan.fee-assessed":
                var feeAmount = payload.RootElement.GetProperty("amount").GetDecimal();
                description = "Fee assessment";
                lines.Add(new JournalLineDto(Guid.NewGuid(), 1, "FeeReceivable", feeAmount, 0m, contract.CurrentTerms.BaseCurrency, null));
                lines.Add(new JournalLineDto(Guid.NewGuid(), 2, "FeeIncome", 0m, feeAmount, contract.CurrentTerms.BaseCurrency, null));
                break;

            case "loan.write-off-applied":
                var writeOffAmount = payload.RootElement.GetProperty("Amount").GetDecimal();
                description = "Write-off";
                lines.Add(new JournalLineDto(Guid.NewGuid(), 1, "WriteOffExpense", writeOffAmount, 0m, contract.CurrentTerms.BaseCurrency, null));
                lines.Add(new JournalLineDto(Guid.NewGuid(), 2, "LoanPrincipal", 0m, writeOffAmount, contract.CurrentTerms.BaseCurrency, null));
                break;
        }

        if (lines.Count == 0)
        {
            return;
        }

        var entry = new JournalEntryDto(
            Guid.NewGuid(),
            envelope.LoanId,
            envelope.EffectiveDate ?? contract.EffectiveDate,
            envelope.EffectiveDate ?? contract.EffectiveDate,
            envelope.SourceEventId,
            sourceEvent.EventType,
            "Primary",
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
