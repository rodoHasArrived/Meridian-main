using System.Diagnostics;
using System.Globalization;
using FluentAssertions;
using Meridian.ProcessTestHelper;

namespace Meridian.Tests.FinancialOperations;

public sealed partial class RecurringJournalStoreTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task EconomicObligations_IndependentProcessesRetainExactlyOneOwner()
    {
        await SeedTwoSchedulesAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        var children = new List<(Process Process, Task<string> Error, string Ready, string Outcome)>();
        try
        {
            var gate = Path.Combine(StateDirectory, "obligation-start.gate");
            foreach (var schedule in new[] { "schedule-a", "schedule-b" })
            {
                var ready = Path.Combine(StateDirectory, schedule + ".ready");
                var outcome = Path.Combine(StateDirectory, schedule + ".outcome");
                var start = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                foreach (var argument in new[]
                {
                    "exec", "--depsfile", Path.Combine(AppContext.BaseDirectory, "Meridian.Tests.deps.json"),
                    "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "Meridian.Tests.runtimeconfig.json"),
                    typeof(ProcessTestHelperMarker).Assembly.Location, "recurring-pin-obligation",
                    StateDirectory, schedule, Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Obligation(), ready, gate, outcome
                })
                    start.ArgumentList.Add(argument);
                var process = Process.Start(start) ?? throw new InvalidOperationException("Recurring obligation helper did not start.");
                children.Add((process, process.StandardError.ReadToEndAsync(), ready, outcome));
            }

            while (!children.All(child => File.Exists(child.Ready)))
            {
                foreach (var child in children.Where(child => child.Process.HasExited))
                    Assert.Fail($"Recurring obligation helper exited before readiness: {await child.Error}");
                await Task.Delay(20, timeout.Token);
            }
            children.Select(child => child.Process.Id).Distinct().Should().HaveCount(2);
            await File.WriteAllTextAsync(gate, "start", timeout.Token);
            await Task.WhenAll(children.Select(child => child.Process.WaitForExitAsync(timeout.Token)));
            foreach (var child in children)
                Assert.True(child.Process.ExitCode == 0, await child.Error);

            var outcomes = await Task.WhenAll(children.Select(child => File.ReadAllTextAsync(child.Outcome, timeout.Token)));
            outcomes.Should().BeEquivalentTo(new[] { "owned", "overlap" });
            await using var restored = await Store().OpenSessionAsync(timeout.Token);
            restored.Occurrences.Should().HaveCount(2);
            restored.Occurrences.SelectMany(claim => claim.EconomicObligationKeys ?? [])
                .Should().ContainSingle().Which.Should().Be(Obligation());
        }
        finally
        {
            foreach (var child in children)
            {
                if (!child.Process.HasExited)
                    child.Process.Kill(entireProcessTree: true);
                await child.Process.WaitForExitAsync();
                child.Process.Dispose();
            }
        }
    }
}
