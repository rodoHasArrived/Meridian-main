using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Tests.Ui;

/// <summary>
/// The legacy conflict-queue clients (browser + WPF) send only Resolution
/// (AcceptA/AcceptB/Dismiss) with no ChosenWinnerSource and an optional reason. The governed
/// delegation demands a concrete candidate and a nonblank rationale, so without translation every
/// field-conflict action from those queues would die on the candidate or rationale guard.
/// </summary>
public sealed class SecurityMasterLegacyConflictActionTests
{
    private static SecurityMasterConflict MakeConflict() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        SecurityMasterConflictKinds.EconomicTermMismatch,
        "EconomicTerms.couponRate",
        "Bloomberg",
        "4.25",
        "Reuters",
        "4.50",
        DateTimeOffset.UtcNow,
        "Open");

    [Theory]
    [InlineData("AcceptA", "Bloomberg")]
    [InlineData("acceptb", "Reuters")]
    [InlineData("Dismiss", "Bloomberg")]
    public void TranslateLegacyConflictAction_MapsTheResolutionToARecordedProvider(
        string resolution, string expectedWinner)
    {
        var conflict = MakeConflict();
        var request = new ResolveConflictRequest(conflict.ConflictId, resolution, "operator");

        var (winner, reason) = SecurityMasterEndpoints.TranslateLegacyConflictAction(request, conflict, "ops-user");

        winner.Should().Be(expectedWinner);
        reason.Should().NotBeNullOrWhiteSpace(
            "the governed path requires a rationale, so the queue action itself is recorded");
        reason.Should().Contain(resolution).And.Contain("ops-user");
    }

    [Fact]
    public void TranslateLegacyConflictAction_KeepsAnExplicitWinnerAndReason()
    {
        var conflict = MakeConflict();
        var request = new ResolveConflictRequest(
            conflict.ConflictId, "AcceptB", "operator",
            Reason: "Reuters confirmed against custodian evidence.",
            ChosenWinnerSource: " Reuters ");

        var (winner, reason) = SecurityMasterEndpoints.TranslateLegacyConflictAction(request, conflict, "ops-user");

        winner.Should().Be("Reuters", "an explicitly chosen source wins over the translated resolution");
        reason.Should().Be("Reuters confirmed against custodian evidence.");
    }
}
