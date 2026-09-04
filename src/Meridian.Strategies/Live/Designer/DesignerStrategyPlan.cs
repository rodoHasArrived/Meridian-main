using System.Globalization;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Live.Designer;

/// <summary>Entry side a designer trade cell opens.</summary>
internal enum DesignerTradeSide
{
    Long,
    Short
}

/// <summary>How a designer trade cell sizes each position.</summary>
internal enum DesignerSizingMethod
{
    FixedShares,
    FixedNotional,
    PercentAum,
    EqualWeight
}

/// <summary>A named, compiled boolean gate traced back to the cell that produced it.</summary>
internal sealed record DesignerGate(string CellId, string Label, DesignerExpression Expression);

/// <summary>The single executable trade intent a designer document carries.</summary>
internal sealed record DesignerTradeIntent(
    string CellId,
    string Label,
    DesignerTradeSide Side,
    DesignerSizingMethod SizingMethod,
    decimal SizingValue);

/// <summary>
/// A Strategy Designer document compiled into the deterministic form the live engine can execute.
/// </summary>
/// <remarks>
/// <para>
/// Compilation is the fail-closed boundary for <c>PRD-020</c>. Every construct a designer document
/// can hold is either given real live semantics here or refused with a reason an operator can act
/// on. Nothing is approximated: a document that reaches <see cref="TryCompile"/> successfully
/// trades exactly what it says, and one that does not never activates.
/// </para>
/// <para>
/// Refused constructs and why: <c>code</c> cells (free-form source needs the plugin path or the
/// QuantLab isolation boundary of <c>PRD-012</c>); <c>state</c> cells (lifecycle phases with no
/// trade mapping in the document); catalog fields with no live source; disabled catalog fields;
/// unparseable universe, filter, rank, or risk sources; non-equity instruments; and documents with
/// no trade cell or more than one.
/// </para>
/// </remarks>
internal sealed class DesignerStrategyPlan
{
    private DesignerStrategyPlan(
        string documentId,
        string name,
        IReadOnlyList<string> universe,
        IReadOnlyList<DesignerGate> entryGates,
        IReadOnlyList<DesignerGate> riskGuards,
        DesignerExpression? rankExpression,
        DesignerTradeIntent trade,
        int? minimumUniverseSize,
        int? maximumPositions,
        IReadOnlySet<string> requiredFields)
    {
        DocumentId = documentId;
        Name = name;
        Universe = universe;
        EntryGates = entryGates;
        RiskGuards = riskGuards;
        RankExpression = rankExpression;
        Trade = trade;
        MinimumUniverseSize = minimumUniverseSize;
        MaximumPositions = maximumPositions;
        RequiredFields = requiredFields;
    }

    public string DocumentId { get; }

    public string Name { get; }

    /// <summary>Symbols the document declares. The engine intersects this with the run universe.</summary>
    public IReadOnlyList<string> Universe { get; }

    /// <summary>Conditions every symbol must satisfy to be eligible for entry.</summary>
    public IReadOnlyList<DesignerGate> EntryGates { get; }

    /// <summary>Conditions that must hold or no new entry is opened for that symbol.</summary>
    public IReadOnlyList<DesignerGate> RiskGuards { get; }

    /// <summary>Optional cross-sectional ranking score; higher ranks first.</summary>
    public DesignerExpression? RankExpression { get; }

    public DesignerTradeIntent Trade { get; }

    /// <summary>Universe-builder <c>minSize</c>: trade nothing until this many names qualify.</summary>
    public int? MinimumUniverseSize { get; }

    /// <summary>Universe-builder <c>maxSize</c>: cap on concurrently held names.</summary>
    public int? MaximumPositions { get; }

    /// <summary>Every catalog field the compiled expressions read.</summary>
    public IReadOnlySet<string> RequiredFields { get; }

