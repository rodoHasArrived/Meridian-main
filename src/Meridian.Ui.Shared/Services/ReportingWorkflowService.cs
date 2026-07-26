using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed class ReportTemplateRegistryService
{
    private readonly ConcurrentDictionary<string, ReportTemplateGovernanceRecordDto> _templates = new(StringComparer.Ordinal);
    private readonly IReportTemplateGovernanceStore? _store;

    public ReportTemplateRegistryService(IReportTemplateGovernanceStore? store = null)
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
        ReportAccessQueryContext? accessContext) =>
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
        ReportAccessQueryContext? accessContext,
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
        ReportAccessQueryContext? accessContext = null)
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
        ReportAccessQueryContext? accessContext = null)
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
        ReportAccessQueryContext? accessContext = null)
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
        ReportAccessQueryContext? accessContext = null)
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

    public bool CanAccess(VersionedReportTemplateIdDto id, ReportAccessQueryContext? context)
    {
        var template = Get(id, context);
        return template is not null && ReportAccessPolicyEvaluator.Evaluate(template.AccessPolicy, context).IsAccessible;
    }

    private static bool IsRecordInScope(
        ReportTemplateGovernanceRecordDto record,
        ReportAccessQueryContext? accessContext)
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
        ReportAccessQueryContext? accessContext)
    {
        var policy = ReportAccessPolicyEvaluator.Normalize(record.Definition.AccessPolicy);
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
        ReportAccessQueryContext? accessContext,
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
        var normalized = ReportAccessPolicyEvaluator.Normalize(policy, defaultOwnerPrincipalId);
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
                return ReportAccessPolicyEvaluator.Normalize(normalized with
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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

        issues.AddRange(ReportAccessPolicyEvaluator.Validate(definition.AccessPolicy));

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
            var references = ExtractFormulaReferences(formula.Expression);
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

    private static FormulaReferenceSet ExtractFormulaReferences(string expression)
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

            rowReferences.Add(identifier);
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
        string.Equals(identifier, "abs", StringComparison.OrdinalIgnoreCase)
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
        ReportAccessQueryContext? accessContext = null) =>
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

public sealed record ReportTemplateGovernanceStoreOptions(string SnapshotPath);

public interface IReportTemplateGovernanceStore
{
    IReadOnlyList<ReportTemplateGovernanceRecordDto> Load();
    void Save(IReadOnlyList<ReportTemplateGovernanceRecordDto> records);
}

public sealed class FileReportTemplateGovernanceStore : IReportTemplateGovernanceStore
{
    private readonly ReportTemplateGovernanceStoreOptions _options;
    private readonly ILogger<FileReportTemplateGovernanceStore> _logger;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FileReportTemplateGovernanceStore(
        ReportTemplateGovernanceStoreOptions options,
        ILogger<FileReportTemplateGovernanceStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SnapshotPath);
    }

    public IReadOnlyList<ReportTemplateGovernanceRecordDto> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_options.SnapshotPath))
            {
                return [];
            }

            try
            {
                var json = File.ReadAllText(_options.SnapshotPath);
                var records = JsonSerializer.Deserialize<ReportTemplateGovernanceSnapshot>(json, _jsonOptions)?.Records
                    ?? throw new JsonException("Report template governance snapshot deserialized to null.");
                ValidateRecords(records);
                return records;
            }
            catch (Exception ex) when (ex is IOException
                                            or JsonException
                                            or InvalidDataException
                                            or ArgumentException
                                            or UnauthorizedAccessException)
            {
                _logger.LogCritical(ex, "Report template governance snapshot at {SnapshotPath} is unreadable; reporting is blocked until the state is recovered.", _options.SnapshotPath);
                throw new ReportingStateCorruptionException(_options.SnapshotPath, ex);
            }
        }
    }

    public void Save(IReadOnlyList<ReportTemplateGovernanceRecordDto> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        lock (_gate)
        {
            ValidateRecords(records);
            var snapshot = new ReportTemplateGovernanceSnapshot(
                records
                    .OrderBy(static record => record.TenantId, StringComparer.Ordinal)
                    .ThenBy(static record => record.CompanyId, StringComparer.Ordinal)
                    .ThenBy(static record => record.Definition.TemplateId.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(static record => record.Definition.TemplateId.Version)
                    .ToArray());
            AtomicFileWriter.Write(_options.SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
        }
    }

    private static void ValidateRecords(IReadOnlyList<ReportTemplateGovernanceRecordDto> records)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (record is null
                || record.Definition is null
                || string.IsNullOrWhiteSpace(record.Definition.TemplateId.Name)
                || record.Definition.TemplateId.Version <= 0)
            {
                throw new InvalidDataException(
                    "The report template governance snapshot contains an invalid record.");
            }

            var hasTenant = !string.IsNullOrWhiteSpace(record.TenantId);
            var hasCompany = !string.IsNullOrWhiteSpace(record.CompanyId);
            if (hasTenant != hasCompany)
            {
                throw new InvalidDataException(
                    $"Template '{record.Definition.TemplateId.Name}@v{record.Definition.TemplateId.Version}' has incomplete tenant/company ownership.");
            }

            if (hasCompany
                && !string.Equals(
                    record.Definition.AccessPolicy?.CompanyId,
                    record.CompanyId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Template '{record.Definition.TemplateId.Name}@v{record.Definition.TemplateId.Version}' has an access policy outside its immutable company scope.");
            }

            var identity = $"{record.TenantId?.Trim() ?? string.Empty}\u001f{record.CompanyId?.Trim() ?? string.Empty}\u001f{record.Definition.TemplateId.Name.Trim().ToLowerInvariant()}:{record.Definition.TemplateId.Version}";
            if (!identities.Add(identity))
            {
                throw new InvalidDataException(
                    $"The report template governance snapshot contains duplicate template identity '{identity}'.");
            }
        }
    }

    private sealed record ReportTemplateGovernanceSnapshot(IReadOnlyList<ReportTemplateGovernanceRecordDto> Records);
}

