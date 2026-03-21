using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Contracts.Domain.Enums;
using Xunit;

namespace Meridian.Tests.Config;

/// <summary>
/// Tests for the unified configuration and deployment model.
/// </summary>
public sealed class ConfigurationUnificationTests
{
    #region ValidatedConfig Tests

    [Fact]
    public void ValidatedConfig_FromConfig_SetsAllProperties()
    {
        var config = new AppConfig { DataRoot = "test/data" };

        var validated = ValidatedConfig.FromConfig(
            config,
            isValid: true,
            sourcePath: "/path/to/config.json",
            validationErrors: Array.Empty<string>(),
            appliedFixes: new[] { "Fix1", "Fix2" },
            warnings: new[] { "Warning1" },
            environmentName: "Production",
            source: ConfigurationOrigin.File);

        validated.Config.Should().Be(config);
        validated.IsValid.Should().BeTrue();
        validated.SourcePath.Should().Be("/path/to/config.json");
        validated.AppliedFixes.Should().HaveCount(2);
        validated.Warnings.Should().HaveCount(1);
        validated.EnvironmentName.Should().Be("Production");
        validated.Source.Should().Be(ConfigurationOrigin.File);
    }

    [Fact]
    public void ValidatedConfig_Failed_SetsInvalidState()
    {
        var errors = new[] { "Error1", "Error2" };

        var validated = ValidatedConfig.Failed(null, errors, "/bad/path.json");

        validated.IsValid.Should().BeFalse();
        validated.ValidationErrors.Should().BeEquivalentTo(errors);
        validated.SourcePath.Should().Be("/bad/path.json");
        validated.Config.Should().NotBeNull();
    }

    [Fact]
    public void ValidatedConfig_Default_IsValidEmpty()
    {
        var validated = ValidatedConfig.Default();

        validated.IsValid.Should().BeTrue();
        validated.Source.Should().Be(ConfigurationOrigin.Default);
        validated.Config.Should().NotBeNull();
    }

    [Fact]
    public void ValidatedConfig_ImplicitConversion_ReturnsConfig()
    {
        var config = new AppConfig { DataRoot = "test" };
        var validated = ValidatedConfig.FromConfig(config, true);

        AppConfig converted = validated;

        converted.Should().BeSameAs(config);
    }

    #endregion

    #region DeploymentContext Tests

    [Fact]
    public void DeploymentContext_FromArgs_ResolvesHeadlessMode()
    {
        var args = new[] { "--config", "test.json" };

        var context = DeploymentContext.FromArgs(args, "test.json");

        context.Mode.Should().Be(DeploymentMode.Headless);
        context.RequiresHttpServer.Should().BeFalse();
        context.RunsCollector.Should().BeTrue();
    }

    [Fact]
    public void DeploymentContext_FromArgs_ResolvesWebMode()
    {
        var args = new[] { "--mode", "web", "--http-port", "9000" };

        var context = DeploymentContext.FromArgs(args, "test.json");

        context.Mode.Should().Be(DeploymentMode.Web);
        context.HttpPort.Should().Be(9000);
        context.RequiresHttpServer.Should().BeTrue();
        context.ModeDescription.Should().Contain("9000");
    }

    [Fact]
    public void DeploymentContext_FromArgs_ResolvesLegacyUiFlag()
    {
        var args = new[] { "--ui" };

        var context = DeploymentContext.FromArgs(args, "test.json");

        context.Mode.Should().Be(DeploymentMode.Web);
        context.HttpPort.Should().Be(8080); // Default port
    }

    [Fact]
    public void DeploymentContext_FromArgs_ResolvesDesktopMode()
    {
        var args = new[] { "--mode", "desktop", "--watch-config" };

        var context = DeploymentContext.FromArgs(args, "test.json");

        context.Mode.Should().Be(DeploymentMode.Desktop);
        context.RequiresHttpServer.Should().BeTrue();
        context.RunsCollector.Should().BeTrue();
        context.HotReloadEnabled.Should().BeTrue();
    }

    [Fact]
    public void DeploymentContext_FromArgs_DetectsOneShotCommands()
    {
        var oneShotArgs = new[]
        {
            new[] { "--help" },
            new[] { "--wizard" },
            new[] { "--validate-config" },
            new[] { "--backfill" },
            new[] { "--dry-run" },
            new[] { "--quick-check" }
        };

        foreach (var args in oneShotArgs)
        {
            var context = DeploymentContext.FromArgs(args, "test.json");
            context.IsOneShotCommand.Should().BeTrue($"for args: {string.Join(" ", args)}");
        }
    }

