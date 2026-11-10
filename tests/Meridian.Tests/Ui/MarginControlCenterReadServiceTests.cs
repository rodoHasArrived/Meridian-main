using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class MarginControlCenterReadServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-margin-control-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetAndCertify_MultiPrimeEvidence_SeparatesProviderAndShadowAndPersistsCertification()
    {
        var asOf = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(-1), TimeSpan.Zero);
        await WriteArtifactAsync("ib", Artifact(
            "ib-flex", "U1", asOf, providerMaintenance: 10000m, providerExcess: 40000m,
            positions:
            [
                Position("U1", "AAPL", 100m, 10000m),
                Position("U1", "GME", -50m, -5000m)
            ]));
        await WriteArtifactAsync("alpaca", Artifact(
            "alpaca", "A1", asOf, providerMaintenance: 2500m, providerExcess: 20000m,
            positions: [Position("A1", "MSFT", 25m, 10000m)]));
        var service = CreateService();

        var center = await service.GetAsync();

        center.ProviderCount.Should().Be(2);
        center.AccountCount.Should().Be(2);
        center.PrimeSummaries.Should().Contain(item => item.ProviderId == "ib-flex" && item.AccountCount == 1);
        var ib = center.Accounts.Single(item => item.ProviderId == "ib-flex");
        ib.ProviderMaintenanceMargin.Should().Be(10000m);
        ib.ShadowModelName.Should().Contain("Reg T");
        ib.ShadowInitialMargin.Should().Be(12500m);
        ib.ShadowMaintenanceMargin.Should().Be(9000m);
        ib.MaintenanceVariance.Should().Be(1000m);
        ib.BorrowPositionCount.Should().Be(1);
        ib.TaxLotCount.Should().Be(1);
        ib.OptionLifecycleEventCount.Should().Be(1);
        ib.PositionContributions.Should().OnlyContain(position =>
            position.SecurityId == null && position.SecurityMasterSource == "ProviderStatementSymbolUnresolved");
        ib.CertificationState.Should().Be("AwaitingOperatorCertification");

        var certified = await service.CertifyAsync(
            new Meridian.Contracts.Workstation.MarginCertificationRequestDto(
                ib.ProviderId, ib.AccountId, ib.AsOf, ib.EvidencePath, "Reviewed provider EOD statement and activity completeness."),
            "fund-controller");
        certified.Status.Should().Be("Certified");

        var refreshed = await service.GetAsync();
        refreshed.Accounts.Single(item => item.ProviderId == "ib-flex").Should().Match<Meridian.Contracts.Workstation.MarginControlAccountDto>(item =>
            item.CertificationState == "Certified" && item.CertifiedBy == "fund-controller");
    }

    [Fact]
    public async Task CertifyAsync_IntradaySnapshot_IsRejected()
    {
        var asOf = DateTimeOffset.UtcNow;
        await WriteArtifactAsync("alpaca", Artifact(
            "alpaca", "A1", asOf, providerMaintenance: 2500m, providerExcess: 20000m,
            positions: [Position("A1", "MSFT", 25m, 10000m)]));
        var service = CreateService();
        var account = (await service.GetAsync()).Accounts.Single();

        var act = () => service.CertifyAsync(
            new Meridian.Contracts.Workstation.MarginCertificationRequestDto(
                account.ProviderId, account.AccountId, account.AsOf, account.EvidencePath, "Attempted intraday certification."),
            "operator");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Intraday*");
    }

    [Fact]
    public async Task CertifyAsync_StaleSnapshot_IsRejectedAndAlerted()
    {
        var asOf = DateTimeOffset.UtcNow.AddDays(-4);
        await WriteArtifactAsync("stale", Artifact(
            "ib-flex", "U-STALE", asOf, providerMaintenance: 2500m, providerExcess: 20000m,
            positions: [Position("U-STALE", "MSFT", 25m, 10000m)]));
        var service = CreateService();
        var center = await service.GetAsync();
        var account = center.Accounts.Single();

        account.CertificationState.Should().Be("StaleEvidence");
        center.Alerts.Should().ContainSingle(alert => alert.Code == "EVIDENCE_STALE");

        var act = () => service.CertifyAsync(
            new Meridian.Contracts.Workstation.MarginCertificationRequestDto(
                account.ProviderId, account.AccountId, account.AsOf, account.EvidencePath, "Attempted stale certification."),
            "operator");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stale*");
    }

    [Fact]
    public async Task GetAndCertify_WithVisibilityFilter_HidesOtherTenantsAccounts()
    {
        var asOf = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(-1), TimeSpan.Zero);
        await WriteArtifactAsync("ib", Artifact(
            "ib-flex", "U1", asOf, providerMaintenance: 10000m, providerExcess: 40000m,
            positions: [Position("U1", "AAPL", 100m, 10000m)]));
        await WriteArtifactAsync("alpaca", Artifact(
            "alpaca", "A1", asOf, providerMaintenance: 2500m, providerExcess: 20000m,
            positions: [Position("A1", "MSFT", 25m, 10000m)]));
        var service = CreateService();
        Func<MarginEvidenceOwner, bool> onlyA1 = owner => owner.AccountId == "A1";

        var center = await service.GetAsync(onlyA1);

        center.Accounts.Should().ContainSingle().Which.AccountId.Should().Be("A1");
        center.PrimeSummaries.Should().ContainSingle().Which.ProviderId.Should().Be("alpaca");
        center.Alerts.Should().OnlyContain(alert => alert.AccountId == "A1");

        var hidden = (await service.GetAsync()).Accounts.Single(item => item.AccountId == "U1");
        var act = () => service.CertifyAsync(
            new Meridian.Contracts.Workstation.MarginCertificationRequestDto(
                hidden.ProviderId, hidden.AccountId, hidden.AsOf, hidden.EvidencePath, "Reviewed."),
            "fund-controller",
            onlyA1);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetAndCertify_SameExternalIdUnderAnotherProvider_StaysWithItsImportingFundAccount()
    {
        // Tenant A's fund account is bound to (alpaca, SHARED); tenant B imported (ib-flex, SHARED)
        // for its own fund account. External ids are unique only within a provider, so the shared
        // id must not admit B's evidence for A.
        var asOf = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(-1), TimeSpan.Zero);
        var tenantAFundAccount = Guid.NewGuid();
        var tenantBFundAccount = Guid.NewGuid();
        await WriteArtifactAsync("a-alpaca", Artifact(
            "alpaca", "SHARED", asOf, providerMaintenance: 2500m, providerExcess: 20000m,
            positions: [Position("SHARED", "MSFT", 25m, 10000m)]) with
        { FundAccountId = tenantAFundAccount.ToString("D") });
        await WriteArtifactAsync("b-ib", Artifact(
            "ib-flex", "SHARED", asOf, providerMaintenance: 10000m, providerExcess: 40000m,
            positions: [Position("SHARED", "AAPL", 100m, 10000m)]) with
        { FundAccountId = tenantBFundAccount.ToString("D") });
        await WriteArtifactAsync("legacy", Artifact(
            "alpaca", "SHARED-LEGACY", asOf, providerMaintenance: 1000m, providerExcess: 5000m,
            positions: [Position("SHARED-LEGACY", "IBM", 5m, 1000m)]));
        var service = CreateService();
        var tenantAOwns = new Dictionary<Guid, HashSet<string>>
        {
            [tenantAFundAccount] = new(StringComparer.OrdinalIgnoreCase) { "SHARED", "SHARED-LEGACY" }
        };
        Func<MarginEvidenceOwner, bool> tenantA = owner => WorkstationEndpoints.IsMarginEvidenceVisible(tenantAOwns, owner);

        var center = await service.GetAsync(tenantA);

        center.Accounts.Should().ContainSingle().Which.ProviderId.Should().Be("alpaca",
            "tenant B's ib-flex evidence and evidence with no recorded fund account must stay hidden");

        var tenantBAccount = (await service.GetAsync()).Accounts.Single(item => item.ProviderId == "ib-flex");
        var act = () => service.CertifyAsync(
            new Meridian.Contracts.Workstation.MarginCertificationRequestDto(
                tenantBAccount.ProviderId, tenantBAccount.AccountId, tenantBAccount.AsOf, tenantBAccount.EvidencePath, "Reviewed."),
            "tenant-a-controller",
            tenantA);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public void IsMarginEvidenceVisible_RequiresTheOwnedFundAccountAndOneOfItsIdentifiers()
    {
        var owned = Guid.NewGuid();
        var other = Guid.NewGuid();
        var ownership = new Dictionary<Guid, HashSet<string>>
        {
            [owned] = new(StringComparer.OrdinalIgnoreCase) { "U1" },
            [other] = new(StringComparer.OrdinalIgnoreCase) { "U2" }
        };

        WorkstationEndpoints.IsMarginEvidenceVisible(ownership, new(owned.ToString("D"), "ib-flex", " u1 ")).Should().BeTrue();
        WorkstationEndpoints.IsMarginEvidenceVisible(ownership, new(owned.ToString("D"), "ib-flex", "U2")).Should()
            .BeFalse("an id owned by a different fund account does not belong to this import");
        WorkstationEndpoints.IsMarginEvidenceVisible(ownership, new(Guid.NewGuid().ToString("D"), "ib-flex", "U1")).Should()
            .BeFalse("evidence imported for an unowned fund account is hidden");
        WorkstationEndpoints.IsMarginEvidenceVisible(ownership, new(null, "ib-flex", "U1")).Should()
            .BeFalse("evidence with no recorded fund account is hidden");
    }

    private MarginControlCenterReadService CreateService()
        => new(new StatementCanonicalEvidenceReader(_root), new MarginCertificationStore(_root));

    private async Task WriteArtifactAsync(string folder, StatementCanonicalEvidenceArtifact artifact)
    {
        var path = Path.Combine(_root, "reconciliation", "statement-connector-imports", folder, "canonical-evidence.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(artifact, options));
    }

    private static StatementCanonicalEvidenceArtifact Artifact(
        string providerId,
        string accountId,
        DateTimeOffset asOf,
        decimal providerMaintenance,
        decimal providerExcess,
        IReadOnlyList<StatementCanonicalRecord> positions)
        => new(
            ConnectorId: providerId,
            ProfileId: "test",
            RetainedAtUtc: asOf.AddHours(1),
            Fingerprint: new StatementFormatFingerprint("hash", [], "json"),
            Records: positions,
            AccountSnapshots:
            [
                new BrokerageAccountSnapshotDto(
                    providerId, accountId, asOf, "USD", "Active", BrokerageMarginRegime.RegulationT,
                    Cash: 35000m, Equity: 50000m, BuyingPower: 70000m,
                    InitialMargin: providerMaintenance * 1.5m,
                    MaintenanceMargin: providerMaintenance,
                    ExcessLiquidity: providerExcess,
                    ShortingEnabled: true)
            ],
            ActivityEvents:
            [
                new BrokerageActivityEventDto(
                    "option-1", "OPASN", BrokerageActivityCategory.OptionLifecycle, BrokerageActivitySubtype.OptionAssignment,
                    asOf, "USD", 0m, "AAPL", -1m, Option: new BrokerageOptionLifecycleSnapshotDto(
                        "AAPL-C", "AAPL", "Call", 190m, DateOnly.FromDateTime(asOf.UtcDateTime), 100m, "Assignment"),
                    Metadata: new Dictionary<string, string> { ["accountId"] = accountId })
            ],
            ActivityCursors: [new BrokerageActivityCursorDto("event-1", asOf, 2, 101, true)],
            TaxLots: [new BrokerageTaxLotSnapshotDto("lot-1", "AAPL", new DateOnly(2026, 1, 2), 100m, 8000m, "USD", AccountId: accountId)],
            BorrowPositions: [new BrokerageBorrowPositionSnapshotDto("GME", -50m, BrokerageBorrowStatus.HardToBorrow, "USD", BorrowRate: 18.5m, AccountId: accountId)]);

    private static StatementCanonicalRecord Position(string accountId, string symbol, decimal quantity, decimal marketValue)
        => new(StatementRecordKind.Position, accountId, symbol, quantity, 100m, marketValue, "position", new DateOnly(2026, 7, 17));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
