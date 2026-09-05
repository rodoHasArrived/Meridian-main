using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("another-tenant", "tenant-test")]
    [InlineData("tenant-test", "another-company")]
    public async Task OpenLotBackfill_UnknownOrForeignOwnerCannotReadOrSurvey(string? tenant, string? company)
    {
        var bookId = Guid.NewGuid();
        var store = Substitute.For<IOpenLotBackfillStore>();
        var books = OpenLotBackfillBookService(bookId);
        var registry = Substitute.For<IFundProfileTenancyRegistry>();
        registry.ResolveAsync("fund-backfill", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(tenant is null ? null : new FundProfileOwnership("fund-backfill", tenant, company)));
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(store);
            services.AddSingleton(books);
            services.AddSingleton(registry);
        }, mapLedgerApi: true, currentUserPermissions: UserPermission.AdminMaintenance);

        var client = app.GetTestClient();
        (await client.GetAsync($"/api/ledger/books/{bookId}/open-lots/backfill/exceptions"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"/api/ledger/books/{bookId}/open-lots/backfill/survey", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await store.DidNotReceiveWithAnyArgs().ListExceptionsAsync(default);
        await store.DidNotReceiveWithAnyArgs().SurveyAsync(default);
    }

    [Fact]
    public async Task OpenLotBackfill_ReadPermissionCannotRepairAcquisitionFacts()
    {
        var bookId = Guid.NewGuid();
        var store = Substitute.For<IOpenLotBackfillStore>();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(store);
            services.AddSingleton(OpenLotBackfillBookService(bookId));
        }, mapLedgerApi: true, currentUserPermissions: UserPermission.ViewLedgerReports);

        var request = new ApplyOpenLotBackfillRequest(bookId, Guid.NewGuid(), 1, 1, Guid.NewGuid(), 2, "backfill", "spoofed",
            OperationsActionOriginDto.HumanOperator);
        var response = await app.GetTestClient().PostAsJsonAsync($"/api/ledger/books/{bookId}/open-lots/backfill/apply", request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await store.DidNotReceiveWithAnyArgs().ApplyAsync(default!);
    }

    [Fact]
    public async Task OpenLotBackfill_ReviewUsesAuthenticatedActorAndPreservesAutomationRefusal()
    {
        var bookId = Guid.NewGuid();
        var evidenceId = Guid.NewGuid();
        var store = Substitute.For<IOpenLotBackfillStore>();
        ReviewOpenLotBackfillEvidenceRequest? received = null;
        store.ReviewEvidenceAsync(Arg.Any<ReviewOpenLotBackfillEvidenceRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                received = call.ArgAt<ReviewOpenLotBackfillEvidenceRequest>(0);
                throw new InvalidOperationException("Human review required.");
            });
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(store);
            services.AddSingleton(OpenLotBackfillBookService(bookId));
        }, mapLedgerApi: true, currentUserPermissions: UserPermission.AdminMaintenance, currentUserName: "reviewer");

        var request = new ReviewOpenLotBackfillEvidenceRequest(bookId, evidenceId, 1, true, "spoofed", "Retained source checked.");
        var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/ledger/books/{bookId}/open-lots/backfill/evidence/{evidenceId}/review", request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        received.Should().NotBeNull();
        received!.Actor.Should().Be("reviewer");
        received.ActionOrigin.Should().Be(OperationsActionOriginDto.AutomationAssistant);
    }

    [Fact]
    public async Task OpenLotBackfill_CrossBookRequestCannotReachStore()
    {
        var bookId = Guid.NewGuid();
        var store = Substitute.For<IOpenLotBackfillStore>();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(store);
            services.AddSingleton(OpenLotBackfillBookService(bookId));
        }, mapLedgerApi: true, currentUserPermissions: UserPermission.AdminMaintenance);

        var request = new ApplyOpenLotBackfillRequest(Guid.NewGuid(), Guid.NewGuid(), 1, 1, Guid.NewGuid(), 2, "backfill", "actor",
            OperationsActionOriginDto.HumanOperator);
        var response = await app.GetTestClient().PostAsJsonAsync($"/api/ledger/books/{bookId}/open-lots/backfill/apply", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await store.DidNotReceiveWithAnyArgs().ApplyAsync(default!);
    }

    private static ILedgerBookService OpenLotBackfillBookService(Guid bookId)
    {
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(bookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookDto(
            bookId, "fund-backfill", Guid.NewGuid(), FundStructureNodeKindDto.Account, "Backfill book", "USD",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        return books;
    }
}
