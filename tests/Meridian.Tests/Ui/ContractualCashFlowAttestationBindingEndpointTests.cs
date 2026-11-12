using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class ContractualCashFlowAttestationBindingEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task NewRunBindingUsesServerScopePreparerAndOrigin_AndCannotBeReassignedByRetry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var runId = Guid.NewGuid();
        var response = await fixture.Client.PostAsJsonAsync(fixture.Route, new
        {
            runId,
            projection = fixture.Request,
            actor = "forged",
            tenantId = "foreign",
            companyId = "foreign",
            attestationContext = new { preparedBy = "forged", origin = "HumanOperator" }
        });
        response.EnsureSuccessStatusCode();
        var run = (await response.Content.ReadFromJsonAsync<ContractualCashFlowRun>(Json))!;
        run.Actor.Should().Be("preparer-a");
        run.AttestationContext.Should().Be(new ContractualCashFlowAttestationContextDto(
            "tenant-a", "company-a", "preparer-a", OperationsActionOriginDto.HumanOperator));
        run.Result.IsPostable.Should().BeFalse();
        fixture.Actor = "another-preparer";
        (await fixture.Client.PostAsJsonAsync(fixture.Route, new CreateContractualCashFlowRunRequest(runId, fixture.Request)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        fixture.Company = "foreign-company";
        (await fixture.Client.GetAsync(fixture.Route + "/" + runId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DisabledFeatureLeavesLegacyPacketShapeUnbound()
    {
        await using var fixture = await Fixture.CreateAsync(enabled: false);
        var response = await fixture.Client.PostAsJsonAsync(fixture.Route, new CreateContractualCashFlowRunRequest(Guid.NewGuid(), fixture.Request));
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadAsStringAsync()).Should().NotContain("attestationContext");
        (await response.Content.ReadFromJsonAsync<ContractualCashFlowRun>(Json))!.AttestationContext.Should().BeNull();
    }

    [Fact]
    public async Task EnabledBindingRequiresRealScopeBeforeRetention()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Tenant = fixture.Company = null;
        var response = await fixture.Client.PostAsJsonAsync(fixture.Route, new CreateContractualCashFlowRunRequest(Guid.NewGuid(), fixture.Request));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Directory.GetFiles(fixture.Root, "*.json", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task ServiceCreatedPacketCannotBecomeAHumanPreparerCheck()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.ApiCredential = true;
        var runId = Guid.NewGuid();
        var response = await fixture.Client.PostAsJsonAsync(fixture.Route, new CreateContractualCashFlowRunRequest(runId, fixture.Request));
        response.EnsureSuccessStatusCode();
        var run = (await response.Content.ReadFromJsonAsync<ContractualCashFlowRun>(Json))!;
        run.AttestationContext!.Origin.Should().Be(OperationsActionOriginDto.AutomationAssistant);
        var subject = await new ContractualCashFlowDraftAttestationSubjectResolver(fixture.Runs, fixture.Securities)
            .ResolveAsync(fixture.SecurityId, runId, "tenant-a", "company-a", fixture.Actor);
        subject.Should().BeNull();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "meridian-cashflow-binding-" + Guid.NewGuid().ToString("N"));
        public Guid SecurityId { get; } = Guid.NewGuid();
        public string Route => $"/api/security-master/{SecurityId:D}/cashflow-projections/runs";
        public string Actor { get; set; } = "preparer-a";
        public string? Tenant { get; set; } = "tenant-a";
        public string? Company { get; set; } = "company-a";
        public bool ApiCredential { get; set; }
        public ContractualCashFlowRequest Request { get; } = new(new(2026, 1, 1), new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        public ISecurityMasterQueryService Securities { get; } = Substitute.For<ISecurityMasterQueryService>();
        public FileContractualCashFlowRunStore Runs { get; private set; } = null!;
        public WebApplication App { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync(bool enabled = true)
        {
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture.Root);
            fixture.Runs = new(new StorageOptions { RootPath = fixture.Root });
            var contract = new ContractualCashFlowContract(new(2026, 1, 1), new(2027, 1, 1), "USD",
                [new("fixed", 1000m, 3, DayCountConvention.Actual365, .05m)]);
            var common = JsonSerializer.SerializeToElement(new { });
            var terms = JsonSerializer.SerializeToElement(new { });
            var security = new SecurityDetailDto(fixture.SecurityId, "Bond", SecurityStatusDto.Active, "Synthetic bond", "USD",
                common, terms, [], [], 1, fixture.Request.KnowledgeCutoff, null);
            fixture.Securities.GetByIdAsync(fixture.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
            fixture.Securities.GetRecordedByIdAsOfAsync(fixture.SecurityId, fixture.Request.KnowledgeCutoff, Arg.Any<CancellationToken>()).Returns(security);
            var result = ContractualCashFlowEngine.Calculate(contract, fixture.Request,
                sourceVersion: $"security:{fixture.SecurityId}:version:1",
                sourceTermsHash: ContractualCashFlowEngine.Hash(new SecurityTermsDigest(common, terms)));
            var previews = Substitute.For<IContractualCashFlowPreviewService>();
            previews.PreviewSecurityAsync(fixture.SecurityId, Arg.Any<ContractualCashFlowRequest>(), Arg.Any<CancellationToken>()).Returns(result);
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["SinglePrincipalAttestation:EnableDraftChecks"] = enabled.ToString() });
            builder.Services.AddSingleton(fixture.Securities);
            builder.Services.AddSingleton(previews);
            builder.Services.AddSingleton<IContractualCashFlowRunStore>(fixture.Runs);
            builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy,
                _ => RateLimitPartition.GetNoLimiter("cashflow-binding-tests")));
            fixture.App = builder.Build();
            fixture.App.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = fixture.Actor;
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ModifySecurityMaster | UserPermission.ViewSecurityMaster;
                if (fixture.Tenant is not null)
                    context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = fixture.Tenant;
                if (fixture.Company is not null)
                    context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = fixture.Company;
                if (fixture.ApiCredential)
                    context.Items[ApiKeyMiddleware.ApiKeyPrincipalKey] = new object();
                await next();
            });
            fixture.App.UseRouting();
            fixture.App.UseRateLimiter();
            var type = typeof(SecurityMasterEndpoints).Assembly.GetType("Meridian.Ui.Shared.Endpoints.ContractualCashFlowRunEndpoints")!;
            type.GetMethod("MapContractualCashFlowRuns", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                [fixture.App.MapGroup("/api/security-master"), "/{subjectId:guid}/cashflow-projections/runs",
                    "Security", UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, Json]);
            await fixture.App.StartAsync();
            fixture.Client = fixture.App.GetTestClient();
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }
    }
}
