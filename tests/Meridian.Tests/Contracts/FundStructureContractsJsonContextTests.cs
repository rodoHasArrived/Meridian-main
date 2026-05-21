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

    [Fact]
    public void MarginCollateralAndTransferDtos_ShouldRoundTripViaGeneratedContext()
    {
        var accountId = Guid.NewGuid();
        var margin = new RecordMarginSnapshotRequest(
            AccountId: accountId,
            EffectiveAt: DateTimeOffset.UtcNow,
            Currency: "USD",
            MarginType: MarginModelTypeDto.RegT,
            MarginCallStatus: MarginCallStatusDto.Potential,
            InitialMargin: 10_000m,
            MaintenanceMargin: 5_000m,
            ExcessLiquidity: 2_500m,
            BuyingPower: 50_000m,
            Requirements:
            [
                new MarginRequirementDto(
                    SecurityId: "sec-aapl",
                    Symbol: "AAPL",
                    Quantity: 100m,
                    MarketValue: 20_000m,
                    InitialRequirement: 10_000m,
                    MaintenanceRequirement: 5_000m,
                    IsMarginable: true,
                    CollateralClass: "equity-large-cap",
                    Haircut: 0.15m,
                    EvidencePath: "artifacts/margin/aapl.json")
            ],
            ProviderId: "alpaca",
            ExternalAccountId: "PA-MARGIN",
            SnapshotEvidencePath: "artifacts/margin/current.json");

        var marginJson = JsonSerializer.Serialize(margin, FundStructureContractsJsonContext.Default.RecordMarginSnapshotRequest);
        marginJson.Should().Contain("\"marginType\":\"RegT\"");
        marginJson.Should().Contain("\"marginCallStatus\":\"Potential\"");

        var marginRoundTrip = JsonSerializer.Deserialize(marginJson, FundStructureContractsJsonContext.Default.RecordMarginSnapshotRequest);
        marginRoundTrip.Should().NotBeNull();
        marginRoundTrip!.Requirements.Should().ContainSingle(requirement => requirement.Symbol == "AAPL");

        var collateral = new CollateralPositionDto(
            CollateralPositionId: Guid.NewGuid(),
            AccountId: accountId,
            SecurityId: "sec-tbill",
            Symbol: "912797LK5",
            Quantity: 100_000m,
            MarketValue: 99_250m,
            Currency: "USD",
            IsPledged: true,
            RestrictionType: "pledged",
            Haircut: 0.02m,
            CollateralValue: 97_265m,
            Eligibility: CollateralEligibilityDto.Eligible,
            ValuationTimestamp: DateTimeOffset.UtcNow,
            LienReference: "LIEN-1",
            SecuredObligationReference: "MARGIN-LOAN-1",
            AgreementEvidencePath: "artifacts/collateral/agreement.pdf");

        var collateralJson = JsonSerializer.Serialize(collateral, FundStructureContractsJsonContext.Default.CollateralPositionDto);
        collateralJson.Should().Contain("\"eligibility\":\"Eligible\"");

        var transfer = new CashTransferDto(
            TransferId: Guid.NewGuid(),
            FromAccountId: accountId,
            ToAccountId: Guid.NewGuid(),
            Amount: 25_000m,
            Currency: "USD",
            TradeDate: new DateOnly(2026, 5, 18),
            SettlementDate: new DateOnly(2026, 5, 19),
            Status: CashTransferStatusDto.Settled,
            ProviderId: "meridian-bank",
            ExternalTransferId: "WIRE-1",
            TransferType: "wire",
            EvidencePath: "artifacts/transfers/wire-1.json");

        var transferJson = JsonSerializer.Serialize(transfer, FundStructureContractsJsonContext.Default.CashTransferDto);
        transferJson.Should().Contain("\"status\":\"Settled\"");

        var match = new TransferMatchDto(
            TransferMatchId: Guid.NewGuid(),
            SourceTransferId: transfer.TransferId,
            MatchedTransferId: Guid.NewGuid(),
            Status: TransferMatchStatusDto.Matched,
            AmountVariance: 0m,
            SettlementDateDeltaDays: 0,
            EvidencePath: "artifacts/transfers/match.json",
            MatchedAt: DateTimeOffset.UtcNow);
        var matchJson = JsonSerializer.Serialize(match, FundStructureContractsJsonContext.Default.TransferMatchDto);
        matchJson.Should().Contain("\"status\":\"Matched\"");
    }
}
