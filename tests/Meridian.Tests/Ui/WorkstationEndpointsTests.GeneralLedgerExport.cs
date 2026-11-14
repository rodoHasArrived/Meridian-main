using System.Net;
using FluentAssertions;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.SecurityMaster;
using System.Text.Json;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task GeneralLedgerExport_ApprovedHistoricalInputsReachSymbolReportThroughHttp()
    {
        var (book, snapshot, marks) = GeneralLedgerSymbolPnlTests.CreateGoldenCase();
        var store = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        store.Setup(service => service.GetLedgerBookAsync(book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        var source = store.As<ILedgerOpenPositionReportingSnapshotSource>();
        source.Setup(service => service.CaptureOpenPositionReportingSnapshotAsync(It.Is<LedgerJournalEntryQuery>(query =>
                query.LedgerBookId == book.LedgerBookId && query.PeriodId == snapshot.Period!.PeriodId), snapshot.Period!.PeriodId,
            It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var owners = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
        owners.Setup(service => service.ResolveAsync(book.FundProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundProfileOwnership(book.FundProfileId, "tenant-alpha", "company-alpha"));
        var pricing = new Mock<ISecurityMasterPricingService>(MockBehavior.Strict);
        pricing.Setup(service => service.GetClosePriceSetsAsync(book.FundStructureNodeId.ToString("D"), book.LedgerBookId.ToString("D"),
            snapshot.Period.EndDate, It.IsAny<CancellationToken>())).ReturnsAsync([marks.ClosePrices]);
        var securities = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
        securities.Setup(service => service.GetByIdAsOfAsync(marks.Securities[0].SecurityId, marks.ClosePrices.Snapshot.EvaluatedAsOf,
            It.IsAny<CancellationToken>())).ReturnsAsync(marks.Securities[0]);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(store.Object);
            services.AddSingleton(owners.Object);
            services.AddSingleton(pricing.Object);
            services.AddSingleton(securities.Object);
        }, currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ExportData | UserPermission.ViewSecurityMaster,
            currentUserTenantId: "tenant-alpha", currentUserCompanyId: "company-alpha", mapLedgerApi: true);

        using var response = await app.GetTestClient().GetAsync(GeneralLedgerExportRoute(book.LedgerBookId, snapshot.Period.PeriodId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var files = GeneralLedgerExportTests.ReadZip(await response.Content.ReadAsByteArrayAsync());
        using var json = JsonDocument.Parse(files["symbol-pnl.v1.json"]);
        var row = json.RootElement.GetProperty("rows")[0];
        row.GetProperty("unrealizedAvailability").GetString().Should().Be("Available");
        decimal.Parse(row.GetProperty("unrealizedGainLoss").GetString()!).Should().Be(37.5m);
        decimal.Parse(row.GetProperty("bookRealizedGainLoss").GetString()!).Should().Be(50m);
    }

    [Fact]
    public async Task GeneralLedgerExport_CapturesExactBookPeriodOnceAndDownloadsOnePackage()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var store = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        store.Setup(service => service.GetLedgerBookAsync(book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        var source = store.As<ILedgerReportingSnapshotSource>();
        source.Setup(service => service.CaptureReportingSnapshotAsync(It.Is<LedgerJournalEntryQuery>(query =>
                query.LedgerBookId == book.LedgerBookId && query.PeriodId == snapshot.Period!.PeriodId), snapshot.Period!.PeriodId,
            It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var owners = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
        owners.Setup(service => service.ResolveAsync(book.FundProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundProfileOwnership(book.FundProfileId, "tenant-alpha", "company-alpha"));
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(store.Object);
            services.AddSingleton(owners.Object);
        }, currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ExportData, currentUserTenantId: "tenant-alpha",
            currentUserCompanyId: "company-alpha", mapLedgerApi: true);

        using var response = await app.GetTestClient().GetAsync(GeneralLedgerExportRoute(book.LedgerBookId, snapshot.Period!.PeriodId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        GeneralLedgerExportTests.ReadZip(await response.Content.ReadAsByteArrayAsync()).Should().HaveCount(6);
        source.Verify(service => service.CaptureReportingSnapshotAsync(It.IsAny<LedgerJournalEntryQuery>(),
            snapshot.Period.PeriodId, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(service => service.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("tenant-foreign", "company-alpha")]
    [InlineData("tenant-alpha", "company-foreign")]
    public async Task GeneralLedgerExport_ForeignOwnerRefusesBeforeCapturingFinancialRows(string ownerTenant, string ownerCompany)
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var store = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        store.Setup(service => service.GetLedgerBookAsync(book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        var source = store.As<ILedgerReportingSnapshotSource>();
        var owners = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
        owners.Setup(service => service.ResolveAsync(book.FundProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundProfileOwnership(book.FundProfileId, ownerTenant, ownerCompany));
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(store.Object);
            services.AddSingleton(owners.Object);
        }, currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ExportData, currentUserTenantId: "tenant-alpha",
            currentUserCompanyId: "company-alpha", mapLedgerApi: true);

        using var response = await app.GetTestClient().GetAsync(GeneralLedgerExportRoute(book.LedgerBookId, snapshot.Period!.PeriodId));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        source.Verify(service => service.CaptureReportingSnapshotAsync(It.IsAny<LedgerJournalEntryQuery>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GeneralLedgerExport_MissingLedgerPermissionRefusesBeforeStoreAccess()
    {
        var store = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        store.As<ILedgerReportingSnapshotSource>();
        await using var app = await CreateAppAsync(services => services.AddSingleton(store.Object),
            currentUserPermissions: UserPermission.ViewMarketData | UserPermission.ExportData, currentUserTenantId: "tenant-alpha",
            currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        using var response = await app.GetTestClient().GetAsync(GeneralLedgerExportRoute(Guid.NewGuid(), Guid.NewGuid()));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GeneralLedgerExport_MissingExportPermissionRefusesBeforeStoreAccess()
    {
        var store = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        store.As<ILedgerReportingSnapshotSource>();
        await using var app = await CreateAppAsync(services => services.AddSingleton(store.Object),
            currentUserPermissions: UserPermission.ViewLedgerReports, currentUserTenantId: "tenant-alpha",
            currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        using var response = await app.GetTestClient().GetAsync(GeneralLedgerExportRoute(Guid.NewGuid(), Guid.NewGuid()));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        store.VerifyNoOtherCalls();
    }

    private static string GeneralLedgerExportRoute(Guid bookId, Guid periodId)
        => $"/api/ledger/books/{bookId:D}/periods/{periodId:D}/exports/general-ledger";
}
