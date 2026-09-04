using System.Globalization;
using System.Text;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Live.Designer;

/// <summary>
/// Pins a promoted run to the exact designer document revision that was backtested and approved.
/// </summary>
/// <remarks>
/// Design documents are stored by id and the design repository returns the latest saved revision,
/// so loading by id alone would let an edit made after approval change what a promoted run trades
/// without any new governance. Promotion records <see cref="ParameterKey"/> alongside the document
/// id, and activation recomputes it and refuses on a mismatch.
/// </remarks>
public static class DesignerDocumentRevision
{
    /// <summary>Run parameter carrying the approved document's content hash.</summary>
    public const string ParameterKey = "designerDocumentHash";

    /// <summary>
    /// Canonical hash of everything in a document that changes what it trades.
    /// </summary>
    /// <remarks>
    /// The designer's own <c>datasetFingerprint</c> covers only the dataset reference, universe,
    /// mapped fields, and version - it does not move when a cell's formula, a risk guard, or a
    /// trade cell's sizing changes, which is exactly the drift that matters here.
    /// </remarks>
    public static string ComputeHash(StrategyDesignDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();
        builder.Append(document.DocumentId).Append('\u001f')
            .Append(document.Version).Append('\u001f')
            .Append(document.DatasetReference).Append('\u001f')
            .Append(string.Join(",", document.Universe ?? Array.Empty<string>()))
            .Append('\u001e');

        foreach (var cell in document.Cells ?? Array.Empty<StrategyDesignCell>())
        {
            builder.Append(cell.CellId).Append('\u001f')
                .Append(cell.Kind).Append('\u001f')
                .Append(cell.Purpose).Append('\u001f')
                .Append(cell.Source).Append('\u001f')
                .Append(string.Join(",", cell.FieldRefs ?? Array.Empty<string>())).Append('\u001f');

            // Ordered so an unrelated dictionary ordering change cannot move the hash.
            foreach (var parameter in (cell.Parameters ?? new Dictionary<string, string>())
                .OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                builder.Append(parameter.Key).Append('=').Append(parameter.Value).Append(';');
            }

            builder.Append('\u001e');
        }

        foreach (var transition in document.Transitions ?? Array.Empty<StrategyDesignTransition>())
        {
            builder.Append(transition.TransitionId).Append('\u001f')
                .Append(transition.FromCellId).Append('\u001f')
                .Append(transition.ToCellId).Append('\u001f')
                .Append(transition.Kind).Append('\u001f')
                .Append(transition.Condition).Append('\u001f')
                .Append(transition.MaxIterations?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
                .Append('\u001e');
        }

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }
}
