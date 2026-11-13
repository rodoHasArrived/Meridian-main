using System.Collections.Concurrent;
using System.Text;
using Meridian.Contracts.Workstation;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Reporting;

/// <summary>
/// Registers versioned reporting templates and manages their governed lifecycle and access policies.
/// </summary>
public sealed class ReportingTemplateRegistryService
{
    private readonly ConcurrentDictionary<string, ReportTemplateGovernanceRecordDto> _templates = new(StringComparer.Ordinal);
    private readonly IReportingTemplateGovernanceStore? _store;

    public ReportingTemplateRegistryService(IReportingTemplateGovernanceStore? store = null)
    {
        _store = store;
        foreach (var template in new DefaultReportingTemplateCatalog().ListTemplates())
        {
            var definition = new ReportTemplateDefinitionDto(
                new VersionedReportTemplateIdDto(template.TemplateId, ParseMajorVersion(template.Version)),
                template.Name,
                [],
                template.Sections.ToArray());
            var now = DateTimeOffset.UtcNow;
            var record = new ReportTemplateGovernanceRecordDto(
                definition,
                ReportTemplateLifecycleStatusDto.Approved,
                template.Family.ToString(),
                IsBuiltIn: true,
                IsLatestApproved: true,
                CreatedBy: "system",
                CreatedAt: now,
                UpdatedBy: "system",
                UpdatedAt: now,
                ValidationIssues: [],
                AuditTrail: [new ReportTemplateAuditEventDto(now, "system", "seed-built-in", ReportTemplateLifecycleStatusDto.Approved, ReportTemplateLifecycleStatusDto.Approved, "Built-in template catalog")],
                ApprovedBy: "system",
                ApprovedAt: now,
                DecisionRationale: "Built-in Reporting template",
                ApprovalReference: $"builtin:{template.TemplateId}@{template.Version}");
            _templates[ToKey(record)] = record;
        }

        foreach (var record in _store?.Load() ?? [])
        {
            if (!record.IsBuiltIn)
            {
                if (!_templates.TryAdd(ToKey(record), record))
                {
                    throw new InvalidDataException(
                        $"Duplicate report template ownership key '{ToKey(record)}'.");
                }
            }
        }

        ReconcileLatestApprovedTemplates();
    }

    public ReportTemplateDefinitionDto Register(ReportTemplateDefinitionDto definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var normalized = NormalizeDefinition(definition, "legacy-register");
        var now = DateTimeOffset.UtcNow;
        var validationIssues = ValidateDefinition(normalized);
        var record = new ReportTemplateGovernanceRecordDto(
            normalized,
            ReportTemplateLifecycleStatusDto.Approved,
            "Custom",
            IsBuiltIn: false,
            IsLatestApproved: true,
            CreatedBy: "legacy-register",
            CreatedAt: now,
            UpdatedBy: "legacy-register",
            UpdatedAt: now,
            ValidationIssues: validationIssues,
            AuditTrail: [new ReportTemplateAuditEventDto(now, "legacy-register", "register", ReportTemplateLifecycleStatusDto.Approved, ReportTemplateLifecycleStatusDto.Approved, "Compatibility template registration")],
            ApprovedBy: "legacy-register",
            ApprovedAt: now,
            DecisionRationale: "Compatibility registration");
        MarkPriorApprovedTemplatesNotLatest(
            normalized.TemplateId.Name,
            normalized.TemplateId.Version,
            tenantId: null,
            companyId: null);
        _templates[ToKey(record)] = record;
        PersistTemplates();
        return normalized;
    }

    public ReportTemplateDefinitionDto? Get(VersionedReportTemplateIdDto id) =>
        TryGetRecord(id, accessContext: null, out var template)
        && template.Status == ReportTemplateLifecycleStatusDto.Approved
            ? template.Definition
            : null;

    public ReportTemplateDefinitionDto? Get(
        VersionedReportTemplateIdDto id,
        ReportingAccessQueryContext? accessContext) =>
        TryGetRecord(id, accessContext, out var template)
        && template.Status == ReportTemplateLifecycleStatusDto.Approved
        && IsRecordInScope(template, accessContext)
            ? BindRecordToScope(template, accessContext).Definition
            : null;

