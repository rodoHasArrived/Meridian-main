using Meridian.Execution;
using Meridian.Execution.Sdk;
using NSubstitute;

namespace Meridian.Tests.Execution;

internal static class RiskValidatorSubstitute
{
    // NSubstitute intercepts default interface methods rather than running their bodies.
    // Forward new-order validation through the same legacy bridge as a concrete validator so
    // these existing tests keep asserting the requests and reservation behavior they own.
    internal static IRiskValidator Create()
    {
        var validator = Substitute.For<IRiskValidator>();
        validator.ValidateResolvedOrderAsync(Arg.Any<GatewayResolvedOrder>(), Arg.Any<CancellationToken>())
            .Returns(call => validator.ValidateOrderAsync(
                call.Arg<GatewayResolvedOrder>().ToRiskRequest(), call.Arg<CancellationToken>()));
        return validator;
    }
}
