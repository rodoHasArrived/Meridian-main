using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.StrategyEngine;

namespace Meridian.Strategies.Services;

/// <summary>
/// Validates Strategy Engine run requests before they can become durable run history,
/// promotion candidates, or paper-session handoffs.
/// </summary>
public sealed class StrategyEngineValidationService
{
    private static readonly JsonSerializerOptions HashJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly StrategyEngineRegistry _registry;

    public StrategyEngineValidationService(StrategyEngineRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public StrategyEngineValidationResult Validate(
        StrategyEngineRunRequest request,
        IReadOnlyList<StrategyEngineDataAvailability>? dataAvailability = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var strategyId = request.StrategyId?.Trim();
        var strategyVersion = request.StrategyVersion?.Trim();
        var dataSource = request.DataSource?.Trim();
        var parameters = request.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var universe = request.Universe?
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .ToArray()
            ?? [];
        var findings = new List<StrategyEngineValidationFinding>();
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            findings.Add(new(
                "strategy-id-required",
                StrategyEngineValidationSeverity.Blocker,
                nameof(request.StrategyId),
                "Strategy ID is required."));
        }

        if (string.IsNullOrWhiteSpace(strategyVersion))
        {
            findings.Add(new(
                "strategy-version-required",
                StrategyEngineValidationSeverity.Blocker,
                nameof(request.StrategyVersion),
                "Strategy version is required."));
        }

        if (string.IsNullOrWhiteSpace(dataSource))
        {
            findings.Add(new(
                "data-source-required",
                StrategyEngineValidationSeverity.Blocker,
                nameof(request.DataSource),
                "Data source is required."));
        }

        if (request.To <= request.From)
        {
            findings.Add(new(
                "invalid-date-range",
                StrategyEngineValidationSeverity.Blocker,
                "dateRange",
                "Strategy run end time must be after the start time."));
        }

        if (universe.Length == 0)
        {
            findings.Add(new(
                "universe-required",
                StrategyEngineValidationSeverity.Blocker,
                nameof(request.Universe),
                "At least one symbol or universe selector is required before execution."));
        }

        StrategyEngineDefinition? definition = null;
        if (!string.IsNullOrWhiteSpace(strategyId) && !string.IsNullOrWhiteSpace(strategyVersion))
        {
            definition = _registry.GetDefinition(strategyId, strategyVersion);
        }

        if (definition is null)
        {
            if (!string.IsNullOrWhiteSpace(strategyId) && !string.IsNullOrWhiteSpace(strategyVersion))
            {
                findings.Add(new(
                    "strategy-definition-missing",
                    StrategyEngineValidationSeverity.Blocker,
                    nameof(request.StrategyId),
                    $"Strategy definition '{strategyId}' version '{strategyVersion}' is not registered.",
                    "/workstation/strategy"));
            }

            return BuildResult(request, null, dataAvailability ?? [], findings);
        }

        if (!definition.SupportedModes.Contains(request.Mode))
        {
            findings.Add(new(
                "unsupported-mode",
                StrategyEngineValidationSeverity.Blocker,
                nameof(request.Mode),
                $"{definition.Name} does not support {request.Mode} mode."));
        }

        ValidateParameters(definition, parameters, findings);
        ValidateDataDependencies(definition, request, dataAvailability ?? [], findings);
        ValidatePromotionSafety(request, findings);

        return BuildResult(request, definition, dataAvailability ?? [], findings);
    }

    private static void ValidateParameters(
        StrategyEngineDefinition definition,
        IReadOnlyDictionary<string, string> parameters,
        List<StrategyEngineValidationFinding> findings)
    {
        foreach (var parameter in definition.ParameterSchema)
        {
            var hasValue = parameters.TryGetValue(parameter.Name, out var rawValue) &&
                           !string.IsNullOrWhiteSpace(rawValue);
            if (parameter.IsRequired && !hasValue && string.IsNullOrWhiteSpace(parameter.DefaultValue))
            {
                findings.Add(new(
                    "parameter-required",
                    StrategyEngineValidationSeverity.Blocker,
                    $"parameters.{parameter.Name}",
                    $"{parameter.DisplayLabel ?? parameter.Name} is required."));
                continue;
            }

            if (!hasValue)
            {
                continue;
            }

            ValidateParameterValue(parameter, rawValue!, findings);
        }
    }

