using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Meridian.Tests.Application.Composition;

namespace Meridian.Tests.Risk;

[Collection("Sequential")]
public sealed class RiskBoundaryContractTests
{
    [Theory]
    [InlineData("Meridian.Execution.IRiskValidator", typeof(IRiskValidator))]
    [InlineData("Meridian.Execution.RiskValidationResult", typeof(RiskValidationResult))]
    [InlineData("Meridian.Execution.Logging.LogSanitizer", typeof(LogSanitizer))]
    [InlineData("Meridian.Execution.Services.RiskEscalationStatus", typeof(RiskEscalationStatus))]
    [InlineData("Meridian.Execution.Services.RiskEscalationEntry", typeof(RiskEscalationEntry))]
    [InlineData("Meridian.Execution.Services.RiskEscalationSnapshot", typeof(RiskEscalationSnapshot))]
    public void RelocatedContract_OldAssemblyQualifiedLookupResolvesSdkIdentity(string fullName, Type expected)
    {
        var retainedIdentity = Type.GetType($"{fullName}, Meridian.Execution", throwOnError: true);

        retainedIdentity.Should().Be(expected);
        Assert.Same(typeof(OrderRequest).Assembly, retainedIdentity!.Assembly);
    }

    [Fact]
    public async Task UiServer_RiskInterfacesResolveTheExistingControlAuditAndApprovalAuthorities()
    {
        using var quiet = new ProductionEnvironmentQuietScope();
        using var aspnet = new EnvironmentVariableScope("ASPNETCORE_ENVIRONMENT", Environments.Development);
        using var dotnet = new EnvironmentVariableScope("DOTNET_ENVIRONMENT", Environments.Development);
        using var governance = new EnvironmentVariableScope("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
        using var unified = new EnvironmentVariableScope(Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable, null);
        using var reporting = new EnvironmentVariableScope("MERIDIAN_REPORTING_CONNECTION_STRING", null);
        using var ledger = new EnvironmentVariableScope("MERIDIAN_LEDGER_CONNECTION_STRING", null);
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", nameof(RiskBoundaryContractTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new
        {
            DataRoot = Path.Combine(root, "data"),
            DataSource = "Synthetic",
            ApiHost = new { Urls = new[] { "http://127.0.0.1:0" }, ServeWorkstationAssets = false },
            Symbols = new[] { new { Symbol = "SPY", SubscribeTrades = true, Currency = "USD" } },
            Backfill = new { Enabled = false }
        }));
        try
        {
            await using var server = new UiServer(configPath, port: 0);
            var app = (WebApplication)typeof(UiServer).GetField("_app", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(server)!;
            var services = app.Services;

            services.GetRequiredService<IRiskCircuitBreakerControl>().Should()
                .BeSameAs(services.GetRequiredService<ExecutionOperatorControlService>());
            services.GetRequiredService<IRiskControlAuditWriter>().Should()
                .BeSameAs(services.GetRequiredService<ExecutionAuditTrailService>());
            services.GetRequiredService<IRiskEscalationQueue>().Should()
                .BeSameAs(services.GetRequiredService<RiskEscalationQueueService>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