    public IReadOnlyList<ReportTemplateGovernanceRecordDto> List(bool includeSuperseded = false) =>
        _templates.Values
            .Where(static record => record.IsBuiltIn
                || string.IsNullOrWhiteSpace(record.TenantId) && string.IsNullOrWhiteSpace(record.CompanyId))
            .Where(record => includeSuperseded || record.Status != ReportTemplateLifecycleStatusDto.Superseded)
            .OrderBy(record => record.Definition.TemplateId.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(record => record.Definition.TemplateId.Version)
            .ToArray();

    public IReadOnlyList<ReportTemplateGovernanceRecordDto> List(
        ReportingAccessQueryContext? accessContext,
        bool includeSuperseded = false)
    {
        var records = _templates.Values
            .Where(record => IsRecordInScope(record, accessContext))
            .Where(record => includeSuperseded || record.Status != ReportTemplateLifecycleStatusDto.Superseded)
            .Select(record => BindRecordToScope(record, accessContext))
            .OrderBy(record => record.Definition.TemplateId.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(record => record.Definition.TemplateId.Version)
            .ToArray();
        var latestApprovedByName = records
            .Where(static record => record.Status == ReportTemplateLifecycleStatusDto.Approved)
            .GroupBy(static record => record.Definition.TemplateId.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Max(record => record.Definition.TemplateId.Version),
                StringComparer.OrdinalIgnoreCase);
        return records
            .Select(record => record with
            {
                IsLatestApproved = record.Status == ReportTemplateLifecycleStatusDto.Approved
                    && latestApprovedByName.TryGetValue(record.Definition.TemplateId.Name, out var version)
                    && record.Definition.TemplateId.Version == version
            })
            .ToArray();
    }

    public ReportTemplateGovernanceRecordDto CreateDraft(
        ReportTemplateDraftRequestDto request,
        string actor,
        string? companyId = null,
        IReadOnlyList<string>? reportGroupPrincipalIds = null,
        string? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        if ((normalizedTenantId is null) != (normalizedCompanyId is null))
        {
            throw new UnauthorizedAccessException(
                "Report template ownership requires tenant and company scope together.");
        }

        var name = NormalizeIdentifier(request.Name);
        var latestVersion = _templates.Values
            .Where(record => string.Equals(record.Definition.TemplateId.Name, name, StringComparison.OrdinalIgnoreCase))
            .Where(record => record.IsBuiltIn
                || string.Equals(record.TenantId, normalizedTenantId, StringComparison.Ordinal)
                && string.Equals(record.CompanyId, normalizedCompanyId, StringComparison.Ordinal))
            .Select(record => record.Definition.TemplateId.Version)
            .DefaultIfEmpty(0)
            .Max();
        var nextVersion = latestVersion + 1;
        var basedOnTemplateId = request.BasedOnVersion is { } basedOnVersion
            ? new VersionedReportTemplateIdDto(name, basedOnVersion)
            : latestVersion > 0
                ? new VersionedReportTemplateIdDto(name, latestVersion)
                : null;
        var definition = NormalizeDefinition(new ReportTemplateDefinitionDto(
            new VersionedReportTemplateIdDto(name, nextVersion),
            request.DisplayName,
            request.Parameters,
            request.Sections,
            request.Grids,
            request.AccessPolicy),
            actor,
            normalizedCompanyId,
            reportGroupPrincipalIds);
        if (normalizedCompanyId is not null
            && !string.Equals(definition.AccessPolicy?.CompanyId, normalizedCompanyId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The report template access policy belongs to another company scope.");
        }
        var now = DateTimeOffset.UtcNow;
        var record = new ReportTemplateGovernanceRecordDto(
            definition,
            ReportTemplateLifecycleStatusDto.Draft,
            string.IsNullOrWhiteSpace(request.Family) ? "Custom" : request.Family.Trim(),
            IsBuiltIn: false,
            IsLatestApproved: false,
            CreatedBy: actor.Trim(),
            CreatedAt: now,
            UpdatedBy: actor.Trim(),
            UpdatedAt: now,
            ValidationIssues: ValidateDefinition(definition),
            AuditTrail: [new ReportTemplateAuditEventDto(now, actor.Trim(), "draft", ReportTemplateLifecycleStatusDto.Draft, ReportTemplateLifecycleStatusDto.Draft, request.Rationale?.Trim())],
            DecisionRationale: string.IsNullOrWhiteSpace(request.Rationale) ? null : request.Rationale.Trim(),
            BasedOnTemplateId: basedOnTemplateId,
            TenantId: normalizedTenantId,
            CompanyId: normalizedCompanyId);
        _templates[ToKey(record)] = record;
        PersistTemplates();
        return record;
    }

    public ReportTemplateGovernanceRecordDto Submit(
        VersionedReportTemplateIdDto id,
        string actor,
        string? note = null,
        ReportingAccessQueryContext? accessContext = null)
    {
        var record = GetRecord(id, accessContext);
        if (record.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in report templates are already approved and cannot be submitted.");
        }

        if (record.Status is not (ReportTemplateLifecycleStatusDto.Draft or ReportTemplateLifecycleStatusDto.Rejected))
        {
            throw new InvalidOperationException($"Template {id.Name}@v{id.Version} cannot be submitted from {record.Status}.");
        }

        var validationIssues = ValidateDefinition(record.Definition);
        if (validationIssues.Count > 0)
        {
            throw new InvalidOperationException($"Template {id.Name}@v{id.Version} is not ready for review: {string.Join("; ", validationIssues)}");
        }

        return Transition(record, ReportTemplateLifecycleStatusDto.InReview, actor, "submit", note);
    }

    public ReportTemplateGovernanceRecordDto Approve(
        VersionedReportTemplateIdDto id,
        ReportTemplateDecisionRequestDto request,
        string actor,
        ReportingAccessQueryContext? accessContext = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Rationale);
        var record = GetRecord(id, accessContext);
        if (record.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in report templates are immutable.");
        }

        if (record.Status != ReportTemplateLifecycleStatusDto.InReview)
        {
            throw new InvalidOperationException($"Template {id.Name}@v{id.Version} must be in review before approval.");
        }

        if (string.Equals(record.CreatedBy, actor, StringComparison.Ordinal)
            || string.Equals(record.SubmittedBy, actor, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The template creator or submitter cannot approve the same template version.");
        }

        var validationIssues = ValidateDefinition(record.Definition);
        if (validationIssues.Count > 0)
        {
            throw new InvalidOperationException($"Template {id.Name}@v{id.Version} cannot be approved: {string.Join("; ", validationIssues)}");
        }

        MarkPriorApprovedTemplatesNotLatest(
            record.Definition.TemplateId.Name,
            record.Definition.TemplateId.Version,
            record.TenantId,
            record.CompanyId);
        var approved = Transition(record, ReportTemplateLifecycleStatusDto.Approved, actor, "approve", request.Rationale.Trim()) with
        {
            ApprovedBy = actor.Trim(),
            ApprovedAt = DateTimeOffset.UtcNow,
            DecisionRationale = request.Rationale.Trim(),
            ApprovalReference = string.IsNullOrWhiteSpace(request.ApprovalReference) ? null : request.ApprovalReference.Trim(),
            IsLatestApproved = true
        };
        _templates[ToKey(approved)] = approved;
        PersistTemplates();
        return approved;
    }

    public ReportTemplateGovernanceRecordDto Reject(
        VersionedReportTemplateIdDto id,
        ReportTemplateDecisionRequestDto request,
        string actor,
        ReportingAccessQueryContext? accessContext = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Rationale);
        var record = GetRecord(id, accessContext);
        if (record.IsBuiltIn)
        {
            throw new InvalidOperationException("Built-in report templates are immutable.");
        }

        if (record.Status != ReportTemplateLifecycleStatusDto.InReview)
        {
            throw new InvalidOperationException($"Template {id.Name}@v{id.Version} must be in review before rejection.");
        }

        var rejected = Transition(record, ReportTemplateLifecycleStatusDto.Rejected, actor, "reject", request.Rationale.Trim()) with
        {
            RejectedBy = actor.Trim(),
            RejectedAt = DateTimeOffset.UtcNow,
            DecisionRationale = request.Rationale.Trim(),
            ApprovalReference = string.IsNullOrWhiteSpace(request.ApprovalReference) ? null : request.ApprovalReference.Trim()
        };
        _templates[ToKey(rejected)] = rejected;
        PersistTemplates();
        return rejected;
    }

    public RenderReportTemplateResponseDto Render(
        RenderReportTemplateRequestDto request,
        ReportingAccessQueryContext? accessContext = null)
    {
        var template = Get(request.TemplateId, accessContext) ?? throw new ArgumentException("Template not found.");
        var missing = template.Parameters.Where(p => p.Required && !request.Parameters.ContainsKey(p.Name)).Select(p => p.Name).ToArray();
        var sections = template.Sections is { Count: > 0 }
            ? string.Join(',', template.Sections)
            : "sections:not-configured";
        var gridDefinitions = request.Grids ?? template.Grids;
        var grids = ReportWriterGridEngine.RenderGrids(gridDefinitions, request.DatasetRows);
        var gridSummary = grids.Count > 0
            ? $";grids={string.Join(',', grids.Select(static grid => $"{grid.GridId}:{grid.Rows.Count}r"))}"
            : ";grids=0";
        var rendered = $"template:{template.TemplateId.Name}@v{template.TemplateId.Version};sections={sections}{gridSummary};" + string.Join(';', request.Parameters.OrderBy(k => k.Key).Select(kvp => $"{kvp.Key}={kvp.Value}"));
        var warnings = grids.SelectMany(static grid => grid.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new RenderReportTemplateResponseDto(template.TemplateId, rendered, missing, grids, warnings);
    }

    public bool CanAccess(VersionedReportTemplateIdDto id, ReportingAccessQueryContext? context)
    {
        var template = Get(id, context);
        return template is not null && ReportingAccessPolicyEvaluator.Evaluate(template.AccessPolicy, context).IsAccessible;
    }

    private static bool IsRecordInScope(
        ReportTemplateGovernanceRecordDto record,
        ReportingAccessQueryContext? accessContext)
    {
        if (record.IsBuiltIn)
        {
            return true;
        }

        if (accessContext is null)
        {
            return string.IsNullOrWhiteSpace(record.TenantId)
                && string.IsNullOrWhiteSpace(record.CompanyId);
        }

        var tenantId = NormalizeOptional(accessContext.TenantId);
        var companyId = NormalizeOptional(accessContext.CompanyId);
        if (accessContext.RequireBoundScope
            && (string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId)
                || tenantId is null
                || companyId is null))
        {
            return false;
        }

        if (tenantId is null || companyId is null)
        {
            return !accessContext.RequireBoundScope
                && string.IsNullOrWhiteSpace(record.TenantId)
                && string.IsNullOrWhiteSpace(record.CompanyId);
        }

        return string.Equals(record.TenantId, tenantId, StringComparison.Ordinal)
            && string.Equals(record.CompanyId, companyId, StringComparison.Ordinal);
    }

    private static ReportTemplateGovernanceRecordDto BindRecordToScope(
        ReportTemplateGovernanceRecordDto record,
        ReportingAccessQueryContext? accessContext)
    {
        var policy = ReportingAccessPolicyEvaluator.Normalize(record.Definition.AccessPolicy);
        if (policy.Mode != ReportAccessModeDto.CompanyWide
            || !string.IsNullOrWhiteSpace(policy.CompanyId)
            || string.IsNullOrWhiteSpace(accessContext?.CompanyId))
        {
            return record;
        }

        return record with
        {
            Definition = record.Definition with
            {
                AccessPolicy = policy with { CompanyId = accessContext.CompanyId.Trim() }
            }
        };
    }

    private static string ToKey(ReportTemplateGovernanceRecordDto record) =>
        ToKey(record.Definition.TemplateId, record.TenantId, record.CompanyId);

    private static string ToKey(
        VersionedReportTemplateIdDto id,
        string? tenantId,
        string? companyId) =>
        $"{tenantId?.Trim() ?? string.Empty}\u001f{companyId?.Trim() ?? string.Empty}\u001f{id.Name.Trim().ToLowerInvariant()}:{id.Version}";

    private bool TryGetRecord(
        VersionedReportTemplateIdDto id,
        ReportingAccessQueryContext? accessContext,
        out ReportTemplateGovernanceRecordDto record)
    {
        var tenantId = NormalizeOptional(accessContext?.TenantId);
        var companyId = NormalizeOptional(accessContext?.CompanyId);
        if (tenantId is not null
            && companyId is not null
            && _templates.TryGetValue(ToKey(id, tenantId, companyId), out var scoped))
        {
            record = scoped;
            return true;
        }

        if (_templates.TryGetValue(
                ToKey(id, tenantId: null, companyId: null),
                out var global))
        {
            record = global;
            return true;
        }

        record = null!;
        return false;
    }

    private static int ParseMajorVersion(string version)
    {
        var major = version.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(major, out var value) && value > 0 ? value : 1;
    }

    private static string NormalizeIdentifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant();
    }

    private static ReportTemplateDefinitionDto NormalizeDefinition(
        ReportTemplateDefinitionDto definition,
        string? defaultOwnerPrincipalId = null,
        string? defaultCompanyId = null,
        IReadOnlyList<string>? defaultReportGroupPrincipalIds = null)
    {
        var id = new VersionedReportTemplateIdDto(
            NormalizeIdentifier(definition.TemplateId.Name),
            definition.TemplateId.Version);
        if (id.Version <= 0)
        {
            throw new ArgumentException("Template version must be greater than zero.");
        }

        var parameters = definition.Parameters
            .Where(static parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .Select(static parameter => parameter with { Name = parameter.Name.Trim() })
            .GroupBy(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sections = definition.Sections?
            .Where(static section => !string.IsNullOrWhiteSpace(section))
            .Select(static section => section.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static section => section, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        var grids = NormalizeGrids(definition.Grids);

        return definition with
        {
            TemplateId = id,
            DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? string.Empty : definition.DisplayName.Trim(),
            Parameters = parameters,
            Sections = sections,
            Grids = grids,
            AccessPolicy = NormalizeAccessPolicyForCaller(
                definition.AccessPolicy,
                defaultOwnerPrincipalId,
                defaultCompanyId,
                defaultReportGroupPrincipalIds)
        };
    }

    private static ReportAccessPolicyDto NormalizeAccessPolicyForCaller(
        ReportAccessPolicyDto? policy,
        string? defaultOwnerPrincipalId,
        string? defaultCompanyId,
        IReadOnlyList<string>? defaultReportGroupPrincipalIds)
    {
        var normalized = ReportingAccessPolicyEvaluator.Normalize(policy, defaultOwnerPrincipalId);
        var companyId = string.IsNullOrWhiteSpace(normalized.CompanyId)
            ? NormalizeOptional(defaultCompanyId)
            : normalized.CompanyId;

        if (normalized.Mode == ReportAccessModeDto.Restricted
            && (normalized.Principals is null || normalized.Principals.Count == 0))
        {
            var groupPrincipals = NormalizeStringList(defaultReportGroupPrincipalIds)
                .Select(static group => new ReportAccessPrincipalDto(
                    ReportAccessPrincipalKindDto.Group,
                    group,
                    group))
                .ToArray();

            if (groupPrincipals.Length > 0)
            {
                return ReportingAccessPolicyEvaluator.Normalize(normalized with
                {
                    CompanyId = companyId,
                    Principals = groupPrincipals
                }, defaultOwnerPrincipalId);
            }
        }

        return normalized with { CompanyId = companyId };
    }

    private static IReadOnlyList<ReportWriterGridDefinitionDto> NormalizeGrids(IReadOnlyList<ReportWriterGridDefinitionDto>? grids) =>
        grids?
            .Where(static grid => grid is not null)
            .Select(static grid => grid with
            {
                GridId = string.IsNullOrWhiteSpace(grid.GridId) ? string.Empty : grid.GridId.Trim(),
                Title = string.IsNullOrWhiteSpace(grid.Title) ? string.Empty : grid.Title.Trim(),
                RowFields = NormalizeStringList(grid.RowFields),
                ColumnFields = NormalizeStringList(grid.ColumnFields),
                Metrics = NormalizeGridMetrics(grid.Metrics),
                Formulas = NormalizeGridFormulas(grid.Formulas),
                SortBy = string.IsNullOrWhiteSpace(grid.SortBy) ? null : grid.SortBy.Trim(),
                Filters = NormalizeGridFilters(grid.Filters)
            })
            .ToArray() ?? [];

    private static IReadOnlyList<string> NormalizeStringList(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>(values.Count);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        return normalized;
    }

    private static IReadOnlyList<ReportWriterMetricDefinitionDto> NormalizeGridMetrics(IReadOnlyList<ReportWriterMetricDefinitionDto>? metrics) =>
        metrics?
            .Where(static metric => metric is not null)
            .Select(static metric => metric with
            {
                Name = string.IsNullOrWhiteSpace(metric.Name) ? string.Empty : metric.Name.Trim(),
                SourceField = string.IsNullOrWhiteSpace(metric.SourceField) ? string.Empty : metric.SourceField.Trim(),
                Label = string.IsNullOrWhiteSpace(metric.Label) ? null : metric.Label.Trim()
            })
            .GroupBy(static metric => metric.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray() ?? [];

    private static IReadOnlyList<ReportWriterFormulaDefinitionDto> NormalizeGridFormulas(IReadOnlyList<ReportWriterFormulaDefinitionDto>? formulas) =>
        formulas?
            .Where(static formula => formula is not null)
            .Select(static formula => formula with
            {
                Name = string.IsNullOrWhiteSpace(formula.Name) ? string.Empty : formula.Name.Trim(),
                Expression = string.IsNullOrWhiteSpace(formula.Expression) ? string.Empty : formula.Expression.Trim(),
                Label = string.IsNullOrWhiteSpace(formula.Label) ? null : formula.Label.Trim()
            })
            .GroupBy(static formula => formula.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray() ?? [];

    private static IReadOnlyList<ReportWriterFilterDefinitionDto> NormalizeGridFilters(IReadOnlyList<ReportWriterFilterDefinitionDto>? filters) =>
        filters?
            .Where(static filter => filter is not null && !string.IsNullOrWhiteSpace(filter.Field))
            .Select(static filter => filter with
            {
                Field = filter.Field.Trim(),
                Value = string.IsNullOrWhiteSpace(filter.Value) ? null : filter.Value.Trim(),
                Label = string.IsNullOrWhiteSpace(filter.Label) ? null : filter.Label.Trim()
            })
            .GroupBy(static filter => $"{filter.Field}:{filter.Operator}:{filter.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray() ?? [];

    private static IReadOnlyList<string> ValidateDefinition(ReportTemplateDefinitionDto definition)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(definition.TemplateId.Name))
        {
            issues.Add("Template name is required.");
        }

        if (definition.TemplateId.Version <= 0)
        {
            issues.Add("Template version must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(definition.DisplayName))
        {
            issues.Add("Display name is required.");
        }

        issues.AddRange(ReportingAccessPolicyEvaluator.Validate(definition.AccessPolicy));

        var grids = definition.Grids ?? [];
        if ((definition.Sections is null || definition.Sections.Count == 0) && grids.Count == 0)
        {
            issues.Add("At least one report section or report writer grid is required.");
        }

        var duplicateParameters = definition.Parameters
            .Where(static parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .GroupBy(static parameter => parameter.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        if (duplicateParameters.Length > 0)
        {
            issues.Add($"Parameter names must be unique: {string.Join(", ", duplicateParameters)}.");
        }

        var duplicateGridIds = grids
            .Where(static grid => !string.IsNullOrWhiteSpace(grid.GridId))
            .GroupBy(static grid => grid.GridId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        if (duplicateGridIds.Length > 0)
        {
            issues.Add($"Report writer grid ids must be unique: {string.Join(", ", duplicateGridIds)}.");
        }

        foreach (var grid in grids)
        {
            if (string.IsNullOrWhiteSpace(grid.GridId))
            {
                issues.Add("Report writer grid id is required.");
            }

            if (string.IsNullOrWhiteSpace(grid.Title))
            {
                issues.Add($"Report writer grid '{grid.GridId}' requires a title.");
            }

            if (grid.Kind is ReportWriterGridKindDto.Pivot or ReportWriterGridKindDto.TopN or ReportWriterGridKindDto.Contribution
                && (grid.Metrics is null || grid.Metrics.Count == 0))
            {
                issues.Add($"Report writer grid '{grid.GridId}' requires at least one metric.");
            }

            if (grid.Kind == ReportWriterGridKindDto.TopN && grid.TopN is <= 0)
            {
                issues.Add($"Report writer grid '{grid.GridId}' Top-N limit must be greater than zero.");
            }

            var duplicateMetricNames = (grid.Metrics ?? [])
                .Where(static metric => !string.IsNullOrWhiteSpace(metric.Name))
                .GroupBy(static metric => metric.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .ToArray();
            if (duplicateMetricNames.Length > 0)
            {
                issues.Add($"Report writer grid '{grid.GridId}' metric names must be unique: {string.Join(", ", duplicateMetricNames)}.");
            }

            if (grid.Kind == ReportWriterGridKindDto.Contribution)
            {
                var reservedContributionFields = ContributionGeneratedFields;
                foreach (var metric in grid.Metrics ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(metric.Name) && reservedContributionFields.Contains(metric.Name.Trim()))
                    {
                        issues.Add($"Report writer grid '{grid.GridId}' metric '{metric.Name}' uses reserved contribution field '{metric.Name.Trim()}'.");
                    }
                }

                foreach (var formula in grid.Formulas ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(formula.Name) && reservedContributionFields.Contains(formula.Name.Trim()))
                    {
                        issues.Add($"Report writer grid '{grid.GridId}' formula '{formula.Name}' uses reserved contribution field '{formula.Name.Trim()}'.");
                    }
                }
            }

            foreach (var metric in grid.Metrics ?? [])
            {
                if (string.IsNullOrWhiteSpace(metric.Name))
                {
                    issues.Add($"Report writer grid '{grid.GridId}' metric name is required.");
                }

                if (string.IsNullOrWhiteSpace(metric.SourceField))
                {
                    issues.Add($"Report writer grid '{grid.GridId}' metric '{metric.Name}' requires a source field.");
                }
            }

            foreach (var formula in grid.Formulas ?? [])
            {
                if (string.IsNullOrWhiteSpace(formula.Name))
                {
                    issues.Add($"Report writer grid '{grid.GridId}' formula name is required.");
                }

                if (string.IsNullOrWhiteSpace(formula.Expression))
                {
                    issues.Add($"Report writer grid '{grid.GridId}' formula '{formula.Name}' requires an expression.");
                }
            }

            issues.AddRange(ValidateGridFormulas(grid));

            foreach (var filter in grid.Filters ?? [])
            {
                if (string.IsNullOrWhiteSpace(filter.Field))
                {
                    issues.Add($"Report writer grid '{grid.GridId}' filter field is required.");
                }

                if (filter.Operator is not (ReportWriterFilterOperatorDto.IsBlank or ReportWriterFilterOperatorDto.IsNotBlank)
                    && string.IsNullOrWhiteSpace(filter.Value))
                {
                    issues.Add($"Report writer grid '{grid.GridId}' filter '{filter.Field}' requires a value.");
                }
            }
        }

        return issues;
    }

    private static IReadOnlyList<string> ValidateGridFormulas(ReportWriterGridDefinitionDto grid)
    {
        var formulas = (grid.Formulas ?? [])
            .Where(static formula => !string.IsNullOrWhiteSpace(formula.Name) && !string.IsNullOrWhiteSpace(formula.Expression))
            .ToArray();
        if (formulas.Length == 0)
        {
            return [];
        }

        var issues = new List<string>();
        var metricNames = (grid.Metrics ?? [])
            .Where(static metric => !string.IsNullOrWhiteSpace(metric.Name))
            .Select(static metric => metric.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var totalFields = (grid.Metrics ?? [])
            .Where(static metric => !string.IsNullOrWhiteSpace(metric.Name))
            .Select(static metric => metric.Name.Trim())
            .Concat((grid.Metrics ?? [])
                .Where(static metric => !string.IsNullOrWhiteSpace(metric.SourceField))
                .Select(static metric => metric.SourceField.Trim()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var formulaIndexes = formulas
            .Select((formula, index) => (formula.Name, Index: index))
            .ToDictionary(static item => item.Name.Trim(), static item => item.Index, StringComparer.OrdinalIgnoreCase);
        var availableRowReferences = metricNames
            .Concat(formulaIndexes.Keys)
            .Concat(ResolveGeneratedFormulaReferences(grid))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var generatedField in ResolveGeneratedFormulaReferences(grid))
        {
            totalFields.Add(generatedField);
        }
        var dependencies = formulas.ToDictionary(
            static formula => formula.Name.Trim(),
            static _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var formula in formulas)
        {
            var formulaName = formula.Name.Trim();
            var references = ExtractFormulaReferences(formula.Expression, availableRowReferences.Contains);
            foreach (var reference in references.RowReferences)
            {
                if (!availableRowReferences.Contains(reference))
                {
                    issues.Add($"Report writer grid '{grid.GridId}' formula '{formulaName}' references unknown metric or formula '{reference}'.");
                    continue;
                }

                if (formulaIndexes.TryGetValue(reference, out var referencedFormulaIndex))
                {
                    dependencies[formulaName].Add(reference);
                    if (string.Equals(reference, formulaName, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add($"Report writer grid '{grid.GridId}' formula '{formulaName}' cannot reference itself.");
                    }
                    else if (referencedFormulaIndex > formulaIndexes[formulaName])
                    {
                        issues.Add($"Report writer grid '{grid.GridId}' formula '{formulaName}' references formula '{reference}' before it is evaluated.");
                    }
                }
            }

            foreach (var reference in references.TotalReferences)
            {
                if (!totalFields.Contains(reference))
                {
                    issues.Add($"Report writer grid '{grid.GridId}' formula '{formulaName}' total field '{reference}' is not a configured metric or metric source field.");
                }
            }
        }

        foreach (var cycle in FindFormulaCycles(dependencies))
        {
            issues.Add($"Report writer grid '{grid.GridId}' formula dependencies cannot be circular: {cycle}.");
        }

        return issues
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static readonly IReadOnlySet<string> ContributionGeneratedFields = new HashSet<string>(
        ["contributionPercent", "contributionAbsPercent"],
        StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> ResolveGeneratedFormulaReferences(ReportWriterGridDefinitionDto grid) =>
        grid.Kind == ReportWriterGridKindDto.Contribution ? ContributionGeneratedFields : [];

    private static FormulaReferenceSet ExtractFormulaReferences(
        string expression,
        Func<string, bool> isKnownRowReference)
    {
        var rowReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        while (position < expression.Length)
        {
            var current = expression[position];
            if (current == '{')
            {
                if (TryReadBraceReference(expression, position, out var reference, out var next))
                {
                    rowReferences.Add(reference);
                    position = next;
                    continue;
                }

                position++;
                continue;
            }

            if (!IsIdentifierStart(current))
            {
                position++;
                continue;
            }

            var identifierStart = position;
            var identifier = ReadIdentifier(expression, ref position);
            var nextToken = SkipWhitespace(expression, position);
            if (string.Equals(identifier, "total", StringComparison.OrdinalIgnoreCase)
                && nextToken < expression.Length
                && expression[nextToken] == '(')
            {
                if (TryReadTotalArgument(expression, nextToken + 1, out var totalReference, out var afterTotal))
                {
                    totalReferences.Add(totalReference);
                    position = afterTotal;
                    continue;
                }

                position = identifierStart + identifier.Length;
                continue;
            }

            if (IsFormulaFunctionIdentifier(identifier)
                && nextToken < expression.Length
                && expression[nextToken] == '(')
            {
                position = nextToken + 1;
                continue;
            }

            // Same hyphen rule as the grid engine: 'pnl-fees' is one reference only when that
            // name is known; otherwise the known prefix is the reference and the rest is re-read.
            var referenceLength = Meridian.Reporting.ReportWriterFormulaReferences.ResolveReferenceLength(
                identifier,
                isKnownRowReference);
            rowReferences.Add(identifier[..referenceLength]);
            position = identifierStart + referenceLength;
        }

        return new FormulaReferenceSet(rowReferences.ToArray(), totalReferences.ToArray());
    }

    private static bool TryReadBraceReference(
        string expression,
        int openBracePosition,
        out string reference,
        out int nextPosition)
    {
        reference = string.Empty;
        nextPosition = openBracePosition + 1;
        var close = expression.IndexOf('}', openBracePosition + 1);
        if (close < 0)
        {
            return false;
        }

        reference = expression[(openBracePosition + 1)..close].Trim();
        nextPosition = close + 1;
        return reference.Length > 0;
    }

    private static bool TryReadTotalArgument(
        string expression,
        int argumentStart,
        out string reference,
        out int nextPosition)
    {
        reference = string.Empty;
        nextPosition = argumentStart;
        var start = SkipWhitespace(expression, argumentStart);
        if (start >= expression.Length)
        {
            return false;
        }

        if (expression[start] == '{')
        {
            if (!TryReadBraceReference(expression, start, out reference, out var afterBrace))
            {
                return false;
            }

            nextPosition = SkipWhitespace(expression, afterBrace);
            if (nextPosition < expression.Length && expression[nextPosition] == ')')
            {
                nextPosition++;
                return true;
            }

            return false;
        }

        var close = expression.IndexOf(')', start);
        if (close < 0)
        {
            return false;
        }

        reference = expression[start..close].Trim();
        nextPosition = close + 1;
        return reference.Length > 0;
    }

    private static string ReadIdentifier(string expression, ref int position)
    {
        var start = position;
        while (position < expression.Length && IsIdentifierPart(expression[position]))
        {
            position++;
        }

        return expression[start..position];
    }

    private static int SkipWhitespace(string expression, int position)
    {
        while (position < expression.Length && char.IsWhiteSpace(expression[position]))
        {
            position++;
        }

        return position;
    }

    private static bool IsIdentifierStart(char value) =>
        char.IsLetter(value) || value == '_';

    private static bool IsIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-' or '.';

    private static bool IsFormulaFunctionIdentifier(string identifier) =>
        string.Equals(identifier, "if", StringComparison.OrdinalIgnoreCase)
        || string.Equals(identifier, "abs", StringComparison.OrdinalIgnoreCase)
        || string.Equals(identifier, "min", StringComparison.OrdinalIgnoreCase)
        || string.Equals(identifier, "max", StringComparison.OrdinalIgnoreCase)
        || string.Equals(identifier, "safeDivide", StringComparison.OrdinalIgnoreCase)
        || string.Equals(identifier, "percent", StringComparison.OrdinalIgnoreCase)
        || string.Equals(identifier, "basisPoints", StringComparison.OrdinalIgnoreCase)
        || string.Equals(identifier, "round", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> FindFormulaCycles(IReadOnlyDictionary<string, HashSet<string>> dependencies)
    {
        var cycles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var formula in dependencies.Keys)
        {
            Visit(formula, formula, [], dependencies, cycles);
        }

        return cycles.ToArray();
    }

    private static void Visit(
        string root,
        string current,
        IReadOnlyList<string> path,
        IReadOnlyDictionary<string, HashSet<string>> dependencies,
        ISet<string> cycles)
    {
        var nextPath = path.Append(current).ToArray();
        if (!dependencies.TryGetValue(current, out var next))
        {
            return;
        }

        foreach (var dependency in next)
        {
            if (!dependencies.ContainsKey(dependency))
            {
                continue;
            }

            if (string.Equals(dependency, root, StringComparison.OrdinalIgnoreCase))
            {
                cycles.Add(string.Join(" -> ", nextPath.Append(root)));
                continue;
            }

            if (nextPath.Contains(dependency, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            Visit(root, dependency, nextPath, dependencies, cycles);
        }
    }

    private sealed record FormulaReferenceSet(
        IReadOnlyList<string> RowReferences,
        IReadOnlyList<string> TotalReferences);

    private ReportTemplateGovernanceRecordDto GetRecord(
        VersionedReportTemplateIdDto id,
        ReportingAccessQueryContext? accessContext = null) =>
        TryGetRecord(id, accessContext, out var record)
        && IsRecordInScope(record, accessContext)
            ? record
            : throw new KeyNotFoundException($"Template {id.Name}@v{id.Version} was not found.");

    private ReportTemplateGovernanceRecordDto Transition(
        ReportTemplateGovernanceRecordDto record,
        ReportTemplateLifecycleStatusDto target,
        string actor,
        string action,
        string? note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var now = DateTimeOffset.UtcNow;
        var next = record with
        {
            Status = target,
            UpdatedBy = actor.Trim(),
            UpdatedAt = now,
            ValidationIssues = ValidateDefinition(record.Definition),
            SubmittedBy = target == ReportTemplateLifecycleStatusDto.InReview ? actor.Trim() : record.SubmittedBy,
            SubmittedAt = target == ReportTemplateLifecycleStatusDto.InReview ? now : record.SubmittedAt,
            AuditTrail = record.AuditTrail.Append(new ReportTemplateAuditEventDto(now, actor.Trim(), action, record.Status, target, note)).ToArray()
        };
        _templates[ToKey(next)] = next;
        PersistTemplates();
        return next;
    }

    private void MarkPriorApprovedTemplatesNotLatest(
        string templateName,
        int newLatestVersion,
        string? tenantId,
        string? companyId)
    {
        foreach (var record in _templates.Values.Where(record =>
                     string.Equals(record.Definition.TemplateId.Name, templateName, StringComparison.OrdinalIgnoreCase)
                     && record.Definition.TemplateId.Version != newLatestVersion
                     && record.Status == ReportTemplateLifecycleStatusDto.Approved
                     && string.Equals(record.TenantId, tenantId, StringComparison.Ordinal)
                     && string.Equals(record.CompanyId, companyId, StringComparison.Ordinal)))
        {
            var updated = record with
            {
                Status = record.IsBuiltIn ? record.Status : ReportTemplateLifecycleStatusDto.Superseded,
                IsLatestApproved = false
            };
            _templates[ToKey(updated)] = updated;
        }
    }

    private void ReconcileLatestApprovedTemplates()
    {
        foreach (var group in _templates.Values.GroupBy(record => new
        {
            TenantId = record.TenantId ?? string.Empty,
            CompanyId = record.CompanyId ?? string.Empty,
            Name = record.Definition.TemplateId.Name.ToLowerInvariant()
        }))
        {
            var latestApprovedVersion = group
                .Where(static record => record.Status == ReportTemplateLifecycleStatusDto.Approved)
                .Select(static record => (int?)record.Definition.TemplateId.Version)
                .Max();
            if (latestApprovedVersion is null)
            {
                continue;
            }

            foreach (var record in group)
            {
                var updated = record with
                {
                    IsLatestApproved = record.Status == ReportTemplateLifecycleStatusDto.Approved
                        && record.Definition.TemplateId.Version == latestApprovedVersion.Value
                };
                _templates[ToKey(updated)] = updated;
            }
        }
    }

    private void PersistTemplates()
    {
        _store?.Save(_templates.Values.Where(static record => !record.IsBuiltIn).ToArray());
    }
}