    private static void ValidateParameterValue(
        StrategyEngineParameterDefinition parameter,
        string rawValue,
        List<StrategyEngineValidationFinding> findings)
    {
        var target = $"parameters.{parameter.Name}";
        switch (parameter.Type)
        {
            case StrategyEngineParameterType.Decimal:
                if (!decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
                {
                    findings.Add(new("parameter-type", StrategyEngineValidationSeverity.Blocker, target, $"{parameter.Name} must be a decimal value."));
                    return;
                }

                ValidateDecimalBounds(parameter, decimalValue, findings, target);
                break;
            case StrategyEngineParameterType.Integer:
                if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue))
                {
                    findings.Add(new("parameter-type", StrategyEngineValidationSeverity.Blocker, target, $"{parameter.Name} must be an integer value."));
                    return;
                }

                ValidateIntegerBounds(parameter, integerValue, findings, target);
                break;
            case StrategyEngineParameterType.Boolean:
                if (!bool.TryParse(rawValue, out _))
                {
                    findings.Add(new("parameter-type", StrategyEngineValidationSeverity.Blocker, target, $"{parameter.Name} must be true or false."));
                }

                break;
            case StrategyEngineParameterType.Date:
                if (!DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
                {
                    findings.Add(new("parameter-type", StrategyEngineValidationSeverity.Blocker, target, $"{parameter.Name} must be a date or timestamp."));
                }

                break;
        }

        if (parameter.AllowedValues is { Count: > 0 } &&
            !parameter.AllowedValues.Contains(rawValue, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new(
                "parameter-allowed-values",
                StrategyEngineValidationSeverity.Blocker,
                target,
                $"{parameter.Name} must be one of: {string.Join(", ", parameter.AllowedValues)}."));
        }
    }

    private static void ValidateDecimalBounds(
        StrategyEngineParameterDefinition parameter,
        decimal value,
        List<StrategyEngineValidationFinding> findings,
        string target)
    {
        if (decimal.TryParse(parameter.MinValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var min) && value < min)
        {
            findings.Add(new("parameter-min", StrategyEngineValidationSeverity.Blocker, target, $"{parameter.Name} must be greater than or equal to {min}."));
        }

        if (decimal.TryParse(parameter.MaxValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var max) && value > max)
        {
            findings.Add(new("parameter-max", StrategyEngineValidationSeverity.Blocker, target, $"{parameter.Name} must be less than or equal to {max}."));
        }
    }

    private static void ValidateIntegerBounds(
        StrategyEngineParameterDefinition parameter,
        int value,
        List<StrategyEngineValidationFinding> findings,
        string target)
    {
        if (int.TryParse(parameter.MinValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var min) && value < min)
        {
            findings.Add(new("parameter-min", StrategyEngineValidationSeverity.Blocker, target, $"{parameter.Name} must be greater than or equal to {min}."));
        }

        if (int.TryParse(parameter.MaxValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) && value > max)
        {
            findings.Add(new("parameter-max", StrategyEngineValidationSeverity.Blocker, target, $"{parameter.Name} must be less than or equal to {max}."));
        }
    }

    private static void ValidateDataDependencies(
        StrategyEngineDefinition definition,
        StrategyEngineRunRequest request,
        IReadOnlyList<StrategyEngineDataAvailability> dataAvailability,
        List<StrategyEngineValidationFinding> findings)
    {
        var availabilityById = dataAvailability.ToDictionary(static item => item.DependencyId, StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in definition.RequiredDataInputs)
        {
            if (!availabilityById.TryGetValue(dependency.DependencyId, out var availability))
            {
                findings.Add(CreateMissingDependencyFinding(dependency, request));
                continue;
            }

            if (!availability.IsAvailable)
            {
                findings.Add(CreateMissingDependencyFinding(dependency, request, availability.DegradationReason));
                continue;
            }

            if (availability.AvailableFrom.HasValue && availability.AvailableFrom.Value > request.From)
            {
                findings.Add(new(
                    "dependency-range-start",
                    dependency.MissingDataPolicy == StrategyEngineMissingDataPolicy.Block ? StrategyEngineValidationSeverity.Blocker : StrategyEngineValidationSeverity.Warning,
                    $"data.{dependency.DependencyId}",
                    $"{dependency.Description} starts at {availability.AvailableFrom:O}, after the requested run start {request.From:O}.",
                    "/workstation/data"));
            }

            if (availability.AvailableTo.HasValue && availability.AvailableTo.Value < request.To)
            {
                findings.Add(new(
                    "dependency-range-end",
                    dependency.MissingDataPolicy == StrategyEngineMissingDataPolicy.Block ? StrategyEngineValidationSeverity.Blocker : StrategyEngineValidationSeverity.Warning,
                    $"data.{dependency.DependencyId}",
                    $"{dependency.Description} ends at {availability.AvailableTo:O}, before the requested run end {request.To:O}.",
                    "/workstation/data"));
            }

            if (dependency.FreshnessWindow.HasValue &&
                availability.LastUpdatedAt.HasValue &&
                DateTimeOffset.UtcNow - availability.LastUpdatedAt.Value > dependency.FreshnessWindow.Value)
            {
                findings.Add(new(
                    "dependency-stale",
                    StrategyEngineValidationSeverity.Warning,
                    $"data.{dependency.DependencyId}",
                    $"{dependency.Description} is outside the freshness window.",
                    "/workstation/data"));
            }

            if (availability.MissingFields is { Count: > 0 })
            {
                findings.Add(new(
                    "dependency-fields-missing",
                    dependency.MissingDataPolicy == StrategyEngineMissingDataPolicy.Block ? StrategyEngineValidationSeverity.Blocker : StrategyEngineValidationSeverity.Warning,
                    $"data.{dependency.DependencyId}",
                    $"{dependency.Description} is missing fields: {string.Join(", ", availability.MissingFields)}.",
                    "/workstation/data"));
            }
        }
    }

