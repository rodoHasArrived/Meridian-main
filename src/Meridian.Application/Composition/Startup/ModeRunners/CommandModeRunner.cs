using Meridian.Application.Composition.Startup.StartupModels;
using Serilog;

namespace Meridian.Application.Composition.Startup.ModeRunners;

/// <summary>
/// Handles one-shot CLI command dispatch (help, validate-config, symbols, dry-run, etc.).
/// Attempts to dispatch the raw args through the command dispatcher.
/// Returns the exit code when a command was handled, or <c>null</c> to signal pass-through
/// to the next startup phase.
/// </summary>
public sealed class CommandModeRunner
{
    private readonly ILogger _log;

    public CommandModeRunner(ILogger log) => _log = log;

    /// <summary>
    /// Tries to dispatch a CLI command from the startup context.
    /// </summary>
    /// <returns>
    /// The command's exit code when a command was matched and handled;
    /// <c>null</c> when no command matched and startup should continue to a runtime mode.
    /// </returns>
    public async Task<int?> TryRunAsync(StartupContext ctx, CancellationToken ct = default)
    {
        var commandPlan = CommandDispatchPlanner.Create(ctx.Config, ctx.ConfigPath, _log, ctx.ConfigurationService);
        var (handled, cliResult) = await commandPlan.Dispatcher.TryDispatchAsync(ctx.CliArgs.Raw, ct);
        if (handled)
            return cliResult.ExitCode;

        return null;
    }
}
