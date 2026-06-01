using FluentAssertions;
using Meridian.Application.Banking;
using Meridian.Application.Config.Credentials;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.Banking;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Plaid;
using Meridian.Infrastructure.Adapters.Plaid;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class PlaidWorkstationServiceTests
{
    [Fact]
    public async Task ExchangePublicToken_StoresAccessTokenOnlyInEncryptedCredentialStore()
    {
        var fixture = await PlaidFixture.CreateAsync();

        var result = await fixture.Service.ExchangePublicTokenAsync(new PlaidPublicTokenExchangeRequest(
            "public-sandbox-token",
            "ins_1",
            "Plaid Test Bank",
            [new PlaidAccountLinkRequest("plaid-account-1", "Operating Cash", null, "123456789", "depository", "checking")],
            "operator"));

        result.Item.ItemId.Should().Be("item-1");
        result.Accounts.Should().ContainSingle(account => account.Mask == "6789");
        (await fixture.Repository.ListItemsAsync()).Should().ContainSingle(item => item.AccessTokenKey == "AccessToken:item-1");
        var vaultText = await File.ReadAllTextAsync(fixture.CredentialStore.VaultPath);
        vaultText.Should().NotContain("access-token-1");
        var repoText = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "data", "plaid", "connections.json"));
        repoText.Should().NotContain("access-token-1");
    }

    [Fact]
    public async Task SyncItem_RecordsPlaidBalancesAndTransactionsAsFundAccountEvidence()
    {
        var fixture = await PlaidFixture.CreateAsync();
        var account = await fixture.FundAccounts.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(),
            AccountTypeDto.Bank,
            "BANK-PLAID",
            "Plaid operating account",
            "USD",
            DateTimeOffset.UtcNow,
            "test",
            BankDetails: new BankAccountDetailsDto(null, "Plaid Test Bank", null, null, null, null, null, null, null, null, null)));
        await fixture.Service.ExchangePublicTokenAsync(new PlaidPublicTokenExchangeRequest(
            "public-sandbox-token",
            "ins_1",
            "Plaid Test Bank",
            [new PlaidAccountLinkRequest("plaid-account-1", "Operating Cash", null, "6789", "depository", "checking", MeridianAccountId: account.AccountId)],
            "operator"));

        var result = await fixture.Service.SyncItemAsync(new PlaidSyncRequest("item-1", RequestedBy: "operator"));

        result.BalanceSnapshotsRecorded.Should().Be(1);
        result.BankStatementLinesIngested.Should().Be(2);
        var balances = await fixture.FundAccounts.GetBalanceHistoryAsync(account.AccountId);
        balances.Should().ContainSingle(snapshot => snapshot.Source == "PlaidBalance" && snapshot.CashBalance == 1250.25m);
        var lines = await fixture.FundAccounts.GetBankStatementLinesAsync(account.AccountId);
        lines.Should().HaveCount(2);
        lines.Should().Contain(line => line.Reference == "txn-1" && line.Amount == -25.10m);
        lines.Should().Contain(line => line.Reference == "txn-2" && line.Amount == 100.00m);
    }

    [Fact]
    public async Task SearchInstitutions_UsesStoredPlaidCredentialsAndReturnsSupportedInstitutions()
    {
        var fixture = await PlaidFixture.CreateAsync();

        var result = await fixture.Service.SearchInstitutionsAsync(
            "Chase",
            [PlaidProductDto.Transactions, PlaidProductDto.Auth],
            ["US"]);

        result.Query.Should().Be("Chase");
        result.Institutions.Should().ContainSingle(institution =>
            institution.InstitutionId == "ins_3" &&
            institution.Name == "Chase" &&
            institution.Products.Contains(PlaidProductDto.Transactions));
        fixture.Client.LastInstitutionSearchQuery.Should().Be("Chase");
        fixture.Client.LastInstitutionSearchProducts.Should().Equal(PlaidProductDto.Transactions, PlaidProductDto.Auth);
        fixture.Client.LastInstitutionSearchCountryCodes.Should().Equal("US");
    }

    [Fact]
    public async Task CreateLinkToken_CarriesSelectedInstitutionIntoSandboxLinkRequest()
    {
        var fixture = await PlaidFixture.CreateAsync();

        var result = await fixture.Service.CreateLinkTokenAsync(new PlaidLinkTokenRequest(
            "operator-1",
            Products: [PlaidProductDto.Transactions, PlaidProductDto.Auth],
            CountryCodes: ["US"],
            InstitutionId: "ins_3",
            InstitutionName: "Chase"));

        result.LinkToken.Should().Be("link-sandbox");
        result.InstitutionId.Should().Be("ins_3");
        result.InstitutionName.Should().Be("Chase");
        result.Environment.Should().Be(PlaidEnvironmentDto.Sandbox);
        fixture.Client.LastLinkTokenRequest?.InstitutionId.Should().Be("ins_3");
        fixture.Client.LastLinkTokenRequest?.InstitutionName.Should().Be("Chase");
        fixture.Client.LastLinkTokenRequest?.Products.Should().Equal(PlaidProductDto.Transactions, PlaidProductDto.Auth);
    }

    [Fact]
    public async Task CreateSandboxTransfer_RequiresApprovedPaymentBeforePlaidAuthorization()
    {
        var fixture = await PlaidFixture.CreateAsync(enableTransfers: true);
        await fixture.Service.ExchangePublicTokenAsync(new PlaidPublicTokenExchangeRequest(
            "public-sandbox-token",
            "ins_1",
            "Plaid Test Bank",
            [new PlaidAccountLinkRequest("plaid-account-1", "Operating Cash", null, "6789", "depository", "checking", EntityId: fixture.EntityId)],
            "operator"));
        var pending = await fixture.Banking.InitiatePaymentAsync(
            fixture.EntityId,
            new InitiatePaymentRequest(42m, DateOnly.FromDateTime(DateTime.UtcNow), "wire-1", "Plaid transfer test"));

        var blocked = await fixture.Service.CreateSandboxTransferAsync(new PlaidTransferRequest(
            "item-1",
            "plaid-account-1",
            pending.PendingPaymentId,
            fixture.EntityId,
            42m,
            "USD",
            "credit",
            "ccd",
            "operator"));

        blocked.Status.Should().Be(PlaidTransferStatusDto.Blocked);
        fixture.Client.AuthorizeCalls.Should().Be(0);

        await fixture.Banking.ApprovePaymentAsync(
            pending.PendingPaymentId,
            new ApprovePaymentRequest("approved", "reviewer"));

        var created = await fixture.Service.CreateSandboxTransferAsync(new PlaidTransferRequest(
            "item-1",
            "plaid-account-1",
            pending.PendingPaymentId,
            fixture.EntityId,
            42m,
            "USD",
            "credit",
            "ccd",
            "operator"));

        created.Status.Should().Be(PlaidTransferStatusDto.Created);
        created.AuthorizationId.Should().Be("auth-1");
        created.TransferId.Should().Be("transfer-1");
        fixture.Client.AuthorizeCalls.Should().Be(1);
    }

    private sealed class PlaidFixture
    {
        private PlaidFixture(
            string root,
            FileProviderCredentialStore credentialStore,
            FilePlaidConnectionRepository repository,
            InMemoryFundAccountService fundAccounts,
            InMemoryBankingService banking,
            FakePlaidClient client,
            PlaidWorkstationService service,
            Guid entityId)
        {
            Root = root;
            CredentialStore = credentialStore;
            Repository = repository;
            FundAccounts = fundAccounts;
            Banking = banking;
            Client = client;
            Service = service;
            EntityId = entityId;
        }

        public string Root { get; }
        public FileProviderCredentialStore CredentialStore { get; }
        public FilePlaidConnectionRepository Repository { get; }
        public InMemoryFundAccountService FundAccounts { get; }
        public InMemoryBankingService Banking { get; }
        public FakePlaidClient Client { get; }
        public PlaidWorkstationService Service { get; }
        public Guid EntityId { get; }

        public static async Task<PlaidFixture> CreateAsync(bool enableTransfers = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "plaid", Guid.NewGuid().ToString("N"));
            var dataRoot = Path.Combine(root, "data");
            Directory.CreateDirectory(dataRoot);
            var credentials = new FileProviderCredentialStore(dataRoot);
            await credentials.SaveAsync(new ProviderCredentialSaveRequest(
                "plaid",
                new Dictionary<string, string?>
                {
                    ["ClientId"] = "client-id",
                    ["Secret"] = "secret"
                },
                Environment: "sandbox",
                Actor: "test"));
            var repository = new FilePlaidConnectionRepository(dataRoot);
            var fundAccounts = new InMemoryFundAccountService();
            var banking = new InMemoryBankingService();
            var client = new FakePlaidClient();
            var service = new PlaidWorkstationService(
                client,
                repository,
                credentials,
                PlaidOptions.Default with { EnableTransfers = enableTransfers },
                NullLogger<PlaidWorkstationService>.Instance,
                fundAccounts,
                banking);
            return new PlaidFixture(root, credentials, repository, fundAccounts, banking, client, service, Guid.NewGuid());
        }
    }

    private sealed class FakePlaidClient : IPlaidClient
    {
        public int AuthorizeCalls { get; private set; }
        public PlaidLinkTokenRequest? LastLinkTokenRequest { get; private set; }
        public string? LastInstitutionSearchQuery { get; private set; }
        public IReadOnlyList<PlaidProductDto> LastInstitutionSearchProducts { get; private set; } = [];
        public IReadOnlyList<string> LastInstitutionSearchCountryCodes { get; private set; } = [];

        public Task<PlaidLinkTokenResponse> CreateLinkTokenAsync(PlaidClientCredentials credentials, PlaidLinkTokenRequest request, CancellationToken ct = default)
        {
            LastLinkTokenRequest = request;
            return Task.FromResult(new PlaidLinkTokenResponse(
                "link-sandbox",
                DateTimeOffset.UtcNow.AddMinutes(30),
                "req-link",
                request.Products ?? [],
                request.InstitutionId,
                request.InstitutionName,
                credentials.Environment));
        }

        public Task<PlaidInstitutionSearchResult> SearchInstitutionsAsync(
            PlaidClientCredentials credentials,
            string query,
            IReadOnlyList<PlaidProductDto>? products = null,
            IReadOnlyList<string>? countryCodes = null,
            CancellationToken ct = default)
        {
            LastInstitutionSearchQuery = query;
            LastInstitutionSearchProducts = products ?? [];
            LastInstitutionSearchCountryCodes = countryCodes ?? [];
            return Task.FromResult(new PlaidInstitutionSearchResult(
                query,
                [
                    new PlaidInstitutionDto(
                        "ins_3",
                        "Chase",
                        [PlaidProductDto.Transactions, PlaidProductDto.Auth, PlaidProductDto.Identity],
                        ["US"],
                        "https://www.chase.com",
                        "#117ACA",
                        Logo: null)
                ],
                "req-institutions"));
        }

        public Task<(string ItemId, string AccessToken, string? RequestId)> ExchangePublicTokenAsync(PlaidClientCredentials credentials, string publicToken, CancellationToken ct = default)
            => Task.FromResult<(string ItemId, string AccessToken, string? RequestId)>(("item-1", "access-token-1", "req-exchange"));

        public Task<IReadOnlyList<PlaidBalanceDto>> GetBalancesAsync(PlaidClientCredentials credentials, string accessToken, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PlaidBalanceDto>>([
                new PlaidBalanceDto("plaid-account-1", 1200m, 1250.25m, "USD", DateTimeOffset.UtcNow, true)
            ]);

        public Task<PlaidTransactionsSyncResult> SyncTransactionsAsync(PlaidClientCredentials credentials, string accessToken, string? cursor, CancellationToken ct = default)
            => Task.FromResult(new PlaidTransactionsSyncResult(
                "cursor-2",
                false,
                [
                    new PlaidTransactionDto("txn-1", "plaid-account-1", new DateOnly(2026, 5, 31), null, 25.10m, "USD", "Coffee", "Coffee", "Food", "in store", false, "place"),
                    new PlaidTransactionDto("txn-2", "plaid-account-1", new DateOnly(2026, 5, 31), null, -100.00m, "USD", "Refund", null, "Refund", "other", false, "special")
                ],
                [],
                [],
                "req-txn"));

        public Task<PlaidInvestmentsSnapshot> GetInvestmentsAsync(PlaidClientCredentials credentials, string accessToken, CancellationToken ct = default)
            => Task.FromResult(new PlaidInvestmentsSnapshot(
                [new PlaidInvestmentHoldingDto("plaid-account-1", "sec-1", "SPY", "SPDR S&P 500 ETF", 2m, 1000m, "USD", DateTimeOffset.UtcNow)],
                [new PlaidInvestmentTransactionDto("inv-txn-1", "plaid-account-1", "sec-1", new DateOnly(2026, 5, 30), "Buy SPY", "buy", 2m, 1000m, "USD")],
                "req-invest"));

        public Task<IReadOnlyList<PlaidIdentitySnapshot>> GetIdentityAsync(PlaidClientCredentials credentials, string accessToken, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PlaidIdentitySnapshot>>([
                new PlaidIdentitySnapshot("plaid-account-1", "Meridian Ops", null, null, "verified", DateTimeOffset.UtcNow)
            ]);

        public Task<(string AuthorizationId, bool Approved, string? DecisionRationale)> AuthorizeTransferAsync(PlaidClientCredentials credentials, string accessToken, PlaidTransferRequest request, CancellationToken ct = default)
        {
            AuthorizeCalls++;
            return Task.FromResult(("auth-1", true, (string?)null));
        }

        public Task<(string TransferId, string Status)> CreateTransferAsync(PlaidClientCredentials credentials, string accessToken, PlaidTransferRequest request, string authorizationId, CancellationToken ct = default)
            => Task.FromResult(("transfer-1", "pending"));
    }
}
