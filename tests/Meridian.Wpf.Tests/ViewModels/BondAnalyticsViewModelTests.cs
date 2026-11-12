#if WINDOWS
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.ViewModels.SecurityMaster;
using Moq;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class BondAnalyticsViewModelTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset AsOf = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PassportEditor_OffersBondToolsOnlyForBondCouponAssetClasses()
    {
        var editor = new SecurityPassportEditorViewModel(new Mock<IWorkstationSecurityMasterApiClient>().Object);

        editor.Parameter = new SecurityPassportEditorParameter(SecurityId, 7, "ACME 5 2030", "Bond");
        editor.HasBondTools.Should().BeTrue();
        editor.BondAnalytics!.Version.Should().Be(7);
        editor.BondMigration!.SecurityId.Should().Be(SecurityId);

        editor.Parameter = new SecurityPassportEditorParameter(SecurityId, 7, "ACME", "Equity");
        editor.HasBondTools.Should().BeFalse();
        editor.BondAnalytics.Should().BeNull();
    }

    [Fact]
    public async Task Calculate_SendsDatedRequestAndShowsExactServerDecimals()
    {
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        BondAnalyticsRequest? sent = null;
        client.Setup(c => c.GetBondAnalyticsAsync(It.IsAny<BondAnalyticsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BondAnalyticsRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(ApiResponse<BondAnalyticsResult>.Ok(Result(SecurityId)));
        var view = new BondAnalyticsViewModel(client.Object, SecurityId, 7)
        {
            AsOf = AsOf.UtcDateTime,
            SettlementDate = AsOf.UtcDateTime.AddDays(1),
            Value = "99.125",
            KnownAt = "2026-10-29T09:30:00Z"
        };

        await view.CalculateCommand.ExecuteAsync(null);

        sent.Should().NotBeNull();
        sent!.CleanPrice.Should().Be(99.125m);
        sent.CalculationVersion.Should().Be("bond-analytics-v2", "the existing fixed/zero model remains the default");
        sent.Yield.Should().BeNull();
        sent.Context.SettlementDate.Should().Be(new DateOnly(2026, 10, 30));
        sent.Context.KnownAt.Should().Be(new DateTimeOffset(2026, 10, 29, 9, 30, 0, TimeSpan.Zero));
        sent.Context.SecurityVersion.Should().Be(7);
        view.HasResult.Should().BeTrue();
        view.Metrics.Should().Contain(row => row.Label == "Dirty price / 100" && row.Value == "1234567.123456789012");
        view.CashFlows.Should().ContainSingle().Which.Interest.Should().Be("2.5");
        view.StatusText.Should().Contain("grant no");
        view.HasFloatingResult.Should().BeFalse();
        view.Metrics.Should().NotContain(row => row.Label.StartsWith("SpreadDv01"));
    }

    [Theory]
    [InlineData("bond-analytics-v1")]
    [InlineData("bond-analytics-v2")]
    [InlineData("bond-analytics-v3")]
    public async Task Calculate_ForwardsTheSelectedVersionWithoutSubstitution(string version)
    {
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        client.Setup(c => c.GetBondAnalyticsAsync(It.IsAny<BondAnalyticsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BondAnalyticsRequest request, CancellationToken _) =>
                ApiResponse<BondAnalyticsResult>.Ok(Result(SecurityId) with { ModelVersion = request.CalculationVersion! }));
        var view = new BondAnalyticsViewModel(client.Object, SecurityId, 7) { Value = "100", CalculationVersion = version };

        await view.CalculateCommand.ExecuteAsync(null);

        client.Verify(c => c.GetBondAnalyticsAsync(It.Is<BondAnalyticsRequest>(request => request.CalculationVersion == version),
            It.IsAny<CancellationToken>()), Times.Once);
        view.ProvenanceText.Should().Contain(version);
        view.HasFloatingResult.Should().Be(version == "bond-analytics-v3");
    }

    [Fact]
    public async Task CalculationVersionChange_CancelsAndDiscardsThePreviousResult()
    {
        var pending = new TaskCompletionSource<ApiResponse<BondAnalyticsResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        CancellationToken token = default;
        client.Setup(c => c.GetBondAnalyticsAsync(It.IsAny<BondAnalyticsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BondAnalyticsRequest, CancellationToken>((_, cancellation) => token = cancellation)
            .Returns(pending.Task);
        var view = new BondAnalyticsViewModel(client.Object, SecurityId, 7) { Value = "100", CalculationVersion = "bond-analytics-v3" };

        var calculation = view.CalculateCommand.ExecuteAsync(null);
        view.IsBusy.Should().BeTrue();
        view.CalculationVersion = "bond-analytics-v2";
        token.IsCancellationRequested.Should().BeTrue();
        pending.SetResult(ApiResponse<BondAnalyticsResult>.Ok(Result(SecurityId) with { ModelVersion = "bond-analytics-v3" }));
        await calculation;

        view.HasResult.Should().BeFalse();
        view.HasFloatingResult.Should().BeFalse();
        view.Metrics.Should().BeEmpty();
        view.CashFlows.Should().BeEmpty();
        view.Inputs.Should().BeEmpty();
        view.StatusText.Should().Be("Calculation version changed. Calculate again.");
    }

    [Fact]
    public async Task Calculate_RejectsASubstitutedModelWithoutDisplayingItsValues()
    {
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        client.Setup(c => c.GetBondAnalyticsAsync(It.IsAny<BondAnalyticsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BondAnalyticsResult>.Ok(Result(SecurityId)));
        var view = new BondAnalyticsViewModel(client.Object, SecurityId, 7) { Value = "100", CalculationVersion = "bond-analytics-v3" };

        await view.CalculateCommand.ExecuteAsync(null);

        view.HasResult.Should().BeFalse();
        view.StatusText.Should().Contain("different calculation version");
        view.Metrics.Should().BeEmpty();
        client.Verify(c => c.GetBondAnalyticsAsync(It.IsAny<BondAnalyticsRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Calculate_RefusesMismatchedSecurityAndSurfacesBlockers()
    {
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        client.Setup(c => c.GetBondAnalyticsAsync(It.IsAny<BondAnalyticsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BondAnalyticsResult>.Ok(Result(Guid.NewGuid())));
        var view = new BondAnalyticsViewModel(client.Object, SecurityId, 7) { Value = "100" };

        await view.CalculateCommand.ExecuteAsync(null);
        view.HasResult.Should().BeFalse();
        view.StatusText.Should().Contain("different security");

        client.Setup(c => c.GetBondAnalyticsAsync(It.IsAny<BondAnalyticsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BondAnalyticsResult>.Ok(Blocked(SecurityId)));
        view.Value = "101";
        await view.CalculateCommand.ExecuteAsync(null);
        view.IsBlocked.Should().BeTrue();
        view.Blockers.Should().ContainSingle().Which.Should().Contain("DeclaredScheduleRequired");
    }

    [Fact]
    public async Task MigrationPreview_RequiresCouponUnit_ThenStagesHashesNotAmounts()
    {
        var client = new Mock<IWorkstationSecurityMasterApiClient>();
        BondMigrationPreviewRequest? previewed = null;
        client.Setup(c => c.PreviewBondMigrationAsync(It.IsAny<BondMigrationPreviewRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BondMigrationPreviewRequest, CancellationToken>((request, _) => previewed = request)
            .ReturnsAsync((BondMigrationPreviewRequest request, CancellationToken _) => ApiResponse<BondMigrationPreviewDto>.Ok(
                new BondMigrationPreviewDto(request, "security-hash", "preview-hash", null, null,
                    [new(new DateOnly(2027, 1, 31), 100m, 100m, 12.5m, 0.125m)], [], true)));
        BondMigrationSubmitRequest? staged = null;
        client.Setup(c => c.StageBondMigrationAsync(It.IsAny<BondMigrationSubmitRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BondMigrationSubmitRequest, CancellationToken>((request, _) => staged = request)
            .ReturnsAsync(ApiResponse<SecurityMasterEditResultDto>.Ok(new SecurityMasterEditResultDto(SecurityId, Guid.NewGuid(), 8,
                SecurityMasterRevisionStateDto.Draft, new SecurityMasterChangeHistoryItemDto("change-8", 8, "FieldUpdated", AsOf, AsOf,
                    "reviewed-operator", "Workstation", "SecurityMaster", "bond-migration:preview-hash", "Reviewed", "Declared conventions",
                    ["scheduleConventions"], "scheduleConventions"))));
        var view = new BondMigrationPreviewViewModel(client.Object, SecurityId, 7) { AsOf = AsOf.UtcDateTime };

        await view.PreviewCommand.ExecuteAsync(null);
        previewed.Should().BeNull();
        view.StatusText.Should().Contain("retained coupon unit");

        view.CouponRateUnit = "Percent";
        await view.PreviewCommand.ExecuteAsync(null);
        previewed.Should().NotBeNull();
        previewed!.ScheduleConventions.GetProperty("couponRateUnit").GetString().Should().Be("Percent");
        previewed.Context.SettlementDate.Should().Be(new DateOnly(2026, 10, 29), "blank settlement defaults to the preview date");
        view.Differences.Should().ContainSingle().Which.CandidateInterest.Should().Be("0.125");
        view.CanStage.Should().BeFalse("justification is required");

        view.Justification = "Contract confirmation reviewed against changed payments.";
        view.CanStage.Should().BeTrue();
        await view.StageCommand.ExecuteAsync(null);
        staged.Should().NotBeNull();
        staged!.PreviewSha256.Should().Be("preview-hash");
        staged.SecurityContentSha256.Should().Be("security-hash");
        view.StagedText.Should().Contain("No approval or publication was requested");
        view.CanStage.Should().BeFalse();
    }

    private static BondAnalyticsResult Result(Guid securityId) => new(securityId, "USD", "bond-analytics-v2",
        new BondCalculationContext(AsOf, new DateOnly(2026, 10, 30), null, 7), BondYieldConvention.NominalPeriodic,
        99.125m, 1234567.123456789012m, 1m, 0.0512345m, 0.05044m, 3.2m, 3.1m, 12.5m, 0.031m, 0.04m,
        [new(Guid.NewGuid(), new DateOnly(2027, 1, 31), new DateOnly(2026, 7, 31), new DateOnly(2027, 1, 31), 2.5m, 100m, 0.25m)],
        [], [new("SecurityTerms", securityId.ToString("D"), 7, "abc123")], [],
        [new("Analytics", true), new("Posting", false, "Analysis only")], []);

    private static BondAnalyticsResult Blocked(Guid securityId) => new(securityId, "USD", "bond-analytics-v2",
        new BondCalculationContext(AsOf), BondYieldConvention.NominalPeriodic,
        null, null, null, null, null, null, null, null, null, null, [], [], [],
        [new("DeclaredScheduleRequired", "Analytics requires reviewed explicit schedule conventions.")],
        [new("Analytics", false, "blocked")], []);
}
#endif
