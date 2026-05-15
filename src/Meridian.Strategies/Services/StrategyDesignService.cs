using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

/// <summary>
/// Validates, previews, and compiles browser Strategy Builder design documents.
/// </summary>
public sealed class StrategyDesignService
{
    private const int MaxLoopIterations = 1_000;
    private static readonly string[] ConcurrentCellSemantics = ["all-pass", "any-pass", "first-wins"];
    private static readonly string[] TradeCellInstruments = ["Equity", "Option", "Future", "ETF"];
    private static readonly string[] TradeCellDirections = ["Buy", "Sell", "SellShort", "BuyToCover"];
    private static readonly string[] TradeCellSizingMethods = ["FixedShares", "FixedNotional", "PercentAUM", "EqualWeight"];
    private static readonly string[] TradeCellSizingValueRequiredMethods = ["FixedShares", "FixedNotional", "PercentAUM"];
    private static readonly Regex FieldTokenRegex = new(
        @"\b[A-Z][A-Z0-9_]{2,}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));

    private readonly IReadOnlyList<StrategyDesignFieldCatalogItem> _fieldCatalog = CreateFieldCatalog();
    private readonly IReadOnlyList<StrategyDesignTemplate> _templates;

    public StrategyDesignService()
    {
        _templates = CreateTemplates();
    }

    /// <summary>Returns AMX-style fields mapped into Meridian canonical sources.</summary>
    public IReadOnlyList<StrategyDesignFieldCatalogItem> GetFieldCatalog() => _fieldCatalog;

    /// <summary>Returns built-in Strategy Builder templates.</summary>
    public IReadOnlyList<StrategyDesignTemplate> GetTemplates() => _templates;

    /// <summary>Normalizes a document before validation or persistence.</summary>
    public StrategyDesignDocument Normalize(StrategyDesignDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var now = DateTimeOffset.UtcNow;
        var documentId = string.IsNullOrWhiteSpace(document.DocumentId)
            ? $"strategy-design-{Guid.NewGuid():N}"
            : document.DocumentId.Trim();
        var cells = (document.Cells ?? Array.Empty<StrategyDesignCell>())
            .Where(static cell => cell is not null)
            .Select(NormalizeCell)
            .ToArray();
        var transitions = (document.Transitions ?? Array.Empty<StrategyDesignTransition>())
            .Where(static transition => transition is not null)
            .Select(NormalizeTransition)
            .ToArray();
        var universe = NormalizeStringList(document.Universe);
        var metadata = document.Metadata is null
            ? null
            : document.Metadata
                .Where(static item => !string.IsNullOrWhiteSpace(item.Key))
                .ToDictionary(
                    static item => item.Key.Trim(),
                    static item => item.Value?.Trim() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);

        return document with
        {
            DocumentId = documentId,
            Name = string.IsNullOrWhiteSpace(document.Name) ? "Untitled strategy design" : document.Name.Trim(),
            Description = document.Description?.Trim() ?? string.Empty,
            Version = string.IsNullOrWhiteSpace(document.Version) ? "1" : document.Version.Trim(),
            DatasetReference = document.DatasetReference?.Trim() ?? string.Empty,
            Universe = universe,
            Cells = cells,
            Transitions = transitions,
            Metadata = metadata,
            CreatedAt = document.CreatedAt == default ? now : document.CreatedAt.ToUniversalTime(),
            UpdatedAt = now
        };
    }

    /// <summary>Validates required inputs, field references, and transition graph safety.</summary>
    public StrategyDesignValidationResult Validate(StrategyDesignDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var messages = new List<StrategyDesignValidationMessage>();
        var cells = document.Cells ?? Array.Empty<StrategyDesignCell>();
        var transitions = document.Transitions ?? Array.Empty<StrategyDesignTransition>();
        var cellIds = cells
            .Where(static cell => !string.IsNullOrWhiteSpace(cell.CellId))
            .Select(static cell => cell.CellId)
            .ToHashSet(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(document.Name))
        {
            messages.Add(Error("NameRequired", document.DocumentId, "Strategy design name is required."));
        }

        if (string.IsNullOrWhiteSpace(document.DatasetReference))
        {
            messages.Add(Error("DatasetRequired", document.DocumentId, "A dataset reference is required before preview or backtest."));
        }

        if (document.Universe is null || document.Universe.Count == 0)
        {
            messages.Add(Error("UniverseRequired", document.DocumentId, "At least one symbol or universe selector is required."));
        }

        if (cells.Count == 0)
        {
            messages.Add(Error("CellsRequired", document.DocumentId, "At least one strategy cell is required."));
        }

        var fieldCatalog = _fieldCatalog.ToDictionary(static item => item.FieldId, StringComparer.OrdinalIgnoreCase);
        foreach (var cell in cells)
        {
            if (string.IsNullOrWhiteSpace(cell.CellId))
            {
                messages.Add(Error("CellIdRequired", document.DocumentId, "Every cell must have a stable ID."));
            }

            if (string.IsNullOrWhiteSpace(cell.Label))
            {
                messages.Add(Error("CellLabelRequired", cell.CellId, "Every cell must have an operator-facing label."));
            }

            if (string.IsNullOrWhiteSpace(cell.Source))
            {
                messages.Add(Error("CellSourceRequired", cell.CellId, $"{SafeLabel(cell)} needs a formula, visual rule, or code source."));
            }

            foreach (var fieldRef in ResolveFieldRefs(cell))
            {
                if (!fieldCatalog.TryGetValue(fieldRef, out var field))
                {
                    messages.Add(Error("UnknownField", cell.CellId, $"{SafeLabel(cell)} references unknown field {fieldRef}."));
                    continue;
                }

                if (!field.IsEnabled)
                {
                    messages.Add(Error(
                        "DisabledField",
                        cell.CellId,
                        $"{fieldRef} is visible in the field catalog but disabled: {field.DisabledReason}"));
                }
            }

            messages.AddRange(ValidateCellKindParameters(cell, cellIds));
        }

        var cellOrder = cells
            .Select((cell, index) => new { cell.CellId, Index = index })
            .Where(static item => !string.IsNullOrWhiteSpace(item.CellId))
            .ToDictionary(static item => item.CellId, static item => item.Index, StringComparer.Ordinal);

        foreach (var transition in transitions)
        {
            var hasFrom = cellOrder.TryGetValue(transition.FromCellId, out var fromIndex);
            var hasTo = cellOrder.TryGetValue(transition.ToCellId, out var toIndex);
            if (!hasFrom)
            {
                messages.Add(Error("TransitionSourceMissing", transition.TransitionId, $"Transition {transition.TransitionId} starts from a missing cell."));
            }

            if (!hasTo)
            {
                messages.Add(Error("TransitionTargetMissing", transition.TransitionId, $"Transition {transition.TransitionId} points to a missing cell."));
            }

            if (!hasFrom || !hasTo)
            {
                continue;
            }

            var isLoop = IsLoopTransition(transition, fromIndex, toIndex);
            if (isLoop && (transition.MaxIterations is null or < 1 or > MaxLoopIterations))
            {
                messages.Add(Error(
                    "LoopGuardRequired",
                    transition.TransitionId,
                    $"Loop transition {transition.TransitionId} must define 1-{MaxLoopIterations} max iterations."));
            }

            if (isLoop && string.IsNullOrWhiteSpace(transition.Rationale))
            {
                messages.Add(Error(
                    "LoopRationaleRequired",
                    transition.TransitionId,
                    $"Loop transition {transition.TransitionId} must explain why the loop is bounded."));
            }
        }

        var hasRiskGuard = cells.Any(static cell =>
            string.Equals(cell.Purpose, "risk", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cell.Kind, "governance", StringComparison.OrdinalIgnoreCase));
        if (!hasRiskGuard && cells.Count > 0)
        {
            messages.Add(Warning("RiskGuardRecommended", document.DocumentId, "Add a risk or governance cell before promotion review."));
        }

        var hasErrors = messages.Any(static message => string.Equals(message.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var summary = hasErrors
            ? $"{messages.Count(static message => string.Equals(message.Severity, "error", StringComparison.OrdinalIgnoreCase))} blocking issue(s)"
            : messages.Count == 0
                ? "Ready for preview and backtest"
                : $"{messages.Count} advisory issue(s)";

        return new StrategyDesignValidationResult(!hasErrors, summary, messages);
    }

    /// <summary>Builds compile preview rows and trace without executing the design.</summary>
    public StrategyDesignPreviewResult Preview(StrategyDesignDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var validation = Validate(document);
        var compiled = Compile(document);
        var rows = BuildPreviewRows(document).ToArray();
        var trace = BuildRunTrace(document, validation).ToArray();
        return new StrategyDesignPreviewResult(validation, compiled, rows, trace);
    }

    /// <summary>Compiles a validated design into QuantScript source.</summary>
    public StrategyDesignCompiledScript Compile(StrategyDesignDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var catalog = _fieldCatalog.ToDictionary(static item => item.FieldId, StringComparer.OrdinalIgnoreCase);
        var fieldRefs = document.Cells
            .SelectMany(ResolveFieldRefs)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var disabledFieldRefs = fieldRefs
            .Where(fieldRef => catalog.TryGetValue(fieldRef, out var field) && !field.IsEnabled)
            .ToArray();
        var fingerprint = ComputeDatasetFingerprint(document, fieldRefs);
        var symbols = document.Universe is { Count: > 0 }
            ? string.Join(", ", document.Universe)
            : "(empty universe)";
        var fieldList = fieldRefs.Length > 0 ? string.Join(", ", fieldRefs) : "(no mapped fields)";
        var cellList = string.Join(
            Environment.NewLine,
            document.Cells.Select(cell => $"Print(\"Cell: {EscapeCSharpString(cell.Label)} [{EscapeCSharpString(cell.Purpose)}]\");"));

        var source = $$"""
            // Generated by Meridian Strategy Builder. Prototype requirement source:
            // stock-strategy-build commit 2826afc0dc5ca4590bf0860871acd45933088bad.
            var strategyId = "{{EscapeCSharpString(document.DocumentId)}}";
            var strategyName = "{{EscapeCSharpString(document.Name)}}";
            var datasetFingerprint = "{{EscapeCSharpString(fingerprint)}}";
            Print($"Strategy Builder run: {strategyName} ({strategyId})");
            Print("Universe: {{EscapeCSharpString(symbols)}}");
            Print("Fields: {{EscapeCSharpString(fieldList)}}");
            Print("Dataset fingerprint: {{EscapeCSharpString(fingerprint)}}");
            PrintMetric("Designer cells", {{document.Cells.Count}});
            PrintMetric("Mapped fields", {{fieldRefs.Length}});
            PrintMetric("Universe size", {{document.Universe?.Count ?? 0}});
            {{cellList}}
            """;

        return new StrategyDesignCompiledScript(source, fingerprint, fieldRefs, disabledFieldRefs);
    }

    /// <summary>Builds a stable summary for draft list views.</summary>
    public static StrategyDesignDraftSummary CreateDraftSummary(StrategyDesignDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var cellCount = document.Cells?.Count ?? 0;
        var transitionCount = document.Transitions?.Count ?? 0;
        var validationSummary = cellCount == 0
            ? "Draft has no cells"
            : $"{cellCount} cell(s), {transitionCount} transition(s)";
        return new StrategyDesignDraftSummary(
            document.DocumentId,
            document.Name,
            document.Version,
            document.DatasetReference,
            cellCount,
            transitionCount,
            document.UpdatedAt,
            validationSummary);
    }

    /// <summary>Builds deterministic trace rows used by preview and run responses.</summary>
    public IReadOnlyList<StrategyDesignRunTraceEntry> BuildRunTrace(
        StrategyDesignDocument document,
        StrategyDesignValidationResult validation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(validation);

        var trace = new List<StrategyDesignRunTraceEntry>
        {
            new(
                "validate",
                "Validate design",
                validation.IsValid ? "complete" : "blocked",
                validation.Summary,
                OccurredAt: DateTimeOffset.UtcNow)
        };

        trace.AddRange(document.Cells.Select(cell => new StrategyDesignRunTraceEntry(
            $"cell-{cell.CellId}",
            cell.Label,
            validation.Messages.Any(message =>
                string.Equals(message.TargetId, cell.CellId, StringComparison.Ordinal) &&
                string.Equals(message.Severity, "error", StringComparison.OrdinalIgnoreCase))
                ? "blocked"
                : "ready",
            BuildCellTraceDetail(cell),
            cell.CellId)));

        trace.Add(new StrategyDesignRunTraceEntry(
            "quant-script",
            "Compile QuantScript request",
            validation.IsValid ? "ready" : "blocked",
            validation.IsValid
                ? "Generated source is ready for the Quant Lab/backtest runner."
                : "Fix validation errors before execution.",
            OccurredAt: DateTimeOffset.UtcNow));

        return trace;
    }

    private IEnumerable<StrategyDesignPreviewRow> BuildPreviewRows(StrategyDesignDocument document)
    {
        var catalog = _fieldCatalog.ToDictionary(static item => item.FieldId, StringComparer.OrdinalIgnoreCase);
        foreach (var cell in document.Cells ?? Array.Empty<StrategyDesignCell>())
        {
            var refs = ResolveFieldRefs(cell).ToArray();
            var disabledRefs = refs
                .Where(fieldRef => catalog.TryGetValue(fieldRef, out var field) && !field.IsEnabled)
                .ToArray();
            var unknownRefs = refs
                .Where(fieldRef => !catalog.ContainsKey(fieldRef))
                .ToArray();
            var status = disabledRefs.Length > 0 || unknownRefs.Length > 0 ? "blocked" : "ready";
            var detail = status == "ready"
                ? $"{cell.Kind} cell compiles into the {cell.Purpose} stage."
                : $"Resolve {disabledRefs.Length + unknownRefs.Length} field reference issue(s).";
            yield return new StrategyDesignPreviewRow(
                $"preview-{cell.CellId}",
                cell.CellId,
                cell.Label,
                cell.Purpose,
                status,
                refs,
                detail);
        }
    }

    private static StrategyDesignCell NormalizeCell(StrategyDesignCell cell)
    {
        var normalizedKind = string.IsNullOrWhiteSpace(cell.Kind) ? "formula" : cell.Kind.Trim().ToLowerInvariant();
        var normalizedRefs = NormalizeStringList(cell.FieldRefs)
            .Select(static field => field.ToUpperInvariant())
            .ToArray();
        if (normalizedRefs.Length == 0 && !string.IsNullOrWhiteSpace(cell.Source))
        {
            normalizedRefs = ExtractSourceFieldRefs(cell.Source).ToArray();
        }

        var normalizedParameters = cell.Parameters is null
            ? null
            : NormalizeCellParameters(
                normalizedKind,
                cell.Parameters
                    .Where(static item => !string.IsNullOrWhiteSpace(item.Key))
                    .ToDictionary(
                        static item => item.Key.Trim(),
                        static item => item.Value?.Trim() ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase));

        return cell with
        {
            CellId = string.IsNullOrWhiteSpace(cell.CellId) ? $"cell-{Guid.NewGuid():N}" : cell.CellId.Trim(),
            Label = string.IsNullOrWhiteSpace(cell.Label) ? "Untitled cell" : cell.Label.Trim(),
            Kind = normalizedKind,
            Purpose = string.IsNullOrWhiteSpace(cell.Purpose) ? "filter" : cell.Purpose.Trim().ToLowerInvariant(),
            Source = cell.Source?.Trim() ?? string.Empty,
            FieldRefs = normalizedRefs,
            Parameters = normalizedParameters,
            DisabledReason = string.IsNullOrWhiteSpace(cell.DisabledReason) ? null : cell.DisabledReason.Trim()
        };
    }

    private static StrategyDesignTransition NormalizeTransition(StrategyDesignTransition transition)
        => transition with
        {
            TransitionId = string.IsNullOrWhiteSpace(transition.TransitionId)
                ? $"transition-{Guid.NewGuid():N}"
                : transition.TransitionId.Trim(),
            FromCellId = transition.FromCellId?.Trim() ?? string.Empty,
            ToCellId = transition.ToCellId?.Trim() ?? string.Empty,
            Kind = string.IsNullOrWhiteSpace(transition.Kind) ? "next" : transition.Kind.Trim().ToLowerInvariant(),
            Condition = transition.Condition?.Trim() ?? string.Empty,
            Rationale = string.IsNullOrWhiteSpace(transition.Rationale) ? null : transition.Rationale.Trim()
        };

    private static IReadOnlyList<string> ResolveFieldRefs(StrategyDesignCell cell)
    {
        var refs = cell.FieldRefs is { Count: > 0 }
            ? NormalizeStringList(cell.FieldRefs).Select(static item => item.ToUpperInvariant()).ToArray()
            : ExtractSourceFieldRefs(cell.Source).ToArray();

        if (!string.Equals(cell.Kind, "universe-builder", StringComparison.OrdinalIgnoreCase) || cell.Parameters is null)
        {
            return refs;
        }

        var includeRules = cell.Parameters.TryGetValue("includeRules", out var include) ? include : null;
        var excludeRules = cell.Parameters.TryGetValue("excludeRules", out var exclude) ? exclude : null;

        return refs
            .Concat(ExtractSourceFieldRefs(includeRules))
            .Concat(ExtractSourceFieldRefs(excludeRules))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractSourceFieldRefs(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return [];
        }

        return FieldTokenRegex
            .Matches(source)
            .Select(static match => match.Value.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeStringList(IReadOnlyList<string>? values)
        => values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    private static Dictionary<string, string> NormalizeCellParameters(string cellKind, Dictionary<string, string> parameters)
    {
        if (parameters.Count == 0)
        {
            return parameters;
        }

        if (string.Equals(cellKind, "concurrent", StringComparison.OrdinalIgnoreCase) &&
            parameters.TryGetValue("semantics", out var semantics) &&
            TryGetCanonicalValue(semantics, ConcurrentCellSemantics, out var canonicalSemantics))
        {
            parameters["semantics"] = canonicalSemantics;
        }

        if (string.Equals(cellKind, "trade", StringComparison.OrdinalIgnoreCase))
        {
            if (parameters.TryGetValue("instrument", out var instrument) &&
                TryGetCanonicalValue(instrument, TradeCellInstruments, out var canonicalInstrument))
            {
                parameters["instrument"] = canonicalInstrument;
            }

            if (parameters.TryGetValue("direction", out var direction) &&
                TryGetCanonicalValue(direction, TradeCellDirections, out var canonicalDirection))
            {
                parameters["direction"] = canonicalDirection;
            }

            if (parameters.TryGetValue("sizingMethod", out var sizingMethod) &&
                TryGetCanonicalValue(sizingMethod, TradeCellSizingMethods, out var canonicalSizingMethod))
            {
                parameters["sizingMethod"] = canonicalSizingMethod;
            }
        }

        return parameters;
    }

    private static bool TryGetCanonicalValue(string? value, IReadOnlyList<string> validValues, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        canonical = validValues.FirstOrDefault(candidate => string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return canonical.Length > 0;
    }

    private static bool IsLoopTransition(StrategyDesignTransition transition, int fromIndex, int toIndex)
        => string.Equals(transition.Kind, "loop", StringComparison.OrdinalIgnoreCase)
           || fromIndex >= toIndex
           || string.Equals(transition.FromCellId, transition.ToCellId, StringComparison.Ordinal);

    private static StrategyDesignValidationMessage Error(string code, string targetId, string message)
        => new(code, "error", targetId, message);

    private static StrategyDesignValidationMessage Warning(string code, string targetId, string message)
        => new(code, "warning", targetId, message);

    private static string SafeLabel(StrategyDesignCell cell)
        => string.IsNullOrWhiteSpace(cell.Label) ? cell.CellId : cell.Label;

    private static IEnumerable<StrategyDesignValidationMessage> ValidateCellKindParameters(StrategyDesignCell cell, IReadOnlySet<string> cellIds)
    {
        var label = SafeLabel(cell);
        var p = cell.Parameters;

        switch (cell.Kind)
        {
            case "state":
                if (p is null || !p.TryGetValue("exitCondition", out var exitCond) || string.IsNullOrWhiteSpace(exitCond))
                    yield return Error("StateCellExitRequired", cell.CellId, $"{label} (state) must define an exitCondition parameter.");

                if (p is null || !p.TryGetValue("stateLabel", out var stateLbl) || string.IsNullOrWhiteSpace(stateLbl))
                    yield return Error("StateCellLabelRequired", cell.CellId, $"{label} (state) must define a stateLabel parameter.");
                break;

            case "concurrent":
                var branchIds = p is null || !p.TryGetValue("branchIds", out var branches)
                    ? []
                    : branches
                        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .ToArray();

                if (branchIds.Length == 0)
                {
                    yield return Error("ConcurrentCellBranchesRequired", cell.CellId, $"{label} (concurrent) must define a branchIds parameter.");
                }
                else
                {
                    var missingBranches = branchIds
                        .Where(branchId => !cellIds.Contains(branchId))
                        .ToArray();
                    if (missingBranches.Length > 0)
                    {
                        yield return Error(
                            "ConcurrentCellBranchMissing",
                            cell.CellId,
                            $"{label} (concurrent) references missing branch cell IDs: {string.Join(", ", missingBranches)}.");
                    }
                }

                if (p is null || !p.TryGetValue("semantics", out var sem) || !ConcurrentCellSemantics.Contains(sem, StringComparer.OrdinalIgnoreCase))
                    yield return Error("ConcurrentCellSemanticsRequired", cell.CellId, $"{label} (concurrent) semantics must be one of: all-pass, any-pass, first-wins.");
                break;

            case "universe-builder":
                if (p is null || !p.TryGetValue("assetClass", out var assetClass) || string.IsNullOrWhiteSpace(assetClass))
                    yield return Error("UniverseBuilderAssetClassRequired", cell.CellId, $"{label} (universe-builder) must define an assetClass parameter.");

                if (p is null || !p.TryGetValue("includeRules", out var includeRules) || string.IsNullOrWhiteSpace(includeRules))
                    yield return Error("UniverseBuilderIncludeRulesRequired", cell.CellId, $"{label} (universe-builder) must define at least one includeRules parameter.");

                if (p is not null)
                {
                    var hasMinSize = p.TryGetValue("minSize", out var minSizeRaw) && !string.IsNullOrWhiteSpace(minSizeRaw);
                    var hasMaxSize = p.TryGetValue("maxSize", out var maxSizeRaw) && !string.IsNullOrWhiteSpace(maxSizeRaw);

                    var minSizeIsValid = !hasMinSize || (decimal.TryParse(minSizeRaw, out var minSize) && minSize >= 0);
                    var maxSizeIsValid = !hasMaxSize || (decimal.TryParse(maxSizeRaw, out var maxSize) && maxSize >= 0);

                    if (!minSizeIsValid)
                    {
                        yield return Error("UniverseBuilderMinSizeInvalid", cell.CellId, $"{label} (universe-builder) minSize must be a non-negative number.");
                    }

                    if (!maxSizeIsValid)
                    {
                        yield return Error("UniverseBuilderMaxSizeInvalid", cell.CellId, $"{label} (universe-builder) maxSize must be a non-negative number.");
                    }

                    if (hasMinSize && hasMaxSize && minSizeIsValid && maxSizeIsValid)
                    {
                        decimal.TryParse(minSizeRaw, out var parsedMinSize);
                        decimal.TryParse(maxSizeRaw, out var parsedMaxSize);
                        if (parsedMinSize > parsedMaxSize)
                        {
                            yield return Error("UniverseBuilderSizeRangeInvalid", cell.CellId, $"{label} (universe-builder) minSize cannot be greater than maxSize.");
                        }
                    }
                }
                break;

            case "trade":
                if (p is null || !p.TryGetValue("instrument", out var instr) || !TradeCellInstruments.Contains(instr, StringComparer.OrdinalIgnoreCase))
                    yield return Error("TradeCellInstrumentRequired", cell.CellId, $"{label} (trade) instrument must be Equity, Option, Future, or ETF.");

                if (p is null || !p.TryGetValue("direction", out var dir) || !TradeCellDirections.Contains(dir, StringComparer.OrdinalIgnoreCase))
                    yield return Error("TradeCellDirectionRequired", cell.CellId, $"{label} (trade) direction must be Buy, Sell, SellShort, or BuyToCover.");

                if (p is null || !p.TryGetValue("sizingMethod", out var sizing) || !TradeCellSizingMethods.Contains(sizing, StringComparer.OrdinalIgnoreCase))
                {
                    yield return Error("TradeCellSizingRequired", cell.CellId, $"{label} (trade) sizingMethod must be FixedShares, FixedNotional, PercentAUM, or EqualWeight.");
                }
                else if (TradeCellSizingValueRequiredMethods.Contains(sizing, StringComparer.OrdinalIgnoreCase))
                {
                    var hasSizingValue = p.TryGetValue("sizingValue", out var sizingValueRaw) && !string.IsNullOrWhiteSpace(sizingValueRaw);
                    if (!hasSizingValue || !decimal.TryParse(sizingValueRaw, out _))
                    {
                        yield return Error("TradeCellSizingValueRequired", cell.CellId, $"{label} (trade) sizingValue must be numeric when sizingMethod is {sizing}.");
                    }
                }
                break;
        }
    }

    private static string BuildCellTraceDetail(StrategyDesignCell cell)
    {
        var refs = ResolveFieldRefs(cell);
        var refsLabel = refs.Count == 0 ? "no catalog fields" : string.Join(", ", refs);
        var p = cell.Parameters;
        return cell.Kind switch
        {
            "state" => $"State '{p?.GetValueOrDefault("stateLabel", cell.CellId)}' exits on: {p?.GetValueOrDefault("exitCondition", "(none)")}.",
            "concurrent" => $"Concurrent evaluation ({p?.GetValueOrDefault("semantics", "?")}) across: {p?.GetValueOrDefault("branchIds", "(none)")}.",
            "universe-builder" => $"Builds {p?.GetValueOrDefault("assetClass", "?")} universe using {refsLabel}.",
            "trade" => $"Trade intent: {p?.GetValueOrDefault("direction", "?")} {p?.GetValueOrDefault("instrument", "?")} via {p?.GetValueOrDefault("sizingMethod", "?")}.",
            _ => refs.Count == 0
                ? $"{cell.Kind} cell runs without catalog fields."
                : $"{cell.Kind} cell uses {refsLabel}."
        };
    }

    private static string ComputeDatasetFingerprint(StrategyDesignDocument document, IReadOnlyList<string> fieldRefs)
    {
        var seed = string.Join(
            "|",
            document.DatasetReference,
            string.Join(",", document.Universe ?? Array.Empty<string>()),
            string.Join(",", fieldRefs),
            document.Version);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        return $"strategy-design:{hash[..16]}";
    }

    private static string EscapeCSharpString(string? value)
        => (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private IReadOnlyList<StrategyDesignTemplate> CreateTemplates()
    {
        var now = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);
        return
        [
            new(
                "investment-grade-income",
                "Investment-grade income screen",
                "Builds a fixed-income universe, filters credit quality and duration, then ranks carry.",
                "Fixed income",
                "stock-strategy-build: PRD field catalog, visual cells, run trace",
                ["fixed-income", "field-catalog", "rank"],
                Normalize(new StrategyDesignDocument(
                    "investment-grade-income",
                    "Investment-grade income screen",
                    "Template rebuilt from the stock-strategy-build AMX-style field catalog.",
                    "1",
                    "security-master/fixed-income + provider-bars/daily",
                    ["AGG", "LQD", "VCIT"],
                    [
                        new("universe", "Investment-grade bond universe", "visual", "universe", "AssetClass == FixedIncome && RATING >= BBB", ["CUSIP", "RATING", "MATURITY"], null),
                        new("carry-rank", "Carry rank", "formula", "rank", "(YIELD - SPREAD) / max(DURATION, 1)", ["YIELD", "SPREAD", "DURATION"], null),
                        new("risk-guard", "Duration and spread guard", "governance", "risk", "DURATION <= 7 && SPREAD <= 250", ["DURATION", "SPREAD"], null),
                        new("backtest-proof", "Backtest proof", "code", "backtest", "rebalance weekly by carry-rank; record run trace", ["PRICE"], null)
                    ],
                    [
                        new("t1", "universe", "carry-rank", "next", "universe loaded"),
                        new("t2", "carry-rank", "risk-guard", "next", "rank complete"),
                        new("t3", "risk-guard", "backtest-proof", "next", "risk accepted")
                    ],
                    new Dictionary<string, string> { ["prototypeCommit"] = "2826afc0dc5ca4590bf0860871acd45933088bad" },
                    now,
                    now))),
            new(
                "equity-momentum-breakout",
                "Equity momentum breakout",
                "Universe, momentum formula, governance guard, and proof run for liquid equities.",
                "Equities",
                "stock-strategy-build: formula autocomplete, transitions, backtest proof",
                ["equity", "formula", "backtest"],
                Normalize(new StrategyDesignDocument(
                    "equity-momentum-breakout",
                    "Equity momentum breakout",
                    "Template showing formula autocomplete and run trace concepts.",
                    "1",
                    "provider-bars/equities/daily",
                    ["SPY", "QQQ", "AAPL", "MSFT"],
                    [
                        new("liquid-universe", "Liquid equity universe", "visual", "universe", "PRICE > 20 && VOLUME_AVG_20D > 1000000", ["PRICE", "VOLUME_AVG_20D"], null),
                        new("momentum-score", "Momentum score", "formula", "rank", "MOMENTUM_63D - VOLATILITY_20D", ["MOMENTUM_63D", "VOLATILITY_20D"], null),
                        new("rebalance-loop", "Weekly rebalance loop", "code", "backtest", "rebalance every Friday with max 20 names", ["PRICE"], null),
                        new("governance-pack", "Governance packet", "governance", "governance", "record dataset fingerprint, field list, run trace", ["PORTFOLIO_WEIGHT"], null)
                    ],
                    [
                        new("t1", "liquid-universe", "momentum-score", "next", "universe ready"),
                        new("t2", "momentum-score", "rebalance-loop", "next", "rank complete"),
                        new("t3", "rebalance-loop", "momentum-score", "loop", "weekly rebalance", 260, "One pass per trading week over the requested backtest window."),
                        new("t4", "rebalance-loop", "governance-pack", "next", "run complete")
                    ],
                    new Dictionary<string, string> { ["prototypeCommit"] = "2826afc0dc5ca4590bf0860871acd45933088bad" },
                    now,
                    now))),
            new(
                "options-payoff",
                "Options payoff template",
                "Carries the existing options-leg payoff tool into the broader Strategy Builder.",
                "Options",
                "Meridian existing strategy-designer payoff tool plus stock-strategy-build templates concept",
                ["options", "payoff", "template"],
                Normalize(new StrategyDesignDocument(
                    "options-payoff",
                    "Options payoff template",
                    "Template preserving the current option-leg payoff panel inside the Strategy Builder.",
                    "1",
                    "options-chain + provider-quotes",
                    ["SPY"],
                    [
                        new("option-context", "Option chain context", "visual", "universe", "underlying == SPY && expiry <= 45d", ["OPTION_EXPIRATION", "OPTION_DELTA", "PRICE"], null),
                        new("payoff-panel", "Options payoff panel", "visual", "options-payoff", "bull call spread payoff sample", ["OPTION_DELTA", "PRICE"], null),
                        new("review-proof", "Review proof", "governance", "governance", "attach payoff, run trace, and no-live-trading acknowledgement", ["PORTFOLIO_WEIGHT"], null)
                    ],
                    [
                        new("t1", "option-context", "payoff-panel", "next", "option context ready"),
                        new("t2", "payoff-panel", "review-proof", "next", "payoff reviewed")
                    ],
                    new Dictionary<string, string> { ["prototypeCommit"] = "2826afc0dc5ca4590bf0860871acd45933088bad" },
                    now,
                    now))),
            new(
                "state-machine-strategy",
                "State machine strategy",
                "Named execution states with entry/exit conditions — model a strategy as a lifecycle state machine.",
                "Advanced",
                "Meridian extended cell kinds",
                ["state", "lifecycle", "conditional"],
                Normalize(new StrategyDesignDocument(
                    "state-machine-strategy",
                    "State machine strategy",
                    "Strategy execution modelled as a state machine with named checkpoints.",
                    "1",
                    "provider-bars/equities/daily",
                    ["SPY", "QQQ"],
                    [
                        new("watching-state", "Watching", "state", "state", "initial monitoring phase", ["MOMENTUM_63D"],
                            new Dictionary<string, string> { ["stateLabel"] = "Watching", ["exitCondition"] = "MOMENTUM_63D > 0.05", ["entryCondition"] = "PORTFOLIO_WEIGHT == 0", ["isTerminal"] = "false" }),
                        new("holding-state", "Holding", "state", "state", "active position phase", ["MOMENTUM_63D", "VOLATILITY_20D"],
                            new Dictionary<string, string> { ["stateLabel"] = "Holding", ["exitCondition"] = "MOMENTUM_63D < 0 || VOLATILITY_20D > 0.35", ["isTerminal"] = "false" }),
                        new("exiting-state", "Exiting", "state", "state", "position wind-down phase", ["PORTFOLIO_WEIGHT"],
                            new Dictionary<string, string> { ["stateLabel"] = "Exiting", ["exitCondition"] = "PORTFOLIO_WEIGHT == 0", ["isTerminal"] = "true" }),
                        new("state-risk-guard", "State risk guard", "governance", "risk", "VOLATILITY_20D < 0.40", ["VOLATILITY_20D"], null)
                    ],
                    [
                        new("t1", "watching-state", "holding-state", "next", "exit: MOMENTUM_63D > 0.05"),
                        new("t2", "holding-state", "exiting-state", "next", "exit: MOMENTUM_63D < 0 || VOLATILITY_20D > 0.35"),
                        new("t3", "exiting-state", "state-risk-guard", "next", "position closed")
                    ],
                    null,
                    now,
                    now))),
            new(
                "concurrent-branch-strategy",
                "Concurrent branch strategy",
                "Parallel evaluation point — multiple signal branches run simultaneously with configurable pass semantics.",
                "Advanced",
                "Meridian extended cell kinds",
                ["concurrent", "parallel", "multi-signal"],
                Normalize(new StrategyDesignDocument(
                    "concurrent-branch-strategy",
                    "Concurrent branch strategy",
                    "Parallel signal evaluation: equity momentum and volatility screens run concurrently.",
                    "1",
                    "provider-bars/equities/daily",
                    ["SPY", "QQQ", "AAPL"],
                    [
                        new("momentum-branch", "Momentum signal", "formula", "filter", "MOMENTUM_63D > 0.05", ["MOMENTUM_63D"], null),
                        new("volatility-branch", "Volatility filter", "formula", "filter", "VOLATILITY_20D < 0.30", ["VOLATILITY_20D"], null),
                        new("concurrent-gate", "Concurrent signal gate", "concurrent", "concurrent", "parallel branch evaluation", [],
                            new Dictionary<string, string> { ["branchIds"] = "momentum-branch,volatility-branch", ["semantics"] = "all-pass" }),
                        new("concurrent-risk", "Risk guard", "governance", "risk", "all concurrent branches satisfied", ["PORTFOLIO_WEIGHT"], null)
                    ],
                    [
                        new("t1", "momentum-branch", "concurrent-gate", "next", "branch ready"),
                        new("t2", "volatility-branch", "concurrent-gate", "next", "branch ready"),
                        new("t3", "concurrent-gate", "concurrent-risk", "next", "gate resolved")
                    ],
                    null,
                    now,
                    now))),
            new(
                "structured-universe-strategy",
                "Structured universe strategy",
                "Explicit asset class, include/exclude rules, and size constraints for disciplined universe construction.",
                "Universe",
                "Meridian extended cell kinds",
                ["universe-builder", "structured", "equity"],
                Normalize(new StrategyDesignDocument(
                    "structured-universe-strategy",
                    "Structured universe strategy",
                    "Universe-builder cell with explicit asset class rules and size bounds.",
                    "1",
                    "provider-bars/equities/daily",
                    ["SPY"],
                    [
                        new("equity-universe", "Liquid equity universe", "universe-builder", "universe", "structured universe definition", ["PRICE", "MOMENTUM_63D", "VOLATILITY_20D"],
                            new Dictionary<string, string> { ["assetClass"] = "Equity", ["includeRules"] = "PRICE > 10, MOMENTUM_63D > 0", ["excludeRules"] = "VOLATILITY_20D > 0.40", ["minSize"] = "20", ["maxSize"] = "100" }),
                        new("universe-rank", "Momentum rank", "formula", "rank", "MOMENTUM_63D - VOLATILITY_20D", ["MOMENTUM_63D", "VOLATILITY_20D"], null),
                        new("universe-risk", "Universe risk guard", "governance", "risk", "VOLATILITY_20D < 0.30", ["VOLATILITY_20D"], null)
                    ],
                    [
                        new("t1", "equity-universe", "universe-rank", "next", "universe built"),
                        new("t2", "universe-rank", "universe-risk", "next", "rank complete")
                    ],
                    null,
                    now,
                    now))),
            new(
                "trade-intent-strategy",
                "Trade intent strategy",
                "Explicit trade definition cell — instrument type, direction, sizing method, and price constraint.",
                "Execution",
                "Meridian extended cell kinds",
                ["trade", "execution", "sizing"],
                Normalize(new StrategyDesignDocument(
                    "trade-intent-strategy",
                    "Trade intent strategy",
                    "Strategy with an explicit trade cell defining instrument, direction, and sizing.",
                    "1",
                    "provider-bars/equities/daily",
                    ["SPY", "QQQ"],
                    [
                        new("trade-universe", "Liquid equity filter", "visual", "universe", "PRICE > 20", ["PRICE"], null),
                        new("trade-rank", "Momentum score", "formula", "rank", "MOMENTUM_63D - VOLATILITY_20D", ["MOMENTUM_63D", "VOLATILITY_20D"], null),
                        new("buy-equities", "Buy equities", "trade", "trade", "execute buy order on ranked universe", [],
                            new Dictionary<string, string> { ["instrument"] = "Equity", ["direction"] = "Buy", ["sizingMethod"] = "EqualWeight", ["priceConstraint"] = "VWAP", ["sizingValue"] = "0.05" }),
                        new("trade-risk", "Execution risk guard", "governance", "risk", "VOLATILITY_20D < 0.30 && PORTFOLIO_WEIGHT <= 0.10", ["VOLATILITY_20D", "PORTFOLIO_WEIGHT"], null)
                    ],
                    [
                        new("t1", "trade-universe", "trade-rank", "next", "universe ready"),
                        new("t2", "trade-rank", "buy-equities", "next", "rank complete"),
                        new("t3", "buy-equities", "trade-risk", "next", "trade submitted")
                    ],
                    null,
                    now,
                    now)))
        ];
    }

    private static IReadOnlyList<StrategyDesignFieldCatalogItem> CreateFieldCatalog()
        =>
        [
            new("PRICE", "Price", "Provider historical bars / live quotes", "market-data", "decimal", "Canonical last/close price resolved through Meridian providers.", true, null, ["close", "last", "bar.close"]),
            new("VOLUME_AVG_20D", "20-day average volume", "Provider historical bars", "market-data", "decimal", "Rolling liquidity estimate derived from historical bars.", true, null, ["volume", "liquidity"]),
            new("MOMENTUM_63D", "63-day momentum", "Provider historical bars", "market-data", "decimal", "Return over the last 63 trading sessions.", true, null, ["return", "trend"]),
            new("VOLATILITY_20D", "20-day volatility", "Provider historical bars", "market-data", "decimal", "Realized volatility over the last 20 trading sessions.", true, null, ["risk", "stdev"]),
            new("CUSIP", "CUSIP", "Security Master", "security-master", "string", "Fixed-income identifier from the Security Master.", true, null, ["identifier", "bond"]),
            new("MATURITY", "Maturity", "Security Master fixed-income reference", "security-master", "date", "Bond maturity date.", true, null, ["duration", "bond"]),
            new("COUPON", "Coupon", "Security Master fixed-income reference", "security-master", "decimal", "Stated fixed-income coupon.", true, null, ["bond", "income"]),
            new("YIELD", "Yield", "Security Master fixed-income reference", "security-master", "decimal", "Mapped yield input for carry screens.", true, null, ["carry", "income"]),
            new("SPREAD", "Spread", "Security Master fixed-income reference", "security-master", "decimal", "Mapped spread input for credit screens.", true, null, ["credit", "oas"]),
            new("RATING", "Rating", "Security Master fixed-income reference", "security-master", "string", "Normalized rating bucket.", true, null, ["credit", "quality"]),
            new("DURATION", "Duration", "Security Master fixed-income reference", "security-master", "decimal", "Effective duration input.", true, null, ["interest-rate", "risk"]),
            new("OPTION_DELTA", "Option delta", "Options chain", "options", "decimal", "Option-chain greeks mapped through Meridian option reference data.", true, null, ["greeks", "options"]),
            new("OPTION_EXPIRATION", "Option expiration", "Options chain", "options", "date", "Expiration date from the Meridian options chain.", true, null, ["expiry", "options"]),
            new("PORTFOLIO_WEIGHT", "Portfolio weight", "Portfolio/run data", "portfolio", "decimal", "Weight from portfolio snapshots and strategy run data.", true, null, ["allocation", "portfolio"]),
            new("LEDGER_CASH", "Ledger cash", "Ledger", "ledger", "decimal", "Cash balance from Meridian ledger projections.", true, null, ["cash", "accounting"]),
            new("AMX_PRIVATE_SCORE", "AMX private score", "Unmapped AMX vocabulary", "unmapped", "decimal", "Prototype-only score kept visible for traceability.", false, "No Meridian canonical source is wired for AMX_PRIVATE_SCORE in v1.", ["amx", "private"]),
            new("AMX_LIVE_TAX_LOT", "AMX live tax lot", "Unmapped AMX vocabulary", "unmapped", "object", "Prototype-only tax-lot field kept visible for traceability.", false, "Tax-lot ingest is not mapped into the Strategy Builder v1 canonical data set.", ["amx", "tax"])
        ];
}
