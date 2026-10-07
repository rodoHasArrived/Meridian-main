using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class LedgerDisposalTaxEndpointTests
{
    [Theory]
    [InlineData(UserPermission.None)]
    [InlineData(UserPermission.ViewReporting)]
    public async Task TaxResults_WithoutLedgerPermission_RejectsBeforeReadingEvidence(UserPermission permission)
    {
        var fixture = CreateFixture();
        await using var app = await CreateAppAsync(permission, fixture.Store, fixture.History);

        var response = await app.GetTestClient().GetAsync(fixture.Url);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Store.ReceivedCalls().Should().BeEmpty();
        fixture.History.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(UserPermission.ViewLedgerReports)]
    [InlineData(UserPermission.ManageLedgerReports)]
    [InlineData(UserPermission.ManageDirectLending)]
    [InlineData(UserPermission.AdminMaintenance)]
    public async Task TaxResults_LedgerReaders_ReceiveRetainedServerResult(UserPermission permission)
    {
        var fixture = CreateFixture();
        await using var app = await CreateAppAsync(permission, fixture.Store, fixture.History);

        var response = await app.GetTestClient().GetAsync(fixture.Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LedgerJournalTaxResultsDto>();
        result.Should().NotBeNull();
        result!.LedgerBookId.Should().Be(fixture.Book.LedgerBookId);
        result.PeriodId.Should().Be(fixture.Period.PeriodId);
        result.JournalEntryId.Should().Be(fixture.Record.Entry.JournalEntryId);
        result.FunctionalCurrency.Should().Be("USD");
        result.EvidenceState.Should().Be("Available");
        var disposal = result.Disposals.Should().ContainSingle().Which;
        disposal.PolicyRevision.Should().Be("retained-tax-policy-v7");
        disposal.RecordedAt.Should().Be(fixture.Disposal.RecordedAt);
        disposal.State.Should().Be("Settled");
        disposal.CanChange.Should().BeFalse();
        disposal.ReEvaluationRequired.Should().BeFalse();
        disposal.Character.Should().Be("ShortTerm");
        disposal.EconomicGainOrLoss.Should().Be("50");
        disposal.RecognizedGainOrLoss.Should().Be("50");
        disposal.DeferredLoss.Should().Be("0");
        var parcel = disposal.Parcels.Should().ContainSingle().Which;
        parcel.LotId.Should().Be(fixture.Disposal.Lots[0].LotId);
        parcel.AcquiredDate.Should().Be(new DateOnly(2026, 1, 1));
        parcel.HoldingPeriodStart.Should().Be(new DateOnly(2026, 1, 1));
        parcel.HoldingPeriodCarried.Should().BeFalse();
        parcel.Character.Should().Be("ShortTerm");
        parcel.Quantity.Should().Be("2.5");
        parcel.Proceeds.Should().Be("350");
        parcel.CostBasis.Should().Be("300");
        parcel.EconomicGainOrLoss.Should().Be("50");
        parcel.RecognizedGainOrLoss.Should().Be("50");
        parcel.DeferredLoss.Should().Be("0");

        await fixture.Store.Received(1).QueryAsync(
            new LedgerJournalEntryQuery(LedgerBookId: fixture.Book.LedgerBookId, PeriodId: fixture.Period.PeriodId,
                JournalEntryId: fixture.Record.Entry.JournalEntryId),
            Arg.Any<CancellationToken>());
        await fixture.Store.DidNotReceive().GetByPeriodAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await fixture.History.Received(1).GetTaxLotDisposalHistoryAsync(
            fixture.Book.LedgerBookId,
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == fixture.Record.Entry.JournalEntryId),
            Arg.Any<CancellationToken>());
        await fixture.Store.DidNotReceive().ListTaxLotPoliciesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("9007199254740993.01", "1", "9007199254740993.02", "9007199254740993.01", "18014398509481986.03")]
    [InlineData("1", "0.0000000000000000000000000001", "0.0099999999999999999999999999", "0.0000000000000000000000000001", "0.01")]
    public async Task TaxResults_JsonPreservesLargeCentsAndSmallQuantitiesAsExactStrings(
        string unitCost, string quantity, string gain, string expectedBasis, string expectedProceeds)
    {
        var fixture = CreateFixture(decimal.Parse(unitCost, CultureInfo.InvariantCulture),
            decimal.Parse(quantity, CultureInfo.InvariantCulture), decimal.Parse(gain, CultureInfo.InvariantCulture));
        await using var app = await CreateAppAsync(UserPermission.ViewLedgerReports, fixture.Store, fixture.History);

        var response = await app.GetTestClient().GetAsync(fixture.Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var disposal = json.RootElement.GetProperty("disposals")[0];
        disposal.GetProperty("state").GetString().Should().Be("Settled");
        AssertDecimalString(disposal, "economicGainOrLoss", gain);
        AssertDecimalString(disposal, "recognizedGainOrLoss", gain);
        AssertDecimalString(disposal, "deferredLoss", "0");
        var parcel = disposal.GetProperty("parcels")[0];
        AssertDecimalString(parcel, "quantity", quantity);
        AssertDecimalString(parcel, "proceeds", expectedProceeds);
        AssertDecimalString(parcel, "costBasis", expectedBasis);
        AssertDecimalString(parcel, "economicGainOrLoss", gain);
        AssertDecimalString(parcel, "recognizedGainOrLoss", gain);
        AssertDecimalString(parcel, "deferredLoss", "0");
    }

    [Fact]
    public async Task TaxResults_MissingCanonicalEvidenceSerializesUnknownAmountsAsNull()
    {
        var fixture = CreateFixture();
        fixture.History.GetTaxLotDisposalHistoryAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>>(
                [fixture.Disposal with { CanonicalLots = null }]));
        await using var app = await CreateAppAsync(UserPermission.ViewLedgerReports, fixture.Store, fixture.History);

        var response = await app.GetTestClient().GetAsync(fixture.Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var disposal = json.RootElement.GetProperty("disposals")[0];
        disposal.GetProperty("state").GetString().Should().Be("MissingEvidence");
        disposal.GetProperty("economicGainOrLoss").ValueKind.Should().Be(JsonValueKind.Null);
        disposal.GetProperty("recognizedGainOrLoss").ValueKind.Should().Be(JsonValueKind.Null);
        disposal.GetProperty("deferredLoss").ValueKind.Should().Be(JsonValueKind.Null);
        disposal.GetProperty("parcels").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task TaxResults_WithoutJournalStore_ReturnsUnavailable()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewLedgerReports);

        var response = await app.GetTestClient().GetAsync(Url(Guid.NewGuid(), Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }

    [Theory]
    [InlineData("unknown-period")]
    [InlineData("unbooked-period")]
    [InlineData("unknown-book")]
    [InlineData("foreign-period-journal")]
    [InlineData("unknown-journal")]
    public async Task TaxResults_OutsideRequestedBookPeriodAndJournal_ReturnsNotFoundWithoutReadingTaxHistory(string fault)
    {
        var fixture = CreateFixture();
        var url = fixture.Url;
        switch (fault)
        {
            case "unknown-period":
                fixture.Store.GetPeriodAsync(fixture.Period.PeriodId, Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<LedgerAccountingPeriod?>(null));
                break;
            case "unbooked-period":
                fixture.Store.GetPeriodAsync(fixture.Period.PeriodId, Arg.Any<CancellationToken>())
                    .Returns(fixture.Period with { LedgerBookId = null });
                break;
            case "unknown-book":
                fixture.Store.GetLedgerBookAsync(fixture.Book.LedgerBookId, Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<LedgerBookRecord?>(null));
                break;
            case "foreign-period-journal":
                // Even a store returning the right journal ID from the wrong period must fail closed.
                fixture.Store.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                        [fixture.Record with { PeriodId = Guid.NewGuid() }]));
                break;
            case "unknown-journal":
                url = Url(fixture.Period.PeriodId, Guid.NewGuid());
                break;
        }
        await using var app = await CreateAppAsync(UserPermission.ViewLedgerReports, fixture.Store, fixture.History);

        var response = await app.GetTestClient().GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        fixture.History.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task TaxResults_HistoryUnavailable_ReturnsMissingEvidenceWithoutInventedDisposal()
    {
        var fixture = CreateFixture();
        await using var app = await CreateAppAsync(UserPermission.ViewLedgerReports, fixture.Store);

        var response = await app.GetTestClient().GetAsync(fixture.Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LedgerJournalTaxResultsDto>();
        result!.EvidenceState.Should().Be("MissingEvidence");
        result.Disposals.Should().BeEmpty();
        result.Message.Should().Contain("unavailable");
    }

    [Fact]
    public async Task TaxResults_NoDisposalForJournal_ReturnsAvailableEmptyHistory()
    {
        var fixture = CreateFixture();
        fixture.History.GetTaxLotDisposalHistoryAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>>([]));
        await using var app = await CreateAppAsync(UserPermission.ViewLedgerReports, fixture.Store, fixture.History);

        var response = await app.GetTestClient().GetAsync(fixture.Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LedgerJournalTaxResultsDto>();
        result!.EvidenceState.Should().Be("Available");
        result.Disposals.Should().BeEmpty();
        result.Message.Should().Contain("No retained tax-lot disposal");
    }

    [Fact]
    public async Task TaxResults_UnsupportedHistory_ReturnsMissingEvidence()
    {
        var fixture = CreateFixture();
        fixture.History.GetTaxLotDisposalHistoryAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>>(new NotSupportedException()));
        await using var app = await CreateAppAsync(UserPermission.ViewLedgerReports, fixture.Store, fixture.History);

        var response = await app.GetTestClient().GetAsync(fixture.Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LedgerJournalTaxResultsDto>();
        result!.EvidenceState.Should().Be("MissingEvidence");
        result.Disposals.Should().BeEmpty();
    }

    private static void AssertDecimalString(JsonElement value, string propertyName, string expected)
    {
        var property = value.GetProperty(propertyName);
        property.ValueKind.Should().Be(JsonValueKind.String, "financial decimals must reach the browser without numeric rounding");
        property.GetString().Should().Be(expected);
    }

    private static Fixture CreateFixture(decimal? unitCost = null, decimal quantity = 2.5m, decimal recognized = 50m)
    {
        var lot = unitCost is { } cost
            ? CanonicalOpenLotConsumerTests.DurableLot(1, cost, 1m)
            : CanonicalOpenLotConsumerTests.DurableLot(1);
        var basis = lot.UnitCost * quantity;
        var entry = CanonicalOpenLotConsumerTests.DisposalJournal(lot, basis, recognized);
        var now = entry.Timestamp;
        var book = new LedgerBookRecord(lot.LedgerBookId, "fund-tax", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Tax result test book", "USD", now, now);
        var period = new LedgerAccountingPeriod(Guid.NewGuid(), book.LedgerBookId, 2026, 7, "2026-07",
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), "Open", now, null, 1);
        var record = new LedgerJournalEntryRecord(entry, Guid.NewGuid(), period.PeriodId, null, null, 1, now);
        var disposal = CanonicalOpenLotConsumerTests.History(lot, entry, lot.ToOpenLot()) with
        {
            Lots = [new(lot.LotId, lot.AcquiredDate, lot.AcquiredDate, quantity, lot.UnitCost, basis)],
            PolicyRevision = "retained-tax-policy-v7",
            RecordedAt = now
        };
        var store = Substitute.For<ILedgerJournalStore>();
        store.GetPeriodAsync(period.PeriodId, Arg.Any<CancellationToken>()).Returns(period);
        store.GetLedgerBookAsync(book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(book);
        store.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([record]));
        var history = Substitute.For<ILedgerTaxLotDisposalHistory>();
        history.GetTaxLotDisposalHistoryAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>>([disposal]));
        return new(store, history, book, period, record, disposal);
    }

    private static async Task<WebApplication> CreateAppAsync(UserPermission permission,
        ILedgerJournalStore? store = null, ILedgerTaxLotDisposalHistory? history = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        if (store is not null)
            builder.Services.AddSingleton(store);
        if (history is not null)
            builder.Services.AddSingleton(history);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "tax-result-reader";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permission;
            await next();
        });
        app.MapLedgerEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }

    private static string Url(Guid periodId, Guid journalEntryId)
        => $"/api/ledger/periods/{periodId:D}/journal-entries/{journalEntryId:D}/tax-results";

    private sealed record Fixture(ILedgerJournalStore Store, ILedgerTaxLotDisposalHistory History,
        LedgerBookRecord Book, LedgerAccountingPeriod Period, LedgerJournalEntryRecord Record,
        LedgerTaxLotDisposalHistoryRecord Disposal)
    {
        public string Url => LedgerDisposalTaxEndpointTests.Url(Period.PeriodId, Record.Entry.JournalEntryId);
    }
}
