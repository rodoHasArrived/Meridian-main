using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Features.Accounting;
using Meridian.Wpf.Tests.Services;
using Meridian.Wpf.ViewModels.Accounting;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Features.Accounting;

[Collection("DesktopAuthenticationEnvironment")]
public sealed partial class AccountingCloseHttpRecoveryTests
{
    private static readonly JsonSerializerOptions ServerJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("stale")]
    [InlineData("unavailable")]
    [InlineData("wrong-workflow")]
    [InlineData("evidence-blocker")]
    public async Task RealModule_UsesBackendCloseAuthority_AndRepairRestoresTheSelectedWorkflow(string failure)
    {
        using var environment = new DesktopAuthenticationSessionTests.EnvironmentVariableScope()
            .Set("MDC_USERS", $$"""[{"username":"controller","passwordHash":"{{PasswordHashing.HashPassword("pw")}}","role":"Controller","companyId":"company-alpha"}]""")
            .Set("MDC_USERNAME", null).Set("MDC_PASSWORD_HASH", null).Set("MDC_AUTH_MODE", null);
        var session = DesktopAuthenticationSessionTests.CreateSession("Production");
        session.SignIn("controller", "pw").Succeeded.Should().BeTrue();
        var workflowId = Guid.NewGuid();
        var scope = new CloseReadinessScopeDto("fund-alpha", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("D"), "2026-07");
        var repaired = false;
        var posts = new List<LockClosePeriodRequestDto>();
        using var api = new ApiClientService(new StubFactory(async (message, ct) =>
        {
            message.RequestUri!.Query.Should().BeEmpty();
            if (message.Method == HttpMethod.Get)
            {
                message.RequestUri.AbsolutePath.Should().Be(UiApiRoutes.LedgerCloseManagementPeriodPlan
                    .Replace("{workflowId:guid}", workflowId.ToString("D")));
                if (!repaired && failure is not "evidence-blocker" and not "wrong-workflow")
                    return new HttpResponseMessage(failure switch
                    {
                        "missing" => HttpStatusCode.NotFound,
                        "foreign" => HttpStatusCode.Forbidden,
                        "stale" => HttpStatusCode.Conflict,
                        _ => HttpStatusCode.ServiceUnavailable
                    })
                    { Content = new StringContent($"Close evidence {failure}; refresh after repair.") };
                return JsonResponse(BuildPlan(!repaired && failure == "wrong-workflow" ? Guid.NewGuid() : workflowId,
                    scope, repaired ? 8 : 7));
            }

            message.Method.Should().Be(HttpMethod.Post);
            message.RequestUri.AbsolutePath.Should().Be(UiApiRoutes.LedgerCloseManagementPeriodLock);
            var request = JsonSerializer.Deserialize<LockClosePeriodRequestDto>(
                await message.Content!.ReadAsStringAsync(ct), ServerJson)!;
            posts.Add(request);
            var plan = BuildPlan(workflowId, scope, repaired ? 8 : 7);
            return repaired
                ? JsonResponse(new ClosePeriodLockResultDto(true, plan with { IsPeriodLocked = true }, null))
                : JsonResponse(new ClosePeriodLockResultDto(false, plan, null,
                    [new("CLOSE_EVIDENCE_STALE", AccountingConfigurationValidationSeverityDto.Critical,
                        "Retained valuation evidence is stale.", "valuation-7", "Repair source evidence and reload.")]));
        }));
        var localClose = Substitute.For<IAccountingCloseManagementService>();
        var services = new ServiceCollection();
        services.AddSingleton(session);
        services.AddSingleton(api);
        services.AddSingleton(localClose);
        services.AddSingleton(Substitute.For<IAccountingProjectionQueryService>());
        new AccountingFeatureModule().Register(services);
        using var provider = services.BuildServiceProvider();
        var viewModel = provider.GetRequiredService<AccountingCloseViewModel>();
        provider.GetRequiredService<IWorkstationAccountingCloseApiClient>().Should().BeOfType<WorkstationAccountingCloseApiClient>();
        viewModel.CloseWorkflowIdText = workflowId.ToString("D");
        viewModel.ApplyCloseScope(scope);

        await viewModel.LoadClosePlanCommand.ExecuteAsync(null);
        if (failure == "evidence-blocker")
        {
            await viewModel.LockClosePeriodCommand.ExecuteAsync(null);
            viewModel.ClosePeriodLockStatusText.Should().Contain("blocked");
            viewModel.ClosePeriodLockIssueRows.Should().ContainSingle(issue => issue.Name == "CLOSE_EVIDENCE_STALE");
        }
        else
        {
            viewModel.LockClosePeriodCommand.CanExecute(null).Should().BeFalse();
            await viewModel.LockClosePeriodCommand.ExecuteAsync(null);
            posts.Should().BeEmpty();
        }

        repaired = true;
        await viewModel.LoadClosePlanCommand.ExecuteAsync(null);
        viewModel.LockClosePeriodCommand.CanExecute(null).Should().BeTrue();
        await viewModel.LockClosePeriodCommand.ExecuteAsync(null);

        posts.Last().WorkflowId.Should().Be(workflowId);
        posts.Last().ExpectedWorkflowVersion.Should().Be(8);
        posts.Last().CloseScope.Should().Be(scope);
        posts.Last().PrepareClosingEntriesOnly.Should().BeFalse();
        viewModel.ClosePeriodLockStatusText.Should().Be("Locked close period 2026-07 with retained close-package evidence.");
        viewModel.LockClosePeriodCommand.CanExecute(null).Should().BeFalse();
        localClose.ReceivedCalls().Should().BeEmpty("the operator page must use configured backend authority, not desktop-local books");
    }

    private static ClosePeriodPlanDto BuildPlan(Guid workflowId, CloseReadinessScopeDto scope, long version)
        => new("close-plan-7", scope.FundAccountId!.Value.ToString("D"), scope.LedgerBookId, scope.PeriodId!,
            new(2026, 7, 1), new(2026, 7, 31), new(2026, 8, 5), false, [], [],
            new("policy-7", 100m, .01m, "USD", "Controller", true),
            ClosingEntriesGate: new("closing-7", "Closing entries", ClosePostingGateStateDto.NotRequired, true, 0, 0,
                "All required postings are retained."), WorkflowVersion: version, WorkflowId: workflowId,
            FundAccountId: scope.FundAccountId, EvidenceVersion: $"evidence-{version}", EvaluatedAtUtc: DateTimeOffset.UtcNow);

    private static HttpResponseMessage JsonResponse<T>(T value)
        => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, ServerJson), Encoding.UTF8, "application/json") };

    private sealed class StubFactory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(responder));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request, cancellationToken);
    }
}
