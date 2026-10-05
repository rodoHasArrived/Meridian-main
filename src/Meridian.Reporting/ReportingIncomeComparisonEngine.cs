using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

/// <summary>
/// Explains only additive movements proven by retained rows. Unsupported changes remain visible
/// even when they offset to zero; compatibility is independent of arithmetic reconciliation.
/// </summary>
public static class ReportingIncomeComparisonEngine
{
    public const string ExplanationVersion = "investment-income-comparison/v1";
    private static readonly JsonSerializerOptions RetentionJson = new(JsonSerializerDefaults.Web);

    public static RetainedReportingIncomeComparison Compare(
        ReportingOutputManifest baseline, ReportingOutputManifest current, string gridId, string metricColumn,
        ReportingIncomeComparisonRunDto? baselineRun = null, ReportingIncomeComparisonRunDto? currentRun = null,
        DateTimeOffset? retainedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(gridId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricColumn);
        if (string.Equals(baseline.RunId, current.RunId, StringComparison.Ordinal))
            throw new ArgumentException("Choose two different retained report runs.");
        // Detach every dictionary and list from live orchestration state before interpreting it.
        baseline = Clone(baseline);
        current = Clone(current);
        var priorGrid = Grid(baseline, gridId);
        var nextGrid = Grid(current, gridId);
        var diff = ReportSnapshotDiffEngine.Diff(priorGrid, nextGrid);
        var priorRun = baselineRun ?? Describe(baseline);
        var nextRun = currentRun ?? Describe(current);
        var differences = Differences(baseline, current, priorRun, nextRun).ToList();
        var warnings = new List<string>(diff.Warnings);
        var contributions = new List<ReportingIncomeContributionDto>();
        var supports = new List<ReportingIncomeContributionSupportDto>();
        var unsupported = false;
        decimal? priorAmount = Total(priorGrid, metricColumn);
        decimal? nextAmount = Total(nextGrid, metricColumn);
        decimal? movement = nextAmount - priorAmount;
        if (priorAmount is null || nextAmount is null)
        {
            unsupported = true;
            warnings.Add("The selected retained metric contains missing or nonnumeric values; its movement cannot be reconciled.");
        }
        if (priorGrid.Rows.Select(r => r.RowKey).Distinct(StringComparer.Ordinal).Count() != priorGrid.Rows.Count
            || nextGrid.Rows.Select(r => r.RowKey).Distinct(StringComparer.Ordinal).Count() != nextGrid.Rows.Count)
        {
            differences.Add(new("Row identity", "Duplicate rows", "Ambiguous rows", false,
                "Duplicate rendered row keys prevent a reliable comparison."));
        }
        var prior = Project(baseline, priorGrid, metricColumn, warnings);
        var next = Project(current, nextGrid, metricColumn, warnings);
        if (prior is null || next is null)
            unsupported = true;
        else
        {
            var priorPopulation = Rows(baseline).Select(Population).ToHashSet(StringComparer.Ordinal);
            var nextPopulation = Rows(current).Select(Population).ToHashSet(StringComparer.Ordinal);
            if (!priorPopulation.SetEquals(nextPopulation))
                differences.Add(new("Retained population", $"{priorPopulation.Count} members", $"{nextPopulation.Count} members", true,
                    "Added and removed population members are separated from journals within the continuing population."));
            var priorRaw = UniqueRows(Rows(baseline));
            var nextRaw = UniqueRows(Rows(current));
            if (priorRaw is null || nextRaw is null)
            {
                unsupported = true;
                warnings.Add("Retained entry identities are missing or duplicated. Journal attribution is unavailable.");
            }
            else
            {
                var covered = new HashSet<string>(StringComparer.Ordinal);
                var effects = new List<(string Id, string Kind, string Record, string Run, decimal Amount)>();
                foreach (var id in prior.Keys.Union(next.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    var oldValue = prior.GetValueOrDefault(id);
                    var newValue = next.GetValueOrDefault(id);
                    if (oldValue == newValue)
                        continue;
                    priorRaw.TryGetValue(id, out var oldRow);
                    nextRaw.TryGetValue(id, out var newRow);
                    // Same immutable entry with altered bytes or a changed inclusion rule is not a
                    // new journal. Only a retained methodology record can explain that difference.
                    if (oldRow is not null && newRow is not null)
                        continue;
                    // Source additions/removals under a different rule combine two effects;
                    // do not label the entire amount as the journal's isolated contribution.
                    if (!SameMethod(priorGrid, nextGrid, metricColumn))
                        continue;
                    var row = newRow ?? oldRow!;
                    var journal = Value(row, "journalEntryId");
                    if (string.IsNullOrWhiteSpace(journal))
                        continue;
                    var population = Population(row);
                    var populationChanged = newRow is not null
                        ? ScopeExcludes(baseline, row)
                        : ScopeExcludes(current, row);
                    effects.Add((id, populationChanged ? "Population" : "Journal",
                        populationChanged ? population : journal,
                        newRow is not null ? current.RunId : baseline.RunId, newValue - oldValue));
                    covered.Add(id);
                }
                var priorJournals = Rows(baseline).GroupBy(r => Value(r, "journalEntryId")).ToDictionary(g => g.Key, g => g.ToArray());
                var nextJournals = Rows(current).GroupBy(r => Value(r, "journalEntryId")).ToDictionary(g => g.Key, g => g.ToArray());
                var priorMembers = Rows(baseline).GroupBy(Population).ToDictionary(g => g.Key, g => g.ToArray());
                var nextMembers = Rows(current).GroupBy(Population).ToDictionary(g => g.Key, g => g.ToArray());
                foreach (var group in effects.GroupBy(e => (e.Kind, e.Record, e.Run)).OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.Record, StringComparer.Ordinal))
                {
                    var amount = group.Sum(e => e.Amount);
                    var oldRows = (group.Key.Kind == "Journal" ? priorJournals : priorMembers).GetValueOrDefault(group.Key.Record) ?? [];
                    var newRows = (group.Key.Kind == "Journal" ? nextJournals : nextMembers).GetValueOrDefault(group.Key.Record) ?? [];
                    Add(group.Key.Kind, group.Key.Kind == "Journal" ? $"Retained journal {group.Key.Record}" : $"Population {group.Key.Record}",
                        amount, group.Key.Record, group.Key.Run,
                        group.Key.Kind == "Journal" ? "Change in the selected measure supported by this exact retained journal."
                            : "Selected income from an added or removed population member; excluded from journal contributions.",
                        oldRows, newRows, []);
                }
                foreach (var evidence in current.IncomeMethodologyEvidence.IsDefault ? [] : current.IncomeMethodologyEvidence)
                {
                    if (evidence.BaselineRunId != baseline.RunId || evidence.GridId != gridId || evidence.MetricColumn != metricColumn)
                        continue;
                    var ids = evidence.EntryIds.IsDefault ? [] : evidence.EntryIds.ToArray();
                    var valid = ids.Length > 0 && ids.Distinct(StringComparer.Ordinal).Count() == ids.Length
                        && !string.IsNullOrWhiteSpace(evidence.RecordId) && !string.IsNullOrWhiteSpace(evidence.Description)
                        && !evidence.EvidenceReferences.IsDefaultOrEmpty && evidence.EvidenceReferences.All(e => !string.IsNullOrWhiteSpace(e))
                        && ids.All(id => !covered.Contains(id) && priorRaw.ContainsKey(id) && nextRaw.ContainsKey(id)
                            && RowsEqual(priorRaw[id], nextRaw[id]))
                        && ids.Sum(id => prior.GetValueOrDefault(id)) == evidence.BaselineAmount
                        && ids.Sum(id => next.GetValueOrDefault(id)) == evidence.CurrentAmount;
                    if (!valid)
                    {
                        unsupported = true;
                        warnings.Add($"Methodology record '{evidence.RecordId}' does not prove an exclusive, quantified impact on unchanged retained lines; its claimed effect remains unexplained.");
                        continue;
                    }
                    foreach (var id in ids)
                        covered.Add(id);
                    Add("Methodology", evidence.Description, evidence.CurrentAmount - evidence.BaselineAmount,
                        evidence.RecordId, current.RunId, "Documented before/after impact on unchanged retained journal lines; excluded from other contribution categories.",
                        ids.Select(id => priorRaw[id]).ToArray(), ids.Select(id => nextRaw[id]).ToArray(), evidence.EvidenceReferences);
                }
                // Saved metric/filter definitions are themselves retained methodology records.
                // Re-evaluate their exact additive effect only on byte-identical continuing lines.
                // An explicitly supplied but invalid record is never silently replaced.
                if (!SameMethod(priorGrid, nextGrid, metricColumn) && current.IncomeMethodologyEvidence.IsDefaultOrEmpty)
                {
                    var methodIds = prior.Keys.Union(next.Keys, StringComparer.Ordinal)
                        .Where(id => !covered.Contains(id) && priorRaw.TryGetValue(id, out var oldRow)
                            && nextRaw.TryGetValue(id, out var newRow) && RowsEqual(oldRow, newRow)
                            && prior.GetValueOrDefault(id) != next.GetValueOrDefault(id)).Order(StringComparer.Ordinal).ToArray();
                    if (methodIds.Length > 0)
                    {
                        foreach (var id in methodIds)
                            covered.Add(id);
                        var oldDefinition = new Dictionary<string, string> { ["recordType"] = "Retained methodology", ["gridId"] = gridId, ["metricColumn"] = metricColumn, ["definition"] = Method(priorGrid, metricColumn), ["templateVersion"] = priorRun.TemplateVersion ?? "Unknown" };
                        var newDefinition = new Dictionary<string, string> { ["recordType"] = "Retained methodology", ["gridId"] = gridId, ["metricColumn"] = metricColumn, ["definition"] = Method(nextGrid, metricColumn), ["templateVersion"] = nextRun.TemplateVersion ?? "Unknown" };
                        var impacts = methodIds.Select(id => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                        {
                            ["recordType"] = "Retained methodology impact",
                            ["entryId"] = id,
                            ["baselineAmount"] = prior.GetValueOrDefault(id).ToString("G29", CultureInfo.InvariantCulture),
                            ["currentAmount"] = next.GetValueOrDefault(id).ToString("G29", CultureInfo.InvariantCulture)
                        }).ToArray();
                        Add("Methodology", "Retained metric/filter definition change",
                            methodIds.Sum(id => next.GetValueOrDefault(id) - prior.GetValueOrDefault(id)),
                            $"{gridId}:{metricColumn}:definition", current.RunId,
                            "Before/after retained definitions evaluated over identical continuing journal lines. New journals and population changes are excluded.",
                            methodIds.Select(id => priorRaw[id]).Append(oldDefinition).Concat(impacts).ToArray(),
                            methodIds.Select(id => nextRaw[id]).Append(newDefinition).Concat(impacts).ToArray(),
                            [$"reporting-run://{baseline.RunId}/report-writer-grids/{gridId}", $"reporting-run://{current.RunId}/report-writer-grids/{gridId}"]);
                    }
                }
                var unexplainedEntries = prior.Keys.Union(next.Keys, StringComparer.Ordinal)
                    .Where(id => prior.GetValueOrDefault(id) != next.GetValueOrDefault(id) && !covered.Contains(id)).ToArray();
                if (unexplainedEntries.Length > 0)
                {
                    unsupported = true;
                    warnings.Add($"{unexplainedEntries.Length} changed retained lines have no supported exclusive explanation, including changes that may offset to zero.");
                    foreach (var id in unexplainedEntries.Order(StringComparer.Ordinal))
                    {
                        Add("Unexplained", $"Unsupported retained line {id}", next.GetValueOrDefault(id) - prior.GetValueOrDefault(id),
                            id, current.RunId, "The retained line changed without a supported journal, population, or documented methodology explanation. This amount is part of the residual, not explained movement.",
                            priorRaw.TryGetValue(id, out var oldRow) ? [oldRow] : [],
                            nextRaw.TryGetValue(id, out var newRow) ? [newRow] : [], []);
                    }
                }
            }
            if (prior.Values.Sum() != priorAmount || next.Values.Sum() != nextAmount)
            {
                unsupported = true;
                warnings.Add("Retained journal measures do not reproduce the rendered totals. The unsupported rendered difference remains in the residual.");
            }
        }
        if (prior is not null && next is not null)
        {
            var expectedPrior = Reconstruct(baseline, priorGrid, metricColumn);
            var expectedNext = Reconstruct(current, nextGrid, metricColumn);
            var actualPrior = priorGrid.Rows.GroupBy(r => r.RowKey).ToDictionary(g => g.Key, g => g.First().Values);
            var actualNext = nextGrid.Rows.GroupBy(r => r.RowKey).ToDictionary(g => g.Key, g => g.First().Values);
            foreach (var key in expectedPrior.Keys.Union(expectedNext.Keys).Union(actualPrior.Keys).Union(actualNext.Keys).Order(StringComparer.Ordinal))
            {
                var oldActual = actualPrior.TryGetValue(key, out var oldValues) ? Number(Value(oldValues, metricColumn)) : 0m;
                var newActual = actualNext.TryGetValue(key, out var newValues) ? Number(Value(newValues, metricColumn)) : 0m;
                var oldExpected = expectedPrior.GetValueOrDefault(key);
                var newExpected = expectedNext.GetValueOrDefault(key);
                if (oldActual == oldExpected && newActual == newExpected)
                    continue;
                unsupported = true;
                var diagnostic = new Dictionary<string, string>
                {
                    ["recordType"] = "Retained rendered-cell validation",
                    ["rowKey"] = key,
                    ["baselineSourceAmount"] = oldExpected.ToString("G29", CultureInfo.InvariantCulture),
                    ["currentSourceAmount"] = newExpected.ToString("G29", CultureInfo.InvariantCulture)
                };
                Add("Unexplained", $"Unsupported rendered row {key}",
                    (newActual ?? 0m) - newExpected - ((oldActual ?? 0m) - oldExpected),
                    $"{gridId}:{key}:{metricColumn}", current.RunId,
                    "The selected retained cell is not reproduced by its retained source rows and definition. Offsetting rendered discrepancies remain unsupported.",
                    oldValues is null ? [diagnostic] : [oldValues, diagnostic],
                    newValues is null ? [diagnostic] : [newValues, diagnostic], []);
            }
            if (contributions.Any(c => c.Kind == "Unexplained" && c.RecordId.StartsWith($"{gridId}:", StringComparison.Ordinal)))
                warnings.Add("Retained rendered cells do not individually match their retained source projection; net-zero offsets do not establish reconciliation.");
        }
        if (!SameMethod(priorGrid, nextGrid, metricColumn))
        {
            differences.Add(new("Methodology", MethodSummary(priorGrid, metricColumn), MethodSummary(nextGrid, metricColumn), true,
                "Changed retained metric or filter definitions require documented quantified support; a version label alone is not an explanation."));
            if (!contributions.Any(c => c.Kind == "Methodology"))
            {
                unsupported = true;
                warnings.Add("The retained methodology changed without a validated quantified methodology contribution.");
            }
        }
        var compatible = differences.All(d => d.Compatible);
        var explained = contributions.Where(c => c.Kind != "Unexplained").Sum(c => c.Amount);
        var residual = movement - explained;
        var status = !compatible ? "Incompatible" : unsupported || residual != 0m ? "Unexplained" : "Reconciled";
        var comparison = new ReportingIncomeComparisonDto("", ExplanationVersion, retainedAtUtc ?? DateTimeOffset.UtcNow,
            priorRun, nextRun, gridId, metricColumn, status, compatible, priorAmount, nextAmount, movement,
            explained, residual, differences, contributions, warnings.Distinct(StringComparer.Ordinal).ToArray(), diff);
        return new(ExplanationVersion, baseline, current, comparison, supports);

        void Add(string kind, string label, decimal amount, string record, string run, string detail,
            IReadOnlyList<IReadOnlyDictionary<string, string>> oldRecords,
            IReadOnlyList<IReadOnlyDictionary<string, string>> newRecords, IReadOnlyList<string> evidence)
        {
            var id = $"contribution-{contributions.Count + 1}";
            var contribution = new ReportingIncomeContributionDto(id, kind, label, amount, record, run, detail);
            contributions.Add(contribution);
            supports.Add(new("", baseline.RunId, current.RunId, contribution, oldRecords, newRecords, evidence));
        }
    }

