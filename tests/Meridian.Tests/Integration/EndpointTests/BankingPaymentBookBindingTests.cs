using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Banking;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Banking;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// A payment intent may name the ledger book it is released from. The book decides which
/// shadow-mode status governs approval, so the server binds it only for a book the authenticated
/// tenant and company own, and binds their identities rather than any the request states.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BankingPaymentBookBindingTests
{
    private static readonly Guid EntityId = Guid.Parse("5a3c8e1f-9d2b-4f66-8e4c-3b7d8f0a2c15");
    private static readonly Guid OwnedBook = Guid.Parse("6b4d9f20-0000-0000-0000-000000000001");
    private static readonly Guid ForeignBook = Guid.Parse("6b4d9f20-0000-0000-0000-000000000002");

    [Fact]
    public async Task PaymentNamingAnOwnedBook_IsBoundToTheSessionTenantAndCompany()
    {
        var banking = new InMemoryBankingService();
        await using var app = await CreateAppAsync(banking);

        using var response = await app.GetTestClient().PostAsync(
            $"/api/banking/payments?entityId={EntityId:D}",
            Body(OwnedBook));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = (await banking.GetPendingPaymentsAsync(EntityId)).Should().ContainSingle().Subject;
        (payment.TenantId, payment.CompanyId, payment.LedgerBookId).Should().Be(("tenant-a", "company-a", OwnedBook));
    }

    [Fact]
    public async Task PaymentNamingAnotherTenantsBook_IsRefused_AndNothingIsCreated()
    {
        var banking = new InMemoryBankingService();
        await using var app = await CreateAppAsync(banking);

        using var response = await app.GetTestClient().PostAsync(
            $"/api/banking/payments?entityId={EntityId:D}",
            Body(ForeignBook));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await banking.GetPendingPaymentsAsync(EntityId)).Should().BeEmpty();
    }

    private static StringContent Body(Guid ledgerBookId)
        => new(
            $$"""{"amount":100,"effectiveDate":"2026-06-01","currency":"USD","ledgerBookId":"{{ledgerBookId:D}}","boundTenantId":"tenant-spoof","boundCompanyId":"company-spoof"}""",
            Encoding.UTF8,
            "application/json");

    private static async Task<WebApplication> CreateAppAsync(InMemoryBankingService banking)
    {
        var ledger = Substitute.For<ILedgerJournalStore>();
        ledger.GetLedgerBookAsync(OwnedBook, Arg.Any<CancellationToken>()).Returns(Book(OwnedBook, "fund-owned"));
        ledger.GetLedgerBookAsync(ForeignBook, Arg.Any<CancellationToken>()).Returns(Book(ForeignBook, "fund-foreign"));
        var registry = Substitute.For<IFundProfileTenancyRegistry>();
        registry.ResolveAsync("fund-owned", Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership("fund-owned", "tenant-a", "company-a"));
        registry.ResolveAsync("fund-foreign", Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership("fund-foreign", "tenant-b", "company-b"));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IBankingService>(banking);
        builder.Services.AddSingleton(ledger);
        builder.Services.AddSingleton(registry);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] =
                UserPermission.ViewDirectLending | UserPermission.ManageDirectLending;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "treasury-operator";
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.Accounting;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
            await next(context);
        });
        app.MapBankingEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return app;
    }

    private static LedgerBookRecord Book(Guid ledgerBookId, string fundProfileId)
        => new(ledgerBookId, fundProfileId, Guid.NewGuid(), FundStructureNodeKindDto.Fund, "Primary book", "USD",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
