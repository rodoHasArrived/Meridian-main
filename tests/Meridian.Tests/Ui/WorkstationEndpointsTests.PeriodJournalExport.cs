using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Serialization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Parquet;

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
            query.EffectiveFrom == null && query.EffectiveTo == null),
            fixture.Snapshot.Period!.PeriodId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("csv", false)]
    [InlineData("parquet", false)]
    [InlineData("csv", true)]
    [InlineData("parquet", true)]
    public async Task PeriodJournalExport_DefaultRetainsEarlierEffectiveJournals(string format, bool selectOnlyLateJournal)
    {
        var fixture = PeriodJournalFixture(includePriorEffectiveDateJournal: true);
        await using var app = await CreateAppAsync(fixture.Register,
            currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ExportData,
            currentUserTenantId: "tenant-alpha", currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var selected = selectOnlyLateJournal ? new[] { fixture.Snapshot.Journals[1] } : fixture.Snapshot.Journals.ToArray();
        var response = await app.GetTestClient().PostAsJsonAsync(fixture.Route, new RunLedgerExportRequest(format)
        { JournalEntryIds = selectOnlyLateJournal ? [selected[0].Entry.JournalEntryId] : null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await ReadPeriodJournalExportRowsAsync(response, format);
        AssertPeriodJournalExportRows(rows, selected);
        fixture.Source.Verify(source => source.CaptureReportingSnapshotAsync(It.Is<LedgerJournalEntryQuery>(query =>
            query.LedgerBookId == fixture.Book.LedgerBookId && query.PeriodId == fixture.Snapshot.Period!.PeriodId &&
            query.EffectiveFrom == null && query.EffectiveTo == null),
            fixture.Snapshot.Period!.PeriodId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("parquet")]
    public async Task PeriodJournalExport_ExplicitWindowStillFiltersByEffectiveDate(string format)
    {
        var fixture = PeriodJournalFixture(includePriorEffectiveDateJournal: true);
        var period = fixture.Snapshot.Period!;
        await using var app = await CreateAppAsync(fixture.Register,
            currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ExportData,
            currentUserTenantId: "tenant-alpha", currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var response = await app.GetTestClient().PostAsJsonAsync(fixture.Route, new RunLedgerExportRequest(format)
        { From = period.StartDate, To = period.EndDate });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertPeriodJournalExportRows(await ReadPeriodJournalExportRowsAsync(response, format), [fixture.Snapshot.Journals[0]]);
        fixture.Source.Verify(source => source.CaptureReportingSnapshotAsync(It.Is<LedgerJournalEntryQuery>(query =>
            query.LedgerBookId == fixture.Book.LedgerBookId && query.PeriodId == period.PeriodId &&
            query.EffectiveFrom == period.StartDate && query.EffectiveTo == period.EndDate),
            period.PeriodId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PeriodJournalExport_ExplicitWindowRejectsSelectedEarlierEffectiveJournal()
    {
        var fixture = PeriodJournalFixture(includePriorEffectiveDateJournal: true);
        var period = fixture.Snapshot.Period!;
        await using var app = await CreateAppAsync(fixture.Register,
            currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ExportData,
            currentUserTenantId: "tenant-alpha", currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var response = await app.GetTestClient().PostAsJsonAsync(fixture.Route, new RunLedgerExportRequest
        { From = period.StartDate, To = period.EndDate, JournalEntryIds = [fixture.Snapshot.Journals[1].Entry.JournalEntryId] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    private static PeriodJournalTestFixture PeriodJournalFixture(bool includePriorEffectiveDateJournal = false)
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        if (includePriorEffectiveDateJournal)
        {
            var original = snapshot.Journals[0];
            var journalId = Guid.NewGuid();
            const decimal amount = 1000m;
            const string description = "Retained late posting";
            var journal = new JournalEntry(journalId, original.Entry.Timestamp, description,
                original.Entry.Lines.Select(line => new LedgerEntry(Guid.NewGuid(), journalId, original.Entry.Timestamp,
                    line.Account, line.Debit > 0m ? amount : 0m, line.Credit > 0m ? amount : 0m, description)).ToArray(),
                original.Entry.Metadata with { EffectiveDate = snapshot.Period!.StartDate.AddDays(-1) });
            snapshot = snapshot with { Journals = [original, original with { Entry = journal, GlobalSequence = original.GlobalSequence + 1 }] };
        }
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

    private static async Task<IReadOnlyList<Dictionary<string, string>>> ReadPeriodJournalExportRowsAsync(HttpResponseMessage response, string format)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var rows = new List<Dictionary<string, string>>();
        if (format == "csv")
        {
            using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(new StringReader(Encoding.UTF8.GetString(bytes)))
            { TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
            parser.SetDelimiters(",");
            var headers = parser.ReadFields()!;
            while (!parser.EndOfData)
                rows.Add(headers.Zip(parser.ReadFields()!).ToDictionary(pair => pair.First, pair => pair.Second));
        }
        else
        {
            using var stream = new MemoryStream(bytes);
            using var reader = await ParquetReader.CreateAsync(stream);
            for (var groupIndex = 0; groupIndex < reader.RowGroupCount; groupIndex++)
            {
                using var group = reader.OpenRowGroupReader(groupIndex);
                var columns = new Dictionary<string, string[]>();
                foreach (var field in reader.Schema.GetDataFields())
                    columns.Add(field.Name, (string[])(await group.ReadColumnAsync(field)).Data);
                for (var row = 0; row < group.RowCount; row++)
                    rows.Add(columns.ToDictionary(column => column.Key, column => column.Value[row]));
            }
        }
        return rows;
    }

    private static void AssertPeriodJournalExportRows(IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<LedgerJournalEntryRecord> expected)
    {
        rows.Should().HaveCount(expected.Sum(record => record.Entry.Lines.Count));
        rows.Select(row => Guid.Parse(row["journal_entry_id"])).Distinct()
            .Should().BeEquivalentTo(expected.Select(record => record.Entry.JournalEntryId));
        rows.Select(row => Guid.Parse(row["entry_id"])).Should()
            .BeEquivalentTo(expected.SelectMany(record => record.Entry.Lines).Select(line => line.EntryId));
        rows.Sum(row => decimal.Parse(row["debit"], CultureInfo.InvariantCulture)).Should()
            .Be(expected.SelectMany(record => record.Entry.Lines).Sum(line => line.Debit));
        rows.Sum(row => decimal.Parse(row["credit"], CultureInfo.InvariantCulture)).Should()
            .Be(expected.SelectMany(record => record.Entry.Lines).Sum(line => line.Credit));
    }

    private sealed record PeriodJournalTestFixture(LedgerBookRecord Book, LedgerReportingSnapshot Snapshot,
        Mock<ILedgerJournalStore> Store, Mock<ILedgerReportingSnapshotSource> Source, Action<IServiceCollection> Register)
    {
        public string Route => $"/api/ledger/periods/{Snapshot.Period!.PeriodId:D}/journal-export";
    }
}
