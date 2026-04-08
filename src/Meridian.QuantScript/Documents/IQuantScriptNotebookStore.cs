namespace Meridian.QuantScript.Documents;

/// <summary>
/// Loads, saves, and imports QuantScript notebook documents.
/// </summary>
public interface IQuantScriptNotebookStore
{
    string ScriptsDirectory { get; }
    string NotebookExtension { get; }

    IReadOnlyList<QuantScriptDocumentReference> ListDocuments();

    Task<QuantScriptNotebookDocument> LoadNotebookAsync(string path, CancellationToken ct = default);

    Task<QuantScriptNotebookDocument> ImportLegacyScriptAsync(string path, CancellationToken ct = default);

    Task SaveNotebookAsync(string path, QuantScriptNotebookDocument document, CancellationToken ct = default);

    string GetSuggestedNotebookPath(string title);
}
