using FluentAssertions;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// First-run arming of the portfolio-aware rails. A fresh install has no operator snapshot, and
/// before these defaults existed six of eight pre-trade rules started unconfigured and approved
/// without measuring — any quantity at any price routed. These tests pin that a composition root
/// opting into <see cref="RiskRuleFirstRunDefaults"/> starts armed, that a persisted operator
/// snapshot (including an explicit clear) always wins, and that a bare options record keeps the
/// long-standing unconfigured semantics unit tests rely on.
/// </summary>
public sealed class RiskRuleRuntimeFirstRunDefaultsTests
{
    private static string FreshSnapshotPath() => Path.Combine(
        Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"), "risk-rules.json");

    private static RiskRuleRuntimeService BuildService(RiskRuleRuntimeOptions options) =>
        new(Mock.Of<IServiceProvider>(), NullLogger<RiskRuleRuntimeService>.Instance, options);

    [Fact]
    public void FreshInstall_WithFirstRunDefaults_ArmsEveryPortfolioAwareRail()
    {
        var service = BuildService(new RiskRuleRuntimeOptions(
            FreshSnapshotPath(),
            RiskRuleFirstRunDefaults.Conservative));

        service.MaxGrossExposure.Should().Be(1_000_000m);
        service.MaxSymbolConcentrationPercent.Should().Be(25m);
        service.MaxOrderNotional.Should().Be(100_000m);
        service.EscalateOrderNotional.Should().Be(25_000m);
        var (maxQuantity, maxDeviationPercent) = service.FatFingerThresholds;
        maxQuantity.Should().Be(10_000m);
        maxDeviationPercent.Should().Be(10m);
        service.PriceCollarThresholds.CollarPercent.Should().Be(5m);
    }

    [Fact]
    public void FreshInstall_WithoutFirstRunDefaults_KeepsRailsUnconfigured()
    {
        var service = BuildService(new RiskRuleRuntimeOptions(FreshSnapshotPath()));

        service.MaxGrossExposure.Should().BeNull();
        service.MaxSymbolConcentrationPercent.Should().BeNull();
        service.MaxOrderNotional.Should().BeNull();
        service.EscalateOrderNotional.Should().BeNull();
        service.FatFingerThresholds.Should().Be(new Meridian.Risk.Rules.FatFingerThresholds(null, null));
        service.PriceCollarThresholds.CollarPercent.Should().BeNull();
    }

    [Fact]
    public void ConservativeDefaults_KeepTheirOwnInvariants()
    {
        var defaults = RiskRuleFirstRunDefaults.Conservative;

        defaults.EscalateOrderNotional.Should().BeLessThan(defaults.MaxOrderNotional,
            "the governed-approval band must exist below the reject ceiling");
        defaults.PriceCollarPercent.Should().BeLessThan(defaults.MaxPriceDeviationPercent,
            "a collar at or above the fat-finger band can never park an order");
        defaults.MaxPriceDeviationPercent.Should().BeLessThan(100m,
            "a band at or above 100 can never reject a sell");
    }

    [Fact]
    public async Task PersistedSnapshot_AlwaysWinsOverFirstRunDefaults()
    {
        var snapshotPath = FreshSnapshotPath();
        var armedOptions = new RiskRuleRuntimeOptions(snapshotPath, RiskRuleFirstRunDefaults.Conservative);

        // An operator tunes one rail and clears another; both decisions persist.
        var firstRun = BuildService(armedOptions);
        await firstRun.UpdateConfigAsync(
            "GrossExposure",
            new RiskRuleConfigUpdateRequest(MaxGrossExposure: 5_000_000m),
            actor: "risk-desk");
        await firstRun.UpdateConfigAsync(
            "PriceCollar",
            new RiskRuleConfigUpdateRequest(PriceCollarPercent: 0m),
            actor: "risk-desk");

        var restarted = BuildService(armedOptions);

        restarted.MaxGrossExposure.Should().Be(5_000_000m,
            "a persisted operator threshold wins over the first-run default");
        restarted.PriceCollarThresholds.CollarPercent.Should().BeNull(
            "an explicit operator clear is a recorded decision the defaults must not undo");
    }

    [Fact]
    public void UnenforceableFirstRunDefaults_RefuseToStart()
    {
        // An escalation band at or above the reject ceiling arms a control that cannot fire.
        var act = () => BuildService(new RiskRuleRuntimeOptions(
            FreshSnapshotPath(),
            RiskRuleFirstRunDefaults.Conservative with { EscalateOrderNotional = 100_000m }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not enforceable*");
    }
}
