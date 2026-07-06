using Meridian.Workflow.Runbooks;
using Meridian.Platform.Results;

namespace Meridian.Application.Commands;

internal sealed class RunbookCommands(IRunbookStore store, IRunbookExecutor executor) : ICliCommand
{
    public IReadOnlyList<string> Triggers { get; } = ["--runbook-list", "--runbook-create", "--runbook-run"];

    public bool CanHandle(string[] args)
        => CliArguments.HasFlag(args, "--runbook-list")
           || CliArguments.GetValue(args, "--runbook-create") is not null
           || CliArguments.GetValue(args, "--runbook-run") is not null;

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        if (CliArguments.HasFlag(args, "--runbook-list"))
        {
            var runbooks = await store.ListAsync(ct).ConfigureAwait(false);
            if (runbooks.Count == 0)
            {
                Console.WriteLine("No runbooks found.");
                return CliResult.Ok();
            }

            foreach (var r in runbooks)
            {
                Console.WriteLine($"{r.Id}\t{r.Name}\tsteps={r.Steps.Count}\tupdated={r.UpdatedAtUtc:O}");
            }

            return CliResult.Ok();
        }

        var createName = CliArguments.GetValue(args, "--runbook-create");
        if (!string.IsNullOrWhiteSpace(createName))
        {
            var id = CliArguments.GetValue(args, "--runbook-id") ?? createName.Trim().ToLowerInvariant().Replace(' ', '-');
            var description = CliArguments.GetValue(args, "--runbook-description") ?? string.Empty;
            var stepsRaw = CliArguments.GetValue(args, "--runbook-steps");
            if (string.IsNullOrWhiteSpace(stepsRaw))
            {
                Console.Error.WriteLine("Error: --runbook-steps is required for --runbook-create");
                Console.Error.WriteLine("Example: --runbook-steps readiness:global,replay:session-123");
                return CliResult.Fail(ErrorCode.ValidationFailed);
            }

            var now = DateTimeOffset.UtcNow;
            var steps = stepsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseStep)
                .ToArray();
            var existing = await store.GetAsync(id, ct).ConfigureAwait(false);
            var createdAt = existing?.CreatedAtUtc ?? now;
            var definition = new RunbookDefinition(id, createName.Trim(), description.Trim(), steps, createdAt, now);
            await store.SaveAsync(definition, ct).ConfigureAwait(false);
            Console.WriteLine($"Saved runbook '{definition.Name}' ({definition.Id}) with {definition.Steps.Count} step(s).");
            return CliResult.Ok();
        }

        var runId = CliArguments.GetValue(args, "--runbook-run");
        if (!string.IsNullOrWhiteSpace(runId))
        {
            var runbook = await store.GetAsync(runId, ct).ConfigureAwait(false);
            if (runbook is null)
            {
                Console.Error.WriteLine($"Runbook '{runId}' not found.");
                return CliResult.Fail(ErrorCode.ValidationFailed);
            }

            var dryRun = CliArguments.HasFlag(args, "--dry-run");
            var result = await executor.ExecuteAsync(runbook, dryRun, ct).ConfigureAwait(false);
            foreach (var message in result.Messages)
                Console.WriteLine(message);
            return result.Success ? CliResult.Ok() : CliResult.Fail(ErrorCode.InternalError);
        }

        return CliResult.Ok();
    }

    private static RunbookStep ParseStep(string raw)
    {
        var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? new RunbookStep(parts[0], parts[1]) : new RunbookStep("custom", raw);
    }
}
