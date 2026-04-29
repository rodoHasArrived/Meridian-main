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
            """
            var symbol = Param<string>("symbol", "SPY");
            var prices = await Data.PricesAsync(symbol, new DateOnly(2024, 1, 2), new DateOnly(2024, 3, 29));
            Print($"Loaded {prices.Count} bars for {symbol}.");
            """)
    ];
}
