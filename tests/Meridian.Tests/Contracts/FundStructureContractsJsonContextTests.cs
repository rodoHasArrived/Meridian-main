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

    [Fact]
    public void SyncHistoryAndReadinessDtos_ShouldRoundTripViaGeneratedContext()
    {
        var request = new RecordAccountSyncHistoryRequest(
            AccountId: Guid.NewGuid(),
            Capability: "brokerage-sync",
            Status: AccountSyncStatusDto.Failed,
            ProviderLinkStatus: AccountProviderLinkStatusDto.Unauthorized,
            ProviderId: "alpaca",
            ExternalAccountId: "PA-123",
            FailureKind: AccountSyncFailureKindDto.Unauthorized,
            FailureMessage: "Unauthorized",
            CorrelationId: "sync-1",
            RawEvidencePath: "artifacts/account-sync/raw.json",
            SecurityMissingCount: 2,
            Warnings: ["Unauthorized provider account."]);

        var requestJson = JsonSerializer.Serialize(request, FundStructureContractsJsonContext.Default.RecordAccountSyncHistoryRequest);
        requestJson.Should().Contain("\"status\":\"Failed\"");
        requestJson.Should().Contain("\"providerLinkStatus\":\"Unauthorized\"");

        var requestRoundTrip = JsonSerializer.Deserialize(requestJson, FundStructureContractsJsonContext.Default.RecordAccountSyncHistoryRequest);
        requestRoundTrip.Should().NotBeNull();
        requestRoundTrip!.FailureKind.Should().Be(AccountSyncFailureKindDto.Unauthorized);
        requestRoundTrip.SecurityMissingCount.Should().Be(2);

        var readiness = new AccountReadinessSnapshotDto(
            AccountId: request.AccountId,
            EvaluatedAt: DateTimeOffset.UtcNow,
            ProviderLinkStatus: AccountProviderLinkStatusDto.Unauthorized,
            LatestSyncStatus: AccountSyncStatusDto.Failed,
            LastSuccessfulSyncAt: null,
            FreshUntil: null,
            IsReady: false,
            Issues:
            [
                new AccountReadinessIssueDto(
                    "account.sync.failed",
                    AccountReadinessSeverityDto.Critical,
                    "Latest account sync failed",
                    "Unauthorized",
                    request.AccountId,
                    ProviderId: "alpaca",
                    ExternalAccountId: "PA-123",
                    Capability: "brokerage-sync",
                    EvidenceLink: "artifacts/account-sync/raw.json")
            ]);

        var readinessJson = JsonSerializer.Serialize(readiness, FundStructureContractsJsonContext.Default.AccountReadinessSnapshotDto);
        readinessJson.Should().Contain("\"severity\":\"Critical\"");

        var readinessRoundTrip = JsonSerializer.Deserialize(readinessJson, FundStructureContractsJsonContext.Default.AccountReadinessSnapshotDto);
        readinessRoundTrip.Should().NotBeNull();
        readinessRoundTrip!.Issues.Should().ContainSingle(issue => issue.Code == "account.sync.failed");
    }
}
