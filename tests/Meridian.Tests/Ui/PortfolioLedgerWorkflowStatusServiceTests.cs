using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class PortfolioLedgerWorkflowStatusServiceTests
{
    [Fact]
    public void Compute_ShouldKeepBlockedGateStatus_WhenDriftSignalsExist()
    {
        var gates = new[]
        {
            new TradingAcceptanceGateDto("report-pack", "Report pack", TradingAcceptanceGateStatusDto.Blocked, "blocked")
        };

        var workItems = new[]
        {
            new OperatorWorkItemDto(
                "sync-1",
                OperatorWorkItemKindDto.BrokerageSync,
                "Brokerage sync requires attention",
                "Cash drift",
                OperatorWorkItemToneDto.Warning,
                DateTimeOffset.UtcNow)
        };

        var snapshot = PortfolioLedgerWorkflowStatusService.Compute(gates, workItems);

        snapshot.PortfolioLedgerReview.Should().Be(PortfolioLedgerWorkflowStatusDto.Blocked);
    }

    [Fact]
    public void Compute_ShouldNotReportAligned_WhenGateRequiresReviewWithoutDrift()
    {
        var gates = new[]
        {
            new TradingAcceptanceGateDto("report-pack", "Report pack", TradingAcceptanceGateStatusDto.ReviewRequired, "review required"),
            new TradingAcceptanceGateDto("reconciliation", "Reconciliation", TradingAcceptanceGateStatusDto.Ready, "ready")
        };

        var snapshot = PortfolioLedgerWorkflowStatusService.Compute(gates, Array.Empty<OperatorWorkItemDto>());

        snapshot.PortfolioLedgerReview.Should().Be(PortfolioLedgerWorkflowStatusDto.Partial);
        snapshot.Summary.Should().Be("Portfolio/ledger workflow posture requires acceptance-gate review before promotion.");
    }
}
