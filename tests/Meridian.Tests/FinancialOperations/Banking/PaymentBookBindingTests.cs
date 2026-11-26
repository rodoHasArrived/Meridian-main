using FluentAssertions;
using Meridian.Contracts.Banking;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Banking;
using Meridian.Storage.Banking;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Banking;

public sealed class PaymentBookBindingTests
{
    private static readonly Guid Book = Guid.Parse("7a1b0000-0000-0000-0000-000000000001");

    [Fact]
    public async Task BoundPayment_OnAShadowBook_IsNotApproved_AndStaysPending()
    {
        var guard = new StaticBookOfRecordGuard(isBookOfRecord: false);
        var service = new InMemoryBankingService(guard);
        var pending = await service.InitiatePaymentAsync(Guid.NewGuid(), BoundRequest());

        var approve = () => service.ApprovePaymentAsync(pending.PendingPaymentId, new ApprovePaymentRequest(null, "reviewer"));

        (await approve.Should().ThrowAsync<BankingConflictException>()).WithMessage("*shadow book*");
        (await service.GetPaymentAsync(pending.PendingPaymentId))!.Status.Should().Be(PaymentApprovalStatus.Pending);
        guard.Requests.Should().ContainSingle().Which.Should().Be(("tenant-a", "company-a", Book));
    }

    [Fact]
    public async Task BoundPayment_OnTheBookOfRecord_IsApproved_AndRetainsItsBinding()
    {
        var service = new InMemoryBankingService(new StaticBookOfRecordGuard(isBookOfRecord: true));
        var pending = await service.InitiatePaymentAsync(Guid.NewGuid(), BoundRequest());

        var approved = await service.ApprovePaymentAsync(pending.PendingPaymentId, new ApprovePaymentRequest(null, "reviewer"));

        approved!.Status.Should().Be(PaymentApprovalStatus.Approved);
        (approved.TenantId, approved.CompanyId, approved.LedgerBookId).Should().Be(("tenant-a", "company-a", Book));
    }

    [Fact]
    public async Task UnboundLegacyPayment_IsApprovedWithoutABookToCheck()
    {
        var guard = new StaticBookOfRecordGuard(isBookOfRecord: false);
        var service = new InMemoryBankingService(guard);
        var pending = await service.InitiatePaymentAsync(
            Guid.NewGuid(), new InitiatePaymentRequest(100m, new DateOnly(2026, 6, 1), null, null, "USD"));

        var approved = await service.ApprovePaymentAsync(pending.PendingPaymentId, new ApprovePaymentRequest(null, "reviewer"));

        approved!.Status.Should().Be(PaymentApprovalStatus.Approved);
        approved.LedgerBookId.Should().BeNull();
        guard.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BoundPayment_WithoutABookOfRecordGuard_FailsClosed()
    {
        var service = new InMemoryBankingService();
        var pending = await service.InitiatePaymentAsync(Guid.NewGuid(), BoundRequest());

        var approve = () => service.ApprovePaymentAsync(pending.PendingPaymentId, new ApprovePaymentRequest(null, "reviewer"));

        await approve.Should().ThrowAsync<BankingConflictException>().WithMessage("*book-of-record status is unavailable*");
    }

    [Theory]
    [InlineData(null, "company-a")]
    [InlineData("tenant-a", " ")]
    public async Task PaymentNamingABook_WithoutTheServerBoundTenantAndCompany_IsRefused(string? tenant, string? company)
    {
        var service = new InMemoryBankingService(new StaticBookOfRecordGuard(isBookOfRecord: true));

        var initiate = () => service.InitiatePaymentAsync(
            Guid.NewGuid(), BoundRequest() with { BoundTenantId = tenant, BoundCompanyId = company });

        await initiate.Should().ThrowAsync<BankingException>().WithMessage("*tenant and company*");
    }

    [Fact]
    public async Task PostgresService_RefusesAShadowBookBeforeTransitioningTheStoredIntent()
    {
        var store = Substitute.For<IBankingStore>();
        var retained = new PendingPaymentDto(Guid.NewGuid(), Guid.NewGuid(), 100m, new DateOnly(2026, 6, 1), null, null,
            PaymentApprovalStatus.Pending, null, null, DateTimeOffset.UtcNow, null, "USD",
            TenantId: "tenant-a", CompanyId: "company-a", LedgerBookId: Book);
        store.GetPendingPaymentAsync(retained.PendingPaymentId, Arg.Any<CancellationToken>()).Returns(retained);
        var service = new PostgresBankingService(store, new StaticBookOfRecordGuard(isBookOfRecord: false));

        var approve = () => service.ApprovePaymentAsync(retained.PendingPaymentId, new ApprovePaymentRequest(null, "reviewer"));

        await approve.Should().ThrowAsync<BankingConflictException>();
        await store.DidNotReceiveWithAnyArgs().TryTransitionPendingPaymentAsync(
            default, default, default, default, default, default);
    }

    [Fact]
    public void ClientRequestBody_CannotStateTheBoundTenantOrCompany()
    {
        var request = System.Text.Json.JsonSerializer.Deserialize<InitiatePaymentRequest>(
            $$"""{"amount":100,"effectiveDate":"2026-06-01","currency":"USD","ledgerBookId":"{{Book}}","boundTenantId":"tenant-spoof","boundCompanyId":"company-spoof"}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        request.LedgerBookId.Should().Be(Book);
        request.BoundTenantId.Should().BeNull();
        request.BoundCompanyId.Should().BeNull();
    }

    private static InitiatePaymentRequest BoundRequest()
        => new(100m, new DateOnly(2026, 6, 1), "ext-1", null, "USD", Book)
        {
            BoundTenantId = "tenant-a",
            BoundCompanyId = "company-a"
        };

    private sealed class StaticBookOfRecordGuard(bool isBookOfRecord) : IBookOfRecordGuard
    {
        public List<(string TenantId, string? CompanyId, Guid LedgerBookId)> Requests { get; } = [];

        public Task<BookOfRecordStatusDto> GetStatusAsync(
            string tenantId, string? companyId, Guid ledgerBookId, CancellationToken ct = default)
        {
            Requests.Add((tenantId, companyId, ledgerBookId));
            return Task.FromResult(new BookOfRecordStatusDto(ledgerBookId, isBookOfRecord,
                isBookOfRecord ? null : OnboardingShadowStates.Shadow, null,
                isBookOfRecord ? "The ledger book is not in shadow mode." : "The ledger book is a shadow book in parallel run."));
        }
    }
}
