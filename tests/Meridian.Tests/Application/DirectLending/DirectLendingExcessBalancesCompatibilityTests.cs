using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// PRD-115 increment 2: <see cref="OutstandingBalancesDto.PrepaidInterest"/> and
/// <see cref="OutstandingBalancesDto.UnappliedCash"/> are appended with 0 defaults, so servicing
/// state persisted before them (the <c>loan_state.servicing_json</c> and snapshot payloads, read
/// with web serializer defaults) loads unchanged, and the in-memory service carries them through.
/// </summary>
public sealed class DirectLendingExcessBalancesCompatibilityTests
{
    private static readonly JsonSerializerOptions StoreJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LegacyBalancesJson_WithoutExcessMembers_DeserializesToZero()
    {
        const string legacy = """
            {"principalOutstanding":250000.00,"interestAccruedUnpaid":1200.50,"commitmentFeeAccruedUnpaid":10.00,
             "feesAccruedUnpaid":0,"penaltyAccruedUnpaid":0}
            """;

        var balances = JsonSerializer.Deserialize<OutstandingBalancesDto>(legacy, StoreJsonOptions)!;

        balances.Should().Be(new OutstandingBalancesDto(250_000m, 1_200.50m, 10m, 0m, 0m));
        balances.PrepaidInterest.Should().Be(0m);
        balances.UnappliedCash.Should().Be(0m);
    }

    [Fact]
    public void Balances_WithExcessMembers_RoundTripThroughStoreJson()
    {
        var balances = new OutstandingBalancesDto(250_000m, 0m, 0m, 0m, 0m, PrepaidInterest: 123.45m, UnappliedCash: 678.90m);

        var json = JsonSerializer.Serialize(balances, StoreJsonOptions);

        json.Should().Contain("\"prepaidInterest\":123.45").And.Contain("\"unappliedCash\":678.90");
        JsonSerializer.Deserialize<OutstandingBalancesDto>(json, StoreJsonOptions).Should().Be(balances);
    }

    [Fact]
    public async Task InMemoryService_NewLoan_StartsWithZeroExcessBalances()
    {
        var service = new InMemoryDirectLendingService();
        var created = await service.CreateLoanAsync(new CreateLoanRequest(
            null,
            "Excess Balances Loan",
            new BorrowerInfoDto(Guid.NewGuid(), "Borrower", null),
            new DateOnly(2026, 1, 15),
            DirectLendingTermsHashCharacterizationTests.FixedBulletTerms()));
        await service.ActivateLoanAsync(created.LoanId, new ActivateLoanRequest(new DateOnly(2026, 1, 15)));
        await service.BookDrawdownAsync(created.LoanId,
            new BookDrawdownRequest(250_000m, new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 15), "wire-1"));

        var servicing = await service.GetServicingProjectionAsync(created.LoanId);

        servicing!.Balances.PrincipalOutstanding.Should().Be(250_000m);
        servicing.Balances.PrepaidInterest.Should().Be(0m);
        servicing.Balances.UnappliedCash.Should().Be(0m);
    }
}
