using Microsoft.CodeAnalysis.Scripting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Wraps Roslyn script state so callers can chain cell execution without exposing Roslyn types
/// outside the QuantScript runtime layer.
/// </summary>
public sealed class ScriptExecutionCheckpoint
{
    internal ScriptExecutionCheckpoint(ScriptState<object> scriptState, QuantScriptGlobals globals)
    {
        ScriptState = scriptState ?? throw new ArgumentNullException(nameof(scriptState));
        Globals = globals ?? throw new ArgumentNullException(nameof(globals));
    }

    internal ScriptState<object> ScriptState { get; }

    internal QuantScriptGlobals Globals { get; }
}
