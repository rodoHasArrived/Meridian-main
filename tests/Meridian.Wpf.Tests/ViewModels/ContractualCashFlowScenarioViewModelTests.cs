#if WINDOWS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels.CashFlows;
using Moq;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ContractualCashFlowScenarioViewModelTests
{
    private static readonly Guid Subject = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ContractualCashFlowEvidence Evidence(string output) => new("contractual-cashflow-v1", "m1", "p1", "k1", "in-1", output, "terms-1", "7", false, [], [], []);

    private static ContractualCashFlowResult Calculated(string output = "out-1") => new(
        [new("e1", "coupon", "p1", new DateOnly(2027, 1, 31), new DateOnly(2026, 7, 31), new DateOnly(2027, 1, 31),
            1000000m, 25000.123456789012m, 0m, 0m, 0m, 0m, 0m, 0m, 1000000m, CashFlowLegDirection.Receive)],
        [new(CashFlowExerciseSide.Call, new DateOnly(2027, 1, 31), "coupon", new DateOnly(2028, 1, 31), CashFlowExerciseStyle.Bermudan,
            [new DateOnly(2027, 1, 31), new DateOnly(2027, 7, 31)], "NYB")],
        [], [], Evidence(output));

    private static ContractualCashFlowResult Blocked() => new([], [], [], [new("CurvePinRequired", "Pin the SOFR curve.", "float")], Evidence("none"));

    [Fact]
    public async Task Preview_ShowsExactAmounts_BlocksRetentionWhileBlocked_AndInvalidatesOnEdit()
    {
        var client = new Mock<IWorkstationContractualCashFlowApiClient>();
        client.SetupSequence(c => c.PreviewAsync(ContractualCashFlowSubjectKinds.Security, Subject, It.IsAny<ContractualCashFlowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ContractualCashFlowResult>.Ok(Blocked()))
            .ReturnsAsync(ApiResponse<ContractualCashFlowResult>.Ok(Calculated()));
        var view = new ContractualCashFlowScenarioViewModel(client.Object, ContractualCashFlowSubjectKinds.Security, Subject, "ACME 5 2030")
        {
            ValuationDate = new DateTime(2026, 10, 31), KnowledgeCutoff = "2026-10-31T09:30:00Z", RateScenario = "Up200"
        };

        await view.PreviewCommand.ExecuteAsync(null);
        view.IsBlocked.Should().BeTrue();
        view.Blockers.Should().ContainSingle().Which.Should().Contain("CurvePinRequired (float)");
        view.CanRetain.Should().BeFalse();

        await view.PreviewCommand.ExecuteAsync(null);
        view.Events.Should().ContainSingle().Which.CashInterest.Should().Be("25000.123456789012");
        view.Exercises.Should().ContainSingle().Which.Dates.Should().Equal("2027-01-31", "2027-07-31");
        view.CanRetain.Should().BeTrue();
        var firstRunId = view.PendingRunId;

        view.RateScenario = "Down100";
        view.HasResult.Should().BeFalse("an edit invalidates the previewed result");
        view.PendingRunId.Should().NotBe(firstRunId, "an edit binds a fresh run identity");
    }

    [Fact]
    public async Task RetainRun_SendsPendingIdentityAndPreviewedRequest_ThenReplayVerifiesByOutputHash()
    {
        var client = new Mock<IWorkstationContractualCashFlowApiClient>();
        client.Setup(c => c.PreviewAsync(ContractualCashFlowSubjectKinds.Loan, Subject, It.IsAny<ContractualCashFlowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ContractualCashFlowResult>.Ok(Calculated("out-2")));
        CreateContractualCashFlowRunRequest? sent = null;
        client.Setup(c => c.RetainRunAsync(ContractualCashFlowSubjectKinds.Loan, Subject, It.IsAny<CreateContractualCashFlowRunRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, CreateContractualCashFlowRunRequest, CancellationToken>((_, _, request, _) => sent = request)
            .ReturnsAsync((string kind, Guid id, CreateContractualCashFlowRunRequest request, CancellationToken _) => ApiResponse<ContractualCashFlowRun>.Ok(
                new ContractualCashFlowRun(request.RunId, kind, id, new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero),
                    new ContractualCashFlowContract(new DateOnly(2026, 1, 1), new DateOnly(2030, 1, 1), "USD", []), request.Projection, Calculated("out-1"), "operator-a")));
        var view = new ContractualCashFlowScenarioViewModel(client.Object, ContractualCashFlowSubjectKinds.Loan, Subject, "Facility A")
        {
            ValuationDate = new DateTime(2026, 10, 31), KnowledgeCutoff = "2026-10-31T00:00:00Z", PrepaymentEnabled = true,
            PrepaymentStreamId = "principal", PrepaymentStart = new DateTime(2027, 1, 1), PrepaymentEnd = new DateTime(2028, 1, 1), PrepaymentMethod = "Cpr", PrepaymentValue = "0.06"
        };

        await view.PreviewCommand.ExecuteAsync(null);
        var pending = view.PendingRunId;
        await view.RetainRunCommand.ExecuteAsync(null);

        sent.Should().NotBeNull();
        sent!.RunId.Should().Be(pending);
        sent.Projection.Prepayments.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new CashFlowPrepaymentAssumption("principal", new DateOnly(2027, 1, 1), new DateOnly(2028, 1, 1), CashFlowPrepaymentMethod.Cpr, 0.06m));
        sent.Projection.RateScenario.Should().Be(StructuredCashFlowScenario.Base);
        view.RunText.Should().Contain("operator-a");
        view.CanRetain.Should().BeFalse("the run is already retained");

        client.Setup(c => c.GetRunAsync(ContractualCashFlowSubjectKinds.Loan, Subject, pending, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ContractualCashFlowRun>.Ok(view.Run!));
        view.RunIdText = pending.ToString("D");
        await view.LoadRunCommand.ExecuteAsync(null);
        view.LoadedRunText.Should().Contain("1 events").And.Contain("out-1");

        await view.VerifyReplayCommand.ExecuteAsync(null);
        view.ReplayStatusText.Should().Be("Replay differs: retained output out-1, current out-2. Source terms unchanged.");
    }
}
#endif
