using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Consolidation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Moq;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed class ConsolidationBookCreationTests
{
    [Fact]
    public async Task NamedDedicatedBooksCanCoexistOnlyWithGovernedPolicyValidationAndRetryKeepsIdentity()
    {
        var rows = new List<LedgerBookRecord>();
        var store = Memory(rows);
        var validator = new ConsolidationBookPolicyValidator(new AccountingPolicyService());
        var service = new PostgresLedgerBookService(store.Object, consolidationPolicies: validator);
        var root = Guid.NewGuid();
        var oldBook = await service.CreateBookAsync(Request(root, "Old reporting"));
        var newBook = await service.CreateBookAsync(Request(root, "New reporting"));
        Assert.NotEqual(oldBook.LedgerBookId, newBook.LedgerBookId);
        Assert.Equal(newBook.LedgerBookId, (await service.CreateBookAsync(Request(root, "New reporting"))).LedgerBookId);
        Assert.Equal(2, rows.Count);
        await Assert.ThrowsAsync<LedgerBookValidationException>(() => service.CreateBookAsync(Request(root, "New reporting") with { BaseCurrency = "EUR" }));
    }

    [Fact]
    public async Task MissingPolicyAuthorityCannotCreateAdditionalReportingBook()
    {
        var store = Memory([]);
        var service = new PostgresLedgerBookService(store.Object);
        await Assert.ThrowsAsync<LedgerBookValidationException>(() => service.CreateBookAsync(Request(Guid.NewGuid(), "Unverified reporting")));
        store.Verify(x => x.SaveLedgerBookAsync(It.IsAny<LedgerBookRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrdinaryEntityPrimaryBookRetainsOriginalScopeIdempotence()
    {
        var rows = new List<LedgerBookRecord>();
        var service = new PostgresLedgerBookService(Memory(rows).Object);
        var entity = Guid.NewGuid();
        var request = new CreateLedgerBookRequest("fund", entity, FundStructureNodeKindDto.Entity, "Entity primary", "USD");
        var first = await service.CreateBookAsync(request);
        var second = await service.CreateBookAsync(request with { DisplayName = "Duplicate ordinary primary" });
        Assert.Equal(first.LedgerBookId, second.LedgerBookId);
        Assert.Single(rows);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task PartialUniqueIndexPermitsOnlyExactFundPrimaryConsolidationPolicyPairs()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var root = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        LedgerBookRecord Book(FundStructureNodeKindDto kind, string name, string policy, string version)
            => new(Guid.NewGuid(), "transition-fund", root, kind, name, "USD", timestamp, timestamp,
                AccountingPolicyId: policy, AccountingPolicyVersion: version);
        await database.JournalStore.SaveLedgerBookAsync(Book(FundStructureNodeKindDto.Fund, "Old reporting", "consolidation-v2", "w10-v2"));
        await database.JournalStore.SaveLedgerBookAsync(Book(FundStructureNodeKindDto.Fund, "New reporting", "consolidation-v3", "w10-v3"));
        await database.JournalStore.SaveLedgerBookAsync(Book(FundStructureNodeKindDto.Fund, "Ordinary fund", "legacy-v1", "legacy-v1"));
        var ordinaryDuplicate = await Assert.ThrowsAsync<PostgresException>(() => database.JournalStore.SaveLedgerBookAsync(Book(FundStructureNodeKindDto.Fund, "Ordinary duplicate", "arbitrary-consolidation", "w10-v2")));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ordinaryDuplicate.SqlState);
        var entity = Book(FundStructureNodeKindDto.Entity, "Source primary", "consolidation-v2", "w10-v2") with { FundStructureNodeId = Guid.NewGuid() };
        await database.JournalStore.SaveLedgerBookAsync(entity);
        var sourceDuplicate = await Assert.ThrowsAsync<PostgresException>(() => database.JournalStore.SaveLedgerBookAsync(entity with { LedgerBookId = Guid.NewGuid(), DisplayName = "Second source primary" }));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, sourceDuplicate.SqlState);
    }

    private static CreateLedgerBookRequest Request(Guid root, string name)
        => new("fund", root, FundStructureNodeKindDto.Fund, name, "USD", AccountingPolicyId: "consolidation-v2", AccountingPolicyVersion: "w10-v2");
    private static Mock<ILedgerJournalStore> Memory(List<LedgerBookRecord> rows)
    {
        var store = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        store.Setup(x => x.ListLedgerBooksAsync(It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<FundStructureNodeKindDto?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? fund, Guid? node, FundStructureNodeKindDto? kind, CancellationToken _) => (IReadOnlyList<LedgerBookRecord>)rows.Where(x => x.FundProfileId == fund && x.FundStructureNodeId == node && x.FundStructureNodeKind == kind).ToArray());
        store.Setup(x => x.SaveLedgerBookAsync(It.IsAny<LedgerBookRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LedgerBookRecord book, CancellationToken _) => { rows.Add(book); return book; });
        return store;
    }
}
