using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;

namespace Meridian.Tests.Contracts;

public sealed class FundStructureContractsJsonContextTests
{
    [Fact]
    public void AccountDetailsEnvelope_ShouldRoundTripWithDiscriminator()
    {
        AccountDetailsDto payload = new CustodianAccountDetailsEnvelopeDto(
            new CustodianAccountDetailsDto(
                SubAccountNumber: "sub-1",
                DtcParticipantCode: "DTC",
                CrestMemberCode: null,
                EuroclearAccountNumber: null,
                ClearstreamAccountNumber: null,
                PrimebrokerGiveupCode: "PB",
                SafekeepingLocation: "NYC",
                ServiceAgreementReference: "SA-9"));

        var json = JsonSerializer.Serialize(payload, FundStructureContractsJsonContext.Default.AccountDetailsDto);
        json.Should().Contain("\"kind\":\"custodian\"");

        var roundTrip = JsonSerializer.Deserialize(json, FundStructureContractsJsonContext.Default.AccountDetailsDto);
        roundTrip.Should().BeOfType<CustodianAccountDetailsEnvelopeDto>();
    }

    [Fact]
    public void CreateAndBalanceDtos_ShouldRoundTripViaGeneratedContext()
    {
        var create = new CreateAccountRequest(
            Guid.NewGuid(),
            AccountTypeDto.Custody,
            "CUST-001",
            "Custodian Primary",
            "USD",
            DateTimeOffset.UtcNow,
            "tester",
            CustodianDetails: new CustodianAccountDetailsDto("sub", null, null, null, null, null, null, null));

        var createJson = JsonSerializer.Serialize(create, FundStructureContractsJsonContext.Default.CreateAccountRequest);
        var createRoundTrip = JsonSerializer.Deserialize(createJson, FundStructureContractsJsonContext.Default.CreateAccountRequest);
        createRoundTrip.Should().NotBeNull();
        createRoundTrip!.AccountCode.Should().Be("CUST-001");

        var snapshotRequest = new RecordAccountBalanceSnapshotRequest(
            Guid.NewGuid(),
            new DateOnly(2026, 4, 1),
            "USD",
            100m,
            "manual",
            PendingSettlement: 10m);

        var snapshotJson = JsonSerializer.Serialize(snapshotRequest, FundStructureContractsJsonContext.Default.RecordAccountBalanceSnapshotRequest);
        var snapshotRoundTrip = JsonSerializer.Deserialize(snapshotJson, FundStructureContractsJsonContext.Default.RecordAccountBalanceSnapshotRequest);
        snapshotRoundTrip.Should().NotBeNull();
        snapshotRoundTrip!.PendingSettlement.Should().Be(10m);
    }
}
