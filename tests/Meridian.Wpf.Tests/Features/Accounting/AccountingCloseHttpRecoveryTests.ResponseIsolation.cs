using System.Net;
using System.Net.Http;
using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Identity.Auth;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Tests.Services;
using Meridian.Wpf.ViewModels.Accounting;

namespace Meridian.Wpf.Tests.Features.Accounting;

public sealed partial class AccountingCloseHttpRecoveryTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task DelayedClosePost_CannotReplaceChangedSelection_AndReloadRecovers(
        bool prepareOnly, bool changeWorkflow, bool oldRequestFails)
    {
        using var environment = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", $$"""[{"username":"controller","passwordHash":"{{PasswordHashing.HashPassword("pw")}}","role":"Controller","companyId":"company-alpha"}]""")
            .Set("MDC_USERNAME", null).Set("MDC_PASSWORD_HASH", null).Set("MDC_AUTH_MODE", null);
        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        session.SignIn("controller", "pw").Succeeded.Should().BeTrue();
        var oldWorkflow = Guid.NewGuid();
        var selectedWorkflow = changeWorkflow ? Guid.NewGuid() : oldWorkflow;
        var originalScope = new CloseReadinessScopeDto("fund-alpha", Guid.NewGuid(), Guid.NewGuid(), "entity-alpha", "2026-07");
        var selectedScope = changeWorkflow ? originalScope : originalScope with { EntityId = "entity-beta" };
        var oldPlan = BuildPlan(oldWorkflow, originalScope, 7);
        if (prepareOnly)
            oldPlan = oldPlan with
            {
                ClosingEntriesGate = oldPlan.ClosingEntriesGate! with
                {
                    State = ClosePostingGateStateDto.Required,
                    IsReadyForLock = false
                }
            };
        var currentPlan = BuildPlan(selectedWorkflow, selectedScope, 12);
        var reloaded = false;
        var firstPost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayedResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var posts = new List<LockClosePeriodRequestDto>();
        using var api = new ApiClientService(new StubFactory(async (message, ct) =>
        {
            if (message.Method == HttpMethod.Get)
                return JsonResponse(reloaded ? currentPlan : oldPlan);

            var request = JsonSerializer.Deserialize<LockClosePeriodRequestDto>(
                await message.Content!.ReadAsStringAsync(ct), ServerJson)!;
            posts.Add(request);
            if (posts.Count == 1)
            {
                firstPost.SetResult();
                return await delayedResponse.Task;
            }
            return JsonResponse(new ClosePeriodLockResultDto(true, currentPlan with { IsPeriodLocked = true }, null));
        }));
        var viewModel = new AccountingCloseViewModel(Substitute.For<IAccountingProjectionQueryService>(),
            new WorkstationAccountingCloseApiClient(api), session);
        viewModel.CloseWorkflowIdText = oldWorkflow.ToString("D");
        viewModel.ApplyCloseScope(originalScope);
        await viewModel.LoadClosePlanCommand.ExecuteAsync(null);
        var command = prepareOnly ? viewModel.QueueClosingEntriesCommand : viewModel.LockClosePeriodCommand;
        command.CanExecute(null).Should().BeTrue();
        var pending = command.ExecuteAsync(null);
        await firstPost.Task.WaitAsync(TimeSpan.FromSeconds(5));

        if (changeWorkflow)
            viewModel.CloseWorkflowIdText = selectedWorkflow.ToString("D");
        else
            viewModel.ApplyCloseScope(selectedScope);
        viewModel.ClosingEntriesGate.Should().BeNull("a changed selection retires evidence for an in-flight command");
        viewModel.LockClosePeriodCommand.CanExecute(null).Should().BeFalse();
        viewModel.CloseScopeEntityId.Should().Be(selectedScope.EntityId);
        viewModel.CloseScopeFundProfileId.Should().Be(selectedScope.FundProfileId);
        viewModel.CloseWorkflowIdText.Should().Be(selectedWorkflow.ToString("D"));
        reloaded = true;
        await viewModel.LoadClosePlanCommand.ExecuteAsync(null);
        var currentStatus = viewModel.ClosePeriodLockStatusText;

        delayedResponse.SetResult(oldRequestFails
            ? new(HttpStatusCode.Conflict) { Content = new StringContent("Old selection has stale evidence.") }
            : JsonResponse(new ClosePeriodLockResultDto(!prepareOnly,
                oldPlan with { IsPeriodLocked = !prepareOnly, WorkflowVersion = 99 }, null,
                [new("OLD_SCOPE_REVIEW", AccountingConfigurationValidationSeverityDto.Critical,
                    "Review belongs to the previous selection.")])));
        await pending;

        viewModel.ClosePeriodLockStatusText.Should().Be(currentStatus);
        viewModel.ClosePeriodLockIssueRows.Should().NotContain(row => row.Name == "OLD_SCOPE_REVIEW");
        viewModel.CloseWorkflowIdText.Should().Be(selectedWorkflow.ToString("D"));
        viewModel.CloseScopeEntityId.Should().Be(selectedScope.EntityId);
        viewModel.LockClosePeriodCommand.CanExecute(null).Should().BeTrue();
        await viewModel.LockClosePeriodCommand.ExecuteAsync(null);
        posts.Should().HaveCount(2);
        posts[0].WorkflowId.Should().Be(oldWorkflow);
        posts[0].ExpectedWorkflowVersion.Should().Be(7);
        posts[0].PrepareClosingEntriesOnly.Should().Be(prepareOnly);
        posts[1].WorkflowId.Should().Be(selectedWorkflow);
        posts[1].ExpectedWorkflowVersion.Should().Be(12);
        posts[1].CloseScope.Should().Be(selectedScope);
        viewModel.ClosePeriodLockStatusText.Should().Be("Locked close period 2026-07 with retained close-package evidence.");
    }
}
