using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Meridian.Tests.Ui;

/// <summary>
/// Phase 4 configuration binding: the workbench source-precedence ladder (and the rest of the options)
/// must bind from the <c>SecurityMasterWorkbench</c> configuration section so the conflict-authority
/// policy ranks the deployment's real source systems rather than the empty default.
/// </summary>
public sealed class SecurityMasterWorkbenchOptionsBindingTests
{
    [Fact]
    public void AddWorkstationSharedServices_BindsWorkbenchOptionsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecurityMasterWorkbench:SourcePrecedence:0"] = "golden-copy",
                ["SecurityMasterWorkbench:SourcePrecedence:1"] = "vendor-a",
                ["SecurityMasterWorkbench:SourcePrecedence:2"] = "vendor-b",
                ["SecurityMasterWorkbench:GoldenCopySource"] = "golden-copy",
                ["SecurityMasterWorkbench:MaxBulkResolveBatch"] = "50",
                ["SecurityMasterWorkbench:RequireIndependentReviewer"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<SecurityMasterWorkbenchOptions>>().CurrentValue;

        options.SourcePrecedence.Should().Equal("golden-copy", "vendor-a", "vendor-b");
        options.GoldenCopySource.Should().Be("golden-copy");
        options.MaxBulkResolveBatch.Should().Be(50);
        options.RequireIndependentReviewer.Should().BeFalse();
    }

    [Fact]
    public void AddWorkstationSharedServices_WithNoSection_LeavesSourcePrecedenceEmpty()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<SecurityMasterWorkbenchOptions>>().CurrentValue;

        // No configured ladder ⇒ the policy applies no presumptive ranking (falls through to freshness
        // then confidence) rather than ranking against a placeholder.
        options.SourcePrecedence.Should().BeEmpty();
    }
}