public sealed record ReportPackWorkflowRecordStoreOptions(string SnapshotPath);

/// <summary>
/// Compatibility-only snapshot seam. Production composition does not register this store;
/// canonical report-pack workflow authority is reporting governance.
/// </summary>
public interface IReportPackWorkflowRecordStore
{
    IReadOnlyList<ReportPackWorkflowRecordDto> Load();

    void Save(IReadOnlyList<ReportPackWorkflowRecordDto> records);
}

public sealed class FileReportPackWorkflowRecordStore : IReportPackWorkflowRecordStore
{
    private readonly ReportPackWorkflowRecordStoreOptions _options;
    private readonly ILogger<FileReportPackWorkflowRecordStore> _logger;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FileReportPackWorkflowRecordStore(
        ReportPackWorkflowRecordStoreOptions options,
        ILogger<FileReportPackWorkflowRecordStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SnapshotPath);
    }

    public IReadOnlyList<ReportPackWorkflowRecordDto> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_options.SnapshotPath))
            {
                return [];
            }

            try
            {
                var json = File.ReadAllText(_options.SnapshotPath);
                return JsonSerializer.Deserialize<ReportPackWorkflowSnapshot>(json, _jsonOptions)?.Records
                    ?? throw new JsonException("Report-pack workflow snapshot deserialized to null.");
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogCritical(ex, "Report-pack workflow snapshot at {SnapshotPath} is unreadable; reporting is blocked until the state is recovered.", _options.SnapshotPath);
                throw new ReportingStateCorruptionException(_options.SnapshotPath, ex);
            }
        }
    }

    public void Save(IReadOnlyList<ReportPackWorkflowRecordDto> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        lock (_gate)
        {
            var snapshot = new ReportPackWorkflowSnapshot(
                records
                    .OrderByDescending(static record => record.UpdatedAt)
                    .ThenBy(static record => record.ReportId)
                    .ToArray());
            AtomicFileWriter.Write(_options.SnapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
        }
    }

    private sealed record ReportPackWorkflowSnapshot(IReadOnlyList<ReportPackWorkflowRecordDto> Records);
}

public sealed class ReportPackWorkflowService
{
    private const string LedgerFinancialRecordExplorerId = "ledger";
    private const string PortfolioFinancialRecordExplorerId = "portfolio";
    private const string SecurityInstrumentFinancialRecordExplorerId = "security-instrument";

    private static readonly IReadOnlyDictionary<ReportPackWorkflowStateDto, ReportPackWorkflowStateDto[]> AllowedTransitions =
        new Dictionary<ReportPackWorkflowStateDto, ReportPackWorkflowStateDto[]>
        {
            [ReportPackWorkflowStateDto.Draft] = [ReportPackWorkflowStateDto.InReview, ReportPackWorkflowStateDto.Validated],
            [ReportPackWorkflowStateDto.Validated] = [ReportPackWorkflowStateDto.InReview, ReportPackWorkflowStateDto.PendingApproval, ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.InReview] = [ReportPackWorkflowStateDto.Approved, ReportPackWorkflowStateDto.PendingApproval, ReportPackWorkflowStateDto.Rejected, ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.PendingApproval] = [ReportPackWorkflowStateDto.Approved, ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.Rejected] = [ReportPackWorkflowStateDto.Draft],
            [ReportPackWorkflowStateDto.Approved] = [ReportPackWorkflowStateDto.Published],
            [ReportPackWorkflowStateDto.Published] = [ReportPackWorkflowStateDto.Restated, ReportPackWorkflowStateDto.Archived],
            [ReportPackWorkflowStateDto.Restated] = [ReportPackWorkflowStateDto.Archived],
            [ReportPackWorkflowStateDto.Archived] = []
        };

