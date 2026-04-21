using Meridian.QuantScript.Compilation;

namespace Meridian.QuantScript.Tests;

public sealed class NotebookExecutionSessionTests
{
    [Fact]
    public void GetReplayStartIndex_UsesFirstMissingCheckpoint()
    {
        var session = new NotebookExecutionSession();
        var cells = new[]
        {
            new NotebookCellExecutionIdentity("cell-1", 1),
            new NotebookCellExecutionIdentity("cell-2", 1),
            new NotebookCellExecutionIdentity("cell-3", 1)
        };

        session.RecordSuccessfulRun(cells[0], CreateCheckpoint());

        session.GetReplayStartIndex(cells, 2).Should().Be(1);
    }

    [Fact]
    public void InvalidateFrom_RemovesDownstreamCheckpoints()
    {
        var session = new NotebookExecutionSession();
        var cells = new[]
        {
            new NotebookCellExecutionIdentity("cell-1", 1),
            new NotebookCellExecutionIdentity("cell-2", 1),
            new NotebookCellExecutionIdentity("cell-3", 1)
        };

        foreach (var cell in cells)
            session.RecordSuccessfulRun(cell, CreateCheckpoint());

        session.InvalidateFrom(cells, 1);

        session.GetValidFrontierIndex(cells).Should().Be(0);
    }

    private static ScriptExecutionCheckpoint CreateCheckpoint()
    {
        var globals = new QuantScriptGlobals(
            new Api.DataProxy(new Helpers.FakeQuantDataContext(), () => CancellationToken.None),
            new Api.BacktestProxy(null, new QuantScriptOptions()),
            () => CancellationToken.None);

        var state = Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript
            .RunAsync("var x = 1;", globals: globals)
            .GetAwaiter()
            .GetResult();

        return new ScriptExecutionCheckpoint(state, globals);
    }
}
