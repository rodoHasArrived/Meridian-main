using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.DirectLending;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class DirectLendingEndpointMutationTests
{
    private readonly EndpointTestFixture _fixture;

    public DirectLendingEndpointMutationTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MixedPaymentFeeAndWriteOff_WritePathMutationsPersistExpectedState()
    {
        using var client = _fixture.CreateNoRedirectClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "directlending-admin");

        var loanId = Guid.NewGuid();
        var createResponse = await client.PostAsJsonAsync("/api/loans", BuildCreateLoanRequest(loanId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var activateResponse = await client.PostAsJsonAsync($"/api/loans/{loanId}/activate", new ActivateLoanRequest(new DateOnly(2026, 4, 1)));
        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var drawdownResponse = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/drawdowns",
            new BookDrawdownRequest(300_000m, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 2), "drawdown-it-1"));
        drawdownResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var paymentResponse = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/payments",
            new ApplyMixedPaymentRequest(
                Amount: 10_000m,
                EffectiveDate: new DateOnly(2026, 4, 3),
                Breakdown: new PaymentBreakdownDto(ToInterest: 0m, ToCommitmentFee: 0m, ToFees: 0m, ToPenalty: 0m, ToPrincipal: 10_000m),
                ExternalRef: "mixed-payment-it-1"));
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var feeResponse = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/fees",
            new AssessFeeRequest(FeeType: "LateFee", Amount: 250m, EffectiveDate: new DateOnly(2026, 4, 4), Note: "integration fee"));
        feeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var writeOffResponse = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/writeoffs",
            new ApplyWriteOffRequest(Amount: 2_000m, EffectiveDate: new DateOnly(2026, 4, 5), Reason: "credit impairment"));
        writeOffResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var servicing = await client.GetFromJsonAsync<LoanServicingStateDto>($"/api/loans/{loanId}/servicing-state");
        var cashTransactions = await client.GetFromJsonAsync<List<CashTransactionDto>>($"/api/loans/{loanId}/cash-transactions");
        var allocations = await client.GetFromJsonAsync<List<PaymentAllocationDto>>($"/api/loans/{loanId}/payment-allocations");
        var feeBalances = await client.GetFromJsonAsync<List<FeeBalanceDto>>($"/api/loans/{loanId}/fee-balances");

        servicing.Should().NotBeNull();
        servicing!.Balances.PrincipalOutstanding.Should().Be(288_000m);
        servicing.Balances.FeesAccruedUnpaid.Should().Be(250m);

        cashTransactions.Should().NotBeNull();
        cashTransactions!.Should().HaveCountGreaterThanOrEqualTo(1);
        cashTransactions.Should().Contain(tx => tx.TransactionType.Contains("Payment", StringComparison.OrdinalIgnoreCase));

        allocations.Should().NotBeNull();
        allocations!.Sum(a => a.AllocatedAmount).Should().Be(10_000m);

        feeBalances.Should().NotBeNull();
        feeBalances!.Should().ContainSingle(f => f.FeeType == "LateFee" && f.UnpaidAmount == 250m);

        var history = await client.GetFromJsonAsync<List<LoanEventLineageDto>>($"/api/loans/{loanId}/history");
        history.Should().NotBeNull();
        history!.Should().Contain(item => item.EventType.Contains("payment", StringComparison.OrdinalIgnoreCase));
        history.Should().Contain(item => item.EventType.Contains("write", StringComparison.OrdinalIgnoreCase) && item.EventType.Contains("off", StringComparison.OrdinalIgnoreCase));
        history.Should().Contain(item => item.EventType.Contains("fee", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PrincipalPaymentMutation_ReducesOutstandingPrincipal()
    {
        using var client = _fixture.CreateNoRedirectClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "directlending-admin");

        var loanId = Guid.NewGuid();
        var createResponse = await client.PostAsJsonAsync("/api/loans", BuildCreateLoanRequest(loanId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var activateResponse = await client.PostAsJsonAsync($"/api/loans/{loanId}/activate", new ActivateLoanRequest(new DateOnly(2026, 5, 1)));
        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var drawdownResponse = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/drawdowns",
            new BookDrawdownRequest(150_000m, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 2), "drawdown-it-2"));
        drawdownResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var principalPaymentResponse = await client.PostAsJsonAsync(
            $"/api/loans/{loanId}/payments/principal",
            new ApplyPrincipalPaymentRequest(Amount: 40_000m, EffectiveDate: new DateOnly(2026, 5, 3), ExternalRef: "principal-payment-it-1"));
        principalPaymentResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var servicing = await client.GetFromJsonAsync<LoanServicingStateDto>($"/api/loans/{loanId}/servicing-state");
        servicing.Should().NotBeNull();
        servicing!.Balances.PrincipalOutstanding.Should().Be(110_000m);
        servicing.LastPaymentDate.Should().Be(new DateOnly(2026, 5, 3));
    }

    private static CreateLoanRequest BuildCreateLoanRequest(Guid loanId)
        => new(
            LoanId: loanId,
            FacilityName: $"Integration Loan {loanId:N}",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Integration Borrower LLC", Guid.NewGuid()),
            EffectiveDate: new DateOnly(2026, 4, 1),
            Terms: new DirectLendingTermsDto(
                OriginationDate: new DateOnly(2026, 4, 1),
                MaturityDate: new DateOnly(2028, 4, 1),
                CommitmentAmount: 1_000_000m,
                BaseCurrency: CurrencyCode.USD,
                RateTypeKind: RateTypeKind.Fixed,
                FixedAnnualRate: 0.08m,
                InterestIndexName: null,
                SpreadBps: null,
                FloorRate: null,
                CapRate: null,
                DayCountBasis: DayCountBasis.Act360,
                PaymentFrequency: PaymentFrequency.Quarterly,
                AmortizationType: AmortizationType.InterestOnly,
                CommitmentFeeRate: 0.01m,
                DefaultRateSpreadBps: 200m,
                PrepaymentAllowed: true,
                CovenantsJson: "{}"));
}
