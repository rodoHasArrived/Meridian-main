using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Banking;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService : IManualJournalEntryWorkbenchService, IManualJournalEntryLifecycleService
{
    private const string DefaultFundProfileId = "default-fund";
    private static readonly IReadOnlyDictionary<Guid, string> EmptyJournalEntryCurrencies = new Dictionary<Guid, string>();

    private readonly IManualJournalEntryDraftStore _draftStore;
    private readonly IAccountingConfigurationService _configurationService;
    private readonly IAccountingActionAuditStore _auditStore;
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;
    private readonly ILedgerJournalStore? _journalStore;
    private readonly ReportPackWorkflowService? _reportPackWorkflowService;
    private readonly IBankTransactionSource? _bankTransactionSource;

    public ManualJournalEntryWorkbenchService(
        IManualJournalEntryDraftStore draftStore,
        IAccountingConfigurationService configurationService,
        IAccountingActionAuditStore auditStore,
        ISecurityMasterQueryService? securityMasterQueryService = null,
        ILedgerJournalStore? journalStore = null,
        ReportPackWorkflowService? reportPackWorkflowService = null,
        IBankTransactionSource? bankTransactionSource = null)
    {
        _draftStore = draftStore ?? throw new ArgumentNullException(nameof(draftStore));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _securityMasterQueryService = securityMasterQueryService;
        _journalStore = journalStore;
        _reportPackWorkflowService = reportPackWorkflowService;
        _bankTransactionSource = bankTransactionSource;
    }

    public async Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fundProfileIds = new List<string>();
        fundProfileIds.AddRange(await _draftStore.ListFundProfileIdsAsync(ct).ConfigureAwait(false));

        if (_journalStore is not null)
        {
            var ledgerBooks = await _journalStore
                .ListLedgerBooksAsync(fundProfileId: null, fundStructureNodeId: null, fundStructureNodeKind: null, ct)
                .ConfigureAwait(false);
            fundProfileIds.AddRange(ledgerBooks.Select(static book => NormalizeFundProfileId(book.FundProfileId)));
        }

        return fundProfileIds
            .Where(static fundProfileId => !string.IsNullOrWhiteSpace(fundProfileId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static fundProfileId => fundProfileId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<BankTransactionDto>> ListBankTransactionsAsync(CancellationToken ct)
    {
        if (_bankTransactionSource is null)
        {
            return [];
        }

        return await _bankTransactionSource
            .GetBankTransactionsAsync(entityId: null, ct)
            .ConfigureAwait(false);
    }

    public async Task<ManualJournalEntryWorkbenchDto> GetWorkbenchAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var configuration = await _configurationService.GetWorkspaceAsync(normalizedFundProfileId, ledgerBookId, ct, normalizedTenantId, normalizedCompanyId).ConfigureAwait(false);
        var drafts = await _draftStore.ListAsync(normalizedFundProfileId, ledgerBookId, ct, normalizedTenantId, normalizedCompanyId).ConfigureAwait(false);
        var audit = await _auditStore.ListAsync(normalizedFundProfileId, ledgerBookId, ct, normalizedTenantId, normalizedCompanyId).ConfigureAwait(false);
        var bankTransactions = await ListBankTransactionsAsync(ct).ConfigureAwait(false);
        var posted = await BuildPostedPrivateCapitalActivityProjectionAsync(normalizedFundProfileId, ledgerBookId, ct, normalizedTenantId, normalizedCompanyId).ConfigureAwait(false);
        var reportPackWorkflowRecords = _reportPackWorkflowService?.ListRecords(200) ?? [];
        var privateCapitalActivity = PrivateCapitalActivityProjectionBuilder.Build(
            new PrivateCapitalActivityProjectionInput(
                normalizedFundProfileId,
                ledgerBookId,
                drafts,
                audit,
                bankTransactions,
                posted,
                reportPackWorkflowRecords));

        return new ManualJournalEntryWorkbenchDto(
            normalizedFundProfileId,
            ledgerBookId,
            DateTimeOffset.UtcNow,
            configuration.LedgerBooks,
            configuration.ChartOfAccounts,
            drafts,
            audit,
            privateCapitalActivity);
    }

    public async Task<PrivateCapitalActivityProjectionDto> GetPrivateCapitalActivityAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var drafts = await _draftStore.ListAsync(normalizedFundProfileId, ledgerBookId, ct, normalizedTenantId, normalizedCompanyId).ConfigureAwait(false);
        var audit = await _auditStore.ListAsync(normalizedFundProfileId, ledgerBookId, ct, normalizedTenantId, normalizedCompanyId).ConfigureAwait(false);
        var bankTransactions = await ListBankTransactionsAsync(ct).ConfigureAwait(false);
        var posted = await BuildPostedPrivateCapitalActivityProjectionAsync(normalizedFundProfileId, ledgerBookId, ct, normalizedTenantId, normalizedCompanyId).ConfigureAwait(false);
        var reportPackWorkflowRecords = _reportPackWorkflowService?.ListRecords(200) ?? [];
        return PrivateCapitalActivityProjectionBuilder.Build(
            new PrivateCapitalActivityProjectionInput(
                normalizedFundProfileId,
                ledgerBookId,
                drafts,
                audit,
                bankTransactions,
                posted,
                reportPackWorkflowRecords));
    }

    private async Task<PostedPrivateCapitalActivityProjection?> BuildPostedPrivateCapitalActivityProjectionAsync(
        string fundProfileId,
        Guid? ledgerBookId,
        CancellationToken ct,
        string? tenantId,
        string? companyId)
    {
        if (_journalStore is null)
        {
            return null;
        }

        var periods = await _journalStore
            .ListPeriodsAsync(ledgerBookId, status: null, fundProfileId, fundStructureNodeId: null, ct)
            .ConfigureAwait(false);
        if (periods.Count == 0)
        {
            return new PostedPrivateCapitalActivityProjection(new PrivateCapitalFundEventLedgerProjection([]), EmptyJournalEntryCurrencies);
        }

        var ledger = new Meridian.Ledger.Ledger();
        var journalEntryIds = new HashSet<Guid>();
        var journalEntryCurrencies = new Dictionary<Guid, string>();
        var ledgerBookCurrencies = new Dictionary<Guid, string>();
        foreach (var period in periods.OrderBy(static item => item.StartDate).ThenBy(static item => item.PeriodNo))
        {
            ct.ThrowIfCancellationRequested();
            var bookCurrency = await ResolveLedgerBookCurrencyAsync(period.LedgerBookId, ledgerBookCurrencies, ct).ConfigureAwait(false);
            var records = await _journalStore.GetByPeriodAsync(period.PeriodId, ct).ConfigureAwait(false);
            foreach (var record in records.OrderBy(static item => item.GlobalSequence).ThenBy(static item => item.Entry.Timestamp))
            {
                if (!MatchesPostedTenantScope(record.Entry.Metadata, tenantId, companyId))
                {
                    continue;
                }

                if (journalEntryIds.Add(record.Entry.JournalEntryId))
                {
                    ledger.Post(record.Entry);
                    journalEntryCurrencies[record.Entry.JournalEntryId] = bookCurrency;
                }
            }
        }

        return new PostedPrivateCapitalActivityProjection(
            PrivateCapitalFundEventLedgerProjector.Project(ledger),
            journalEntryCurrencies);
    }


    private static bool MatchesPostedTenantScope(JournalEntryMetadata? metadata, string? tenantId, string? companyId)
    {
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        if (normalizedTenantId is null && normalizedCompanyId is null)
        {
            return true;
        }

        var tags = metadata?.Tags;
        return MatchesPostedScopeTag(tags, "tenantId", normalizedTenantId) &&
               MatchesPostedScopeTag(tags, "companyId", normalizedCompanyId);
    }

    private static bool MatchesPostedScopeTag(
        IReadOnlyDictionary<string, string>? tags,
        string tagName,
        string? expected)
    {
        if (expected is null)
        {
            return true;
        }

        return tags is not null &&
               tags.TryGetValue(tagName, out var actual) &&
               string.Equals(NormalizeOptional(actual), expected, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ResolveLedgerBookCurrencyAsync(
        Guid? ledgerBookId,
        Dictionary<Guid, string> cache,
        CancellationToken ct)
    {
        if (ledgerBookId is not { } resolvedLedgerBookId)
        {
            return "USD";
        }

        if (cache.TryGetValue(resolvedLedgerBookId, out var cached))
        {
            return cached;
        }

        var book = _journalStore is null
            ? null
            : await _journalStore.GetLedgerBookAsync(resolvedLedgerBookId, ct).ConfigureAwait(false);
        var currency = NormalizeCurrency(book?.BaseCurrency);
        cache[resolvedLedgerBookId] = currency;
        return currency;
    }

    public async Task<ManualJournalEntryDraftDto> SaveDraftAsync(
        SaveManualJournalEntryDraftRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsurePeriodUnlocked(request.PeriodIsLocked, "save manual journal entry drafts");
        var normalizedDraft = await NormalizeAndValidateAsync(request.Draft with
        {
            TenantId = NormalizeOptional(request.TenantId) ?? NormalizeOptional(request.Draft.TenantId),
            CompanyId = NormalizeOptional(request.CompanyId) ?? NormalizeOptional(request.Draft.CompanyId)
        }, allowIncomplete: true, ct).ConfigureAwait(false);
        EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, normalizedDraft);
        var existing = await _draftStore.GetAsync(normalizedDraft.FundProfileId, normalizedDraft.JournalEntryId, ct, normalizedDraft.TenantId, normalizedDraft.CompanyId).ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, existing);
        }
        if (existing is not null && existing.Version != request.Draft.Version)
        {
            throw new InvalidOperationException("Manual journal entry draft version is stale.");
        }

        if (existing is not null && !CanSaveManualJournalDraft(existing.Status))
        {
            throw new InvalidOperationException(
                $"Manual journal entry '{existing.JournalEntryId:D}' is {existing.Status} and cannot be edited through draft save; use the governed lifecycle or correction workflow.");
        }

        var now = DateTimeOffset.UtcNow;
        var saved = normalizedDraft with
        {
            Status = normalizedDraft.ValidationIssues.Any(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
                ? ManualJournalEntryStatusDto.NeedsFix
                : ManualJournalEntryStatusDto.Draft,
            CreatedAtUtc = existing?.CreatedAtUtc ?? (normalizedDraft.CreatedAtUtc == default ? now : normalizedDraft.CreatedAtUtc),
            UpdatedAtUtc = now,
            Version = (existing?.Version ?? 0) + 1,
            PreparedBy = RequireText(request.Actor, nameof(request.Actor)),
            EvidenceLinks = MergeEvidenceLinks(normalizedDraft.EvidenceLinks, request.EvidenceLinks)
        };
        saved = ClearManualJournalReviewMetadataForEditableDraft(saved);

        await _draftStore.SaveAsync(saved, ct).ConfigureAwait(false);
        await AppendAuditAsync(saved, "manual-je.save-draft", request.Actor, request.CorrelationId, saved.EvidenceLinks, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
        return saved;
    }

    private static ManualJournalEntryDraftDto ClearManualJournalReviewMetadataForEditableDraft(
        ManualJournalEntryDraftDto draft)
        => draft.Status is ManualJournalEntryStatusDto.Draft or ManualJournalEntryStatusDto.NeedsFix
            ? draft with
            {
                ApprovalId = null,
                SubmittedAtUtc = null,
                SubmittedBy = null,
                ApprovedAtUtc = null,
                ApprovedBy = null,
                PostedAtUtc = null,
                PostedBy = null,
                ClosedLockedAtUtc = null,
                CloseLockedBy = null
            }
            : draft;

    private static bool CanSaveManualJournalDraft(ManualJournalEntryStatusDto status)
        => status is ManualJournalEntryStatusDto.Draft
            or ManualJournalEntryStatusDto.NeedsFix
            or ManualJournalEntryStatusDto.Rejected;

    public Task<ManualJournalEntryDraftDto> ValidateDraftAsync(
        ValidateManualJournalEntryDraftRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, request.Draft);
        return NormalizeAndValidateAsync(request.Draft with
        {
            TenantId = NormalizeOptional(request.TenantId) ?? NormalizeOptional(request.Draft.TenantId),
            CompanyId = NormalizeOptional(request.CompanyId) ?? NormalizeOptional(request.Draft.CompanyId)
        }, allowIncomplete: false, ct, periodIsLocked: request.PeriodIsLocked);
    }

    public async Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(
        SubmitManualJournalEntryApprovalRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "submit manual journal entries for approval");
        EnsurePeriodUnlocked(request.PeriodIsLocked, "submit manual journal entries for approval");
        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var draft = await _draftStore.GetAsync(fundProfileId, request.JournalEntryId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Manual journal entry '{request.JournalEntryId:D}' was not found.");
        EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, draft);
        if (FindIdempotentLifecycleTransition(
                draft,
                JournalEntryLifecycleActionDto.Submit,
                request.Actor,
                request.CorrelationId) is not null)
        {
            return draft;
        }

        if (draft.Version != request.Version)
        {
            throw new InvalidOperationException("Manual journal entry draft version is stale.");
        }

        if (!CanSubmitManualJournalEntry(draft.Status))
        {
            throw new InvalidOperationException(
                $"Manual journal entry '{draft.JournalEntryId:D}' is {draft.Status} and cannot be submitted for approval.");
        }

        var validated = await NormalizeAndValidateAsync(
            draft,
            allowIncomplete: false,
            ct,
            periodIsLocked: request.PeriodIsLocked).ConfigureAwait(false);
        if (validated.ValidationIssues.Any(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            throw new InvalidOperationException("Manual journal entry cannot be submitted while critical validation issues remain.");
        }

        var now = DateTimeOffset.UtcNow;
        var transition = BuildSubmitTransition(validated.Status, request, now);
        var submitted = validated with
        {
            Status = ManualJournalEntryStatusDto.Submitted,
            ApprovalId = validated.ApprovalId ?? $"manual-je-approval-{validated.JournalEntryId:N}",
            SubmittedAtUtc = now,
            SubmittedBy = RequireText(request.Actor, nameof(request.Actor)),
            UpdatedAtUtc = now,
            Version = validated.Version + 1,
            EvidenceLinks = MergeEvidenceLinks(validated.EvidenceLinks, request.EvidenceLinks),
            LifecycleTransitions = validated.LifecycleTransitions.Append(transition).ToArray()
        };

        await _draftStore.SaveAsync(submitted, ct).ConfigureAwait(false);
        await AppendAuditAsync(submitted, "manual-je.submit-approval", request.Actor, request.CorrelationId, submitted.EvidenceLinks, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
        return submitted;
    }

    public async Task<ManualJournalEntryDraftDto> AttachEvidenceAsync(
        AttachManualJournalEntryEvidenceRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Attachment);
        EnsureHumanOrigin(request.ActionOrigin, "attach evidence to manual journal entries");
        EnsurePeriodUnlocked(request.PeriodIsLocked, "attach evidence to manual journal entries");

        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var draft = await _draftStore.GetAsync(fundProfileId, request.JournalEntryId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Manual journal entry '{request.JournalEntryId:D}' was not found.");
        EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, draft);
        if (draft.Version != request.Version)
        {
            throw new InvalidOperationException("Manual journal entry draft version is stale.");
        }

        if (draft.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.Reversed or ManualJournalEntryStatusDto.Rebooked or ManualJournalEntryStatusDto.CloseLocked)
        {
            throw new InvalidOperationException("Posted, reversed, rebooked, and close-locked journal entries are immutable; attach evidence before posting or create a correction draft.");
        }

        var normalizedAttachments = NormalizeAttachments([request.Attachment], request.Actor);
        if (normalizedAttachments.Count == 0)
        {
            throw new ArgumentException("Manual journal evidence attachment requires a display name and URI.", nameof(request));
        }

        var normalizedAttachment = normalizedAttachments[0];
        if (string.IsNullOrWhiteSpace(normalizedAttachment.DisplayName) || string.IsNullOrWhiteSpace(normalizedAttachment.Uri))
        {
            throw new ArgumentException("Manual journal evidence attachment requires a display name and URI.", nameof(request));
        }

        if (!string.IsNullOrWhiteSpace(normalizedAttachment.LineId) &&
            !draft.Lines.Any(line => string.Equals(line.LineId, normalizedAttachment.LineId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Manual journal evidence attachment references missing line '{normalizedAttachment.LineId}'.");
        }

        var attachments = (draft.EvidenceAttachments ?? [])
            .Where(item => !string.Equals(item.AttachmentId, normalizedAttachment.AttachmentId, StringComparison.OrdinalIgnoreCase))
            .Append(normalizedAttachment)
            .OrderBy(item => item.AddedAtUtc)
            .ThenBy(item => item.AttachmentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evidenceLinks = MergeEvidenceLinks(
            MergeEvidenceLinks(draft.EvidenceLinks, request.EvidenceLinks),
            [normalizedAttachment.Uri]);
        var next = draft with
        {
            EvidenceAttachments = attachments,
            EvidenceLinks = evidenceLinks,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Version = draft.Version + 1
        };

        await _draftStore.SaveAsync(next, ct).ConfigureAwait(false);
        await AppendAuditAsync(next, "manual-je.attach-evidence", request.Actor, request.CorrelationId, evidenceLinks, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
        return next;
    }

    public async Task<JournalEntryLifecycleActionResultDto> ApplyLifecycleActionAsync(
        JournalEntryLifecycleActionRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, $"apply journal entry lifecycle action '{request.Action}'");
        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var draft = await _draftStore.GetAsync(fundProfileId, request.JournalEntryId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Manual journal entry '{request.JournalEntryId:D}' was not found.");
        EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, draft);
        var idempotent = await TryBuildIdempotentLifecycleResultAsync(fundProfileId, draft, request, ct).ConfigureAwait(false);
        if (idempotent is not null)
        {
            return idempotent;
        }

        if (draft.Version != request.Version)
        {
            throw new InvalidOperationException("Manual journal entry draft version is stale.");
        }

        if (request.PeriodIsLocked &&
            request.Action is not (JournalEntryLifecycleActionDto.Validate or JournalEntryLifecycleActionDto.LockAfterClose))
        {
            throw new InvalidOperationException("Manual journal entry lifecycle action is blocked because the accounting period is locked after close.");
        }

        var validated = await NormalizeAndValidateAsync(
            draft,
            allowIncomplete: false,
            ct,
            periodIsLocked: request.PeriodIsLocked).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return request.Action switch
        {
            JournalEntryLifecycleActionDto.Validate => await ApplyStatusTransitionAsync(
                validated,
                request,
                validated.ValidationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
                    ? ManualJournalEntryStatusDto.NeedsFix
                    : validated.Status,
                "manual-je.validate",
                now,
                ct).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.Submit => await ApplySubmitLifecycleActionAsync(
                request,
                ct).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.Approve => await ApplyStatusTransitionAsync(
                RequireStatus(validated, ManualJournalEntryStatusDto.Submitted, request.Action),
                RequireApprovalLifecycleEvidence(RequireLifecycleDecisionNotes(request), validated),
                ManualJournalEntryStatusDto.Approved,
                "manual-je.approve",
                now,
                ct,
                item => item with { ApprovedAtUtc = now, ApprovedBy = RequireText(request.Actor, nameof(request.Actor)) }).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.Reject => await ApplyStatusTransitionAsync(
                RequireStatus(validated, ManualJournalEntryStatusDto.Submitted, request.Action),
                RequireApprovalLifecycleEvidence(RequireLifecycleDecisionNotes(request), validated),
                ManualJournalEntryStatusDto.Rejected,
                "manual-je.reject",
                now,
                ct).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.Post => await PostApprovedManualJournalEntryAsync(
                RequireStatus(validated, ManualJournalEntryStatusDto.Approved, request.Action),
                RequirePostingLifecycleEvidence(
                    RequirePostingLifecycleNotes(request),
                    validated),
                now,
                ct).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.LockAfterClose => await ApplyStatusTransitionAsync(
                RequireStatus(validated, ManualJournalEntryStatusDto.Posted, request.Action),
                RequireCloseLockLifecycleEvidence(RequirePostingLifecycleNotes(request), validated),
                ManualJournalEntryStatusDto.CloseLocked,
                "manual-je.lock-after-close",
                now,
                ct,
                item => item with { ClosedLockedAtUtc = now, CloseLockedBy = RequireText(request.Actor, nameof(request.Actor)) }).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.Reverse => await CreateCorrectionDraftAsync(
                RequirePostedForCorrection(validated, request.Action),
                request,
                reverseSides: true,
                "manual-je.reverse-draft",
                now,
                ct).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.Rebook => await CreateCorrectionDraftAsync(
                RequirePostedForCorrection(validated, request.Action),
                request,
                reverseSides: false,
                "manual-je.rebook-draft",
                now,
                ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported journal entry lifecycle action.")
        };
    }

    private async Task<JournalEntryLifecycleActionResultDto> ApplySubmitLifecycleActionAsync(
        JournalEntryLifecycleActionRequestDto request,
        CancellationToken ct)
    {
        var submitted = await SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            request.JournalEntryId,
            request.FundProfileId,
            request.Actor,
            request.Version,
            request.Notes,
            request.CorrelationId,
            request.EvidenceLinks,
            request.ActionOrigin,
            request.PeriodIsLocked,
            request.LedgerBookId,
            request.TenantId,
            request.CompanyId,
            request.ReportGroupPrincipalIds), ct).ConfigureAwait(false);
        var transition = submitted.LifecycleTransitions.Last(static item =>
            item.Action == JournalEntryLifecycleActionDto.Submit &&
            item.ToStatus == ManualJournalEntryStatusDto.Submitted);
        return new JournalEntryLifecycleActionResultDto(submitted, transition);
    }

    private async Task<JournalEntryLifecycleActionResultDto> ApplyStatusTransitionAsync(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionRequestDto request,
        ManualJournalEntryStatusDto toStatus,
        string auditAction,
        DateTimeOffset recordedAtUtc,
        CancellationToken ct,
        Func<ManualJournalEntryDraftDto, ManualJournalEntryDraftDto>? mutate = null)
    {
        if (draft.ValidationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical) &&
            toStatus is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked)
        {
            throw new InvalidOperationException("Manual journal entry lifecycle action is blocked while critical validation issues remain.");
        }

        EnsureLifecycleActorIndependentFromPreparer(draft, request);

        var transition = BuildTransition(draft.Status, toStatus, request, recordedAtUtc);
        var next = draft with
        {
            Status = toStatus,
            UpdatedAtUtc = recordedAtUtc,
            Version = draft.Version + 1,
            EvidenceLinks = MergeEvidenceLinks(draft.EvidenceLinks, request.EvidenceLinks),
            LifecycleTransitions = draft.LifecycleTransitions.Append(transition).ToArray()
        };
        if (mutate is not null)
        {
            next = mutate(next);
        }

        await _draftStore.SaveAsync(next, ct).ConfigureAwait(false);
        await AppendAuditAsync(next, auditAction, request.Actor, request.CorrelationId, next.EvidenceLinks, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
        return new JournalEntryLifecycleActionResultDto(next, transition);
    }

    private async Task<JournalEntryLifecycleActionResultDto> PostApprovedManualJournalEntryAsync(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionRequestDto request,
        DateTimeOffset recordedAtUtc,
        CancellationToken ct)
    {
        var journalStore = _journalStore
            ?? throw new InvalidOperationException("Manual journal entries cannot be posted because no ledger journal store is configured.");
        if (draft.ValidationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            throw new InvalidOperationException("Manual journal entry lifecycle action is blocked while critical validation issues remain.");
        }

        EnsureLifecycleActorIndependentFromPreparer(draft, request);
        var ledgerBookId = draft.LedgerBookId
            ?? throw new InvalidOperationException("Manual journal entry posting requires a ledger book id.");
        if (string.IsNullOrWhiteSpace(draft.PeriodId) ||
            !Guid.TryParse(draft.PeriodId, out var periodId) ||
            periodId == Guid.Empty)
        {
            throw new InvalidOperationException("Manual journal entry posting requires a durable ledger period id.");
        }

        var ledgerBook = await journalStore.GetLedgerBookAsync(ledgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Ledger book '{ledgerBookId:D}' was not found.");
        if (ledgerBook.AccountingBasis != draft.AccountingBasis)
        {
            throw new InvalidOperationException(
                $"Manual journal entry basis '{draft.AccountingBasis}' does not match ledger book '{ledgerBook.LedgerBookId:D}' basis '{ledgerBook.AccountingBasis}'.");
        }

        var mergedEvidence = MergeEvidenceLinks(draft.EvidenceLinks, request.EvidenceLinks);
        var postingCommand = BuildManualPostingCommand(draft, request, ledgerBookId, periodId, mergedEvidence, recordedAtUtc);
        var write = await BuildManualJournalEntryWriteAsync(
                draft,
                ledgerBook,
                periodId,
                postingCommand,
                mergedEvidence,
                recordedAtUtc,
                ct)
            .ConfigureAwait(false);

        await journalStore.AppendAsync(write, ct).ConfigureAwait(false);

        var transition = BuildTransition(draft.Status, ManualJournalEntryStatusDto.Posted, request, recordedAtUtc);
        var next = draft with
        {
            Status = ManualJournalEntryStatusDto.Posted,
            UpdatedAtUtc = recordedAtUtc,
            Version = draft.Version + 1,
            EvidenceLinks = mergedEvidence,
            LifecycleTransitions = draft.LifecycleTransitions.Append(transition).ToArray(),
            PostedAtUtc = recordedAtUtc,
            PostedBy = RequireText(request.Actor, nameof(request.Actor))
        };

        await _draftStore.SaveAsync(next, ct).ConfigureAwait(false);
        await AppendAuditAsync(next, "manual-je.post", request.Actor, request.CorrelationId, next.EvidenceLinks, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
        return new JournalEntryLifecycleActionResultDto(
            next,
            transition,
            PostedJournal: new PostedLedgerJournalEntryResultDto(
                write.Entry.JournalEntryId,
                ledgerBookId,
                write.AccountingBasis,
                periodId,
                write.AggregateId,
                write.CommandId,
                write.SourceEventId,
                write.CorrelationId,
                PostedAtUtc: recordedAtUtc,
                IdempotencyKey: postingCommand.IdempotencyKey));
    }

    private async Task<LedgerJournalEntryWrite> BuildManualJournalEntryWriteAsync(
        ManualJournalEntryDraftDto draft,
        LedgerBookRecord ledgerBook,
        Guid periodId,
        AccountingPostingCommandDto postingCommand,
        IReadOnlyList<string> evidenceLinks,
        DateTimeOffset recordedAtUtc,
        CancellationToken ct)
    {
        var configuration = await _configurationService
            .GetWorkspaceAsync(draft.FundProfileId, draft.LedgerBookId, ct, draft.TenantId, draft.CompanyId)
            .ConfigureAwait(false);
        var chartByPath = BuildChartByPath(configuration.ChartOfAccounts);
        var timestamp = ToAccountingTimestamp(draft.AccountingDate);
        var description = NormalizeOptional(draft.Memo) ?? $"Manual journal entry {draft.JournalEntryId:D}";
        var lines = new List<LedgerEntry>(draft.Lines.Count);

        foreach (var line in draft.Lines)
        {
            if (!chartByPath.TryGetValue(line.AccountPath, out var account) || account.IsArchived)
            {
                throw new InvalidOperationException($"Manual journal entry line '{line.LineId}' references unavailable GL account '{line.AccountPath}'.");
            }

            if (!TryMapLedgerAccountType(account.AccountType, out var accountType))
            {
                throw new InvalidOperationException(
                    $"Manual journal entry line '{line.LineId}' references GL account '{account.Path}' with unsupported account type '{account.AccountType}'.");
            }

            var amount = Math.Abs(line.Amount);
            // Preserve the structured ledger-account identity (symbol / financial account) supplied by
            // automated drafts so scoped postings (for example period-close entries on broker-scoped
            // revenue) land on the scoped account and zero the scoped trial-balance row rather than an
            // unscoped aggregate. Client-entered lines leave these null, matching prior behavior.
            lines.Add(new LedgerEntry(
                CreateDeterministicGuid("manual-je-line", draft.JournalEntryId.ToString("D"), line.LineId, line.Side.ToString()),
                draft.JournalEntryId,
                timestamp,
                new LedgerAccount(
                    account.AccountName,
                    accountType,
                    NormalizeOptional(line.LedgerAccountSymbol),
                    NormalizeOptional(line.LedgerAccountFinancialAccountId)),
                line.Side == AccountingTemplateLineSideDto.Debit ? amount : 0m,
                line.Side == AccountingTemplateLineSideDto.Credit ? amount : 0m,
                description,
                ToLedgerLineDimensions(line.Dimensions)));
        }

        var entry = new JournalEntry(
            draft.JournalEntryId,
            timestamp,
            description,
            lines,
            BuildManualJournalEntryMetadata(draft, postingCommand, evidenceLinks, recordedAtUtc));

        return new LedgerJournalEntryWrite(
            entry,
            postingCommand.AggregateId,
            periodId,
            postingCommand.CommandId,
            postingCommand.CorrelationId,
            draft.AccountingBasis,
            ledgerBook.AccountingPolicyId,
            ledgerBook.AccountingPolicyVersion,
            "manual-journal-entry",
            "v1",
            postingCommand.SourceEventId,
            postingCommand.SourceJournalEntryId,
            BuildManualPostingKind(draft),
            PostingCommand: postingCommand,
            LedgerBookId: ledgerBook.LedgerBookId);
    }

    private static AccountingPostingCommandDto BuildManualPostingCommand(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionRequestDto request,
        Guid ledgerBookId,
        Guid periodId,
        IReadOnlyList<string> evidenceLinks,
        DateTimeOffset recordedAtUtc)
    {
        var treasuryContext = NormalizeTreasuryContext(draft.TreasuryContext, draft.AccountingDate);
        var sourceJournalEntryId = draft.ReversalOfJournalEntryId ?? draft.RebookedFromJournalEntryId;
        var idempotencyKey = NormalizeOptional(treasuryContext?.IdempotencyKey)
                             ?? $"manual-je:{ledgerBookId:N}:{draft.JournalEntryId:N}";
        var commandId = CreateDeterministicGuid(
            "manual-je-posting-command",
            ledgerBookId.ToString("D"),
            periodId.ToString("D"),
            draft.JournalEntryId.ToString("D"),
            idempotencyKey);

        var treasuryFundEventType = NormalizeOptional(treasuryContext?.FundEventType);
        var sourceEventType = treasuryFundEventType is not null
            ? $"ManualJournalEntry:{treasuryFundEventType}"
            : null;

        return new AccountingPostingCommandDto(
            commandId,
            ledgerBookId,
            periodId,
            draft.AccountingDate,
            recordedAtUtc,
            idempotencyKey,
            BuildManualPostingIntent(draft),
            SourceEventId: draft.JournalEntryId,
            CorrelationId: TryParseGuid(request.CorrelationId),
            CausationId: draft.JournalEntryId,
            SourceJournalEntryId: sourceJournalEntryId,
            SourceEventType: sourceEventType,
            TreasuryContext: treasuryContext,
            ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: NormalizeOptional(draft.ApprovalId),
            OperatorRationale: NormalizeOptional(request.Notes),
            Evidence: evidenceLinks.Select(link => new AccountingPostingEvidenceReferenceDto(
                EvidenceId: link,
                Uri: link,
                Kind: ClassifyManualPostingEvidence(link),
                SourceSystem: "ManualJournalEntryWorkbench",
                RetainedAtUtc: recordedAtUtc,
                RetainedBy: RequireText(request.Actor, nameof(request.Actor)),
                SubjectId: draft.JournalEntryId.ToString("D"))).ToArray(),
            ActionOrigin: request.ActionOrigin,
            LedgerBookId: ledgerBookId);
    }

    private static JournalEntryMetadata BuildManualJournalEntryMetadata(
        ManualJournalEntryDraftDto draft,
        AccountingPostingCommandDto postingCommand,
        IReadOnlyList<string> evidenceLinks,
        DateTimeOffset recordedAtUtc)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddMetadataTag(tags, "manualJournalEntryId", draft.JournalEntryId.ToString("D"));
        AddMetadataTag(tags, "manualJournalEntryStatus", draft.Status.ToString());
        AddMetadataTag(tags, "manualJournalEntryType", draft.EntryType.ToString());
        AddMetadataTag(tags, "accountingBasis", draft.AccountingBasis.ToString());
        AddMetadataTag(tags, "ledgerBookId", draft.LedgerBookId?.ToString("D"));
        AddMetadataTag(tags, "periodId", draft.PeriodId);
        AddMetadataTag(tags, "tenantId", draft.TenantId);
        AddMetadataTag(tags, "companyId", draft.CompanyId);
        AddMetadataTag(tags, "sourceEventId", postingCommand.SourceEventId?.ToString("D"));
        AddMetadataTag(tags, "sourceJournalEntryId", postingCommand.SourceJournalEntryId?.ToString("D"));
        if (evidenceLinks.Count > 0)
        {
            tags["evidenceLinks"] = string.Join("|", evidenceLinks);
        }

        return new JournalEntryMetadata(
            ActivityType: "ManualJournalEntry",
            ProjectId: NormalizeOptional(draft.FundProfileId),
            LedgerBook: draft.LedgerBookId?.ToString("D"),
            EffectiveDate: postingCommand.TreasuryContext?.EffectiveDate ?? draft.AccountingDate,
            IdempotencyKey: postingCommand.IdempotencyKey,
            FundEventId: NormalizeOptional(postingCommand.TreasuryContext?.FundEventId),
            FundEventType: NormalizeOptional(postingCommand.TreasuryContext?.FundEventType),
            CapitalAccountId: NormalizeOptional(postingCommand.TreasuryContext?.CapitalAccountId),
            InvestorId: NormalizeOptional(postingCommand.TreasuryContext?.InvestorId),
            PaymentIntentId: NormalizeOptional(postingCommand.TreasuryContext?.PaymentIntentId),
            SettlementReference: NormalizeOptional(postingCommand.TreasuryContext?.SettlementReference),
            Tags: tags,
            EvidenceReferences: evidenceLinks.Select(link => new JournalEvidenceReference(
                EvidenceId: link,
                Uri: link,
                Kind: ClassifyManualPostingEvidence(link).ToString(),
                SourceSystem: "ManualJournalEntryWorkbench",
                RetainedAtUtc: recordedAtUtc,
                RetainedBy: NormalizeOptional(draft.PostedBy) ?? "manual-journal-entry-workbench",
                SubjectId: draft.JournalEntryId.ToString("D"))).ToArray());
    }

    private static AccountingPostingIntentDto BuildManualPostingIntent(ManualJournalEntryDraftDto draft)
    {
        if (draft.ReversalOfJournalEntryId.HasValue)
        {
            return AccountingPostingIntentDto.Reversal;
        }

        if (draft.RebookedFromJournalEntryId.HasValue)
        {
            return AccountingPostingIntentDto.Rebook;
        }

        return AccountingPostingIntentDto.Originating;
    }

    private static LedgerPostingKindDto BuildManualPostingKind(ManualJournalEntryDraftDto draft)
        => draft.EntryType == ManualJournalEntryTypeDto.ClosingEntry
            ? LedgerPostingKindDto.ClosingEntry
            : draft.ReversalOfJournalEntryId.HasValue || draft.RebookedFromJournalEntryId.HasValue
                ? LedgerPostingKindDto.Adjustment
                : LedgerPostingKindDto.Originating;

    private static AccountingPostingEvidenceKindDto ClassifyManualPostingEvidence(string evidenceLink)
    {
        if (evidenceLink.Contains("approval", StringComparison.OrdinalIgnoreCase))
        {
            return AccountingPostingEvidenceKindDto.Approval;
        }

        if (evidenceLink.Contains("reconciliation", StringComparison.OrdinalIgnoreCase) ||
            evidenceLink.Contains("reconcile", StringComparison.OrdinalIgnoreCase))
        {
            return AccountingPostingEvidenceKindDto.Reconciliation;
        }

        if (evidenceLink.Contains("posting", StringComparison.OrdinalIgnoreCase) ||
            evidenceLink.Contains("audit", StringComparison.OrdinalIgnoreCase))
        {
            return AccountingPostingEvidenceKindDto.AuditSupport;
        }

        if (evidenceLink.Contains("correction", StringComparison.OrdinalIgnoreCase) ||
            evidenceLink.Contains("reversal", StringComparison.OrdinalIgnoreCase) ||
            evidenceLink.Contains("rebook", StringComparison.OrdinalIgnoreCase))
        {
            return AccountingPostingEvidenceKindDto.Correction;
        }

        return AccountingPostingEvidenceKindDto.Source;
    }

    private static LedgerLineDimensionSet? ToLedgerLineDimensions(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        return new LedgerLineDimensionSet(
            FundId: NormalizeOptional(dimensions.FundId),
            EntityId: NormalizeOptional(dimensions.EntityId),
            SleeveId: NormalizeOptional(dimensions.SleeveId),
            StrategyId: NormalizeOptional(dimensions.StrategyId),
            InvestorId: NormalizeOptional(dimensions.InvestorId),
            CapitalAccountId: NormalizeOptional(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: NormalizeOptional(dimensions.TaxLotId),
            CostCenterId: NormalizeOptional(dimensions.CostCenterId),
            CounterpartyId: NormalizeOptional(dimensions.CounterpartyId),
            ExternalGlDimensions: dimensions.ExternalGlDimensions
                .Where(static item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static item => item.Key.Trim(), static item => item.Value.Trim(), StringComparer.OrdinalIgnoreCase),
            OrganizationId: NormalizeOptional(dimensions.OrganizationId),
            PortfolioId: NormalizeOptional(dimensions.PortfolioId),
            BookId: NormalizeOptional(dimensions.BookId),
            AccountId: NormalizeOptional(dimensions.AccountId),
            CustomerId: NormalizeOptional(dimensions.CustomerId),
            VendorId: NormalizeOptional(dimensions.VendorId),
            ProjectId: NormalizeOptional(dimensions.ProjectId));
    }

    private static bool TryMapLedgerAccountType(string? value, out LedgerAccountType accountType)
    {
        switch (value?.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant())
        {
            case "ASSET":
            case "CONTRAASSET":
                accountType = LedgerAccountType.Asset;
                return true;
            case "LIABILITY":
            case "CONTRALIABILITY":
                accountType = LedgerAccountType.Liability;
                return true;
            case "EQUITY":
                accountType = LedgerAccountType.Equity;
                return true;
            case "REVENUE":
            case "INCOME":
                accountType = LedgerAccountType.Revenue;
                return true;
            case "EXPENSE":
                accountType = LedgerAccountType.Expense;
                return true;
            default:
                accountType = default;
                return false;
        }
    }

    private static DateTimeOffset ToAccountingTimestamp(DateOnly accountingDate)
        => new(accountingDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static Guid? TryParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty ? parsed : null;

    private static Guid CreateDeterministicGuid(params string?[] parts)
    {
        var input = string.Join("|", parts.Select(NormalizeOptional));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash[..16]);
    }

    private static void AddMetadataTag(Dictionary<string, string> tags, string key, string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is not null)
        {
            tags[key] = normalized;
        }
    }

    private async Task<JournalEntryLifecycleActionResultDto> CreateCorrectionDraftAsync(
        ManualJournalEntryDraftDto posted,
        JournalEntryLifecycleActionRequestDto request,
        bool reverseSides,
        string auditAction,
        DateTimeOffset recordedAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new InvalidOperationException("Manual journal reversal and rebook actions require a correction reason.");
        }

        if (request.EvidenceLinks.Count == 0)
        {
            throw new InvalidOperationException("Manual journal reversal and rebook actions require retained correction evidence.");
        }

        if (!HasManualJournalCorrectionEvidence(request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal reversal and rebook actions require retained reversal, rebook, correction, approval, or review evidence.");
        }

        if (!HasManualJournalCorrectionEvidenceWithProvenance(posted, request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal reversal and rebook evidence must reference correction intent, the posted journal entry or accounting period, the scoped ledger book, and any retained tenant/company scope on the same evidence artifact.");
        }

        EnsureLifecycleActorIndependentFromPreparer(posted, request);

        var correctionId = Guid.NewGuid();
        var correctionLines = request.RebookLines.Count > 0 && !reverseSides
            ? request.RebookLines
            : posted.Lines
                .Select(line => line with
                {
                    LineId = $"{(reverseSides ? "reversal" : "rebook")}-{line.LineId}",
                    Side = reverseSides ? ReverseSide(line.Side) : line.Side,
                    EvidenceLink = line.EvidenceLink ?? request.EvidenceLinks.FirstOrDefault()
                })
                .ToArray();
        var toStatus = reverseSides
            ? ManualJournalEntryStatusDto.Reversed
            : ManualJournalEntryStatusDto.Rebooked;
        var transition = BuildTransition(posted.Status, toStatus, request, recordedAtUtc);
        var actor = RequireText(request.Actor, nameof(request.Actor));
        var reason = request.Notes.Trim();
        var reversal = reverseSides
            ? new JournalEntryReversalDto(posted.JournalEntryId, correctionId, reason, recordedAtUtc, actor)
            : null;
        var rebook = reverseSides
            ? null
            : new JournalEntryRebookDto(posted.JournalEntryId, correctionId, reason, recordedAtUtc, actor);
        var correctionTransition = BuildTransition(posted.Status, ManualJournalEntryStatusDto.Draft, request, recordedAtUtc);
        var corrected = posted with
        {
            Status = toStatus,
            UpdatedAtUtc = recordedAtUtc,
            Version = posted.Version + 1,
            EvidenceLinks = MergeEvidenceLinks(posted.EvidenceLinks, request.EvidenceLinks),
            LifecycleTransitions = posted.LifecycleTransitions.Append(transition).ToArray(),
            Reversal = reversal,
            Rebook = rebook
        };
        var correction = posted with
        {
            JournalEntryId = correctionId,
            Status = ManualJournalEntryStatusDto.Draft,
            Memo = string.IsNullOrWhiteSpace(request.Notes)
                ? $"{(reverseSides ? "Reversal" : "Rebook")} for journal entry {posted.JournalEntryId:D}"
                : request.Notes.Trim(),
            CreatedAtUtc = recordedAtUtc,
            UpdatedAtUtc = recordedAtUtc,
            Version = 1,
            PreparedBy = RequireText(request.Actor, nameof(request.Actor)),
            Lines = correctionLines,
            EvidenceLinks = MergeEvidenceLinks(posted.EvidenceLinks, request.EvidenceLinks),
            EvidenceAttachments = NormalizeAttachments(posted.EvidenceAttachments, request.Actor),
            ValidationIssues = [],
            ApprovalId = null,
            SubmittedAtUtc = null,
            SubmittedBy = null,
            EntryType = reverseSides ? ManualJournalEntryTypeDto.Reversal : posted.EntryType,
            LifecycleTransitions = [correctionTransition],
            ReversalOfJournalEntryId = reverseSides ? posted.JournalEntryId : null,
            RebookedFromJournalEntryId = reverseSides ? null : posted.JournalEntryId,
            ApprovedAtUtc = null,
            ApprovedBy = null,
            PostedAtUtc = null,
            PostedBy = null,
            ClosedLockedAtUtc = null,
            CloseLockedBy = null,
            Reversal = reversal,
            Rebook = rebook
        };
        correction = await NormalizeAndValidateAsync(correction, allowIncomplete: false, ct).ConfigureAwait(false);
        if (correction.ValidationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            throw new InvalidOperationException("Manual journal reversal and rebook actions cannot transition the posted entry while the generated correction draft has critical validation issues.");
        }

        await _draftStore.SaveAsync(corrected, ct).ConfigureAwait(false);
        await _draftStore.SaveAsync(correction, ct).ConfigureAwait(false);
        await AppendAuditAsync(corrected, reverseSides ? "manual-je.reverse" : "manual-je.rebook", request.Actor, request.CorrelationId, corrected.EvidenceLinks, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
        await AppendAuditAsync(correction, auditAction, request.Actor, request.CorrelationId, correction.EvidenceLinks, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
        return new JournalEntryLifecycleActionResultDto(corrected, transition, [correction]);
    }
}
