#if WINDOWS
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.FixedIncome;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels.Settings;
using Moq;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class BondCutoverConsoleViewModelTests
{
    private static readonly Guid Declared = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Undeclared = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static BondMigrationInventoryDto Inventory(bool enabled = false, string? receipt = null) => new("inv-hash",
    [
        new(Declared, 3, "aaa111", "ACME 5 2030", true, null),
        new(Undeclared, 7, "bbb222", "BETA FRN 2029", false, "Bond cutover requires reviewed explicit schedule conventions; legacy live calculation is retired.")
    ], enabled, !enabled, receipt);

    [Fact]
    public async Task Submit_RequiresReviewAndABlockForEveryUndeclaredBond_ThenBindsBlocksToVersionAndHash()
    {
        var client = new Mock<IWorkstationBondCutoverApiClient>();
        client.SetupSequence(c => c.GetReadinessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BondMigrationInventoryDto>.Ok(Inventory()))
            .ReturnsAsync(ApiResponse<BondMigrationInventoryDto>.Ok(Inventory(true, "receipt-1")));
        BondMigrationCutoverRequest? sent = null;
        client.Setup(c => c.CutoverAsync(It.IsAny<BondMigrationCutoverRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BondMigrationCutoverRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync((BondMigrationCutoverRequest request, CancellationToken _) => ApiResponse<BondMigrationCutoverReceiptDto>.Ok(
                new BondMigrationCutoverReceiptDto(request.ExpectedInventorySha256, "reviewed-operator",
                    new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), request.ExplicitBlocks, "bond-migration-v1")));
        var view = new BondCutoverConsoleViewModel(client.Object);

        await view.LoadCommand.ExecuteAsync(null);
        view.Rows.Should().HaveCount(2);
        view.UndeclaredCount.Should().Be(1);
        view.Reviewed = true;
        view.CanSubmit.Should().BeFalse("the undeclared bond has no reviewed block");
        view.StatusText.Should().Contain("1 undeclared bond(s) still need");

        view.Rows[1].BlockReason = "Awaiting confirmation";
        view.CanSubmit.Should().BeTrue();
        await view.SubmitCommand.ExecuteAsync(null);

        sent.Should().NotBeNull();
        sent!.ExpectedInventorySha256.Should().Be("inv-hash");
        sent.ExpectedPreviousReceiptSha256.Should().BeNull("first cutover names no previous receipt");
        sent.ExplicitBlocks.Should().ContainSingle().Which.Should().Be(new BondMigrationBlockDto(Undeclared, 7, "bbb222", "Awaiting confirmation"));
        view.ReceiptText.Should().Contain("reviewed-operator").And.Contain("first cutover");
        view.CutoverEnabled.Should().BeTrue();
        view.SubmitLabel.Should().Be("Amend cutover");
        view.CurrentReceiptHash.Should().Be("receipt-1");
        view.Reviewed.Should().BeFalse("a refreshed inventory restarts the review");
    }

    [Fact]
    public async Task BatchPreview_SendsOneConventionSetForUndeclaredBonds_AndStagesNothing()
    {
        var client = new Mock<IWorkstationBondCutoverApiClient>();
        client.Setup(c => c.GetReadinessAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ApiResponse<BondMigrationInventoryDto>.Ok(Inventory()));
        BondMigrationBatchPreviewRequest? sent = null;
        client.Setup(c => c.PreviewMigrationBatchAsync(It.IsAny<BondMigrationBatchPreviewRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BondMigrationBatchPreviewRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync((BondMigrationBatchPreviewRequest request, CancellationToken _) => ApiResponse<BondMigrationPreviewDto[]>.Ok(
                request.Bonds.Select(bond => new BondMigrationPreviewDto(bond, "bbb222", "preview-hash", null, null,
                    [new(new DateOnly(2027, 1, 31), 0m, 0m, 12.5m, 0.125m)], [], true)).ToArray()));
        var view = new BondCutoverConsoleViewModel(client.Object) { BatchBusinessDayConvention = "Following" };

        view.PreviewBatchCommand.CanExecute(null).Should().BeFalse("no inventory yet");
        await view.LoadCommand.ExecuteAsync(null);
        view.PreviewBatchCommand.CanExecute(null).Should().BeTrue();
        await view.PreviewBatchCommand.ExecuteAsync(null);

        sent.Should().NotBeNull();
        sent!.Bonds.Should().ContainSingle().Which.SecurityId.Should().Be(Undeclared);
        sent.Bonds[0].ExpectedVersion.Should().Be(7);
        sent.Bonds[0].ScheduleConventions.GetProperty("businessDayConvention").GetString().Should().Be("Following");
        sent.Bonds[0].ScheduleConventions.GetProperty("couponRateUnit").GetString().Should().Be("Percent");
        view.BatchPreviews.Should().ContainSingle().Which.CanStage.Should().BeTrue();
        view.BatchSummaryText.Should().StartWith("1 of 1 can be staged");
        client.Verify(c => c.CutoverAsync(It.IsAny<BondMigrationCutoverRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Amendment_EchoesCurrentReceiptHash_AndRefusalsExplainWithoutAReceipt()
    {
        var client = new Mock<IWorkstationBondCutoverApiClient>();
        client.Setup(c => c.GetReadinessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BondMigrationInventoryDto>.Ok(new BondMigrationInventoryDto("inv-2",
                [new(Declared, 3, "aaa111", "ACME 5 2030", true, null)], true, false, "receipt-1")));
        BondMigrationCutoverRequest? sent = null;
        client.Setup(c => c.CutoverAsync(It.IsAny<BondMigrationCutoverRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BondMigrationCutoverRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(ApiResponse<BondMigrationCutoverReceiptDto>.Fail("Active bond inventory changed; rebuild and review the cutover inventory.", 409));
        var view = new BondCutoverConsoleViewModel(client.Object);

        await view.LoadCommand.ExecuteAsync(null);
        view.Reviewed = true;
        await view.SubmitCommand.ExecuteAsync(null);

        sent!.ExpectedPreviousReceiptSha256.Should().Be("receipt-1");
        view.Receipt.Should().BeNull();
        view.StatusText.Should().Contain("Reload the inventory and review again");

        client.Setup(c => c.GetReadinessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BondMigrationInventoryDto>.Fail("Forbidden", 403));
        await view.LoadCommand.ExecuteAsync(null);
        view.StatusText.Should().Contain("AdminMaintenance");
    }
}
#endif
