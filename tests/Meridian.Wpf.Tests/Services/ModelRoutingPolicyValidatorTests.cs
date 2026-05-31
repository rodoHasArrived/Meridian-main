using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class ModelRoutingPolicyValidatorTests
{
    [Fact]
    public void Validate_WithCurrentPolicyDocument_ReturnsValid()
    {
        var validator = new ModelRoutingPolicyValidator();
        var json = File.ReadAllText(Path.Combine("docs", "ai", "model-routing-policy.json"));

        var result = validator.Validate(json);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhenRouteReferencesUnknownModelClass_ReturnsError()
    {
        var validator = new ModelRoutingPolicyValidator();
        var json = """
                   {
                     "version": "1.0.0",
                     "defaultRouteId": "route-a",
                     "modelClasses": [{ "id": "economy" }],
                     "telemetrySignals": [{ "signal": "route_id" }],
                     "routingRules": [
                       {
                         "id": "route-a",
                         "modelClass": "balanced",
                         "telemetrySignals": ["route_id"]
                       }
                     ]
                   }
                   """;

        var result = validator.Validate(json);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("unknown modelClass", StringComparison.Ordinal));
    }
}
