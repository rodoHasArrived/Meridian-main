using FluentAssertions;
using FsCheck.Xunit;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Tests.Execution.Enhancements;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution;

public sealed partial class BrokerageOrderRecoveryTests
{
    [Property(MaxTest = 32)]
    public void FaultReplay_PreservesDispatchAndExactLedgerEffects(int seed)
    {
        RunFaultReplayAsync(seed).GetAwaiter().GetResult();
    }

    private static async Task RunFaultReplayAsync(int seed)
    {
        var random = new Random(seed);
        var losesAcknowledgment = random.Next(2) == 0;
        var firstQuantity = random.Next(1, 5);
        var finalQuantity = random.Next(firstQuantity + 1, 11);
        var duplicateCount = random.Next(1, 5);
        var request = Request("fault-replay");
        var orderId = request.ClientOrderId!;
        var accepted = Report(orderId, OrderStatus.Accepted, 0m);
        var partial = Report(orderId, OrderStatus.PartiallyFilled, firstQuantity);
        var cancelled = Report(orderId, OrderStatus.Cancelled, firstQuantity);
        var late = Report(orderId, finalQuantity == 10 ? OrderStatus.Filled : OrderStatus.PartiallyFilled, finalQuantity);

        using var directory = new RecoveryDirectory();
        var posting = new TradeFillLedgerPostingContext("fault-replay",
            Guid.Parse("edac7ddd-842f-4999-b18f-c726d1563faf"),
            Guid.Parse("b3f75a60-c378-4dbe-9494-ea634cca3c74"),
            Guid.Parse("ee446b0c-f31b-493d-a81b-d94b2ca094f7"));
        var postingTarget = new EventDrivenDecouplingTests.InMemoryTradeFillLedgerPostingTarget();
        LedgerPostingConsumer NewConsumer() => new(postingTarget, NullLogger<LedgerPostingConsumer>.Instance,
            new WalTradeFillPostingStore(new TradeFillPostingStoreOptions(Path.GetDirectoryName(directory.Path)!, posting),
                NullLogger<WalTradeFillPostingStore>.Instance), posting,
            securityValidationGate: new EventDrivenDecouplingTests.PassingSecurityValidationGate());

        var gateway = new RecoveryGateway
        {
            SubmitResponse = (_, _) => losesAcknowledgment
                ? Task.FromException<ExecutionReport>(new IOException("Synthetic broker acknowledgement interrupted."))
                : Task.FromResult(accepted)
        };
        await using (var consumer = NewConsumer())
        await using (var oms = CreateOms(gateway, recoveryPath: directory.Path, publisher: consumer))
        {
            var placed = await oms.PlaceOrderAsync(request);
            placed.Success.Should().Be(!losesAcknowledgment);
            if (losesAcknowledgment)
                oms.GetRecoveryOrders(FundAccountId).Should().ContainSingle();
            (await oms.PlaceOrderAsync(request)).Success.Should().BeFalse();
            gateway.SubmitCount.Should().Be(1);

            await gateway.PublishAsync(partial);
            (await ReadFillAsync(oms)).FilledQuantity.Should().Be(firstQuantity);
            for (var index = 0; index < duplicateCount; index++)
                await gateway.PublishAsync(random.Next(2) == 0 ? partial : accepted);
            (await DrainThroughMarkerAsync(oms, gateway)).Should().BeEmpty();
            oms.GetOrder(orderId)!.FilledQuantity.Should().Be(firstQuantity);
        }

        var reconnect = new RecoveryGateway();
        await using (var consumer = NewConsumer())
        await using (var recovered = CreateOms(reconnect, recoveryPath: directory.Path, publisher: consumer))
        {
            recovered.GetOrder(orderId)!.FilledQuantity.Should().Be(firstQuantity);
            (await recovered.PlaceOrderAsync(request)).Success.Should().BeFalse();
            reconnect.SubmitCount.Should().Be(0);

            await recovered.ReconcileRecoveryOrderAsync(cancelled);
            recovered.GetOrder(orderId)!.Status.Should().Be(OrderStatus.Cancelled);
            // The venue executed this remainder before cancel confirmation but its report was
            // missing during the disconnect. It may increase filled quantity, even after cancel.
            await recovered.ReconcileRecoveryOrderAsync(late);
            (await ReadFillAsync(recovered)).FilledQuantity.Should().Be(finalQuantity - firstQuantity);
            for (var index = 0; index < duplicateCount; index++)
                await reconnect.PublishAsync(random.Next(2) == 0 ? late : partial);
            (await DrainThroughMarkerAsync(recovered, reconnect)).Should().BeEmpty();
            recovered.GetOrder(orderId)!.FilledQuantity.Should().Be(finalQuantity);
            recovered.GetOrder(orderId)!.Status.Should().Be(finalQuantity == 10 ? OrderStatus.Filled : OrderStatus.Cancelled);
        }

        postingTarget.RetainedEntries.Should().HaveCount(2);
        postingTarget.RetainedEntries.Sum(entry => entry.Lines.Sum(line => line.Debit))
            .Should().Be(finalQuantity * 150m);
        postingTarget.RetainedEntries.Sum(entry => entry.Lines.Sum(line => line.Credit))
            .Should().Be(finalQuantity * 150m);
    }
}
