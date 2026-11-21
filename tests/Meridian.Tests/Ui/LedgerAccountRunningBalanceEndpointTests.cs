using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class LedgerAccountRunningBalanceEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData(null, null)]
    [InlineData("all", "company")]
    [InlineData("tenant", null)]
    public async Task MissingOperatorScopeIsRefusedBeforeReadingAnyLedgerSource(string? tenant, string? company)
    {
        await using var harness = await Harness.CreateAsync(tenant, company);
        using var response = await harness.Client.GetAsync(harness.Path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        harness.Journals.Verify(store => store.GetPeriodAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.VerifyNoPopulationRead();
    }

    [Theory]
    [InlineData("foreign-tenant", "company", "fund")]
    [InlineData("tenant", "foreign-company", "fund")]
    [InlineData("tenant", "company", "foreign-fund")]
    public async Task BroadLedgerPermissionCannotReadForeignRetainedOwnership(string ownerTenant, string ownerCompany, string ownerFund)
    {
        await using var harness = await Harness.CreateAsync(owner: new(ownerFund, ownerTenant, ownerCompany));
        using var response = await harness.Client.GetAsync(harness.Path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        harness.VerifyNoPopulationRead();
    }

    [Fact]
    public async Task MissingRetainedOwnershipIsRefusedBeforeReadingJournalPopulation()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Tenancy.Setup(registry => registry.ResolveAsync("fund", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundProfileOwnership?)null);
        using var response = await harness.Client.GetAsync(harness.Path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        harness.VerifyNoPopulationRead();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MismatchedReturnedSourceIdentityIsRefusedBeforeReadingJournalPopulation(bool periodMismatch)
    {
        await using var harness = await Harness.CreateAsync();
        if (periodMismatch)
            harness.Journals.Setup(store => store.GetPeriodAsync(harness.Period.PeriodId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(harness.Period with { PeriodId = Guid.NewGuid() });
        else
            harness.Books.Setup(service => service.GetBookAsync(harness.Book.LedgerBookId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(harness.Book with { LedgerBookId = Guid.NewGuid() });
        using var response = await harness.Client.GetAsync(harness.Path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        harness.VerifyNoPopulationRead();
    }

    [Fact]
    public async Task ForeignPeriodReturnedByPermissiveStoreCannotBecomeAccountEvidence()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Journals.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Harness.Record(Guid.NewGuid(), 1, 100m, AccountingBasisKindDto.Primary)]);
        using var response = await harness.Client.GetAsync(harness.Path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task OwnedBookWithViewPermissionFiltersSelectedBasisAndPreservesOmittedBasis()
    {
        await using var harness = await Harness.CreateAsync();
        var primary = await harness.Client.GetFromJsonAsync<LedgerAccountRunningBalanceDto>(harness.Path + "&accountingBasis=Primary", Json);
        var gaap = await harness.Client.GetFromJsonAsync<LedgerAccountRunningBalanceDto>(harness.Path + "&accountingBasis=Gaap", Json);
        var omitted = await harness.Client.GetFromJsonAsync<LedgerAccountRunningBalanceDto>(harness.Path, Json);
        Assert.Equal(100m, primary!.EndingBalance);
        Assert.Single(primary.Points);
        Assert.Equal(40m, gaap!.EndingBalance);
        Assert.Single(gaap.Points);
        Assert.Equal(140m, omitted!.EndingBalance);
        Assert.Equal(2, omitted.Points.Count);
        harness.Books.Verify(service => service.GetBookAsync(harness.Book.LedgerBookId, It.IsAny<CancellationToken>()),
            Times.Exactly(3), "each request uses one authorized book snapshot");
    }

    [Theory]
    [InlineData("999")]
    [InlineData("UnsupportedBasis")]
    public async Task InvalidAccountingBasisIsRefusedBeforeReadingJournalPopulation(string basis)
    {
        await using var harness = await Harness.CreateAsync();
        using var response = await harness.Client.GetAsync(harness.Path + "&accountingBasis=" + basis);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        harness.VerifyNoPopulationRead();
    }

    private sealed class Harness(WebApplication app, Mock<ILedgerJournalStore> journals,
        Mock<ILedgerBookService> books, Mock<IFundProfileTenancyRegistry> tenancy,
        LedgerBookDto book, LedgerAccountingPeriod period) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();
        public Mock<ILedgerJournalStore> Journals => journals;
        public Mock<ILedgerBookService> Books => books;
        public Mock<IFundProfileTenancyRegistry> Tenancy => tenancy;
        public LedgerBookDto Book => book;
        public LedgerAccountingPeriod Period => period;
        public string Path => UiApiRoutes.LedgerPeriodAccountRunningBalance.Replace("{periodId:guid}", period.PeriodId.ToString("D")) + "?account=Cash";

        public void VerifyNoPopulationRead() => journals.Verify(
            store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()), Times.Never);

        public static async Task<Harness> CreateAsync(string? tenant = "tenant", string? company = "company", FundProfileOwnership? owner = null)
        {
            var timestamp = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
            var book = new LedgerBookDto(Guid.NewGuid(), "fund", Guid.NewGuid(), FundStructureNodeKindDto.Account,
                "Synthetic owned ledger", "USD", timestamp, timestamp);
            var period = new LedgerAccountingPeriod(Guid.NewGuid(), book.LedgerBookId, 2026, 6, "2026-P06",
                new(2026, 6, 1), new(2026, 6, 30), "Open", timestamp, null, 1);
            var journals = new Mock<ILedgerJournalStore>();
            journals.Setup(store => store.GetPeriodAsync(period.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(period);
            journals.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([Record(period.PeriodId, 1, 100m, AccountingBasisKindDto.Primary), Record(period.PeriodId, 2, 40m, AccountingBasisKindDto.Gaap)]);
            var books = new Mock<ILedgerBookService>();
            books.Setup(service => service.GetBookAsync(book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
            var tenancy = new Mock<IFundProfileTenancyRegistry>();
            tenancy.Setup(registry => registry.ResolveAsync("fund", It.IsAny<CancellationToken>()))
                .ReturnsAsync(owner ?? new("fund", "tenant", "company"));
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(journals.Object);
            builder.Services.AddSingleton(books.Object);
            builder.Services.AddSingleton(tenancy.Object);
            builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy,
                _ => RateLimitPartition.GetNoLimiter("running-balance-scope-test")));
            var app = builder.Build();
            app.Use((context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "synthetic-ledger-reader";
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewLedgerReports;
                if (tenant is not null)
                    context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenant;
                if (company is not null)
                    context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = company;
                return next();
            });
            app.UseRouting();
            app.UseRateLimiter();
            app.MapLedgerEndpoints(Json);
            await app.StartAsync();
            return new(app, journals, books, tenancy, book, period);
        }

        public static LedgerJournalEntryRecord Record(Guid periodId, long sequence, decimal amount, AccountingBasisKindDto basis)
        {
            var id = Guid.NewGuid();
            var timestamp = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(sequence);
            return new(new JournalEntry(id, timestamp, "Synthetic receipt",
                [new LedgerEntry(Guid.NewGuid(), id, timestamp, new("Cash", LedgerAccountType.Asset), amount, 0m, "Synthetic receipt"),
                 new LedgerEntry(Guid.NewGuid(), id, timestamp, new("Capital", LedgerAccountType.Equity), 0m, amount, "Synthetic receipt")]),
                Guid.NewGuid(), periodId, null, null, sequence, timestamp, basis);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