    private readonly ConcurrentDictionary<Guid, ReportPackWorkflowRecordDto> _records = new();
    private readonly IReportPackWorkflowRecordStore? _store;
    private readonly IReportPackSecurityLineIndex? _securityLineIndex;

    public ReportPackWorkflowService(
        IReportPackWorkflowRecordStore? store = null,
        IReportPackSecurityLineIndex? securityLineIndex = null)
    {
        _store = store;
        _securityLineIndex = securityLineIndex;
        foreach (var record in _store?.Load() ?? [])
        {
            _records[record.ReportId] = record;
        }

        // Backfill the derived security→report-line index from the loaded records (the workflow records
        // are the source of truth), so the index is current on first use after a process start/upgrade.
        _securityLineIndex?.Rebuild(_records.Values);
    }

    public ReportPackWorkflowRecordDto Create(
        string fundProfileId,
        string fundAccountId,
        string period,
        VersionedReportTemplateIdDto templateId,
        string actor,
        IReadOnlyList<ReportPackLineProvenanceDto>? lineProvenance = null,
        ReportAccessPolicyDto? accessPolicy = null,
        ReportAccessQueryContext? accessContext = null)
    {
        var accessIssues = ReportAccessPolicyEvaluator.Validate(accessPolicy);
        if (accessIssues.Count > 0)
        {
            throw new ArgumentException($"Report pack access policy is invalid: {string.Join("; ", accessIssues)}.");
        }

        if (accessContext?.RequireBoundScope == true
            && (string.IsNullOrWhiteSpace(accessContext.TenantId)
                || string.IsNullOrWhiteSpace(accessContext.CompanyId)
                || string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId)))
        {
            throw new UnauthorizedAccessException(
                "A server-resolved actor, tenant, and company scope is required to create a reporting pack.");
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var normalizedAccessPolicy = ReportAccessPolicyEvaluator.Normalize(accessPolicy, actor);
        if (normalizedAccessPolicy.Mode == ReportAccessModeDto.CompanyWide
            && string.IsNullOrWhiteSpace(normalizedAccessPolicy.CompanyId)
            && !string.IsNullOrWhiteSpace(accessContext?.CompanyId))
        {
            normalizedAccessPolicy = normalizedAccessPolicy with { CompanyId = accessContext.CompanyId.Trim() };
        }

        if (accessContext?.RequireBoundScope == true
            && !string.IsNullOrWhiteSpace(normalizedAccessPolicy.CompanyId)
            && !string.Equals(normalizedAccessPolicy.CompanyId, accessContext.CompanyId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The reporting pack access policy belongs to another company.");
        }

        var accessPolicyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(normalizedAccessPolicy)))).ToLowerInvariant();
        var record = new ReportPackWorkflowRecordDto(id, fundProfileId, fundAccountId, period, templateId, ReportPackWorkflowStateDto.Draft, 1, now, actor, now,
            [new ReportPackAuditEventDto(now, actor, "create", ReportPackWorkflowStateDto.Draft, ReportPackWorkflowStateDto.Draft)]
            , null,
            NormalizeLineProvenance(lineProvenance),
            AccessPolicy: normalizedAccessPolicy,
            TenantId: accessContext?.TenantId?.Trim(),
            CompanyId: accessContext?.CompanyId?.Trim(),
            AccessPolicySnapshotHash: accessPolicyHash);
        SaveAndIndex(record);
        return record;
    }

    public ReportPackWorkflowRecordDto Submit(Guid reportId, string actor, string role, string? note = null) =>
        TransitionCore(reportId, ReportPackWorkflowStateDto.InReview, actor, role, note);

    public ReportPackWorkflowRecordDto Transition(
        Guid reportId,
        ReportPackWorkflowStateDto target,
        string actor,
        string role,
        string? note = null,
        OperationsActionOriginDto actionOrigin = OperationsActionOriginDto.HumanOperator)
    {
        if (target == ReportPackWorkflowStateDto.Published)
        {
            throw new InvalidOperationException("Report pack publication requires sign-off, evidence hash, and retained manifest metadata.");
        }

        EnsureHumanOriginForMaterialTransition(target, actionOrigin);
        return TransitionCore(reportId, target, actor, role, note);
    }

    private ReportPackWorkflowRecordDto TransitionCore(Guid reportId, ReportPackWorkflowStateDto target, string actor, string role, string? note = null)
    {
        if (!_records.TryGetValue(reportId, out var record))
            throw new KeyNotFoundException("report pack not found");
        EnsureRole(target, role);
        if (!AllowedTransitions[record.State].Contains(target))
            throw new InvalidOperationException($"invalid transition {record.State} -> {target}");
        var now = DateTimeOffset.UtcNow;
        var next = record with
        {
            State = target,
            UpdatedAt = now,
            AuditTrail = record.AuditTrail.Append(new ReportPackAuditEventDto(now, actor, target.ToString().ToLowerInvariant(), record.State, target, note)).ToArray()
        };
        SaveAndIndex(next);
        return next;
    }

    public ReportPackWorkflowRecordDto Reject(Guid reportId, string reason, string actor, string role, IReadOnlyList<ReportPackEvidenceLinkDto>? evidenceLinks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var transitioned = TransitionCore(reportId, ReportPackWorkflowStateDto.Rejected, actor, role, reason.Trim());
        var rejectedAt = DateTimeOffset.UtcNow;
        var next = transitioned with
        {
            Rejection = new ReportPackRejectionMetadataDto(
                reason.Trim(),
                actor.Trim(),
                role.Trim(),
                rejectedAt,
                evidenceLinks is null ? null : NormalizeEvidenceLinks(evidenceLinks))
        };
        SaveAndIndex(next);
        return next;
    }

    public ReportPackWorkflowRecordDto Reject(Guid reportId, ReportPackRejectRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Reject(reportId, request.Reason, request.Actor, request.ActorRole, request.EvidenceLinks);
    }

    public ReportPackWorkflowRecordDto Restate(
        Guid reportId,
        string actor,
        string role,
        string reasonCode,
        string approver,
        Guid priorVersionReportId,
        IReadOnlyList<ReportPackChangedLineDto> changedLines,
        OperationsActionOriginDto actionOrigin = OperationsActionOriginDto.HumanOperator)
    {
        EnsureHumanOrigin(actionOrigin, "restate reports");
        if (string.IsNullOrWhiteSpace(reasonCode))
            throw new ArgumentException("reasonCode is required");
        if (changedLines.Count == 0)
            throw new ArgumentException("changedLines are required");
        var linesWithoutEvidence = changedLines
            .Where(static line => line.EvidenceLinks is null || line.EvidenceLinks.Count == 0 || line.EvidenceLinks.All(static link => string.IsNullOrWhiteSpace(link.EvidenceId)))
            .Select(static line => line.LineKey)
            .ToArray();
        if (linesWithoutEvidence.Length > 0)
        {
            throw new ArgumentException($"Restatement changed lines require evidence links: {string.Join(", ", linesWithoutEvidence)}.");
        }

        var transitioned = Transition(reportId, ReportPackWorkflowStateDto.Restated, actor, role, note: reasonCode);
        var evidenceLinks = changedLines
            .SelectMany(static line => line.EvidenceLinks ?? [])
            .GroupBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        var next = transitioned with
        {
            Version = transitioned.Version + 1,
            Restatement = new ReportPackRestatementMetadataDto(reasonCode, approver, priorVersionReportId, changedLines, evidenceLinks)
        };
        SaveAndIndex(next);
        return next;
    }

    public ReportPackWorkflowRecordDto Publish(
        Guid reportId,
        string actor,
        string role,
        string signedOffBy,
        string evidenceHash,
        string manifestId,
        string retainedManifestPath,
        IReadOnlyList<ReportPackEvidenceLinkDto> evidenceLinks,
        string? note = null,
        ReportBrandingThemeDto? brandingTheme = null,
        OperationsActionOriginDto actionOrigin = OperationsActionOriginDto.HumanOperator,
        string? signedOffRole = null,
        string? signOffReason = null,
        string? signOffContext = null)
    {
        EnsureHumanOrigin(actionOrigin, "publish reports");
        ArgumentException.ThrowIfNullOrWhiteSpace(signedOffBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedManifestPath);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        if (evidenceLinks.Count == 0)
        {
            throw new ArgumentException("Publication requires retained evidence links.", nameof(evidenceLinks));
        }

        if (!_records.TryGetValue(reportId, out var record))
        {
            throw new KeyNotFoundException("report pack not found");
        }

        EnsureLineProvenanceTraceable(record.LineProvenance ?? []);
        EnsureNoOrphanEvidence(record.LineProvenance ?? [], evidenceLinks);
        var transitioned = TransitionCore(reportId, ReportPackWorkflowStateDto.Published, actor, role, note);
        var next = transitioned with
        {
            Publication = new ReportPackPublicationManifestDto(
                manifestId.Trim(),
                retainedManifestPath.Trim(),
                evidenceHash.Trim(),
                signedOffBy.Trim(),
                DateTimeOffset.UtcNow,
                NormalizeEvidenceLinks(evidenceLinks),
                NormalizePublicationBrandingTheme(brandingTheme),
                SignedOffRole: NormalizeWorkflowOptional(signedOffRole) ?? role.Trim(),
                SignOffReason: NormalizeWorkflowOptional(signOffReason) ?? NormalizeWorkflowOptional(note),
                SignOffContext: NormalizeWorkflowOptional(signOffContext) ?? BuildPublicationSignOffContext(actor, role, actionOrigin),
                ActionOrigin: actionOrigin)
        };
        SaveAndIndex(next);
        return next;
    }

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException(
                $"Reviewed automation cannot {action}; a human operator approval is required.");
        }
    }

    private static void EnsureHumanOriginForMaterialTransition(
        ReportPackWorkflowStateDto target,
        OperationsActionOriginDto actionOrigin)
    {
        switch (target)
        {
            case ReportPackWorkflowStateDto.Approved:
                EnsureHumanOrigin(actionOrigin, "approve reports");
                break;
            case ReportPackWorkflowStateDto.Restated:
                EnsureHumanOrigin(actionOrigin, "restate reports");
                break;
            case ReportPackWorkflowStateDto.Archived:
                EnsureHumanOrigin(actionOrigin, "archive reports");
                break;
        }
    }

    public IReadOnlyList<ReportPackWorkflowRecordDto> GetHistory(string period, string fundAccountId) =>
        _records.Values.Where(x => x.Period == period && x.FundAccountId == fundAccountId).OrderByDescending(x => x.Version).ToArray();

    public ReportPackWorkflowRecordDto? GetRecord(Guid reportId) =>
        _records.TryGetValue(reportId, out var record) ? record : null;

    public IReadOnlyList<ReportPackWorkflowRecordDto> ListRecords(int limit = 25) =>
        _records.Values
            .OrderByDescending(static x => x.UpdatedAt)
            .ThenByDescending(static x => x.Version)
            .ThenBy(static x => x.ReportId)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();

    /// <summary>
    /// Every retained report-pack record scoped to a fund profile, newest period first. Unlike
    /// <see cref="ListRecords"/> this is intentionally uncapped: callers that locate restatement
    /// candidates for a closed-period reference-data edit must see the full set of the fund's published
    /// packs, since a silently dropped pack would be a missed restatement.
    /// </summary>
    public IReadOnlyList<ReportPackWorkflowRecordDto> ListRecordsForFundProfile(string fundProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        var key = fundProfileId.Trim();
        return OrderFundRecords(
            _records.Values.Where(record => string.Equals(record.FundProfileId, key, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The report packs in a fund profile whose retained report-line provenance references
    /// <paramref name="securityId"/>. When a security→report-line index is wired this is an O(matches)
    /// lookup; otherwise it falls back to the full fund scan filtered by the shared matcher, preserving
    /// behaviour. Callers apply their own state gating (e.g. only Published/Restated packs).
    /// </summary>
    public IReadOnlyList<ReportPackWorkflowRecordDto> ListRecordsForSecurityInFund(Guid securityId, string fundProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        var key = fundProfileId.Trim();

        if (_securityLineIndex is not null)
        {
            var matches = new List<ReportPackWorkflowRecordDto>();
            foreach (var entry in _securityLineIndex.LookupByFund(securityId, key))
            {
                if (_records.TryGetValue(entry.ReportId, out var record))
                {
                    matches.Add(record);
                }
            }

            return OrderFundRecords(matches);
        }

        return OrderFundRecords(
            _records.Values.Where(record =>
                string.Equals(record.FundProfileId, key, StringComparison.OrdinalIgnoreCase)
                && ReportPackSecurityLineMatcher.RecordReferencesSecurity(record, securityId)));
    }

    private static IReadOnlyList<ReportPackWorkflowRecordDto> OrderFundRecords(IEnumerable<ReportPackWorkflowRecordDto> records) =>
        records
            .OrderByDescending(static record => record.Period, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(static record => record.Version)
            .ThenBy(static record => record.ReportId)
            .ToArray();

    private void SaveAndIndex(ReportPackWorkflowRecordDto record)
    {
        // Single chokepoint for every report-pack mutation. Update the authoritative record map and the
        // derived security→report-line index together BEFORE persisting, so the two in-memory views stay
        // consistent even if PersistRecords throws. (Persisting between them would, on a write failure,
        // leave the index permanently missing a record _records already has — a silent restatement
        // candidate miss, since the index-backed lookup no longer scans _records.) PersistRecords runs
        // last because it serializes the current _records snapshot.
        _records[record.ReportId] = record;
        _securityLineIndex?.Upsert(record);
        PersistRecords();
    }

    private void PersistRecords()
    {
        _store?.Save(_records.Values.ToArray());
    }

    private static void EnsureRole(ReportPackWorkflowStateDto target, string role)
    {
        var normalized = role.Trim().ToLowerInvariant();

        var allowed =
            target == ReportPackWorkflowStateDto.InReview ||
            target == ReportPackWorkflowStateDto.Validated ||
            target == ReportPackWorkflowStateDto.PendingApproval
                ? IsReportingReviewRole(normalized) || IsReportingOperationsRole(normalized)
                : target switch
                {
                    ReportPackWorkflowStateDto.Rejected => IsReportingReviewRole(normalized) || IsReportingApprovalRole(normalized),
                    ReportPackWorkflowStateDto.Approved => IsReportingApprovalRole(normalized),
                    ReportPackWorkflowStateDto.Published => IsReportingPublicationRole(normalized),
                    ReportPackWorkflowStateDto.Restated => IsReportingApprovalRole(normalized),
                    ReportPackWorkflowStateDto.Archived => normalized is "admin" or "records-manager",
                    _ => true
                };
        if (!allowed)
            throw new UnauthorizedAccessException($"Role '{role}' cannot transition to {target}.");
    }

    private static bool IsReportingOperationsRole(string normalized) =>
        normalized is "operator" or "validator" or "admin" or "accounting" or "fundaccountant" or "reportinganalyst" or "controller";

    private static bool IsReportingReviewRole(string normalized) =>
        normalized is "reviewer" or "admin" or "accounting" or "fundaccountant" or "reportinganalyst" or "controller" or "compliance";

    private static bool IsReportingApprovalRole(string normalized) =>
        normalized is "approver" or "admin" or "accounting" or "controller" or "compliance";

    private static bool IsReportingPublicationRole(string normalized) =>
        normalized is "publisher" or "admin" or "accounting" or "controller";

    private static string BuildPublicationSignOffContext(
        string actor,
        string role,
        OperationsActionOriginDto actionOrigin) =>
        $"Published by {actor.Trim()} as {role.Trim()} via {actionOrigin}.";

    private static string? NormalizeWorkflowOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<ReportPackLineProvenanceDto> NormalizeLineProvenance(IReadOnlyList<ReportPackLineProvenanceDto>? lineProvenance) =>
        lineProvenance?
            .Where(static item =>
                !string.IsNullOrWhiteSpace(item.LineKey) &&
                !string.IsNullOrWhiteSpace(item.SourceKind) &&
                !string.IsNullOrWhiteSpace(item.SourceId) &&
                !string.IsNullOrWhiteSpace(item.EvidenceId))
            .Select(NormalizeLineProvenanceItem)
            .ToArray() ?? [];

    private static ReportPackLineProvenanceDto NormalizeLineProvenanceItem(ReportPackLineProvenanceDto item)
    {
        var normalized = item with
        {
            LineKey = item.LineKey.Trim(),
            SourceKind = item.SourceKind.Trim(),
            SourceId = item.SourceId.Trim(),
            EvidenceId = item.EvidenceId.Trim(),
            RunId = NormalizeNullable(item.RunId),
            LedgerEntryId = NormalizeNullable(item.LedgerEntryId),
            ReconciliationCaseId = NormalizeNullable(item.ReconciliationCaseId),
            ReportValue = NormalizeNullable(item.ReportValue),
            SourceSessionId = NormalizeNullable(item.SourceSessionId),
            ReconciliationRunId = NormalizeNullable(item.ReconciliationRunId),
            ProviderEventId = NormalizeNullable(item.ProviderEventId),
            SecurityMasterId = NormalizeNullable(item.SecurityMasterId),
            SecurityDefinitionId = NormalizeNullable(item.SecurityDefinitionId),
            ReconciliationOutcome = NormalizeNullable(item.ReconciliationOutcome),
            ApprovalId = NormalizeNullable(item.ApprovalId),
            FinancialRecordExplorerId = NormalizeFinancialRecordExplorerId(item.FinancialRecordExplorerId),
            FinancialRecordHref = NormalizeNullable(item.FinancialRecordHref)
        };

        var explorerId = normalized.FinancialRecordExplorerId ?? ResolveFinancialRecordExplorerId(normalized);
        return normalized with
        {
            FinancialRecordExplorerId = explorerId,
            FinancialRecordHref = normalized.FinancialRecordHref ?? BuildFinancialRecordHref(explorerId, normalized)
        };
    }

    private static string ResolveFinancialRecordExplorerId(ReportPackLineProvenanceDto item)
    {
        if (ContainsToken(item.SourceKind, "portfolio") ||
            ContainsToken(item.LineKey, "position") ||
            ContainsToken(item.SourceId, "position"))
        {
            return PortfolioFinancialRecordExplorerId;
        }

        if (ContainsToken(item.SourceKind, "ledger") ||
            !string.IsNullOrWhiteSpace(item.LedgerEntryId) ||
            !string.IsNullOrWhiteSpace(item.RunId) ||
            !string.IsNullOrWhiteSpace(item.SourceSessionId) ||
            !string.IsNullOrWhiteSpace(item.ReconciliationRunId) ||
            !string.IsNullOrWhiteSpace(item.ReconciliationCaseId))
        {
            return LedgerFinancialRecordExplorerId;
        }

        if (!string.IsNullOrWhiteSpace(item.SecurityMasterId) ||
            !string.IsNullOrWhiteSpace(item.SecurityDefinitionId) ||
            ContainsToken(item.SourceKind, "security"))
        {
            return SecurityInstrumentFinancialRecordExplorerId;
        }

        return LedgerFinancialRecordExplorerId;
    }

    private static string? NormalizeFinancialRecordExplorerId(string? value)
    {
        var normalized = NormalizeNullable(value);
        if (normalized is null)
        {
            return null;
        }

        return normalized.Equals(PortfolioFinancialRecordExplorerId, StringComparison.OrdinalIgnoreCase)
            ? PortfolioFinancialRecordExplorerId
            : normalized.Equals(SecurityInstrumentFinancialRecordExplorerId, StringComparison.OrdinalIgnoreCase)
                ? SecurityInstrumentFinancialRecordExplorerId
                : LedgerFinancialRecordExplorerId;
    }

    private static string BuildFinancialRecordHref(string explorerId, ReportPackLineProvenanceDto line)
    {
        var route = UiApiRoutes.WithParam(
            UiApiRoutes.WorkstationFinancialRecordExplorer,
            "explorerId",
            explorerId);
        var queryParts = new List<string>
        {
            $"lineKey={Uri.EscapeDataString(line.LineKey)}",
            $"sourceId={Uri.EscapeDataString(line.SourceId)}",
            $"evidenceId={Uri.EscapeDataString(line.EvidenceId)}"
        };
        if (!string.IsNullOrWhiteSpace(line.RunId))
        {
            queryParts.Add($"runId={Uri.EscapeDataString(line.RunId)}");
        }

        return UiApiRoutes.WithQuery(route, string.Join("&", queryParts));
    }

    private static bool ContainsToken(string? value, string token) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ReportPackEvidenceLinkDto> NormalizeEvidenceLinks(IReadOnlyList<ReportPackEvidenceLinkDto> evidenceLinks) =>
        evidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceId))
            .GroupBy(static link => link.EvidenceId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var link = group.First();
                return link with
                {
                    EvidenceId = link.EvidenceId.Trim(),
                    Label = string.IsNullOrWhiteSpace(link.Label) ? link.EvidenceId.Trim() : link.Label.Trim(),
                    Route = string.IsNullOrWhiteSpace(link.Route) ? null : link.Route.Trim(),
                    Source = string.IsNullOrWhiteSpace(link.Source) ? "report-pack" : link.Source.Trim()
                };
            })
            .ToArray();

    private static ReportBrandingThemeDto? NormalizePublicationBrandingTheme(ReportBrandingThemeDto? theme)
    {
        if (theme is null)
        {
            return null;
        }

        return new ReportBrandingThemeDto(
            NormalizeRequired(theme.ThemeId, nameof(theme.ThemeId)),
            NormalizeRequired(theme.Name, nameof(theme.Name)),
            NormalizeRequired(theme.FirmName, nameof(theme.FirmName)),
            NormalizeRequired(theme.PrimaryColor, nameof(theme.PrimaryColor)),
            NormalizeRequired(theme.AccentColor, nameof(theme.AccentColor)),
            NormalizeRequired(theme.TextColor, nameof(theme.TextColor)),
            NormalizeRequired(theme.BackgroundColor, nameof(theme.BackgroundColor)),
            NormalizeNullable(theme.LogoUri),
            NormalizeNullable(theme.FooterText),
            NormalizeNullable(theme.Disclaimer),
            theme.IsBuiltIn);
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureLineProvenanceTraceable(IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance)
    {
        var missingValues = lineProvenance
            .Where(static line => string.IsNullOrWhiteSpace(line.ReportValue))
            .Select(static line => line.LineKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingValues.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack line provenance requires report values for: {string.Join(", ", missingValues.Order(StringComparer.OrdinalIgnoreCase))}.");
        }

        var missingSourcePointers = lineProvenance
            .Where(static line =>
                string.IsNullOrWhiteSpace(line.RunId) &&
                string.IsNullOrWhiteSpace(line.SourceSessionId) &&
                string.IsNullOrWhiteSpace(line.LedgerEntryId) &&
                string.IsNullOrWhiteSpace(line.ReconciliationCaseId) &&
                string.IsNullOrWhiteSpace(line.ReconciliationRunId))
            .Select(static line => line.LineKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingSourcePointers.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack line provenance requires run, session, ledger, or reconciliation source pointers for: {string.Join(", ", missingSourcePointers.Order(StringComparer.OrdinalIgnoreCase))}.");
        }

        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => !string.IsNullOrWhiteSpace(line.LedgerEntryId),
            "ledger entries");
        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => !string.IsNullOrWhiteSpace(line.ProviderEventId),
            "provider events");
        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => !string.IsNullOrWhiteSpace(line.SecurityMasterId) || !string.IsNullOrWhiteSpace(line.SecurityDefinitionId),
            "Security Master definitions");
        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => (!string.IsNullOrWhiteSpace(line.ReconciliationRunId) || !string.IsNullOrWhiteSpace(line.ReconciliationCaseId))
                && !string.IsNullOrWhiteSpace(line.ReconciliationOutcome),
            "reconciliation outcomes");
        EnsureLineProvenanceCategory(
            lineProvenance,
            static line => !string.IsNullOrWhiteSpace(line.ApprovalId),
            "approval references");
    }

    private static void EnsureLineProvenanceCategory(
        IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance,
        Func<ReportPackLineProvenanceDto, bool> predicate,
        string category)
    {
        var missing = lineProvenance
            .Where(line => !predicate(line))
            .Select(static line => line.LineKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack line provenance requires {category} for: {string.Join(", ", missing.Order(StringComparer.OrdinalIgnoreCase))}.");
        }
    }

    private static void EnsureNoOrphanEvidence(
        IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance,
        IReadOnlyList<ReportPackEvidenceLinkDto> evidenceLinks)
    {
        var retainedEvidenceIds = evidenceLinks
            .Select(static link => link.EvidenceId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingEvidence = lineProvenance
            .Select(static line => line.EvidenceId)
            .Where(evidenceId => !retainedEvidenceIds.Contains(evidenceId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingEvidence.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack publication has orphan evidence: {string.Join(", ", missingEvidence.Order(StringComparer.OrdinalIgnoreCase))}.");
        }

        var missingPointers = lineProvenance
            .SelectMany(EnumerateRetainedProvenancePointers)
            .Where(pointer => !retainedEvidenceIds.Contains(pointer))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingPointers.Length > 0)
        {
            throw new InvalidOperationException(
                $"Report pack publication has orphan provenance pointers: {string.Join(", ", missingPointers.Order(StringComparer.OrdinalIgnoreCase))}.");
        }
    }

    private static IEnumerable<string> EnumerateRetainedProvenancePointers(ReportPackLineProvenanceDto line)
    {
        yield return line.SourceId;

        if (!string.IsNullOrWhiteSpace(line.RunId))
            yield return line.RunId;
        if (!string.IsNullOrWhiteSpace(line.SourceSessionId))
            yield return line.SourceSessionId;
        if (!string.IsNullOrWhiteSpace(line.LedgerEntryId))
            yield return line.LedgerEntryId;
        if (!string.IsNullOrWhiteSpace(line.ReconciliationCaseId))
            yield return line.ReconciliationCaseId;
        if (!string.IsNullOrWhiteSpace(line.ReconciliationRunId))
            yield return line.ReconciliationRunId;
        if (!string.IsNullOrWhiteSpace(line.ProviderEventId))
            yield return line.ProviderEventId;
        if (!string.IsNullOrWhiteSpace(line.SecurityMasterId))
            yield return line.SecurityMasterId;
        if (!string.IsNullOrWhiteSpace(line.SecurityDefinitionId))
            yield return line.SecurityDefinitionId;
        if (!string.IsNullOrWhiteSpace(line.ApprovalId))
            yield return line.ApprovalId;
    }
}