    public static ReportingIncomeComparisonRunDto Describe(ReportingOutputManifest manifest,
        GovernedReportingRun? governed = null)
    {
        var parameters = manifest.ResolvedParameters;
        var published = governed?.Release is not null;
        return new(manifest.RunId, manifest.TemplateId, manifest.ResolvedTemplate?.Version.ToString(CultureInfo.InvariantCulture),
            governed is null ? "Publication not verified"
                : governed.RestatementOfRunId is not null ? (published ? "Restated published" : "Restated draft")
                : published ? "Originally published" : "Retained draft",
            governed?.Revision, governed?.RestatementOfRunId, governed?.GovernanceState.ToString() ?? manifest.Status.ToString(),
            parameters?.PeriodId ?? manifest.OperationalScope?.PeriodId ?? "Unknown",
            manifest.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Scope(manifest),
            parameters?.AccountingBasis.ToString() ?? manifest.AuthoritativeSource?.AccountingBasis ?? "Unknown",
            parameters?.PresentationCurrency ?? "Unknown",
            (manifest.RenderedReportWriterGrids.IsDefault ? [] : manifest.RenderedReportWriterGrids)
                .Select(grid => new ReportingIncomeComparisonGridDto(grid.GridId, grid.Title,
                    grid.Columns.Where(column => column.Role is "metric" or "formula")
                        .Select(column => new ReportingIncomeComparisonMetricDto(column.Key, column.Label,
                            grid.Lineage?.Metrics.FirstOrDefault(m => m.Name == column.Key)?.SourceField)).ToArray())).ToArray());
    }