    [Fact]
    public void DeploymentContext_FromArgs_DeterminesCommand()
    {
        var context = DeploymentContext.FromArgs(new[] { "--wizard" }, "test.json");
        context.Command.Should().Be("wizard");

        context = DeploymentContext.FromArgs(new[] { "--backfill" }, "test.json");
        context.Command.Should().Be("backfill");

        context = DeploymentContext.FromArgs(new[] { "--validate-config" }, "test.json");
        context.Command.Should().Be("validate-config");
    }

    [Fact]
    public void DeploymentContext_ForCommand_CreatesOneShotContext()
    {
        var context = DeploymentContext.ForCommand("wizard", "/config.json");

        context.Mode.Should().Be(DeploymentMode.Headless);
        context.IsOneShotCommand.Should().BeTrue();
        context.Command.Should().Be("wizard");
        context.ConfigPath.Should().Be("/config.json");
    }

    [Fact]
    public void DeploymentContext_ForWeb_CreatesWebContext()
    {
        var context = DeploymentContext.ForWeb("/config.json", port: 3000);

        context.Mode.Should().Be(DeploymentMode.Web);
        context.HttpPort.Should().Be(3000);
        context.IsOneShotCommand.Should().BeFalse();
        context.RequiresHttpServer.Should().BeTrue();
    }

    [Fact]
    public void DeploymentContext_ForDesktop_CreatesDesktopContext()
    {
        var context = DeploymentContext.ForDesktop("/config.json", port: 5000, hotReload: true);

        context.Mode.Should().Be(DeploymentMode.Desktop);
        context.HttpPort.Should().Be(5000);
        context.HotReloadEnabled.Should().BeTrue();
        context.IsDocker.Should().BeFalse();
    }

    [Fact]
    public void DeploymentContext_ComputedProperties_AreConsistent()
    {
        // Web mode
        var webContext = DeploymentContext.ForWeb("test.json");
        webContext.RequiresHttpServer.Should().BeTrue();
        webContext.RunsCollector.Should().BeFalse(); // Web mode only runs UI server
        webContext.RequiresGracefulShutdown.Should().BeTrue();

        // Desktop mode
        var desktopContext = DeploymentContext.ForDesktop("test.json");
        desktopContext.RequiresHttpServer.Should().BeTrue();
        desktopContext.RunsCollector.Should().BeTrue();
        desktopContext.RequiresGracefulShutdown.Should().BeTrue();

        // One-shot command
        var commandContext = DeploymentContext.ForCommand("validate", "test.json");
        commandContext.RequiresHttpServer.Should().BeFalse();
        commandContext.RunsCollector.Should().BeFalse();
        commandContext.RequiresGracefulShutdown.Should().BeFalse();
    }

    #endregion

    #region PipelineOptions Tests

    [Fact]
    public void PipelineOptions_Default_EnablesBothSelfHealingAndValidation()
    {
        var options = PipelineOptions.Default;

        options.ApplySelfHealing.Should().BeTrue();
        options.ValidateConfig.Should().BeTrue();
    }

    [Fact]
    public void PipelineOptions_Strict_DisablesSelfHealingOnly()
    {
        var options = PipelineOptions.Strict;

        options.ApplySelfHealing.Should().BeFalse();
        options.ValidateConfig.Should().BeTrue();
    }

    [Fact]
    public void PipelineOptions_Lenient_DisablesBoth()
    {
        var options = PipelineOptions.Lenient;

        options.ApplySelfHealing.Should().BeFalse();
        options.ValidateConfig.Should().BeFalse();
    }

    #endregion

    #region ConfigurationSource Tests

    [Fact]
    public void ConfigurationOrigin_HasAllExpectedValues()
    {
        var sources = Enum.GetValues<ConfigurationOrigin>();

        sources.Should().Contain(ConfigurationOrigin.Default);
        sources.Should().Contain(ConfigurationOrigin.File);
        sources.Should().Contain(ConfigurationOrigin.Wizard);
        sources.Should().Contain(ConfigurationOrigin.AutoConfig);
        sources.Should().Contain(ConfigurationOrigin.HotReload);
        sources.Should().Contain(ConfigurationOrigin.Programmatic);
    }

    #endregion
}
