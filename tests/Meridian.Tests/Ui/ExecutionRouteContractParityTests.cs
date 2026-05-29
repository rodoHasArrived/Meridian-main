using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ExecutionRouteContractParityTests
{
    [Fact]
    public void ExecutionControlRoutes_ShouldMatchContractsAndEndpointMap()
    {
        var mappedRoutes = MapExecutionRoutes();

        mappedRoutes.Should().Contain(UiApiRoutes.ExecutionControlsDefaultPositionLimit,
            "endpoint map diverged from contracts for execution default position-limit route");

        mappedRoutes.Should().Contain(UiApiRoutes.ExecutionControlsSymbolPositionLimit,
            "endpoint map diverged from contracts for execution symbol position-limit route");

        mappedRoutes.Should().Contain(UiApiRoutes.ExecutionControlsManualOverrides,
            "endpoint map diverged from contracts for execution manual override create route");

        mappedRoutes.Should().Contain(UiApiRoutes.ExecutionControlsManualOverrideClear,
            "endpoint map diverged from contracts for execution manual override clear route");
    }

    private static HashSet<string> MapExecutionRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        app.MapExecutionEndpoints(new JsonSerializerOptions());

        return ((IEndpointRouteBuilder)app)
            .DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .Select(route => route!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
