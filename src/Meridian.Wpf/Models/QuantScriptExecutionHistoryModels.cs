using System.Text.Json.Serialization;
using Meridian.QuantScript.Documents;

namespace Meridian.Wpf.Models;

public sealed record QuantScriptExecutionMetricRecord(
    string Label,
    string Value,
    string? Category = null);

public sealed record QuantScriptResolvedParameterDescriptorRecord(
    string Name,
    string TypeName,
    string Label,
    string? DefaultValue,
    string? ResolvedValue,
    double Min = double.MinValue,
    double Max = double.MaxValue,
    string? Description = null);

public sealed record QuantScriptExecutionRecord(
    string ExecutionId,
    string DocumentTitle,
    string DocumentPath,
    QuantScriptDocumentKind DocumentKind,
    DateTimeOffset ExecutedAtUtc,
    bool Success,
    Dictionary<string, string> ParameterSnapshot,
    List<QuantScriptResolvedParameterDescriptorRecord> RuntimeParameters,
    string ConsoleExcerpt,
    List<QuantScriptExecutionMetricRecord> Metrics,
    List<string> PlotTitles,
    int CapturedBacktestCount,
    string? MirroredRunId = null,
    string? Warning = null)
{
    [JsonIgnore]
    public string ExecutedAtText => ExecutedAtUtc.LocalDateTime.ToString("g");

    [JsonIgnore]
    public string DocumentKindLabel => DocumentKind == QuantScriptDocumentKind.Notebook ? "Notebook" : "Script";

    [JsonIgnore]
    public string StatusText => Success ? "Success" : "Failed";

    [JsonIgnore]
    public bool HasMirroredRun => !string.IsNullOrWhiteSpace(MirroredRunId);

    [JsonIgnore]
    public string MirroredRunLabel => HasMirroredRun ? MirroredRunId! : "Local only";

    [JsonIgnore]
    public string ConsolePreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ConsoleExcerpt))
                return "No console output";

            var firstLine = ConsoleExcerpt
                .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(firstLine) ? "No console output" : firstLine;
        }
    }

    [JsonIgnore]
    public string ParameterSummary =>
        ParameterSnapshot.Count == 0
            ? "No parameters"
            : string.Join(", ", ParameterSnapshot
                .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .Select(static entry => $"{entry.Key}={entry.Value}"))
              + (ParameterSnapshot.Count > 3 ? "…" : string.Empty);
}

public sealed record QuantScriptTemplateDefinition(
    string Id,
    string Title,
    string Description,
    QuantScriptDocumentKind DocumentKind,
    string ContentFile,
    string Category = "General")
{
    [JsonIgnore]
    public string KindLabel => DocumentKind == QuantScriptDocumentKind.Notebook ? "Notebook" : "Script";
}

public sealed record QuantScriptTemplateCatalogManifest(
    List<QuantScriptTemplateDefinition> Templates);

public sealed record QuantScriptTemplateDocument(
    QuantScriptTemplateDefinition Definition,
    string Source,
    string? Path = null);

public sealed record StrategyRunsNavigationContext(
    string? StrategyId = null,
    string? PrimaryRunId = null,
    string? ComparisonRunId = null,
    bool AutoCompare = false);
