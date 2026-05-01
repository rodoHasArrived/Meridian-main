namespace Meridian.Application.Runbooks;

public interface IRunbookExecutor
{
    Task<RunbookExecutionResult> ExecuteAsync(RunbookDefinition definition, bool dryRun, CancellationToken ct = default);
}

public sealed class RunbookExecutor : IRunbookExecutor
{
    public Task<RunbookExecutionResult> ExecuteAsync(RunbookDefinition definition, bool dryRun, CancellationToken ct = default)
    {
        var started = DateTimeOffset.UtcNow;
        var messages = new List<string> { $"Runbook '{definition.Name}' started ({(dryRun ? "dry-run" : "execute")})." };
        for (var i = 0; i < definition.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = definition.Steps[i];
            messages.Add($"Step {i + 1}: {step.Kind} -> {step.Payload}");
        }

        messages.Add("Runbook completed.");
        return Task.FromResult(new RunbookExecutionResult(definition.Id, started, DateTimeOffset.UtcNow, true, messages));
    }
}