    private static ReportingOutputManifest Clone(ReportingOutputManifest manifest) =>
        JsonSerializer.Deserialize<ReportingOutputManifest>(JsonSerializer.Serialize(manifest with
        {
            Sections = manifest.Sections.IsDefault ? [] : manifest.Sections,
            Artifacts = manifest.Artifacts.IsDefault ? [] : manifest.Artifacts,
            ReportWriterGrids = manifest.ReportWriterGrids.IsDefault ? [] : manifest.ReportWriterGrids,
            RenderedReportWriterGrids = manifest.RenderedReportWriterGrids.IsDefault ? [] : manifest.RenderedReportWriterGrids,
            ReportWriterGridDiffs = manifest.ReportWriterGridDiffs.IsDefault ? [] : manifest.ReportWriterGridDiffs,
            CertifiedDatasetRows = manifest.CertifiedDatasetRows.IsDefault ? [] : manifest.CertifiedDatasetRows
        }, RetentionJson), RetentionJson)!;

    private static ReportWriterGridRenderDto Grid(ReportingOutputManifest manifest, string gridId) =>
        (manifest.RenderedReportWriterGrids.IsDefault ? [] : manifest.RenderedReportWriterGrids)
            .SingleOrDefault(g => string.Equals(g.GridId, gridId, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Run '{manifest.RunId}' does not retain grid '{gridId}'.");

    private static decimal? Total(ReportWriterGridRenderDto grid, string metric)
    {
        if (!grid.Columns.Any(c => c.Key == metric && c.Role is "metric" or "formula"))
            return null;
        decimal total = 0;
        foreach (var row in grid.Rows)
        {
            if (!decimal.TryParse(Value(row.Values, metric), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                return null;
            total += value;
        }
        return total;
    }

    private static Dictionary<string, decimal>? Project(ReportingOutputManifest manifest,
        ReportWriterGridRenderDto grid, string metric, List<string> warnings)
    {
        var lineage = grid.Lineage?.Metrics.SingleOrDefault(m => m.Name == metric);
        if (lineage is null || lineage.Function != "Sum" || grid.Kind is ReportWriterGridKindDto.TopN
            || manifest.CertifiedDatasetRows.IsDefault)
        {
            warnings.Add($"Run '{manifest.RunId}' lacks a supported additive retained metric mapping; no journal impact is inferred.");
            return null;
        }
        var filters = new List<ReportWriterFilterDefinitionDto>();
        foreach (var filter in grid.Lineage?.Filters ?? [])
        {
            if (!Enum.TryParse<ReportWriterFilterOperatorDto>(filter.Operator, out var op))
                return null;
            filters.Add(new(filter.Field, op, filter.Value, filter.Label));
        }
        var projection = ReportWriterGridEngine.RenderGrids(
            [new("income-support", "Retained source projection", ReportWriterGridKindDto.Detail,
                RowFields: ["entryId"], Metrics: [new(metric, lineage.SourceField, ReportWriterAggregateFunctionDto.Sum)], Filters: filters)],
            manifest.CertifiedDatasetRows)[0];
        var sourceRows = UniqueRows(manifest.CertifiedDatasetRows);
        if (sourceRows is null)
            return null;
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var row in projection.Rows)
        {
            var id = Value(row.Values, "entryId");
            if (string.IsNullOrWhiteSpace(id) || !decimal.TryParse(Value(row.Values, metric), NumberStyles.Number,
                    CultureInfo.InvariantCulture, out var amount) || !values.TryAdd(id, amount))
                return null;
            var source = sourceRows.GetValueOrDefault(id);
            // Report-writer aggregation treats absent numbers as zero; evidence cannot do that.
            if (source is null || !decimal.TryParse(Value(source, lineage.SourceField), NumberStyles.Number,
                    CultureInfo.InvariantCulture, out _))
                return null;
        }
        return values;
    }

    private static Dictionary<string, decimal> Reconstruct(ReportingOutputManifest manifest, ReportWriterGridRenderDto grid, string metric)
    {
        var lineage = grid.Lineage!.Metrics.Single(m => m.Name == metric);
        var filters = (grid.Lineage.Filters ?? []).Select(filter => new ReportWriterFilterDefinitionDto(filter.Field,
            Enum.Parse<ReportWriterFilterOperatorDto>(filter.Operator), filter.Value, filter.Label)).ToArray();
        var reconstructed = ReportWriterGridEngine.RenderGrids(
            [new(grid.GridId, grid.Title, grid.Kind, RowFields: grid.Columns.Where(c => c.Role == "dimension").Select(c => c.Key).ToArray(),
                Metrics: [new(metric, lineage.SourceField)], Filters: filters)], manifest.CertifiedDatasetRows)[0];
        return reconstructed.Rows.GroupBy(r => r.RowKey).ToDictionary(g => g.Key,
            g => g.Sum(r => Number(Value(r.Values, metric)) ?? 0m), StringComparer.Ordinal);
    }

    private static decimal? Number(string raw) => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static IEnumerable<ReportingIncomeComparisonDifferenceDto> Differences(ReportingOutputManifest a,
        ReportingOutputManifest b, ReportingIncomeComparisonRunDto ar, ReportingIncomeComparisonRunDto br)
    {
        yield return Difference("Period", $"{ar.PeriodId} / {ar.AsOfDate}", $"{br.PeriodId} / {br.AsOfDate}", false);
        yield return Difference("Population", ar.Population, br.Population, true);
        yield return Difference("Accounting basis", ar.AccountingBasis, br.AccountingBasis, false);
        yield return Difference("Currency", ar.Currency, br.Currency, false);
        yield return Difference("Ledger book", a.OperationalScope?.BookId ?? a.ResolvedParameters?.LedgerBook.LedgerBookId?.ToString() ?? "Unknown",
            b.OperationalScope?.BookId ?? b.ResolvedParameters?.LedgerBook.LedgerBookId?.ToString() ?? "Unknown", false);
        if (a.ResolvedParameters is null || b.ResolvedParameters is null)
            yield return new("Input completeness", a.ResolvedParameters is null ? "Missing parameters" : "Retained", b.ResolvedParameters is null ? "Missing parameters" : "Retained",
                false, "Missing retained comparison parameters prevent a compatible reconciliation.");
        foreach (var manifest in new[] { a, b })
        {
            var parameters = manifest.ResolvedParameters;
            if (parameters is not null && (!Known(parameters.PeriodId) || manifest.AsOfDate == default
                || parameters.AsOfDate == default || manifest.AsOfDate != parameters.AsOfDate
                || !Known(parameters.PresentationCurrency) || !Enum.IsDefined(parameters.AccountingBasis)
                || parameters.LedgerBook.LedgerBookId == Guid.Empty
                || !Known(manifest.OperationalScope?.BookId ?? parameters.LedgerBook.LedgerBookId?.ToString())))
                yield return new("Input completeness", manifest.RunId, "Missing or invalid retained scope", false,
                    "Period, as-of date, accounting basis, currency and ledger book must have valid retained identities.");
            var currencies = Rows(manifest).Select(r => Value(r, "currency")).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (currencies.Any(c => !string.Equals(c, manifest.ResolvedParameters?.PresentationCurrency, StringComparison.OrdinalIgnoreCase)))
                yield return new("Source currency", manifest.RunId, string.Join(", ", currencies), false,
                    "Retained ledger measures use a different currency from presentation. No retained conversion bridge is available.");
        }
    }

    private static ReportingIncomeComparisonDifferenceDto Difference(string dimension, string a, string b, bool allowed) =>
        new(dimension, a, b, Known(a) && Known(b) && (a == b || allowed),
            a == b ? "Same retained scope." : allowed ? "Population differences are disclosed and attributed separately when supported."
                : "Different retained scope; arithmetic agreement cannot establish reconciliation.");

    private static bool Known(string? value) => !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase);

    private static string DimensionSummary(object dimensions) => string.Join("; ",
        JsonSerializer.SerializeToElement(dimensions, RetentionJson).EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString())
                || p.Value.ValueKind == JsonValueKind.Object && p.Value.EnumerateObject().Any())
            .Select(p => $"{p.Name}: {p.Value}"));

