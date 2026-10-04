using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class CloseReadinessSubjectSourceTests
{
    [Theory]
    [InlineData(FundStructureNodeKindDto.Account)]
    [InlineData(FundStructureNodeKindDto.Entity)]
    [InlineData(FundStructureNodeKindDto.Fund)]
    [InlineData(FundStructureNodeKindDto.Sleeve)]
    [InlineData(FundStructureNodeKindDto.Vehicle)]
    public async Task RetainedMembership_BindsAccountAndEntityToBook(FundStructureNodeKindDto kind)
    {
        var account = Account();
        var nodeId = kind switch
        {
            FundStructureNodeKindDto.Account => account.AccountId,
            FundStructureNodeKindDto.Entity => account.EntityId!.Value,
            FundStructureNodeKindDto.Fund => account.FundId!.Value,
            FundStructureNodeKindDto.Sleeve => account.SleeveId!.Value,
            _ => account.VehicleId!.Value
        };
        var (service, scope) = Create(account, kind, nodeId);
        var subject = await service.GetSubjectAsync(scope);
        subject!.Status.Should().Be("Ready");
        subject.Scope.Should().Be(scope);
        subject.RecordIds.Should().Contain(account.AccountId.ToString("D"));
        subject.EvidenceVersion.Should().HaveLength(64);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("entity")]
    [InlineData("fund")]
    [InlineData("inactive")]
    [InlineData("expired")]
    [InlineData("unknown-node")]
    public async Task IncorrectOwnership_BlocksThenRetainedRepairRestoresSubject(string defect)
    {
        var retained = Account();
        var current = defect switch
        {
            "account" => retained with { AccountId = Guid.NewGuid() },
            "entity" => retained with { EntityId = Guid.NewGuid() },
            "fund" => retained with { FundId = Guid.NewGuid() },
            "inactive" => retained with { IsActive = false },
            "expired" => retained with { EffectiveTo = DateTimeOffset.UtcNow.AddDays(-1) },
            _ => retained
        };
        var book = new LedgerBookDto(Guid.NewGuid(), "named-fund", retained.FundId!.Value,
            defect == "unknown-node" ? FundStructureNodeKindDto.Organization : FundStructureNodeKindDto.Fund,
            "Primary", "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var scope = new CloseReadinessScopeDto(book.FundProfileId, book.LedgerBookId, retained.AccountId,
            retained.EntityId!.Value.ToString("D"), "2026-06");
        var accounts = new Mock<IAccountQueryService>();
        accounts.Setup(x => x.GetAccountAsync(retained.AccountId, It.IsAny<CancellationToken>())).ReturnsAsync(() => current);
        var books = new Mock<ILedgerBookService>();
        books.Setup(x => x.GetBookAsync(book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(() => book);
        var service = new CloseReadinessSubjectSource(books.Object, accounts.Object);

        var blocked = await service.GetSubjectAsync(scope);
        blocked!.Status.Should().Be("ScopeMismatch");
        current = retained;
        book = book with { FundStructureNodeKind = FundStructureNodeKindDto.Fund };
        var repaired = await service.GetSubjectAsync(scope);
        repaired!.Status.Should().Be("Ready");
        repaired.EvidenceVersion.Should().NotBe(blocked.EvidenceVersion);
    }

    [Fact]
    public async Task MissingAccount_DoesNotAttestEchoedCallerScope()
    {
        var account = Account();
        var (service, scope) = Create(account, FundStructureNodeKindDto.Account, account.AccountId);
        (await service.GetSubjectAsync(scope with { FundAccountId = Guid.NewGuid() })).Should().BeNull();
        (await new CloseReadinessSubjectSource().GetSubjectAsync(scope)).Should().BeNull();
    }

    private static (CloseReadinessSubjectSource Service, CloseReadinessScopeDto Scope) Create(
        AccountSummaryDto account, FundStructureNodeKindDto kind, Guid nodeId)
    {
        var book = new LedgerBookDto(Guid.NewGuid(), "named-fund", nodeId, kind, "Primary", "USD",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var accounts = new Mock<IAccountQueryService>();
        accounts.Setup(x => x.GetAccountAsync(account.AccountId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var books = new Mock<ILedgerBookService>();
        books.Setup(x => x.GetBookAsync(book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        return (new(books.Object, accounts.Object),
            new(book.FundProfileId, book.LedgerBookId, account.AccountId, account.EntityId!.Value.ToString("D"), "2026-06"));
    }

    private static AccountSummaryDto Account()
        => new(Guid.NewGuid(), default, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "ACCT", "Operating account", "USD", "Custodian", true, DateTimeOffset.UtcNow.AddYears(-1),
            null, null, "primary-book", null, null);
}
