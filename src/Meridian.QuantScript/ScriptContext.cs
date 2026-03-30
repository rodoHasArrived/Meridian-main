using Meridian.QuantScript.Plotting;

namespace Meridian.QuantScript;

/// <summary>
/// Provides ambient script context for the currently-executing script run.
/// <see cref="ScriptRunner"/> sets <see cref="PlotQueue"/> before calling <c>RunAsync</c>
/// so that <see cref="Api.ReturnSeries.Plot"/> and <see cref="Api.PortfolioResult.PlotHeatmap"/>
/// can enqueue chart requests without needing an injected dependency.
/// </summary>
internal static class ScriptContext
{
    private static readonly AsyncLocal<PlotQueue?> _plotQueue = new();

    internal static PlotQueue? PlotQueue
    {
        get => _plotQueue.Value;
        set => _plotQueue.Value = value;
    }
}
