using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Dispatches CLI arguments to the appropriate command handler.
/// Iterates through registered handlers and executes the first match.
/// </summary>
internal sealed class CommandDispatcher
{
    private readonly ICliCommand[] _commands;
    private readonly ILogger _logger;

    public CommandDispatcher(params ICliCommand[] commands)
        : this(commands.AsEnumerable())
    {
    }

    public CommandDispatcher(IEnumerable<ICliCommand> commands, ILogger? logger = null)
    {
        _commands = commands?.ToArray() ?? throw new ArgumentNullException(nameof(commands));
        _logger = logger ?? Serilog.Core.Logger.None;
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
                try
                {
                    var result = await command.ExecuteAsync(args, ct);
                    return (true, result);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                {
                    _logger.Warning(
                        ex,
                        "CLI command {CommandType} failed during dispatch.",
                        command.GetType().FullName);
                    throw;
                }
            }
        }

        return (false, CliResult.Ok());
    }
}