    private static StrategyEngineValidationFinding CreateMissingDependencyFinding(
        StrategyEngineDataDependency dependency,
        StrategyEngineRunRequest request,
        string? reason = null)
    {
        var severity = dependency.MissingDataPolicy switch
        {
            StrategyEngineMissingDataPolicy.Block => StrategyEngineValidationSeverity.Blocker,
            StrategyEngineMissingDataPolicy.AllowDegradedWithEvidence => StrategyEngineValidationSeverity.Warning,
            _ => StrategyEngineValidationSeverity.Info
        };
        var action = dependency.Kind is StrategyEngineDataDependencyKind.PriceBars or StrategyEngineDataDependencyKind.ProviderBackfill
            ? "/workstation/data"
            : "/workstation/reporting/evidence";
        var suffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" {reason}";
        return new(
            "dependency-missing",
            severity,
            $"data.{dependency.DependencyId}",
            $"{dependency.Description} is not available for {request.From:yyyy-MM-dd} to {request.To:yyyy-MM-dd}.{suffix}",
            action);
    }

    private static void ValidatePromotionSafety(
        StrategyEngineRunRequest request,
        List<StrategyEngineValidationFinding> findings)
    {
        if (request.Mode == StrategyEngineRunMode.LiveDisabled)
        {
            findings.Add(new(
                "live-promotion-disabled",
                StrategyEngineValidationSeverity.Info,
                nameof(request.Mode),
                "Live promotion remains disabled; use paper validation, replay, review, or report-pack handoff."));
        }
    }

    private static StrategyEngineValidationResult BuildResult(
        StrategyEngineRunRequest request,
        StrategyEngineDefinition? definition,
        IReadOnlyList<StrategyEngineDataAvailability> dataAvailability,
        IReadOnlyList<StrategyEngineValidationFinding> findings)
    {
        var inputHash = ComputeInputHash(request);
        var isBlocked = findings.Any(static finding => finding.Severity is StrategyEngineValidationSeverity.Blocker or StrategyEngineValidationSeverity.Error);
        var isDegraded = findings.Any(static finding => finding.Severity == StrategyEngineValidationSeverity.Warning);
        var warnings = findings
            .Where(static finding => finding.Severity is StrategyEngineValidationSeverity.Warning or StrategyEngineValidationSeverity.Info)
            .Select(static finding => finding.Message)
            .ToArray();
        var evidence = new StrategyEngineRunEvidence(
            EvidenceId: $"strategy-engine:{inputHash[..16]}",
            StrategyId: request.StrategyId?.Trim() ?? string.Empty,
            StrategyVersion: request.StrategyVersion?.Trim() ?? string.Empty,
            InputHash: inputHash,
            OutputHash: null,
            DataSource: request.DataSource?.Trim() ?? string.Empty,
            CapturedAt: DateTimeOffset.UtcNow,
            DataSourceReferences: dataAvailability
                .Select(static item => item.SourceReference)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Warnings: warnings,
            ReviewRoute: "/workstation/strategy",
            EvidenceRoute: "/workstation/reporting/evidence",
            ReportPackRoute: definition?.UiMetadata?.GetValueOrDefault("reportPackRoute") ?? "/workstation/reporting/report-packs");

        return new StrategyEngineValidationResult(!isBlocked, isDegraded, inputHash, findings, evidence);
    }

    public static string ComputeInputHash(StrategyEngineRunRequest request)
    {
        var canonical = new
        {
            strategyId = request.StrategyId?.Trim() ?? string.Empty,
            strategyVersion = request.StrategyVersion?.Trim() ?? string.Empty,
            mode = request.Mode.ToString(),
            from = request.From.ToUniversalTime(),
            to = request.To.ToUniversalTime(),
            dataSource = request.DataSource?.Trim() ?? string.Empty,
            universe = (request.Universe ?? [])
                .Select(static item => item.Trim().ToUpperInvariant())
                .Where(static item => item.Length > 0)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToArray(),
            parameters = (request.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                .Where(static item => !string.IsNullOrWhiteSpace(item.Key))
                .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static item => item.Key.Trim(), static item => item.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            costModel = request.CostModel?.Trim(),
            slippageModel = request.SlippageModel?.Trim(),
            riskConstraints = request.RiskConstraints?
                .Where(static item => !string.IsNullOrWhiteSpace(item.Key))
                .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static item => item.Key.Trim(), static item => item.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        };
        var json = JsonSerializer.Serialize(canonical, HashJsonOptions);
        return Sha256Digest.ComputeUtf8(json);
    }
}
