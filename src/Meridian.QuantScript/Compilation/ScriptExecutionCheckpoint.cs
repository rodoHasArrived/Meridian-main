using Microsoft.CodeAnalysis.Scripting;
using Meridian.QuantScript.Runtime;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Opaque notebook checkpoint used to chain cell execution. Isolated runs retain replayable cell
/// inputs rather than a live Roslyn state object, allowing every continuation to execute in a new,
/// killable worker process.
/// </summary>
public sealed class ScriptExecutionCheckpoint
{
    internal ScriptExecutionCheckpoint(ScriptState<object> scriptState, QuantScriptGlobals globals)
    {
        ScriptState = scriptState ?? throw new ArgumentNullException(nameof(scriptState));
        Globals = globals ?? throw new ArgumentNullException(nameof(globals));
        ReplayCells = Array.Empty<WorkerScriptCell>();
    }

    internal ScriptExecutionCheckpoint(IReadOnlyList<WorkerScriptCell> replayCells)
    {
        ArgumentNullException.ThrowIfNull(replayCells);
        if (replayCells.Count == 0)
            throw new ArgumentException("A checkpoint must contain at least one successful cell.", nameof(replayCells));

        ReplayCells = replayCells.ToArray();
    }

    // Retained only for source-compatible internal tests and checkpoints created by older
    // in-process integrations. Such checkpoints fail closed if submitted to the isolated runner.
    internal ScriptState<object>? ScriptState { get; }

    internal QuantScriptGlobals? Globals { get; }

    internal IReadOnlyList<WorkerScriptCell> ReplayCells { get; }

    internal bool IsReplayable => ReplayCells.Count > 0;
}
