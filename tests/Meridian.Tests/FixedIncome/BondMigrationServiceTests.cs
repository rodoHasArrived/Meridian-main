using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed class BondMigrationServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bond-migration-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset AsOf = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PreviewComparesActualPaymentDates_AndStagesOnlyThroughTheWorkbench()
    {
        var bond = Bond(false);
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(bond.SecurityId, Arg.Any<CancellationToken>()).Returns(bond);
        var flows = Substitute.For<ISecurityMasterCashFlowService>();
        flows.GetProjectionAsync(Arg.Any<SecurityDetailDto>(), StructuredCashFlowScenario.Base,
                Arg.Any<BondCalculationContext>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<StructuredCashFlowProjectionDto?>(Projection(bond.SecurityId,
                call.Arg<SecurityDetailDto>().AssetSpecificTerms.TryGetProperty("scheduleConventions", out _) ? 0.125m : 12.5m)));
        var service = Service(store, flows);

        var preview = await service.PreviewAsync(Request(bond));
        var field = await service.PrepareEditAsync(preview, "operator", "Confirm half-percent coupon units");

        preview.CanStage.Should().BeTrue();
        preview.CashDifferences.Should().ContainSingle().Which.Should().Be(
            new BondMigrationCashDifference(new DateOnly(2026, 4, 1), 0m, 0m, 12.5m, 0.125m));
        preview.Gaps.Should().Contain(gap => gap.Contains("accounting effects", StringComparison.Ordinal));
        field.FieldPath.Should().Be("assetSpecificTerms.scheduleConventions");
        field.ExpectedVersion.Should().Be(bond.Version);
        field.SourceRecordId.Should().Contain(preview.PreviewSha256);
        await store.DidNotReceiveWithAnyArgs().UpsertProjectionAsync(default!, default);
    }

    [Fact]
    public async Task PreviewRefusesAnInferredCouponUnit()
    {
        var bond = Bond(false);
        var service = Service(Substitute.For<ISecurityMasterStore>(), Substitute.For<ISecurityMasterCashFlowService>());
        var request = Request(bond) with { ScheduleConventions = JsonSerializer.SerializeToElement(new { accrualAdjusted = false }) };

        await service.Invoking(value => value.PreviewAsync(request)).Should().ThrowAsync<ArgumentException>()
            .WithMessage("*explicitly reviewed*");
    }

    [Fact]
    public async Task StagingRefusesChangedSourceEconomicsEvenWhenSecurityVersionIsUnchanged()
    {
        var bond = Bond(false);
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(bond.SecurityId, Arg.Any<CancellationToken>()).Returns(bond);
        var flows = Substitute.For<ISecurityMasterCashFlowService>();
        var interest = 1m;
        flows.GetProjectionAsync(Arg.Any<SecurityDetailDto>(), StructuredCashFlowScenario.Base,
                Arg.Any<BondCalculationContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<StructuredCashFlowProjectionDto?>(Projection(bond.SecurityId, interest)));
        var service = Service(store, flows);
        var preview = await service.PreviewAsync(Request(bond));
        interest = 2m;

        await service.Invoking(value => value.PrepareEditAsync(preview, "operator", "Reviewed"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*inputs changed*");
    }

    [Fact]
    public async Task FullInventoryCutoverRequiresEveryUndeclaredBondToBeExplicitlyBlocked()
    {
        var declared = Bond(true);
        var legacy = Bond(false);
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { declared, legacy });
        var policy = new FileBondCalculationPolicy(Path.Combine(_directory, "cutover.json"));
        var service = new BondMigrationService(store, Substitute.For<ISecurityMasterCashFlowService>(), policy);
        var inventory = await service.GetInventoryAsync();

        inventory.Bonds.Should().HaveCount(2);
        await service.Invoking(value => value.CutoverAsync(new(inventory.InventorySha256, "controller", [])))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*Every active bond*");
        var row = inventory.Bonds.Single(value => value.SecurityId == legacy.SecurityId);
        var request = new BondMigrationCutoverRequest(inventory.InventorySha256, "controller",
            [new(legacy.SecurityId, row.Version, row.SecurityContentSha256, "Coupon unit unresolved; trading and posting held")]);
        var first = await service.CutoverAsync(request);
        var retry = await service.CutoverAsync(request);
        retry.Should().BeEquivalentTo(first);
        (await service.GetInventoryAsync()).CutoverEnabled.Should().BeTrue();
        (await policy.GetBlockerAsync(Detail(legacy))).Should().Contain("explicitly blocked");
        (await policy.GetBlockerAsync(Detail(declared))).Should().BeNull();

        // A later, unreviewed record cannot revive the retired engine after the snapshot was approved.
        var later = Bond(false);
        (await policy.GetBlockerAsync(Detail(later))).Should().Contain("legacy live calculation is retired");
        var restarted = new FileBondCalculationPolicy(Path.Combine(_directory, "cutover.json"));
        (await restarted.GetBlockerAsync(Detail(later))).Should().Contain("legacy live calculation is retired");
        (await service.GetInventoryAsync()).RequiresLegacyCalculations.Should().BeFalse();
        await service.Invoking(value => value.CutoverAsync(request with { Reviewer = "different-controller" }))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable*");
        File.Delete(Path.Combine(_directory, "cutover.json"));
        var missingReceiptRestart = new FileBondCalculationPolicy(Path.Combine(_directory, "cutover.json"));
        (await missingReceiptRestart.GetBlockerAsync(Detail(later))).Should().Contain("state is unavailable");
    }

    [Fact]
    public async Task InventoryChangeInvalidatesReviewedCutover_AndUnreadableReceiptFailsClosed()
    {
        var bond = Bond(true);
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { bond });
        var path = Path.Combine(_directory, "cutover.json");
        var policy = new FileBondCalculationPolicy(path);
        var service = new BondMigrationService(store, Substitute.For<ISecurityMasterCashFlowService>(), policy);
        var inventory = await service.GetInventoryAsync();
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { bond with { Version = bond.Version + 1 } });
        await service.Invoking(value => value.CutoverAsync(new(inventory.InventorySha256, "controller", [])))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*inventory changed*");
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, "{}");
        (await policy.GetBlockerAsync(Detail(bond))).Should().Contain("state is unavailable");
    }

    [Fact]
    public async Task ReviewedAmendmentReleasesRepairedBond_PreservesHistoryAndRejectsStaleReceiptTokens()
    {
        var legacy = Bond(false);
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { legacy });
        var path = Path.Combine(_directory, "cutover.json");
        var policy = new FileBondCalculationPolicy(path);
        var service = new BondMigrationService(store, Substitute.For<ISecurityMasterCashFlowService>(), policy);
        var before = await service.GetInventoryAsync();
        var row = before.Bonds.Single();
        await service.CutoverAsync(new(before.InventorySha256, "controller",
            [new(legacy.SecurityId, row.Version, row.SecurityContentSha256, "Rate-unit review pending")]));
        var current = await service.GetInventoryAsync();
        var firstBytes = await File.ReadAllTextAsync(path);
        var repaired = Bond(true) with { SecurityId = legacy.SecurityId, Version = legacy.Version + 1 };
        store.LoadActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { repaired });
        var after = await service.GetInventoryAsync();
        var amendment = new BondMigrationCutoverRequest(after.InventorySha256, "second-controller", [], current.CurrentReceiptSha256);

        var receipt = await service.CutoverAsync(amendment);

        receipt.PreviousReceiptSha256.Should().Be(current.CurrentReceiptSha256);
        (await service.CutoverAsync(amendment)).Should().BeEquivalentTo(receipt);
        (await policy.GetBlockerAsync(Detail(repaired))).Should().BeNull();
        Directory.GetFiles(path + ".history").Should().HaveCount(2);
        (await File.ReadAllTextAsync(Path.Combine(path + ".history", current.CurrentReceiptSha256 + ".json"))).Should().Be(firstBytes);
        await service.Invoking(value => value.CutoverAsync(amendment with { Reviewer = "stale-controller" }))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*current receipt hash*");
        var allowedBytes = await File.ReadAllTextAsync(path);
        var released = await service.GetInventoryAsync();
        var repairedRow = released.Bonds.Single();
        await service.CutoverAsync(new(released.InventorySha256, "controller-new-hold",
            [new(repairedRow.SecurityId, repairedRow.Version, repairedRow.SecurityContentSha256, "New independently reviewed hold")],
            released.CurrentReceiptSha256));
        (await policy.GetBlockerAsync(Detail(repaired))).Should().Contain("explicitly blocked");
        var blockedBytes = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, allowedBytes);
        var rolledBackRestart = new FileBondCalculationPolicy(path);
        (await rolledBackRestart.GetBlockerAsync(Detail(repaired))).Should().Contain("state is unavailable");
        await File.WriteAllTextAsync(path, blockedBytes);
        await File.WriteAllTextAsync(Path.Combine(path + ".history", current.CurrentReceiptSha256 + ".json"), "{}");
        (await policy.GetBlockerAsync(Detail(repaired))).Should().Contain("state is unavailable");
    }

    private BondMigrationService Service(ISecurityMasterStore store, ISecurityMasterCashFlowService flows)
        => new(store, flows, new FileBondCalculationPolicy(Path.Combine(_directory, "cutover.json")));

    private static BondMigrationPreviewRequest Request(SecurityProjectionRecord bond)
        => new(bond.SecurityId, bond.Version, new(AsOf), JsonSerializer.SerializeToElement(new
        { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "DayCount" }));

    private static SecurityProjectionRecord Bond(bool declared)
    {
        var fields = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["termsFormat"] = "flat",
            ["assetClassTermsVersion"] = declared ? 2 : 1,
            ["issueDate"] = "2026-01-01",
            ["maturity"] = "2028-01-01",
            ["couponType"] = "Fixed",
            ["couponRate"] = 0.5m,
            ["paymentFrequency"] = "Quarterly",
            ["par"] = 100m,
            ["dayCount"] = "30/360",
            ["isCallable"] = false,
            ["subclass"] = "Corporate"
        };
        if (declared)
            fields["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "DayCount" };
        return new(Guid.NewGuid(), "Bond", SecurityStatusDto.Active, "Bond", "USD", "Isin", "US0000000001",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(fields),
            JsonSerializer.SerializeToElement(new { }), 1, AsOf, null, [], []);
    }

    private static SecurityDetailDto Detail(SecurityProjectionRecord record)
        => new(record.SecurityId, record.AssetClass, record.Status, record.DisplayName, record.Currency,
            record.CommonTerms, record.AssetSpecificTerms, record.Identifiers, record.Aliases,
            record.Version, record.EffectiveFrom, record.EffectiveTo);

    private static StructuredCashFlowProjectionDto Projection(Guid securityId, decimal interest)
        => new(securityId, StructuredCashFlowSourceKind.CalculatedBullet, StructuredCashFlowScenario.Base, AsOf,
            [new(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), 0m, interest, 1m)]);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
