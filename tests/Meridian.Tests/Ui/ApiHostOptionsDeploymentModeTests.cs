using FluentAssertions;
using Meridian.Application.Composition;
using Microsoft.Extensions.Configuration;

namespace Meridian.Tests.Ui;

public sealed class ApiHostOptionsDeploymentModeTests
{
    [Fact]
    public void FromConfiguration_WithoutDeploymentMode_DefaultsToLocalWorkstation()
    {
        var options = ApiHostOptions.FromConfiguration(BuildConfiguration(new Dictionary<string, string?>()), port: 8080);

        options.DeploymentMode.Should().Be(MeridianApiDeploymentMode.LocalWorkstation);
        options.ToDeploymentPosture().Should().Be(MeridianDeploymentPosture.LocalWorkstation);
    }

    [Theory]
    [InlineData("LocalWorkstation", MeridianApiDeploymentMode.LocalWorkstation)]
    [InlineData("productionapi", MeridianApiDeploymentMode.ProductionApi)]
    [InlineData("Worker", MeridianApiDeploymentMode.Worker)]
    [InlineData("MIGRATION", MeridianApiDeploymentMode.Migration)]
    public void FromConfiguration_ParsesDefinedDeploymentModesCaseInsensitively(
        string configured, MeridianApiDeploymentMode expected)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ApiHost:DeploymentMode"] = configured
        });

        ApiHostOptions.FromConfiguration(configuration, port: 8080)
            .DeploymentMode.Should().Be(expected);
    }

    [Theory]
    [InlineData("Prod")]
    [InlineData("production")]
    [InlineData("2")]
    [InlineData("-1")]
    public void FromConfiguration_RejectsUnrecognizedDeploymentModesInsteadOfFallingBack(string configured)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ApiHost:DeploymentMode"] = configured
        });

        Action act = () => ApiHostOptions.FromConfiguration(configuration, port: 8080);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*Unrecognized ApiHost deployment mode '{configured}'*");
    }

    [Fact]
    public void FromConfiguration_HonorsDeploymentModeVariableWhenSectionIsAbsent()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["MERIDIAN_API_DEPLOYMENT_MODE"] = "ProductionApi"
        });

        var options = ApiHostOptions.FromConfiguration(configuration, port: 8080);

        options.DeploymentMode.Should().Be(MeridianApiDeploymentMode.ProductionApi);
        options.IsProductionApi.Should().BeTrue();
    }

    [Theory]
    [InlineData(MeridianApiDeploymentMode.LocalWorkstation, MeridianDeploymentPosture.LocalWorkstation)]
    [InlineData(MeridianApiDeploymentMode.ProductionApi, MeridianDeploymentPosture.ProductionApi)]
    [InlineData(MeridianApiDeploymentMode.Worker, MeridianDeploymentPosture.Worker)]
    [InlineData(MeridianApiDeploymentMode.Migration, MeridianDeploymentPosture.Migration)]
    public void ToDeploymentPosture_MapsEveryDeploymentMode(
        MeridianApiDeploymentMode mode, MeridianDeploymentPosture expected)
    {
        new ApiHostOptions { DeploymentMode = mode }.ToDeploymentPosture().Should().Be(expected);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
