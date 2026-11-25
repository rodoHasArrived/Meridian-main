using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Onboarding;
using Meridian.FinancialOperations.Onboarding.OpeningBalances;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class OnboardingOpeningStateSourceTests
{
    [Fact]
    public async Task Capture_UsesCurrentCanonicalSubjectAndPinsItsBookDateAndMembership()
    {
        using var fixture = new Fixture();
        var workspace = await fixture.CreateWorkspaceAsync();

        var capture = await fixture.Source.CaptureOpeningAsync(workspace);

        capture.MissingSources.Should().BeEmpty();
        capture.Observations.Single(row => row.Kind == "Balance").MeridianAmount.Should().Be(100m);
        capture.Observations.Single(row => row.Kind == "Nav").MeridianAmount.Should().Be(112m);
        var snapshot = capture.Snapshots.Should().ContainSingle().Which;
        using var payload = JsonDocument.Parse(snapshot.PayloadJson);
        payload.RootElement.GetProperty("scope").GetProperty("ledgerBookId").GetGuid().Should().Be(fixture.Scope.LedgerBookId);
        payload.RootElement.GetProperty("asOfDate").GetString().Should().Be("2026-05-31");
        payload.RootElement.GetProperty("subjectEvidenceVersion").GetString().Should().NotBeNullOrWhiteSpace();
        payload.RootElement.GetProperty("ownership").GetProperty("companyId").GetString().Should().Be(Fixture.Company);
        fixture.Tenancy.Verify(registry => registry.ResolveAsync(Fixture.Fund, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("unbound")]
    [InlineData("entity")]
    [InlineData("inactive")]
    [InlineData("book-fund")]
    [InlineData("book-node")]
    public async Task Capture_RefusesStaleOwnershipOrMembershipBeforeReadingFinancialRecords(string change)
    {
        using var fixture = new Fixture();
        var workspace = await fixture.CreateWorkspaceAsync();
        fixture.ChangeScope(change);

        await fixture.Source.Invoking(source => source.CaptureOpeningAsync(workspace))
            .Should().ThrowAsync<OnboardingValidationException>();

        fixture.AssertNoFinancialReads();
    }

    [Theory]
    [InlineData("accounts")]
    [InlineData("subjects")]
    [InlineData("tenancy")]
    public async Task Capture_RefusesUnavailableScopeAuthorityBeforeReadingFinancialRecords(string missing)
    {
        using var fixture = new Fixture();
        var workspace = await fixture.CreateWorkspaceAsync();
        var source = new OnboardingOpeningStateSource(missing == "accounts" ? null : fixture.Accounts.Object,
            missing == "subjects" ? null : fixture.Subjects, missing == "tenancy" ? null : fixture.Tenancy.Object);

        await source.Invoking(value => value.CaptureOpeningAsync(workspace))
            .Should().ThrowAsync<OnboardingValidationException>().WithMessage("*authorities are required*");

        fixture.AssertNoFinancialReads();
    }

    [Fact]
    public async Task Capture_ValidatesTheWholeSelectedPopulationBeforeAnyFinancialRead()
    {
        using var fixture = new Fixture();
        var second = Guid.NewGuid();
        var workspace = await fixture.CreateWorkspaceAsync(fixture.Scope with
        {
            AccountIds = [fixture.AccountId.ToString("D"), second.ToString("D")]
        });
        fixture.Accounts.Setup(accounts => accounts.GetAccountAsync(second, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.Account with { AccountId = second, EntityId = Guid.NewGuid() });

        await fixture.Source.Invoking(source => source.CaptureOpeningAsync(workspace))
            .Should().ThrowAsync<OnboardingValidationException>();

        fixture.AssertNoFinancialReads();
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("entity")]
    [InlineData("book-version")]
    public async Task Import_RefusesScopeDriftDuringCaptureWithoutPersistingAnyComparison(string change)
    {
        using var fixture = new Fixture();
        var workspace = await fixture.CreateWorkspaceAsync();
        fixture.DuringPositionRead = () => fixture.ChangeScope(change);
        var request = new ImportOnboardingOpeningBalancesRequestDto(workspace.Version,
            OnboardingOpeningSourceKinds.AdministratorPack, "opening-map", "1",
            [new("incumbent-account", fixture.Scope.AccountIds.Single())], "opening.csv",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(
                "record_type,account,currency,instrument,quantity,market_value,amount,as_of_date\n" +
                "CASH,incumbent-account,USD,,,,100,2026-05-31\n")));

        await fixture.Service.Invoking(service => service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company,
            workspace.WorkspaceId, request, Fixture.Owner)).Should().ThrowAsync<OnboardingValidationException>();

        var retained = (await fixture.Store.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!;
        retained.Version.Should().Be(workspace.Version);
        retained.DataRevision.Should().Be(workspace.DataRevision);
        retained.Comparisons.Should().BeEmpty();
        retained.ShadowAudit.Should().ContainSingle().Which.Action.Should().Be("ShadowDesignated");
        fixture.Accounts.Verify(accounts => accounts.GetBalanceTimelineAsync(fixture.AccountId, Fixture.Date, Fixture.Date,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Capture_MissingBalanceRetainsTheGapInsteadOfInventingCashOrNav()
    {
        using var fixture = new Fixture();
        var workspace = await fixture.CreateWorkspaceAsync();
        fixture.Balance = null;
        fixture.Positions = [fixture.Position(10m, false)];

        var capture = await fixture.Source.CaptureOpeningAsync(workspace);

        capture.MissingSources.Should().ContainSingle().Which.SourceKind.Should().Be("MeridianOpeningState");
        capture.Observations.Should().OnlyContain(row => row.Kind == "Position");
        capture.Observations.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(10, true, -10)]
    [InlineData(-10, true, -10)]
    [InlineData(10, false, 10)]
    public async Task Capture_RetainsTheSameShortQuantityConventionAsPeriodComparisons(int raw, bool isShort, int expected)
    {
        using var fixture = new Fixture();
        var workspace = await fixture.CreateWorkspaceAsync();
        fixture.Positions = [fixture.Position(raw, isShort)];

        var capture = await fixture.Source.CaptureOpeningAsync(workspace);

        capture.Observations.Single(row => row.InstrumentId == "position-quantity:TEST").MeridianAmount.Should().Be(expected);
        capture.Observations.Single(row => row.InstrumentId == "position-market-value:TEST").MeridianAmount.Should().Be(25m);
        capture.Observations.Should().OnlyContain(row => row.Currency == "USD");
    }

    private sealed class Fixture : IDisposable
    {
        public const string Tenant = "opening-tenant";
        public const string Company = "opening-company";
        public const string Fund = "opening-fund";
        public const string Owner = "opening-owner";
        public static readonly DateOnly Date = new(2026, 5, 31);
        private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-opening-scope", Guid.NewGuid().ToString("N"));
        public Guid AccountId { get; } = Guid.NewGuid();
        public Mock<IAccountQueryService> Accounts { get; } = new();
        public Mock<ILedgerBookService> Books { get; } = new();
        public Mock<IFundProfileTenancyRegistry> Tenancy { get; } = new();
        public FundProfileOwnership? Ownership { get; set; } = new(Fund, Tenant, Company);
        public AccountSummaryDto Account { get; set; }
        public LedgerBookDto Book { get; set; }
        public AccountBalanceSnapshotDto? Balance { get; set; }
        public IReadOnlyList<CustodianPositionLineDto> Positions { get; set; } = [];
        public Action? DuringPositionRead { get; set; }
        public OnboardingScopeDto Scope { get; }
        public CloseReadinessSubjectSource Subjects { get; }
        public OnboardingOpeningStateSource Source { get; }
        public FileOnboardingWorkspaceStore Store { get; }
        public OnboardingWorkspaceService Service { get; }

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            var entity = Guid.NewGuid();
            var fund = Guid.NewGuid();
            var timestamp = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            Account = new(AccountId, AccountTypeDto.Custody, entity, fund, null, null, "TEST", "Opening account", "USD",
                null, true, timestamp, null, null, null, null, null);
            Book = new(Guid.NewGuid(), Fund, entity, FundStructureNodeKindDto.Entity, "Opening book", "USD", timestamp, timestamp);
            Balance = new(Guid.NewGuid(), AccountId, fund, Date, "USD", 100m, 10m, 1m, 1m,
                "retained", new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero), "opening-evidence");
            Scope = new(Tenant, Company, entity.ToString("D"), Fund, Book.LedgerBookId,
                [AccountId.ToString("D")], Date, Date);
            Tenancy.Setup(registry => registry.ResolveAsync(Fund, It.IsAny<CancellationToken>())).ReturnsAsync(() => Ownership);
            Books.Setup(books => books.GetBookAsync(Book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Book);
            Accounts.Setup(accounts => accounts.GetAccountAsync(AccountId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Account);
            Accounts.Setup(accounts => accounts.GetBalanceTimelineAsync(AccountId, Date, Date, It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<IReadOnlyList<AccountBalanceSnapshotDto>>(Balance is { } balance ? [balance] : []));
            Accounts.Setup(accounts => accounts.GetCustodianPositionsAsync(AccountId, Date, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    DuringPositionRead?.Invoke();
                    return Task.FromResult(Positions);
                });
            Subjects = new(Books.Object, Accounts.Object);
            Source = new(Accounts.Object, Subjects, Tenancy.Object);
            Store = new(Path.Combine(_root, "onboarding.json"));
            Service = new(Store, Mock.Of<IOnboardingSourceProvider>(), openingSources: new OnboardingOpeningSourceReader(), openingState: Source);
        }

        public async Task<OnboardingWorkspaceDto> CreateWorkspaceAsync(OnboardingScopeDto? selectedScope = null)
        {
            var workspace = await Service.CreateAsync(new("Opening source scope", selectedScope ?? Scope,
                new([Date], ["Balance"], 0m, 0m, 0m, 100m, ["independent-reviewer"], 1, false, "Inspect retained sources")), Owner);
            return await Service.DesignateShadowAsync(Tenant, Company, workspace.WorkspaceId, new(workspace.Version, "Parallel run"), Owner);
        }

        public CustodianPositionLineDto Position(decimal quantity, bool shortPosition) => new(Guid.NewGuid(), Guid.NewGuid(),
            AccountId, Date, "TEST", "Ticker", quantity, 25m, "USD", "Test position", "Equity", shortPosition);

        public void ChangeScope(string change)
        {
            switch (change)
            {
                case "tenant":
                    Ownership = Ownership! with { TenantId = "different-tenant" };
                    break;
                case "company":
                    Ownership = Ownership! with { CompanyId = "different-company" };
                    break;
                case "unbound":
                    Ownership = null;
                    break;
                case "entity":
                    Account = Account with { EntityId = Guid.NewGuid() };
                    break;
                case "inactive":
                    Account = Account with { IsActive = false };
                    break;
                case "book-fund":
                    Book = Book with { FundProfileId = "different-fund" };
                    break;
                case "book-node":
                    Book = Book with { FundStructureNodeId = Guid.NewGuid() };
                    break;
                case "book-version":
                    Book = Book with { UpdatedAt = Book.UpdatedAt.AddTicks(1) };
                    break;
                default:
                    throw new InvalidOperationException("Unknown fixture mutation.");
            }
        }

        public void AssertNoFinancialReads()
        {
            Accounts.Verify(accounts => accounts.GetBalanceTimelineAsync(It.IsAny<Guid>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(),
                It.IsAny<CancellationToken>()), Times.Never);
            Accounts.Verify(accounts => accounts.GetCustodianPositionsAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        public void Dispose() => Directory.Delete(_root, true);
    }
}
