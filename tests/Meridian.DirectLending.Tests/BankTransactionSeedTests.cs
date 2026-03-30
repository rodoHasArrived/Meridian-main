using FluentAssertions;
using Meridian.Application.Banking;
using Meridian.Contracts.Banking;

namespace Meridian.DirectLending.Tests;

public sealed class BankTransactionSeedTests
{
    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldSeedTransactionsForAllKnownEntities()
    {
        var service = new InMemoryBankingService();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        // Pre-register entities by seeding them explicitly
        var result = await service.SeedBankTransactionsAsync(
            new BankTransactionSeedRequest(
                EntityIds: [id1, id2],
                CountPerEntity: 3,
                FromDate: new DateOnly(2025, 7, 1),
                ToDate: new DateOnly(2025, 12, 31)));

        result.EntitiesProcessed.Should().Be(2);
        result.TransactionsSeeded.Should().Be(6);
        result.ProcessedEntityIds.Should().Contain(id1);
        result.ProcessedEntityIds.Should().Contain(id2);

        var t1 = await service.GetBankTransactionsAsync(id1);
        var t2 = await service.GetBankTransactionsAsync(id2);
        t1.Should().HaveCount(3);
        t2.Should().HaveCount(3);
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldSeedOnlySpecifiedEntities()
    {
        var service = new InMemoryBankingService();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var result = await service.SeedBankTransactionsAsync(
            new BankTransactionSeedRequest([id1], 5, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30)));

        result.EntitiesProcessed.Should().Be(1);
        result.TransactionsSeeded.Should().Be(5);

        (await service.GetBankTransactionsAsync(id1)).Should().HaveCount(5);
        (await service.GetBankTransactionsAsync(id2)).Should().BeEmpty();
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldProduceDeterministicResults()
    {
        var entityId = Guid.NewGuid();
        var req = new BankTransactionSeedRequest([entityId], 4, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));

        var svc1 = new InMemoryBankingService();
        await svc1.SeedBankTransactionsAsync(req);

        var svc2 = new InMemoryBankingService();
        await svc2.SeedBankTransactionsAsync(req);

        var t1 = await svc1.GetBankTransactionsAsync(entityId);
        var t2 = await svc2.GetBankTransactionsAsync(entityId);

        t1.Should().HaveCount(4);
        t2.Should().HaveCount(4);

        for (var i = 0; i < t1.Count; i++)
        {
            t1[i].Amount.Should().Be(t2[i].Amount);
            t1[i].TransactionType.Should().Be(t2[i].TransactionType);
        }
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldThrow_WhenCountIsZero()
    {
        var service = new InMemoryBankingService();
        var act = () => service.SeedBankTransactionsAsync(new BankTransactionSeedRequest(null, 0, null, null));
        var ex = await Assert.ThrowsAsync<BankingException>(act);
        ex.Message.Should().Contain("CountPerEntity");
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldSetAllAmountsPositiveAndNotVoided()
    {
        var service = new InMemoryBankingService();
        var entityId = Guid.NewGuid();

        await service.SeedBankTransactionsAsync(
            new BankTransactionSeedRequest([entityId], 10, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)));

        var txns = await service.GetBankTransactionsAsync(entityId);
        txns.Should().HaveCount(10);
        txns.Should().OnlyContain(t => t.Amount > 0m);
        txns.Should().OnlyContain(t => !t.IsVoided);
        txns.Should().OnlyContain(t => t.EntityId == entityId);
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldOnlyUseSeedTransactionTypes()
    {
        var service = new InMemoryBankingService();
        var entityId = Guid.NewGuid();

        await service.SeedBankTransactionsAsync(
            new BankTransactionSeedRequest([entityId], 50, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)));

        var txns = await service.GetBankTransactionsAsync(entityId);
        string[] expectedTypes = ["InterestPayment", "PrincipalPayment", "FeePayment", "MixedPayment", "Drawdown"];
        txns.Should().HaveCount(50);
        txns.Should().OnlyContain(t => expectedTypes.Contains(t.TransactionType));
    }

    [Fact]
    public async Task SeedBankTransactionsAsync_ShouldReturnZero_WhenNoEntityIdsAndNoneKnown()
    {
        var service = new InMemoryBankingService();
        var result = await service.SeedBankTransactionsAsync(new BankTransactionSeedRequest(null, 5, null, null));
        result.EntitiesProcessed.Should().Be(0);
        result.TransactionsSeeded.Should().Be(0);
    }
}
