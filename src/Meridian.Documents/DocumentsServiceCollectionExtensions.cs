using Meridian.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Documents;

/// <summary>
/// Composition helpers that flip the ledger export path from the dependency-free plain-text
/// fallback (<see cref="BuiltInLedgerReportBinaryRenderer"/>) to the client-grade QuestPDF/ClosedXML
/// renderer. A host that calls <see cref="AddFinancialReportDocumentRenderer"/> resolves
/// <see cref="ILedgerReportBinaryRenderer"/> as <see cref="FinancialReportDocumentRenderer"/>, so
/// governed report packs export branded, tabular PDF and multi-sheet XLSX deliverables.
/// </summary>
public static class DocumentsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the QuestPDF/ClosedXML <see cref="ILedgerReportBinaryRenderer"/> as a singleton.
    /// Uses <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton"/> so a host that has
    /// already chosen a renderer keeps its choice.
    /// </summary>
    public static IServiceCollection AddFinancialReportDocumentRenderer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ILedgerReportBinaryRenderer, FinancialReportDocumentRenderer>();
        return services;
    }
}
