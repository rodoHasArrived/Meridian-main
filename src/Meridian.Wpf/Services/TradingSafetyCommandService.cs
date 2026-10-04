namespace Meridian.Wpf.Services;

/// <summary>
/// Carries out the Trading command bar's desk safety commands against the shared execution
/// service, and reports what each one actually achieved.
/// <para>
/// Separate from <see cref="TradingWorkspaceShellPresentationService"/> on purpose. That service
/// composes shell presentation from half a dozen workspace services; executing a halt needs none
/// of them, and hanging the safety seam off it would make the desk's kill path depend on the whole
/// presentation graph being constructible.
/// </para>
/// </summary>
public sealed class TradingSafetyCommandService
{
    private readonly IExecutionSafetyControlClient? _client;

    /// <param name="client">
    /// Optional so a host composed without execution-control access still resolves. A null client
    /// reports that nothing was sent rather than silently succeeding — on a safety control the
    /// difference is whether an operator believes the desk is halted.
    /// </param>
    public TradingSafetyCommandService(IExecutionSafetyControlClient? client = null)
    {
        _client = client;
    }

    /// <summary>
    /// Command ids that must reach the shared execution service rather than only rearranging
    /// panes. Every other command in the bar is navigation.
    /// </summary>
    public static bool IsSafetyCommand(string? commandId) =>
        commandId is "Stop" or "CancelAll";

    /// <summary>
    /// Runs a desk safety command and returns its real outcome. Commands that are not safety
    /// commands, and hosts with no execution-control connection, both report failure rather than
    /// a silent no-op.
    /// </summary>
    public async Task<SafetyCommandOutcome> ExecuteAsync(string commandId, CancellationToken ct = default)
    {
        if (!IsSafetyCommand(commandId))
        {
            return new SafetyCommandOutcome(false, $"'{commandId}' is not a desk safety command.");
        }

        if (_client is null)
        {
            return new SafetyCommandOutcome(
                false,
                "This workstation has no execution-control connection, so the command was not sent. Halt the desk at the broker, or from a workstation that has an execution-control connection.");
        }

        return commandId switch
        {
            "Stop" => await _client
                .HaltTradingAsync("Halted from the WPF Trading command bar.", ct)
                .ConfigureAwait(false),
            "CancelAll" => await _client.CancelAllOrdersAsync(ct).ConfigureAwait(false),
            _ => new SafetyCommandOutcome(false, $"'{commandId}' is not a desk safety command.")
        };
    }
}
