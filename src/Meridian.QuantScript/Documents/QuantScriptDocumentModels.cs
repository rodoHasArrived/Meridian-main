namespace Meridian.QuantScript.Documents;

public enum QuantScriptDocumentKind
{
    Notebook,
    LegacyScript
}

public sealed record QuantScriptDocumentReference(
    string Name,
    string FullPath,
    QuantScriptDocumentKind Kind);

public sealed record QuantScriptNotebookCellDocument(
    string Id,
    string Source,
    bool Collapsed = false);

public sealed class QuantScriptNotebookDocument
{
    public const int CurrentVersion = 1;

    public string Title { get; init; } = "QuantScript Notebook";

    public int Version { get; init; } = CurrentVersion;

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    public IReadOnlyList<QuantScriptNotebookCellDocument> Cells { get; init; } =
    [
        new QuantScriptNotebookCellDocument(
            Guid.NewGuid().ToString("N"),
            "var spy = Data.Prices(\"SPY\");" + Environment.NewLine + "Print(spy);")
    ];
}
