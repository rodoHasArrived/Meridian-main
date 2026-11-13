using FluentAssertions;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class CorporateActionFetchResultTests
{
    [Fact]
    public void RateLimitException_WithoutTransportEvidence_DoesNotInventHttpStatus()
    {
        var result = CorporateActionFetchResult.FromException(new RateLimitException(
            "Body quota response", retryAfter: TimeSpan.FromSeconds(30)));

        var failure = result.Failures.Should().ContainSingle().Subject;
        failure.Code.Should().Be("rate-limited");
        failure.HttpStatusCode.Should().BeNull();
        failure.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Combine_EmptySuccessfulEndpointAndFailedEndpoint_IsPartial()
    {
        var result = CorporateActionFetchResult.Combine(
            CorporateActionFetchResult.Complete([]),
            CorporateActionFetchResult.Failed("unauthorized", 403, scope: "splits"));

        result.Actions.Should().BeEmpty();
        result.Status.Should().Be(CorporateActionFetchStatus.Partial);
        result.Failures.Should().ContainSingle().Which.Scope.Should().Be("splits");
    }

    [Fact]
    public void Combine_UnreportedCoverage_NeverBecomesComplete()
    {
        var result = CorporateActionFetchResult.Combine(
            CorporateActionFetchResult.Complete([]),
            new([], CorporateActionFetchStatus.Unknown, [new("coverage-unreported")]));

        result.Status.Should().Be(CorporateActionFetchStatus.Unknown);
    }
}
