using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.AccountingClose;

public interface IAccountingClosePreparationService
{
    Task<ClosePlanTemplateDto> CaptureTemplateAsync(CaptureClosePlanTemplateRequestDto request, string actor,
        string tenantId, string companyId, CancellationToken ct = default);
    Task<ClosePlanTemplateDto?> GetTemplateAsync(Guid templateId, int version, CancellationToken ct = default);
    Task<IReadOnlyList<ClosePlanTemplateDto>> ListTemplatesAsync(Guid sourceWorkflowId, CancellationToken ct = default);
    Task<ClosePreparationPreviewDto> PreviewAsync(PreviewClosePreparationRequestDto request, string actor,
        string tenantId, string companyId, CancellationToken ct = default);
    Task<ClosePreparationPreviewDto?> GetPreviewAsync(Guid previewId, CancellationToken ct = default);
    Task<PreparedClosePlanResultDto> CreateAsync(CreatePreparedClosePlanRequestDto request, string actor,
        string tenantId, string companyId, CancellationToken ct = default);
}

public sealed class ClosePreparationPreviewStaleException(string message) : InvalidOperationException(message);
public sealed class ClosePreparationRecoveryRequiredException(string message) : InvalidOperationException(message);

/// <summary>
/// Captures configuration, never execution state. A retained creation claim precedes workflow creation;
/// the same claim resumes the same fresh workflow after interruption. Book/period authority is reread
/// before creation and client supplied dates, policy identities, and completion state are never accepted.
/// </summary>
public sealed partial class AccountingClosePreparationService(
    IAccountingCloseManagementService closePlans,
    IOperationsContinuityWorkflowService workflows,
    ILedgerBookService? books,
    ILedgerJournalStore? journalStore,
    StorageOptions? storageOptions = null,
    TimeProvider? timeProvider = null) : IAccountingClosePreparationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly string? _directory = storageOptions is null ? null
        : Path.Combine(storageOptions.RootPath, "accounting", "close-preparation");
    private PreparationDocument _memory = new(1, [], [], []);

    public async Task<ClosePlanTemplateDto> CaptureTemplateAsync(CaptureClosePlanTemplateRequestDto request,
        string actor, string tenantId, string companyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMutation(actor, tenantId, companyId, request.ActionOrigin);
        var name = Required(request.Name, "Template name");
        ValidateCalendar(request.Calendar);
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        var state = await ReadAsync(ct).ConfigureAwait(false);
        var source = await ReadSourceAsync(request.SourceWorkflowId, tenantId, companyId, ct).ConfigureAwait(false);
        var rules = request.DeadlineRules ?? throw new ArgumentException("Deadline rules are required.");
        if (rules.Any(rule => rule is null) || rules.Count != source.Plan.Tasks.Count || rules.Select(rule => rule.TaskId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rules.Count)
            throw new ArgumentException("Exactly one explicit deadline rule is required for every source task.");
        var tasks = source.Plan.Tasks.Select(task =>
        {
            var rule = rules.SingleOrDefault(rule => Same(rule.TaskId, task.TaskId))
                ?? throw new ArgumentException($"A deadline rule is required for task '{task.TaskId}'.");
            _ = CalculateDeadline(source.Period.StartDate, source.Period.EndDate, rule, request.Calendar);
            var requirements = task.SignOffRequirements.Select(requirement =>
                new CloseTaskSignOffRequirementConfigurationDto(requirement.Role,
                    requirement.RequiredApprovalCount, requirement.EvidenceRequirement)).ToArray();
            if (requirements.Length == 0 || requirements.Any(requirement => requirement.RequiredApprovalCount < 1 || string.IsNullOrWhiteSpace(requirement.Role)))
                throw new InvalidOperationException($"Task '{task.TaskId}' lacks reusable sign-off requirements.");
            var configuration = new CloseTaskConfigurationDto(task.TaskId, task.DisplayName, task.Owner,
                DueDate: null, DependencyConfigurations: task.Dependencies.Select(dependency =>
                    new CloseTaskDependencyConfigurationDto(dependency.DependsOnTaskId, dependency.Reason)).ToArray(),
                SignOffRequirementConfigurations: requirements, HasExplicitDependencies: true);
            return new ClosePlanTemplateTaskDto(configuration, task.DueDate, rule);
        }).ToArray();
        ValidateGraph(tasks.Select(task => task.Configuration).ToArray());
        var templateId = request.TemplateId ?? Guid.NewGuid();
        if (templateId == Guid.Empty)
            throw new ArgumentException("TemplateId must not be empty.");
        var existing = state.Templates.Where(entry => entry.Template.TemplateId == templateId).ToArray();
        foreach (var entry in existing)
        {
            RequireScope(entry.TenantId, entry.CompanyId, tenantId, companyId);
            if (entry.Template.SourceWorkflowId != request.SourceWorkflowId)
                throw new InvalidOperationException("A template version cannot change its source workflow.");
        }
        var now = _timeProvider.GetUtcNow();
        var version = existing.Length == 0 ? 1 : existing.Max(entry => entry.Template.Version) + 1;
        var history = existing.OrderByDescending(entry => entry.Template.Version).FirstOrDefault()?.Template.History ?? [];
        var template = new ClosePlanTemplateDto(templateId, version, name, source.Workflow.WorkflowId,
            source.Book.LedgerBookId, source.Workflow.PeriodId, source.Workflow.FundAccountId,
            source.Book.FundProfileId, source.Book.AccountingPolicyId, source.Book.AccountingPolicyVersion,
            source.Plan.MaterialityPolicy, request.Calendar, tasks, now, actor.Trim(),
            [.. history, new("TemplateCaptured", now, actor.Trim(), $"Captured reusable close configuration as version {version}.")]);
        await SaveAsync(state with { Templates = [.. state.Templates, new(tenantId, companyId, template)] }, ct).ConfigureAwait(false);
        return Copy(template);
    }

    public async Task<ClosePlanTemplateDto?> GetTemplateAsync(Guid templateId, int version, CancellationToken ct = default)
    {
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        var state = await ReadAsync(ct).ConfigureAwait(false);
        return Copy(state.Templates.FirstOrDefault(entry => entry.Template.TemplateId == templateId && entry.Template.Version == version)?.Template);
    }

    public async Task<IReadOnlyList<ClosePlanTemplateDto>> ListTemplatesAsync(Guid sourceWorkflowId, CancellationToken ct = default)
    {
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        return Copy((await ReadAsync(ct).ConfigureAwait(false)).Templates.Where(entry => entry.Template.SourceWorkflowId == sourceWorkflowId)
            .Select(entry => entry.Template).OrderByDescending(template => template.CapturedAtUtc).ToArray());
    }

    public async Task<ClosePreparationPreviewDto?> GetPreviewAsync(Guid previewId, CancellationToken ct = default)
    {
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        return Copy((await ReadAsync(ct).ConfigureAwait(false)).Previews.FirstOrDefault(entry => entry.Preview.PreviewId == previewId)?.Preview);
    }

    public async Task<ClosePreparationPreviewDto> PreviewAsync(PreviewClosePreparationRequestDto request,
        string actor, string tenantId, string companyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMutation(actor, tenantId, companyId, request.ActionOrigin);
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        var state = await ReadAsync(ct).ConfigureAwait(false);
        var template = FindTemplate(state, request.TemplateId, request.TemplateVersion, tenantId, companyId);
        var source = await ReadSourceAsync(template.SourceWorkflowId, tenantId, companyId, ct).ConfigureAwait(false);
        var target = await ReadTargetAsync(template, request.TargetLedgerBookId, request.TargetPeriodId, ct).ConfigureAwait(false);
        var issues = new List<ClosePreparationIssueDto>();
        var mappings = (request.OwnerMappings ?? []).ToArray();
        if (mappings.Any(mapping => mapping is null || string.IsNullOrWhiteSpace(mapping.SourceOwner)) ||
            mappings.GroupBy(mapping => mapping.SourceOwner.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new ArgumentException("Owner mappings must have unique, nonblank source owners.");
        var ownerMap = mappings.ToDictionary(mapping => mapping.SourceOwner.Trim(), mapping => mapping.TargetOwner?.Trim() ?? "", StringComparer.OrdinalIgnoreCase);
        if (ownerMap.Keys.Any(owner => !template.Tasks.Any(task => Same(task.Configuration.Owner, owner))))
            throw new ArgumentException("Owner mappings must refer to an owner in the selected template.");
        var preparedTasks = template.Tasks.Select(task =>
        {
            var sourceOwner = task.Configuration.Owner ?? "";
            var owner = ownerMap.GetValueOrDefault(sourceOwner, "");
            if (string.IsNullOrWhiteSpace(owner))
                issues.Add(new("OwnerMappingRequired", $"Confirm a target owner for {task.Configuration.DisplayName}.", true, task.Configuration.TaskId));
            var ownerChanged = !Same(sourceOwner, owner);
            if (ownerChanged && owner.Length > 0)
                issues.Add(new("OwnerChanged", $"Owner changes from {sourceOwner} to {owner}.", false, task.Configuration.TaskId));
            return new ClosePreparationTaskDto(task.Configuration.TaskId, task.Configuration.DisplayName ?? task.Configuration.TaskId,
                sourceOwner, owner, task.SourceDueDate,
                CalculateDeadline(target.Period.StartDate, target.Period.EndDate, task.DeadlineRule, template.Calendar),
                task.DeadlineRule, task.Configuration.DependencyConfigurations,
                task.Configuration.SignOffRequirementConfigurations, ownerChanged);
        }).ToArray();
        var policyChanged = !Same(template.SourceAccountingPolicyId, target.Book.AccountingPolicyId)
            || !Same(template.SourceAccountingPolicyVersion, target.Book.AccountingPolicyVersion);
        if (policyChanged)
            issues.Add(new("PolicyChanged", $"Accounting policy changes from {template.SourceAccountingPolicyId} / {template.SourceAccountingPolicyVersion} to {target.Book.AccountingPolicyId} / {target.Book.AccountingPolicyVersion}. Review the retained requirements.", !request.AcknowledgePolicyChange));
        if (!Same(template.MaterialityPolicy.Currency, target.Book.BaseCurrency))
            issues.Add(new("MaterialityCurrencyUnresolved", "The materiality policy currency does not match the target book. Capture a compatible source policy before creating this plan.", true));
        if (!Same(target.Period.Status, "Open"))
            issues.Add(new("TargetPeriodNotOpen", "The authoritative target period is not open.", true));
        if (target.Period.StartDate <= source.Period.EndDate)
            issues.Add(new("TargetPeriodNotLater", "Choose an authoritative period after the source period.", true));
        // PostgreSQL permits only one open workflow per account and period across all books.
        // Retain the stricter same-book rule for closed plans and recognize legacy period aliases.
        var existing = await workflows.ListAsync(template.FundAccountId, ct: ct).ConfigureAwait(false);
        if (existing.Any(workflow => PeriodMatches(workflow.PeriodId, target.Period)
                && (workflow.LedgerBookId == target.Book.LedgerBookId || workflow.Status != OperationsWorkflowStatusDto.Closed)) ||
            state.Creations.Any(creation => !creation.Abandoned && creation.TargetBookId == target.Book.LedgerBookId && creation.TargetPeriodId == target.Period.PeriodId))
            issues.Add(new("TargetPlanExists", "A close plan or retained creation exists for this book and period, or an open workflow already uses this account and period.", true));
        var now = _timeProvider.GetUtcNow();
        var preview = new ClosePreparationPreviewDto(Guid.NewGuid(), template.TemplateId, template.Version,
            template.SourceWorkflowId, target.Book, ToDto(target.Book, target.Period), template.Calendar,
            preparedTasks, issues, policyChanged, issues.All(issue => !issue.IsBlocking), now, now.AddMinutes(15));
        var latestVersion = state.Templates.Where(entry => entry.Template.TemplateId == template.TemplateId).Max(entry => entry.Template.Version);
        var entry = new PreviewEntry(tenantId, companyId, request, preview, SourceFingerprint(source), Hash(target), latestVersion);
        await SaveAsync(state with { Previews = [.. state.Previews, entry] }, ct).ConfigureAwait(false);
        return Copy(preview);
    }

    public async Task<PreparedClosePlanResultDto> CreateAsync(CreatePreparedClosePlanRequestDto request,
        string actor, string tenantId, string companyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMutation(actor, tenantId, companyId, request.ActionOrigin);
        var key = Required(request.IdempotencyKey, "Idempotency key");
        if (key.Length > 160)
            throw new ArgumentException("Idempotency key must be at most 160 characters.");
        await using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        var state = await ReadAsync(ct).ConfigureAwait(false);
        var previewEntry = state.Previews.SingleOrDefault(entry => entry.Preview.PreviewId == request.PreviewId)
            ?? throw new InvalidOperationException("The preparation preview was not found; generate a fresh preview.");
        RequireScope(previewEntry.TenantId, previewEntry.CompanyId, tenantId, companyId);
        var preview = previewEntry.Preview;
        var template = FindTemplate(state, preview.TemplateId, preview.TemplateVersion, tenantId, companyId);
        var claim = state.Creations.SingleOrDefault(creation => creation.TenantId == tenantId && creation.CompanyId == companyId && creation.IdempotencyKey == key);
        if (claim is not null)
        {
            if (claim.PreviewId != request.PreviewId)
                throw new InvalidOperationException("This idempotency key already identifies another preparation.");
            if (claim.Completed)
                return await BuildResultAsync(claim, true, tenantId, companyId, ct).ConfigureAwait(false);
            if (claim.Abandoned)
                throw new ClosePreparationPreviewStaleException("This unstarted preparation was abandoned after its inputs changed. Generate a fresh preview and request key.");
            var recovery = await TryRecoverConfiguredClaimAsync(state, claim, tenantId, companyId, ct).ConfigureAwait(false);
            if (recovery is not null)
                return recovery;
        }
        if (!preview.CanCreate)
            throw new InvalidOperationException("Resolve the blocking mappings and policies, then generate a fresh preview.");
        if (claim is null && preview.ExpiresAtUtc <= _timeProvider.GetUtcNow())
            throw new ClosePreparationPreviewStaleException("This preparation preview expired; generate a fresh preview.");
        if (state.Templates.Any(entry => entry.Template.TemplateId == template.TemplateId && entry.Template.Version > previewEntry.LatestTemplateVersion))
            await RejectStaleAsync(state, claim, "The template version changed.", ct).ConfigureAwait(false);
        var source = await ReadSourceAsync(template.SourceWorkflowId, tenantId, companyId, ct).ConfigureAwait(false);
        var target = await ReadTargetAsync(template, preview.TargetBook.LedgerBookId, preview.TargetPeriod.PeriodId, ct).ConfigureAwait(false);
        if (SourceFingerprint(source) != previewEntry.SourceFingerprint || Hash(target) != previewEntry.TargetFingerprint)
            await RejectStaleAsync(state, claim, "The source plan or authoritative target changed; this preview is stale.", ct).ConfigureAwait(false);
        if (!Same(target.Period.Status, "Open"))
            throw new InvalidOperationException("The target period is no longer open.");
        if (claim is null)
        {
            if (state.Creations.Any(creation => !creation.Abandoned && creation.TargetBookId == target.Book.LedgerBookId && creation.TargetPeriodId == target.Period.PeriodId))
                throw new InvalidOperationException("A creation already exists for this book and period. Retry the original request.");
            var existing = await workflows.ListAsync(template.FundAccountId, ct: ct).ConfigureAwait(false);
            if (existing.Any(workflow => PeriodMatches(workflow.PeriodId, target.Period)
                    && (workflow.LedgerBookId == target.Book.LedgerBookId || workflow.Status != OperationsWorkflowStatusDto.Closed)))
                throw new InvalidOperationException("A close plan exists for this book and period, or an open workflow already uses this account and period.");
            var now = _timeProvider.GetUtcNow();
            claim = new CreationEntry(tenantId, companyId, key, preview.PreviewId, Guid.NewGuid(),
                template.TemplateId, template.Version, template.SourceWorkflowId, target.Book.LedgerBookId,
                target.Period.PeriodId, now, actor.Trim(), false,
                [new("CreationRequested", now, actor.Trim(), "Reserved one fresh close plan for the authoritative book and period.")]);
            state = state with { Creations = [.. state.Creations, claim] };
            await SaveAsync(state, ct).ConfigureAwait(false);
        }
        if (!claim.StartAttempted)
        {
            claim = claim with { StartAttempted = true };
            state = ReplaceClaim(state, claim);
            await SaveAsync(state, ct).ConfigureAwait(false);
        }
        var start = await workflows.StartPreparedWorkflowAsync(new OperationsStartWorkflowRequestDto(
            template.FundAccountId, target.Period.PeriodId.ToString("D"), null, source.Workflow.BrokerSource,
            claim.CreatedBy, $"Prepared from close template {template.TemplateId:D} version {template.Version}.",
            claim.IdempotencyKey, LedgerBookId: target.Book.LedgerBookId), claim.WorkflowId, ct).ConfigureAwait(false);
        if (!start.Success || start.Workflow is null || start.Workflow.WorkflowId != claim.WorkflowId)
            throw new InvalidOperationException("Fresh workflow creation did not complete; retry this same preparation request.");
        var retained = await closePlans.GetPeriodPlanScopedAsync(claim.WorkflowId, tenantId, companyId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The prepared workflow does not yet expose its close plan; retry this request.");
        var evidence = ConfigurationEvidence(claim);
        if (retained.Configuration is null)
        {
            if (retained.Tasks.Any(task => task.SignOffs.Count > 0 || task.EvidenceLinks.Count > 0 || task.Status == CloseTaskStatusDto.SignedOff)
                || retained.LateAdjustments.Count > 0 || retained.EvidenceReviews.Count > 0 || retained.IsPeriodLocked)
                throw new InvalidOperationException("The target workflow has execution history; preparation cannot overwrite it.");
            var configurations = template.Tasks.Select(task =>
            {
                var prepared = preview.Tasks.Single(item => Same(item.TaskId, task.Configuration.TaskId));
                return task.Configuration with { Owner = prepared.Owner, DueDate = prepared.DueDate };
            }).ToArray();
            var history = claim.History.Append(new ClosePreparationHistoryDto("PlanCreated", claim.CreatedAtUtc, claim.CreatedBy,
                "Created fresh tasks and evidence requirements; prior-period execution history remains at its source.")).ToArray();
            var configurationRequest = new UpsertClosePeriodPlanConfigurationRequestDto(claim.WorkflowId,
                template.MaterialityPolicy, configurations, claim.CreatedBy, [evidence], claim.IdempotencyKey)
            {
                Preparation = new(template.TemplateId, template.Version, template.SourceWorkflowId, target.Period.PeriodId,
                    target.Period.StartDate, target.Period.EndDate, claim.CreatedAtUtc, claim.CreatedBy, history)
            };
            retained = await closePlans.ConfigurePeriodPlanScopedAsync(configurationRequest, claim.CreatedBy, tenantId, companyId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The prepared close configuration was not retained; retry this request.");
        }
        else if (!retained.Configuration.EvidenceLinks.Contains(evidence, StringComparer.Ordinal))
            throw new InvalidOperationException("The target already has an unrelated close configuration; it cannot be overwritten.");
        claim = claim with { Completed = true, History = retained.Configuration?.Preparation?.History ?? claim.History };
        state = ReplaceClaim(state, claim);
        await SaveAsync(state, ct).ConfigureAwait(false);
        return Result(claim, retained, false);
    }

    private async Task<PreparedClosePlanResultDto> BuildResultAsync(CreationEntry claim, bool retry,
        string tenantId, string companyId, CancellationToken ct)
    {
        var plan = await closePlans.GetPeriodPlanScopedAsync(claim.WorkflowId, tenantId, companyId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained prepared plan is unavailable; restore it before retrying.");
        return Result(claim, plan, retry);
    }

    private static PreparedClosePlanResultDto Result(CreationEntry claim, ClosePeriodPlanDto plan, bool retry)
        => new(claim.WorkflowId, claim.TemplateId, claim.TemplateVersion, claim.SourceWorkflowId,
            claim.TargetBookId, claim.TargetPeriodId, claim.CreatedAtUtc, claim.CreatedBy, retry, plan, claim.History);
}
