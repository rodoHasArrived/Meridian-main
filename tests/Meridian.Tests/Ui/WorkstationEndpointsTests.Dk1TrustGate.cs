using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Monitoring;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using IReconciliationApiService = Meridian.Contracts.Reconciliation.IReconciliationApiService;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ReconciliationCaseSummaryDto = Meridian.Contracts.Reconciliation.ReconciliationCaseSummaryDto;
using ReconciliationQueueAccountStatusDto = Meridian.Contracts.Reconciliation.ReconciliationQueueAccountStatusDto;
using StatementImportSummaryDto = Meridian.Contracts.Reconciliation.StatementImportSummaryDto;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task Dk1TrustGateReadinessService_WithEvidenceBundle_ShouldExposeCalibrationPosture()
    {
        var automationRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "dk1-calibration-bundle",
            Guid.NewGuid().ToString("N"));
        WriteReadyDk1Packet(automationRoot);
        WriteProviderValidationEvidenceBundle(
            automationRoot,
            """
            {
              "generatedAtUtc": "2026-04-27T09:30:00Z",
              "promotionPosture": {
                "status": "candidate-approved",
                "candidateKernelVersion": "kernel-v2"
              }
            }
            """);

        var service = new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance);

        var readiness = await service.GetCurrentAsync();

        readiness.CalibrationVersion.Should().Be("kernel-v2");
        readiness.PromotionPosture.Should().Be("candidate-approved");
        readiness.CalibrationValidatedAt.Should().Be(new DateTimeOffset(2026, 4, 27, 9, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Dk1TrustGateReadinessService_WhenPacketMissing_ShouldNotDiscloseFilesystemPaths()
    {
        var automationRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "dk1-missing-packet",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(automationRoot);

        var service = new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance);

        var readiness = await service.GetCurrentAsync();

        readiness.Status.Should().Be("packet-unavailable");
        readiness.PacketPath.Should().BeNull();
        readiness.Detail.Should().Be("No DK1 pilot parity packet was found under the configured automation root.");
        readiness.Detail.Should().NotContain(NormalizePathForAssertion(automationRoot));
    }

    [Fact]
    public async Task Dk1TrustGateReadinessService_CacheHit_ShouldNotRescanAutomationRoot()
    {
        var automationRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "dk1-cache-hit",
            Guid.NewGuid().ToString("N"));
        WriteReadyDk1Packet(automationRoot);

        var service = new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance);

        var first = await service.GetCurrentAsync();
        Directory.Delete(automationRoot, recursive: true);

        var second = await service.GetCurrentAsync();

        second.Status.Should().Be(first.Status);
        second.PacketPath.Should().Be(first.PacketPath);
        second.Detail.Should().Be(first.Detail);
    }

    [Fact]
    public async Task Dk1TrustGateReadinessService_WithLegacyPacketWithoutContracts_ShouldBlockReview()
    {
        var automationRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "dk1-legacy-packet",
            Guid.NewGuid().ToString("N"));
        WriteReadyDk1Packet(automationRoot, includeContractReview: false);

        var service = new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance);

        var readiness = await service.GetCurrentAsync();

        readiness.ReadyForOperatorReview.Should().BeTrue();
        readiness.Blockers.Should().Contain("DK1 explainability contract is missing from the parity packet.");
        readiness.Blockers.Should().Contain("DK1 calibration contract is missing from the parity packet.");
        readiness.Detail.Should().Contain("explainability contract is missing");
        readiness.TrustRationaleContract.Should().BeNull();
        readiness.BaselineThresholdContract.Should().BeNull();
    }

    [Fact]
    public async Task Dk1TrustGateReadinessService_WithValidatedContractMissingRequiredFields_ShouldBlockReview()
    {
        var automationRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "dk1-malformed-contract-packet",
            Guid.NewGuid().ToString("N"));
        WriteReadyDk1Packet(
            automationRoot,
            """
            {
              "requiredOwners": [ "Data", "Provider Reliability", "Trading" ],
              "status": "signed",
              "requiredBeforeDk1Exit": true,
              "signedOwners": [ "Data", "Provider Reliability", "Trading" ],
              "missingOwners": []
            }
            """,
            includeContractReview: false);

        var packetPath = Path.Combine(automationRoot, "unit-ready", "dk1-pilot-parity-packet.json");
        var malformedPacket = File.ReadAllText(packetPath).Replace(
            "\"operatorSignoff\":",
            """
            "trustRationaleContract": { "status": "validated" },
            "baselineThresholdContract": { "status": "validated" },
            "operatorSignoff":
            """);
        File.WriteAllText(packetPath, malformedPacket);

        var service = new Dk1TrustGateReadinessService(
            new Dk1TrustGateReadinessOptions(automationRoot),
            NullLogger<Dk1TrustGateReadinessService>.Instance);

        var readiness = await service.GetCurrentAsync();

        readiness.ReadyForOperatorReview.Should().BeTrue();
        readiness.OperatorSignoffStatus.Should().Be("signed");
        readiness.Blockers.Should().Contain("DK1 explainability contract is validated but missing required contract fields.");
        readiness.Blockers.Should().Contain("DK1 calibration contract is validated but missing required contract fields.");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithSecurityLookup_ShouldExposeSecurityCoverageInBootstrapPayloads()
    {
        await using var app = await CreateAppAsync(services =>
        {
            var lookup = new StubSecurityReferenceLookup();
            lookup.Register("AAPL", new WorkstationSecurityReference(
                SecurityId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                DisplayName: "Apple Inc.",
                AssetClass: "Equity",
                Currency: "USD",
                Status: SecurityStatusDto.Active,
                PrimaryIdentifier: "AAPL"));

            services.AddSingleton<IStrategyRepository>(new StrategyRunStore());
            services.AddSingleton<ISecurityReferenceLookup>(lookup);
            services.AddSingleton<PortfolioReadService>();
            services.AddSingleton<LedgerReadService>();
            services.AddSingleton<StrategyRunReadService>();
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-security",
            strategyId: "carry-1",
            strategyName: "Carry Pair",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/fx/spot",
            feedReference: "synthetic:fx").Complete(BuildBacktestResultWithSymbol("AAPL")));

        var client = app.GetTestClient();

        using var session = await ReadJsonAsync(client, "/api/workstation/session");
        var latestCoverage = session.RootElement.GetProperty("latestRun").GetProperty("securityCoverage");
        latestCoverage.GetProperty("portfolioResolved").GetInt32().Should().Be(1);
        latestCoverage.GetProperty("portfolioMissing").GetInt32().Should().Be(0);
        latestCoverage.GetProperty("ledgerResolved").GetInt32().Should().Be(1);
        latestCoverage.GetProperty("ledgerMissing").GetInt32().Should().Be(0);
        latestCoverage.GetProperty("hasIssues").GetBoolean().Should().BeFalse();
        latestCoverage.GetProperty("tone").GetString().Should().Be("success");
        latestCoverage.GetProperty("resolvedReferences").GetArrayLength().Should().Be(2);
        latestCoverage.GetProperty("missingReferences").GetArrayLength().Should().Be(0);
        latestCoverage.GetProperty("summary").GetString().Should().Contain("no unresolved symbols");

        using var research = await ReadJsonAsync(client, "/api/workstation/research");
        var runCoverage = research.RootElement.GetProperty("runs")[0].GetProperty("securityCoverage");
        runCoverage.GetProperty("portfolioResolved").GetInt32().Should().Be(1);
        runCoverage.GetProperty("ledgerResolved").GetInt32().Should().Be(1);
        runCoverage.GetProperty("hasIssues").GetBoolean().Should().BeFalse();
        runCoverage.GetProperty("resolvedReferences").EnumerateArray()
            .Should()
            .Contain(item =>
                item.GetProperty("source").GetString() == "portfolio" &&
                item.GetProperty("symbol").GetString() == "AAPL" &&
                item.GetProperty("displayName").GetString() == "Apple Inc.");
    }
}
