using System.Reflection;
using FluentAssertions;
using Meridian.FSharp.Trading;
using Meridian.Strategies.Models;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.Strategies;

public sealed class StrategyLifecycleInteropContractTests
{
    private const string PriorFaultReason = "Prior provider failure.";
    private const string FailureReason = "Replacement cleanup failure.";

    private static readonly Endpoint[] Endpoints =
    [
        new("Start", StrategyLifecycleInterop.EvaluateStart, StrategyLifecycleInterop.EvaluateStart),
        new("WarmupCompleted", StrategyLifecycleInterop.EvaluateWarmupCompleted, StrategyLifecycleInterop.EvaluateWarmupCompleted),
        new("Pause", StrategyLifecycleInterop.EvaluatePause, StrategyLifecycleInterop.EvaluatePause),
        new("Stop", StrategyLifecycleInterop.EvaluateStop, StrategyLifecycleInterop.EvaluateStop),
        new("StopCompleted", StrategyLifecycleInterop.EvaluateStopCompleted, StrategyLifecycleInterop.EvaluateStopCompleted),
        new("Fail",
            (state, fault) => StrategyLifecycleInterop.EvaluateFail(state, fault, FailureReason),
            state => StrategyLifecycleInterop.EvaluateFail(state, FailureReason))
    ];

    [Fact]
    public void CSharp_status_names_match_every_FSharp_state_case()
    {
        Enum.GetNames<StrategyStatus>().Should().BeEquivalentTo(
            FSharpType.GetUnionCases(typeof(StrategyLifecycleState), null).Select(state => state.Name));
    }

    [Fact]
    public void Contract_cases_cover_every_kernel_command_and_exported_endpoint()
    {
        // Resume is deliberately exposed to C# through EvaluateStart("Paused").
        Endpoints.Select(endpoint => endpoint.Command).Append("Resume").Should().BeEquivalentTo(
            FSharpType.GetUnionCases(typeof(StrategyCommand), null).Select(command => command.Name));

        Endpoints.Select(endpoint => $"Evaluate{endpoint.Command}").Should().BeEquivalentTo(
            typeof(StrategyLifecycleInterop).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name.StartsWith("Evaluate", StringComparison.Ordinal))
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal));
    }

    public static IEnumerable<object[]> StateAndCommandPairs()
    {
        foreach (var state in Enum.GetValues<StrategyStatus>())
        {
            foreach (var endpoint in Endpoints)
            {
                yield return [state, endpoint.Command];
            }
        }
    }

    [Theory]
    [MemberData(nameof(StateAndCommandPairs))]
    public void CSharp_endpoints_preserve_kernel_results_for_every_state_and_command(
        StrategyStatus status, string command)
    {
        var endpoint = Endpoints.Single(candidate => candidate.Command == command);
        var stateName = status.ToString();
        var kernelCommand = CreateUnion<StrategyCommand>(
            command == "Start" && status == StrategyStatus.Paused ? "Resume" : command,
            FailureReason);

        // The kernel owns transition expectations. This test owns the string/enum boundary,
        // overload compatibility, diagnostics and fact ordering rather than another state table.
        var expected = StrategyLifecycleTransitions.apply(
            kernelCommand, CreateUnion<StrategyLifecycleState>(stateName, PriorFaultReason));
        AssertContract(endpoint.Evaluate(stateName, PriorFaultReason), expected);

        var legacyExpected = StrategyLifecycleTransitions.apply(
            kernelCommand, CreateUnion<StrategyLifecycleState>(stateName, string.Empty));
        AssertContract(endpoint.EvaluateLegacy(stateName), legacyExpected);
    }

    [Fact]
    public void CSharp_start_while_paused_is_the_kernel_resume_contract()
    {
        var resumed = StrategyLifecycleInterop.EvaluateStart(StrategyStatus.Paused.ToString());
        var expected = StrategyLifecycleTransitions.apply(StrategyCommand.Resume, StrategyLifecycleState.Paused);

        AssertContract(resumed, expected);
        resumed.IsValid.Should().BeTrue();
        Enum.Parse<StrategyStatus>(resumed.NextState).Should().Be(StrategyStatus.Running);
    }

    private static void AssertContract(StrategyTransitionDto actual, TransitionResult expected)
    {
        var previous = FSharpValue.GetUnionFields(expected.PreviousState, typeof(StrategyLifecycleState), null);
        var next = FSharpValue.GetUnionFields(expected.NextState, typeof(StrategyLifecycleState), null);

        actual.PreviousState.Should().Be(previous.Item1.Name);
        actual.NextState.Should().Be(next.Item1.Name);
        // LiveStrategyBase uses this exact parse when applying the returned state.
        Enum.Parse<StrategyStatus>(actual.PreviousState).ToString().Should().Be(previous.Item1.Name);
        Enum.Parse<StrategyStatus>(actual.NextState).ToString().Should().Be(next.Item1.Name);
        actual.PreviousFaultReason.Should().Be(previous.Item2.OfType<string>().SingleOrDefault() ?? string.Empty);
        actual.FaultReason.Should().Be(next.Item2.OfType<string>().SingleOrDefault() ?? string.Empty);
        actual.IsValid.Should().Be(expected.IsValid);
        actual.Reason.Should().Be(expected.Reason?.Value ?? string.Empty);
        actual.EmittedFacts.Should().Equal(expected.EmittedFacts);
    }

    private static T CreateUnion<T>(string name, string reason)
    {
        var unionCase = FSharpType.GetUnionCases(typeof(T), null).Single(candidate => candidate.Name == name);
        var fields = unionCase.GetFields();
        fields.Should().OnlyContain(field => field.PropertyType == typeof(string),
            "a new lifecycle payload needs an explicit C#/F# compatibility fixture");
        return (T)FSharpValue.MakeUnion(unionCase, fields.Select(_ => (object)reason).ToArray(), null);
    }

    private sealed record Endpoint(
        string Command,
        Func<string, string, StrategyTransitionDto> Evaluate,
        Func<string, StrategyTransitionDto> EvaluateLegacy);
}
