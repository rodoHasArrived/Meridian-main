namespace Meridian.Application.Commands;

/// <summary>
/// Dispatches CLI arguments to the appropriate command handler.
/// Iterates through registered handlers and executes the first match.
/// </summary>
internal sealed class CommandDispatcher
{
    private readonly ICliCommand[] _commands;

    public CommandDispatcher(params ICliCommand[] commands)
    {
        _commands = commands;
    }

    /// <summary>
    /// Tries to dispatch the args to a registered command.
    /// Returns a <see cref="CliResult"/> with <c>Handled=true</c> if a command matched.
    /// Returns <c>Handled=false</c> if no command matched (caller should continue normal startup).
    /// </summary>
    public async Task<(bool Handled, CliResult Result)> TryDispatchAsync(string[] args, CancellationToken ct = default)
    {
        foreach (var command in _commands)
        {
            if (command.CanHandle(args))
            {
                var result = await command.ExecuteAsync(args, ct);
                return (true, result);
            }
        }

        return (false, CliResult.Ok());
    }
}