    private static string MethodSummary(ReportWriterGridRenderDto grid, string metric)
    {
        var source = grid.Lineage?.Metrics.FirstOrDefault(m => m.Name == metric);
        var formula = grid.Lineage?.Formulas.FirstOrDefault(f => f.Name == metric);
        var rule = source is not null ? $"{source.Function}({source.SourceField})" : formula?.Expression ?? "Unknown retained definition";
        var filters = grid.Lineage?.Filters ?? [];
        return filters.Count == 0 ? rule : $"{rule}; {string.Join("; ", filters.Select(f => $"{f.Field} {f.Operator} {f.Value}"))}";
    }

    private static string Scope(ReportingOutputManifest manifest) => manifest.ResolvedParameters is { } p
        ? string.Join(" · ", new[] { $"Fund {p.Scope.FundProfileId}", p.Scope.EntityScopeKind.ToString(),
            p.Scope.EntityId is null ? null : $"Entity {p.Scope.EntityId}",
            p.Scope.PortfolioId is null ? null : $"Portfolio {p.Scope.PortfolioId}",
            p.Scope.InvestorId is null ? null : $"Investor {p.Scope.InvestorId}",
            p.Scope.Dimensions is null ? null : DimensionSummary(p.Scope.Dimensions),
            $"Consolidation {p.ConsolidationLevel}" }.Where(v => v is not null))
        : manifest.OperationalScope?.FundId ?? "Unknown";