    /// <summary>
    /// Compiles <paramref name="document"/>, or explains precisely why it cannot run live.
    /// </summary>
    public static bool TryCompile(
        StrategyDesignDocument document,
        StrategyDesignValidationResult validation,
        out DesignerStrategyPlan? plan,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(validation);
        plan = null;

        if (!validation.IsValid)
        {
            var errors = validation.Messages
                .Where(static message => string.Equals(message.Severity, "error", StringComparison.OrdinalIgnoreCase))
                .Select(static message => $"{message.Code}: {message.Message}")
                .ToArray();
            failureReason =
                $"Designer document '{document.DocumentId}' does not pass designer validation and cannot be " +
                $"activated: {string.Join(" | ", errors)}";
            return false;
        }

        var cells = document.Cells ?? Array.Empty<StrategyDesignCell>();
        var universe = (document.Universe ?? Array.Empty<string>())
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToArray();
        if (universe.Length == 0)
        {
            failureReason = $"Designer document '{document.DocumentId}' declares no universe symbols.";
            return false;
        }

        if (cells.FirstOrDefault(static cell => string.Equals(cell.Kind, "code", StringComparison.OrdinalIgnoreCase)) is { } codeCell)
        {
            failureReason =
                $"Designer document '{document.DocumentId}' contains code cell '{codeCell.Label}' ({codeCell.CellId}). " +
                "Free-form code is not executable from a designer document: compile it into a strategy plugin " +
                "assembly and promote the run with the 'pluginAssembly' parameter, or run it in QuantLab where " +
                "script isolation applies.";
            return false;
        }

        if (cells.FirstOrDefault(static cell => string.Equals(cell.Kind, "state", StringComparison.OrdinalIgnoreCase)) is { } stateCell)
        {
            failureReason =
                $"Designer document '{document.DocumentId}' contains state cell '{stateCell.Label}' ({stateCell.CellId}). " +
                "State cells describe lifecycle phases but carry no mapping from a state to a trade action, so the " +
                "live engine cannot execute them. Express the entry condition on a filter cell and the position " +
                "intent on a trade cell.";
            return false;
        }

        var tradeCells = cells
            .Where(static cell => string.Equals(cell.Kind, "trade", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (tradeCells.Length == 0)
        {
            failureReason =
                $"Designer document '{document.DocumentId}' has no trade cell, so it expresses no executable " +
                "trade intent. Add a trade cell naming instrument, direction, and sizing before promoting it.";
            return false;
        }

        if (tradeCells.Length > 1)
        {
            failureReason =
                $"Designer document '{document.DocumentId}' has {tradeCells.Length} trade cells " +
                $"({string.Join(", ", tradeCells.Select(static cell => cell.CellId))}); the live engine executes " +
                "exactly one trade intent per promoted run.";
            return false;
        }

        if (!TryCompileTrade(tradeCells[0], out var trade, out failureReason))
        {
            return false;
        }

        var entryGates = new List<DesignerGate>();
        var riskGuards = new List<DesignerGate>();
        DesignerExpression? rank = null;
        int? minimumUniverseSize = null;
        int? maximumPositions = null;
        var branchGates = new Dictionary<string, DesignerGate>(StringComparer.Ordinal);

        foreach (var cell in cells)
        {
            if (!TryCompileCell(
                    document,
                    cell,
                    entryGates,
                    riskGuards,
                    branchGates,
                    ref rank,
                    ref minimumUniverseSize,
                    ref maximumPositions,
                    out failureReason))
            {
                return false;
            }
        }

        // Concurrent cells compose branch gates rather than adding their own condition, so they are
        // resolved after every branch cell has been compiled.
        foreach (var cell in cells.Where(static cell => string.Equals(cell.Kind, "concurrent", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryCompileConcurrent(document, cell, branchGates, entryGates, out failureReason))
            {
                return false;
            }
        }

        if (entryGates.Count == 0)
        {
            failureReason =
                $"Designer document '{document.DocumentId}' has no executable entry condition. Add a universe or " +
                "filter cell whose source is a condition over catalog fields (for example 'PRICE > 20').";
            return false;
        }

        var requiredFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var gate in entryGates.Concat(riskGuards))
        {
            requiredFields.UnionWith(gate.Expression.ReferencedFields());
        }

        if (rank is not null)
        {
            requiredFields.UnionWith(rank.ReferencedFields());
        }

        plan = new DesignerStrategyPlan(
            document.DocumentId,
            string.IsNullOrWhiteSpace(document.Name) ? document.DocumentId : document.Name,
            universe,
            entryGates,
            riskGuards,
            rank,
            trade!,
            minimumUniverseSize,
            maximumPositions,
            requiredFields);
        failureReason = null;
        return true;
    }

    private static bool TryCompileCell(
        StrategyDesignDocument document,
        StrategyDesignCell cell,
        List<DesignerGate> entryGates,
        List<DesignerGate> riskGuards,
        Dictionary<string, DesignerGate> branchGates,
        ref DesignerExpression? rank,
        ref int? minimumUniverseSize,
        ref int? maximumPositions,
        out string? failureReason)
    {
        failureReason = null;
        var kind = cell.Kind ?? string.Empty;
        var purpose = cell.Purpose ?? string.Empty;

        // Every field a cell declares is checked even when the cell contributes no gate, so a
        // document filtering on an unresolvable field is refused rather than quietly narrowed.
        if (!TryValidateFields(document, cell, out failureReason))
        {
            return false;
        }

        if (string.Equals(kind, "trade", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "concurrent", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(kind, "universe-builder", StringComparison.OrdinalIgnoreCase))
        {
            return TryCompileUniverseBuilder(
                document, cell, entryGates, ref minimumUniverseSize, ref maximumPositions, out failureReason);
        }

        if (string.Equals(kind, "governance", StringComparison.OrdinalIgnoreCase))
        {
            // A risk-purpose governance cell must be executable: dropping one because its source is
            // prose would activate a run whose stated risk limit never applies. A control-purpose
            // cell ("attach payoff and run trace") is review documentation and carries no runtime
            // condition, so an unparseable one is left as documentation.
            if (!string.Equals(purpose, "risk", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!DesignerExpression.TryParse(cell.Source ?? string.Empty, DesignerLiveFields.Supported, out var guard, out var guardError))
            {
                failureReason =
                    $"Risk guard cell '{cell.Label}' ({cell.CellId}) in designer document '{document.DocumentId}' " +
                    $"is not an executable condition: {guardError} Source: \"{cell.Source}\".";
                return false;
            }

            riskGuards.Add(new DesignerGate(cell.CellId, cell.Label, guard!));
            return true;
        }

        if (string.Equals(purpose, "rank", StringComparison.OrdinalIgnoreCase))
        {
            if (!DesignerExpression.TryParse(cell.Source ?? string.Empty, DesignerLiveFields.Supported, out var rankExpression, out var rankError))
            {
                failureReason =
                    $"Rank cell '{cell.Label}' ({cell.CellId}) in designer document '{document.DocumentId}' is not " +
                    $"an executable score: {rankError} Source: \"{cell.Source}\".";
                return false;
            }

            if (rank is not null)
            {
                failureReason =
                    $"Designer document '{document.DocumentId}' declares more than one rank cell; the live engine " +
                    "orders candidates by a single score.";
                return false;
            }

            rank = rankExpression;
            return true;
        }

        if (string.Equals(kind, "visual", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "formula", StringComparison.OrdinalIgnoreCase))
        {
            if (!DesignerExpression.TryParse(cell.Source ?? string.Empty, DesignerLiveFields.Supported, out var gate, out var gateError))
            {
                failureReason =
                    $"Cell '{cell.Label}' ({cell.CellId}) in designer document '{document.DocumentId}' is not an " +
                    $"executable condition: {gateError} Source: \"{cell.Source}\".";
                return false;
            }

            var compiled = new DesignerGate(cell.CellId, cell.Label, gate!);
            branchGates[cell.CellId] = compiled;
            entryGates.Add(compiled);
            return true;
        }

        failureReason =
            $"Designer document '{document.DocumentId}' contains cell '{cell.Label}' ({cell.CellId}) of unsupported " +
            $"kind '{kind}'. The live engine executes visual, formula, universe-builder, concurrent, governance, and " +
            "trade cells.";
        return false;
    }

    private static bool TryCompileUniverseBuilder(
        StrategyDesignDocument document,
        StrategyDesignCell cell,
        List<DesignerGate> entryGates,
        ref int? minimumUniverseSize,
        ref int? maximumPositions,
        out string? failureReason)
    {
        failureReason = null;
        var parameters = cell.Parameters;

        foreach (var (key, negate) in new[] { ("includeRules", Negate: false), ("excludeRules", Negate: true) })
        {
            if (parameters is null || !parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var rule in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!DesignerExpression.TryParse(rule, DesignerLiveFields.Supported, out var expression, out var ruleError))
                {
                    failureReason =
                        $"Universe-builder cell '{cell.Label}' ({cell.CellId}) in designer document " +
                        $"'{document.DocumentId}' has an unexecutable {key} rule \"{rule}\": {ruleError}";
                    return false;
                }

                entryGates.Add(new DesignerGate(
                    cell.CellId,
                    negate ? $"{cell.Label} (exclude: {rule})" : $"{cell.Label} ({rule})",
                    negate ? DesignerExpression.Parse($"!({rule})", DesignerLiveFields.Supported) : expression!));
            }
        }

        if (parameters is not null && parameters.TryGetValue("minSize", out var minRaw) && !string.IsNullOrWhiteSpace(minRaw))
        {
            if (!int.TryParse(minRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minSize) || minSize < 0)
            {
                failureReason =
                    $"Universe-builder cell '{cell.Label}' ({cell.CellId}) has a non-integer minSize '{minRaw}'.";
                return false;
            }

            minimumUniverseSize = minimumUniverseSize is { } existing ? Math.Max(existing, minSize) : minSize;
        }

        if (parameters is not null && parameters.TryGetValue("maxSize", out var maxRaw) && !string.IsNullOrWhiteSpace(maxRaw))
        {
            if (!int.TryParse(maxRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxSize) || maxSize <= 0)
            {
                failureReason =
                    $"Universe-builder cell '{cell.Label}' ({cell.CellId}) has a non-positive maxSize '{maxRaw}'.";
                return false;
            }

            maximumPositions = maximumPositions is { } existingMax ? Math.Min(existingMax, maxSize) : maxSize;
        }

        return true;
    }

    private static bool TryCompileConcurrent(
        StrategyDesignDocument document,
        StrategyDesignCell cell,
        IReadOnlyDictionary<string, DesignerGate> branchGates,
        List<DesignerGate> entryGates,
        out string? failureReason)
    {
        failureReason = null;
        var parameters = cell.Parameters;
        var branchIds = parameters is not null && parameters.TryGetValue("branchIds", out var raw)
            ? raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();

        var semantics = parameters is not null && parameters.TryGetValue("semantics", out var sem) ? sem : string.Empty;
        var branches = new List<DesignerGate>();
        foreach (var branchId in branchIds)
        {
            if (!branchGates.TryGetValue(branchId, out var branch))
            {
                failureReason =
                    $"Concurrent cell '{cell.Label}' ({cell.CellId}) in designer document '{document.DocumentId}' " +
                    $"names branch '{branchId}', which is not a compiled condition cell.";
                return false;
            }

            branches.Add(branch);
        }

        if (branches.Count == 0)
        {
            failureReason =
                $"Concurrent cell '{cell.Label}' ({cell.CellId}) names no compiled branches.";
            return false;
        }

        // all-pass is the conjunction the branches already impose individually, so the branch gates
        // stay as they are. any-pass and first-wins relax them: a symbol passing any one branch is
        // eligible, so the individual branch gates are replaced by their disjunction.
        if (string.Equals(semantics, "all-pass", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(semantics, "any-pass", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(semantics, "first-wins", StringComparison.OrdinalIgnoreCase))
        {
            failureReason =
                $"Concurrent cell '{cell.Label}' ({cell.CellId}) declares unsupported semantics '{semantics}'.";
            return false;
        }

        entryGates.RemoveAll(gate => branches.Any(branch => ReferenceEquals(branch, gate)));
        entryGates.Add(new DesignerGate(
            cell.CellId,
            $"{cell.Label} ({semantics})",
            new AnyOfGate(branches.Select(static branch => branch.Expression).ToArray())));
        return true;
    }

    private static bool TryValidateFields(
        StrategyDesignDocument document,
        StrategyDesignCell cell,
        out string? failureReason)
    {
        foreach (var fieldRef in cell.FieldRefs ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(fieldRef) || DesignerLiveFields.Supported.Contains(fieldRef))
            {
                continue;
            }

            var reason = DesignerLiveFields.UnsupportedReasons.TryGetValue(fieldRef, out var known)
                ? known
                : "No live source is mapped for this field.";
            failureReason =
                $"Cell '{cell.Label}' ({cell.CellId}) in designer document '{document.DocumentId}' references field " +
                $"'{fieldRef}', which cannot be resolved during live execution: {reason}";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryCompileTrade(
        StrategyDesignCell cell,
        out DesignerTradeIntent? trade,
        out string? failureReason)
    {
        trade = null;
        var parameters = cell.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var instrument = parameters.GetValueOrDefault("instrument", string.Empty);
        if (!string.Equals(instrument, "Equity", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(instrument, "ETF", StringComparison.OrdinalIgnoreCase))
        {
            failureReason =
                $"Trade cell '{cell.Label}' ({cell.CellId}) trades instrument '{instrument}'. The live trading " +
                "engine routes equity and ETF orders only; option and future intents have no live routing path.";
            return false;
        }

        var direction = parameters.GetValueOrDefault("direction", string.Empty);
        DesignerTradeSide side;
        if (string.Equals(direction, "Buy", StringComparison.OrdinalIgnoreCase))
        {
            side = DesignerTradeSide.Long;
        }
        else if (string.Equals(direction, "SellShort", StringComparison.OrdinalIgnoreCase))
        {
            side = DesignerTradeSide.Short;
        }
        else
        {
            // Sell and BuyToCover close a position the document never opens. Treating one as an
            // entry would invert the operator's stated intent, so both are refused.
            failureReason =
                $"Trade cell '{cell.Label}' ({cell.CellId}) declares direction '{direction}', which closes a " +
                "position rather than opening one. A promoted designer run needs an entry direction (Buy or " +
                "SellShort); the matching exit is placed automatically when the entry conditions stop holding.";
            return false;
        }

        var sizingMethodRaw = parameters.GetValueOrDefault("sizingMethod", string.Empty);
        DesignerSizingMethod sizingMethod;
        if (string.Equals(sizingMethodRaw, "FixedShares", StringComparison.OrdinalIgnoreCase))
        {
            sizingMethod = DesignerSizingMethod.FixedShares;
        }
        else if (string.Equals(sizingMethodRaw, "FixedNotional", StringComparison.OrdinalIgnoreCase))
        {
            sizingMethod = DesignerSizingMethod.FixedNotional;
        }
        else if (string.Equals(sizingMethodRaw, "PercentAUM", StringComparison.OrdinalIgnoreCase))
        {
            sizingMethod = DesignerSizingMethod.PercentAum;
        }
        else if (string.Equals(sizingMethodRaw, "EqualWeight", StringComparison.OrdinalIgnoreCase))
        {
            sizingMethod = DesignerSizingMethod.EqualWeight;
        }
        else
        {
            failureReason =
                $"Trade cell '{cell.Label}' ({cell.CellId}) declares unsupported sizingMethod '{sizingMethodRaw}'.";
            return false;
        }

        var sizingValue = 0m;
        if (sizingMethod != DesignerSizingMethod.EqualWeight)
        {
            var raw = parameters.GetValueOrDefault("sizingValue", string.Empty);
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out sizingValue)
                || sizingValue <= 0m)
            {
                failureReason =
                    $"Trade cell '{cell.Label}' ({cell.CellId}) needs a positive sizingValue for sizingMethod " +
                    $"'{sizingMethodRaw}'; found '{raw}'.";
                return false;
            }

            if (sizingMethod == DesignerSizingMethod.PercentAum && sizingValue > 1m)
            {
                failureReason =
                    $"Trade cell '{cell.Label}' ({cell.CellId}) declares PercentAUM sizingValue '{raw}'. Express " +
                    "the weight as a fraction of portfolio value (0.05 for five percent).";
                return false;
            }
        }

        trade = new DesignerTradeIntent(cell.CellId, cell.Label, side, sizingMethod, sizingValue);
        failureReason = null;
        return true;
    }

    /// <summary>Disjunction of branch conditions used by any-pass/first-wins concurrent cells.</summary>
    private sealed class AnyOfGate(IReadOnlyList<DesignerExpression> branches) : DesignerExpression
    {
        public override DesignerValue Evaluate(IReadOnlyDictionary<string, decimal> fields)
        {
            foreach (var branch in branches)
            {
                if (branch.EvaluateCondition(fields))
                {
                    return DesignerValue.FromBoolean(true);
                }
            }

            return DesignerValue.FromBoolean(false);
        }

        public override IEnumerable<string> ReferencedFields() =>
            branches.SelectMany(static branch => branch.ReferencedFields());
    }
}
