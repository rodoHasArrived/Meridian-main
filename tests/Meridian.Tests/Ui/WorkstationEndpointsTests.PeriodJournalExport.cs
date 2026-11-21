using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Serialization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData(UserPermission.ViewLedgerReports)]
    [InlineData(UserPermission.ExportData)]
    public async Task PeriodJournalExport_RequiresBothLedgerReadAndExportPermissions(UserPermission permissions)
    {
        var fixture = PeriodJournalFixture();
        await using var app = await CreateAppAsync(fixture.Register, currentUserPermissions: permissions,
            currentUserTenantId: "tenant-alpha", currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var response = await app.GetTestClient().PostAsJsonAsync(fixture.Route, new RunLedgerExportRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PeriodJournalExport_CapturesOnceAndReturnsCompleteChecksummedCsv()
    {
        var fixture = PeriodJournalFixture();
        await using var app = await CreateAppAsync(fixture.Register,
            currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ExportData,
            currentUserTenantId: "tenant-alpha", currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var response = await app.GetTestClient().PostAsJsonAsync(fixture.Route, new RunLedgerExportRequest());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var content = await response.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(content).Should().Contain(fixture.Snapshot.Journals[0].Entry.JournalEntryId.ToString("D"));
        response.Headers.GetValues("X-Content-Sha256").Single().Should().Be(Meridian.Contracts.Integrity.Sha256Digest.Compute(content));
        response.Headers.GetValues("X-Ledger-Snapshot-Sha256").Single().Should().HaveLength(64);
        fixture.Source.Verify(source => source.CaptureReportingSnapshotAsync(It.Is<LedgerJournalEntryQuery>(query =>
            query.LedgerBookId == fixture.Book.LedgerBookId && query.PeriodId == fixture.Snapshot.Period!.PeriodId &&
            query.EffectiveFrom == fixture.Snapshot.Period.StartDate && query.EffectiveTo == fixture.Snapshot.Period.EndDate),
            fixture.Snapshot.Period!.PeriodId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PeriodJournalExport_UnknownSelectedIdRejectsEntireDownload()
    {
        var fixture = PeriodJournalFixture();
        await using var app = await CreateAppAsync(fixture.Register,
            currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ExportData,
            currentUserTenantId: "tenant-alpha", currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var request = new RunLedgerExportRequest { JournalEntryIds = [fixture.Snapshot.Journals[0].Entry.JournalEntryId, Guid.NewGuid()] };
        var response = await app.GetTestClient().PostAsJsonAsync(fixture.Route, request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().NotBe("text/csv");
    }

    [Fact]
    public async Task PeriodJournalExport_ChangedPeriodRequiresRefresh()
    {
        var fixture = PeriodJournalFixture();
        fixture.Source.Setup(source => source.CaptureReportingSnapshotAsync(It.IsAny<LedgerJournalEntryQuery>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(fixture.Snapshot with
            { Period = fixture.Snapshot.Period! with { Version = fixture.Snapshot.Period.Version + 1 } });
        await using var app = await CreateAppAsync(fixture.Register,
            currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ExportData,
            currentUserTenantId: "tenant-alpha", currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var response = await app.GetTestClient().PostAsJsonAsync(fixture.Route, new RunLedgerExportRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PeriodJournalExport_ForeignOwnerIsRejectedBeforeSnapshotRead()
    {
        var fixture = PeriodJournalFixture();
        await using var app = await CreateAppAsync(fixture.Register,
            currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ExportData,
            currentUserTenantId: "another-tenant", currentUserCompanyId: "another-company", mapLedgerApi: true);
        var response = await app.GetTestClient().PostAsJsonAsync(fixture.Route, new RunLedgerExportRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Source.Verify(source => source.CaptureReportingSnapshotAsync(It.IsAny<LedgerJournalEntryQuery>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PeriodJournalTestFixture PeriodJournalFixture()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var store = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        store.Setup(service => service.GetPeriodAsync(snapshot.Period!.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot.Period);
        var source = store.As<ILedgerReportingSnapshotSource>();
        source.Setup(service => service.CaptureReportingSnapshotAsync(It.IsAny<LedgerJournalEntryQuery>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var books = new Mock<ILedgerBookService>(MockBehavior.Strict);
        books.Setup(service => service.GetBookAsync(book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new LedgerBookDto(book.LedgerBookId, book.FundProfileId, book.FundStructureNodeId, book.FundStructureNodeKind,
                book.DisplayName, book.BaseCurrency, book.CreatedAt, book.UpdatedAt));
        var owners = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
        owners.Setup(service => service.ResolveAsync(book.FundProfileId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new FundProfileOwnership(book.FundProfileId, "tenant-alpha", "company-alpha"));
        return new(book, snapshot, store, source, services =>
        {
            services.AddSingleton(store.Object);
            services.AddSingleton(books.Object);
            services.AddSingleton(owners.Object);
        });
    }

    private sealed record PeriodJournalTestFixture(LedgerBookRecord Book, LedgerReportingSnapshot Snapshot,
        Mock<ILedgerJournalStore> Store, Mock<ILedgerReportingSnapshotSource> Source, Action<IServiceCollection> Register)
    {
        public string Route => $"/api/ledger/periods/{Snapshot.Period!.PeriodId:D}/journal-export";
    }
}