    private static bool ScopeExcludes(ReportingOutputManifest manifest, IReadOnlyDictionary<string, string> row)
    {
        if (manifest.ResolvedParameters is not { } parameters)
            return false;
        var scope = parameters.Scope;
        bool Different(string field, string? expected) => !string.IsNullOrWhiteSpace(expected)
            && !string.IsNullOrWhiteSpace(Value(row, field))
            && !string.Equals(Value(row, field), expected, StringComparison.OrdinalIgnoreCase);
        if (Different("fundId", scope.FundProfileId) || Different("entityId", scope.EntityId)
            || Different("portfolioId", scope.PortfolioId) || Different("investorId", scope.InvestorId))
            return true;
        if (scope.Dimensions is null)
            return false;
        // These are the retained explicit scope predicates. Missing row dimensions do not prove
        // exclusion, and a newly seen instrument inside the continuing entity is still a journal.
        foreach (var property in JsonSerializer.SerializeToElement(scope.Dimensions, RetentionJson).EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && Different(property.Name, property.Value.GetString()))
                return true;
            if (property.Name == "externalGlDimensions" && property.Value.ValueKind == JsonValueKind.Object)
                foreach (var external in property.Value.EnumerateObject())
                    if (Different($"externalGl.{external.Name}", external.Value.GetString()))
                        return true;
        }
        return false;
    }

    private static string Population(IReadOnlyDictionary<string, string> row) => string.Join(" / ",
        new[] { "fundId", "entityId", "portfolioId", "investorId", "instrumentId", "accountId", "financialAccountId",
            "sleeveId", "strategyId", "capitalAccountId", "taxLotId", "costCenterId", "counterpartyId", "organizationId", "bookId", "customerId", "vendorId", "projectId", "positionId" }
            .Where(key => !string.IsNullOrWhiteSpace(Value(row, key))).Select(key => $"{key}={Value(row, key)}"));

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(ReportingOutputManifest manifest) =>
        manifest.CertifiedDatasetRows.IsDefault ? [] : manifest.CertifiedDatasetRows;
    private static string Value(IReadOnlyDictionary<string, string> row, string key) => row.GetValueOrDefault(key) ?? "";
    private static Dictionary<string, IReadOnlyDictionary<string, string>>? UniqueRows(IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var id = Value(row, "entryId");
            if (string.IsNullOrWhiteSpace(id) || !result.TryAdd(id, row))
                return null;
        }
        return result;
    }
    private static bool RowsEqual(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);
    private static string Method(ReportWriterGridRenderDto grid, string metric) => JsonSerializer.Serialize(new
    {
        grid.Kind,
        Metric = grid.Lineage?.Metrics.FirstOrDefault(m => m.Name == metric),
        Formula = grid.Lineage?.Formulas.FirstOrDefault(f => f.Name == metric),
        Filters = grid.Lineage?.Filters
    }, RetentionJson);
    private static bool SameMethod(ReportWriterGridRenderDto a, ReportWriterGridRenderDto b, string metric) => Method(a, metric) == Method(b, metric);
}
