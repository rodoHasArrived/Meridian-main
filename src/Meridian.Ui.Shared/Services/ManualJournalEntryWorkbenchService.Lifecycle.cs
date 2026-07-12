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

public sealed partial class ManualJournalEntryWorkbenchService
{
    private static ManualJournalEntryDraftDto RequireStatus(
        ManualJournalEntryDraftDto draft,
        ManualJournalEntryStatusDto requiredStatus,
        JournalEntryLifecycleActionDto action)
    {
        if (draft.Status != requiredStatus)
        {
            throw new InvalidOperationException($"Manual journal entry lifecycle action '{action}' requires status '{requiredStatus}', but current status is '{draft.Status}'.");
        }

        return draft;
    }

    private static ManualJournalEntryDraftDto RequirePostedForCorrection(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionDto action)
    {
        if (draft.Status == ManualJournalEntryStatusDto.CloseLocked)
        {
            throw new InvalidOperationException(
                $"Manual journal entry lifecycle action '{action}' cannot change a close-locked journal entry; use governed late-adjustment or restatement workflows.");
        }

        if (draft.Status != ManualJournalEntryStatusDto.Posted)
        {
            throw new InvalidOperationException($"Manual journal entry lifecycle action '{action}' requires a posted journal entry.");
        }

        return draft;
    }

    private static void EnsureLifecycleActorIndependentFromPreparer(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionRequestDto request)
    {
        if (request.Action is JournalEntryLifecycleActionDto.Validate or JournalEntryLifecycleActionDto.Submit)
        {
            return;
        }

        var actor = RequireText(request.Actor, nameof(request.Actor));
        var preparedBy = NormalizeOptional(draft.PreparedBy);
        if (preparedBy is null)
        {
            return;
        }

        if (string.Equals(actor, preparedBy, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Manual journal lifecycle action '{request.Action}' requires an independent actor; '{actor}' prepared the journal entry and cannot approve, reject, post, close-lock, reverse, or rebook it.");
        }
    }

    private async Task<JournalEntryLifecycleActionResultDto?> TryBuildIdempotentLifecycleResultAsync(
        string fundProfileId,
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionRequestDto request,
        CancellationToken ct)
    {
        var transition = FindIdempotentLifecycleTransition(draft, request.Action, request.Actor, request.CorrelationId);
        if (transition is null)
        {
            return null;
        }

        var generated = await LoadIdempotentCorrectionDraftsAsync(fundProfileId, draft, request.Action, ct).ConfigureAwait(false);
        return new JournalEntryLifecycleActionResultDto(draft, transition, generated);
    }

    private async Task<IReadOnlyList<ManualJournalEntryDraftDto>> LoadIdempotentCorrectionDraftsAsync(
        string fundProfileId,
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionDto action,
        CancellationToken ct)
    {
        var correctionId = action switch
        {
            JournalEntryLifecycleActionDto.Reverse => draft.Reversal?.ReversalJournalEntryId,
            JournalEntryLifecycleActionDto.Rebook => draft.Rebook?.RebookJournalEntryId,
            _ => null
        };
        if (!correctionId.HasValue)
        {
            return [];
        }

        var correction = await _draftStore.GetAsync(fundProfileId, correctionId.Value, ct, draft.TenantId, draft.CompanyId).ConfigureAwait(false);
        return correction is null ? [] : [correction];
    }

    private static JournalEntryLifecycleTransitionDto? FindIdempotentLifecycleTransition(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionDto action,
        string? actor,
        string? correlationId)
    {
        var normalizedActor = NormalizeOptional(actor);
        var normalizedCorrelationId = NormalizeOptional(correlationId);
        if (normalizedActor is null || normalizedCorrelationId is null)
        {
            return null;
        }

        return draft.LifecycleTransitions.LastOrDefault(transition =>
            transition.Action == action &&
            string.Equals(transition.Actor, normalizedActor, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(transition.CorrelationId, normalizedCorrelationId, StringComparison.OrdinalIgnoreCase));
    }

    private static JournalEntryLifecycleActionRequestDto RequireLifecycleDecisionNotes(
        JournalEntryLifecycleActionRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new InvalidOperationException("Manual journal approval and rejection actions require reviewer notes.");
        }

        return request;
    }

    private static JournalEntryLifecycleActionRequestDto RequirePostingLifecycleNotes(
        JournalEntryLifecycleActionRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new InvalidOperationException("Manual journal posting and close-lock actions require operator notes.");
        }

        return request;
    }

    private static JournalEntryLifecycleActionRequestDto RequireApprovalLifecycleEvidence(
        JournalEntryLifecycleActionRequestDto request,
        ManualJournalEntryDraftDto journalEntry)
    {
        if (request.EvidenceLinks.Count == 0)
        {
            throw new InvalidOperationException("Manual journal approval and rejection actions require retained reviewer evidence.");
        }

        if (!HasManualJournalApprovalEvidence(request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal approval and rejection actions require retained approval, rejection, sign-off, or review evidence.");
        }

        if (!HasManualJournalApprovalEvidenceWithProvenance(journalEntry, request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal approval and rejection evidence must reference reviewer intent, the journal entry or accounting period, the scoped ledger book, and any retained tenant/company scope on the same evidence artifact.");
        }

        return request;
    }

    private static JournalEntryLifecycleActionRequestDto RequirePostingLifecycleEvidence(
        JournalEntryLifecycleActionRequestDto request,
        ManualJournalEntryDraftDto journalEntry)
    {
        if (request.EvidenceLinks.Count == 0)
        {
            throw new InvalidOperationException("Manual journal posting actions require retained posting evidence.");
        }

        if (!HasManualJournalPostingEvidence(request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal posting actions require retained posting, approval, certification, sign-off, or review evidence.");
        }

        if (!HasManualJournalPostingEvidenceWithProvenance(journalEntry, request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal posting evidence must reference posting intent, the journal entry or accounting period, the scoped ledger book, and any retained tenant/company scope on the same evidence artifact.");
        }

        return request;
    }

    private static JournalEntryLifecycleActionRequestDto RequireCloseLockLifecycleEvidence(
        JournalEntryLifecycleActionRequestDto request,
        ManualJournalEntryDraftDto journalEntry)
    {
        if (request.EvidenceLinks.Count == 0)
        {
            throw new InvalidOperationException("Manual journal close-lock actions require retained close evidence.");
        }

        if (!HasManualJournalCloseLockEvidence(request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal close-lock actions require retained close, period-lock, sign-off, certification, approval, or review evidence.");
        }

        if (!HasManualJournalCloseLockEvidenceWithProvenance(journalEntry, request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal close-lock evidence must reference close-lock intent, the journal entry or accounting period, the scoped ledger book, and any retained tenant/company scope on the same evidence artifact.");
        }

        return request;
    }

    private static bool HasManualJournalLifecycleEvidenceProvenance(
        ManualJournalEntryDraftDto journalEntry,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasManualJournalEntryOrPeriodProvenance(journalEntry, link) &&
            HasManualJournalLedgerBookProvenance(journalEntry, link) &&
            HasManualJournalTenantCompanyProvenance(journalEntry, link));

    private static bool HasManualJournalEntryOrPeriodProvenance(
        ManualJournalEntryDraftDto journalEntry,
        string evidenceLink)
        => ReferencesEvidenceToken(evidenceLink, journalEntry.JournalEntryId.ToString("D")) ||
           ReferencesEvidenceToken(evidenceLink, journalEntry.JournalEntryId.ToString("N")) ||
           (!string.IsNullOrWhiteSpace(journalEntry.PeriodId) &&
               ReferencesEvidenceToken(evidenceLink, journalEntry.PeriodId));

    private static bool HasManualJournalLedgerBookProvenance(
        ManualJournalEntryDraftDto journalEntry,
        string evidenceLink)
    {
        if (journalEntry.LedgerBookId is not { } ledgerBookId)
        {
            return true;
        }

        return ReferencesEvidenceToken(evidenceLink, ledgerBookId.ToString("D")) ||
               ReferencesEvidenceToken(evidenceLink, ledgerBookId.ToString("N"));
    }

    private static bool HasManualJournalTenantCompanyProvenance(
        ManualJournalEntryDraftDto journalEntry,
        string evidenceLink)
        => ReferencesOptionalScope(evidenceLink, "tenant", "tenantId", journalEntry.TenantId) &&
           ReferencesOptionalScope(evidenceLink, "company", "companyId", journalEntry.CompanyId);

    private static bool ReferencesOptionalScope(
        string evidenceLink,
        string pathToken,
        string queryToken,
        string? scopeValue)
    {
        var normalized = NormalizeOptional(scopeValue);
        if (normalized is null)
        {
            return true;
        }

        return ReferencesScopedEvidenceToken(evidenceLink, $"{pathToken}:", normalized) ||
               ReferencesScopedEvidenceToken(evidenceLink, $"{pathToken}/", normalized) ||
               ReferencesScopedEvidenceToken(evidenceLink, $"{queryToken}=", normalized) ||
               ReferencesScopedEvidenceToken(evidenceLink, $"{queryToken}:", normalized) ||
               ReferencesScopedEvidenceToken(evidenceLink, $"{queryToken}/", normalized);
    }

    private static bool ReferencesScopedEvidenceToken(string evidenceLink, string prefix, string token)
    {
        var searchIndex = 0;
        while (searchIndex < evidenceLink.Length)
        {
            var prefixIndex = evidenceLink.IndexOf(prefix, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (prefixIndex < 0)
            {
                return false;
            }

            var tokenIndex = prefixIndex + prefix.Length;
            if (evidenceLink.Length >= tokenIndex + token.Length &&
                string.Compare(evidenceLink, tokenIndex, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                IsEvidenceTokenBoundary(evidenceLink, tokenIndex + token.Length))
            {
                return true;
            }

            searchIndex = tokenIndex;
        }

        return false;
    }

    private static bool ReferencesEvidenceToken(string evidenceLink, string token)
    {
        var normalized = NormalizeOptional(token);
        if (normalized is null)
        {
            return false;
        }

        var searchIndex = 0;
        while (searchIndex < evidenceLink.Length)
        {
            var tokenIndex = evidenceLink.IndexOf(normalized, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (tokenIndex < 0)
            {
                return false;
            }

            if (IsEvidenceTokenBoundary(evidenceLink, tokenIndex - 1) &&
                IsEvidenceTokenBoundary(evidenceLink, tokenIndex + normalized.Length))
            {
                return true;
            }

            searchIndex = tokenIndex + normalized.Length;
        }

        return false;
    }

    private static bool IsEvidenceTokenBoundary(string evidenceLink, int index)
        => index < 0 ||
           index >= evidenceLink.Length ||
           evidenceLink[index] is '/' or ':' or '=' or '?' or '&' or '#' or ';' or ',' or ')' or ']' or '}' or ' ' or '\t' or '\r' or '\n';

    private static bool HasManualJournalCorrectionEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("reversal", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("reverse", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("rebook", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("correction", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasManualJournalApprovalEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approved", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("rejection", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("rejected", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasManualJournalPostingEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("posting", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("posted", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("ledger-post", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("certification", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasManualJournalCloseLockEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("close", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("period-lock", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("periodlock", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("certification", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasManualJournalCorrectionEvidenceWithProvenance(
        ManualJournalEntryDraftDto journalEntry,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasManualJournalCorrectionEvidence([link]) &&
            HasManualJournalLifecycleEvidenceProvenance(journalEntry, [link]));

    private static bool HasManualJournalApprovalEvidenceWithProvenance(
        ManualJournalEntryDraftDto journalEntry,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasManualJournalApprovalEvidence([link]) &&
            HasManualJournalLifecycleEvidenceProvenance(journalEntry, [link]));

    private static bool HasManualJournalPostingEvidenceWithProvenance(
        ManualJournalEntryDraftDto journalEntry,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasManualJournalPostingEvidence([link]) &&
            HasManualJournalLifecycleEvidenceProvenance(journalEntry, [link]));

    private static bool HasManualJournalCloseLockEvidenceWithProvenance(
        ManualJournalEntryDraftDto journalEntry,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasManualJournalCloseLockEvidence([link]) &&
            HasManualJournalLifecycleEvidenceProvenance(journalEntry, [link]));

    private static AccountingTemplateLineSideDto ReverseSide(AccountingTemplateLineSideDto side)
        => side == AccountingTemplateLineSideDto.Debit
            ? AccountingTemplateLineSideDto.Credit
            : AccountingTemplateLineSideDto.Debit;

    private static JournalEntryLifecycleTransitionDto BuildTransition(
        ManualJournalEntryStatusDto fromStatus,
        ManualJournalEntryStatusDto toStatus,
        JournalEntryLifecycleActionRequestDto request,
        DateTimeOffset recordedAtUtc)
        => new(
            $"manual-je-transition-{Guid.NewGuid():N}",
            fromStatus,
            toStatus,
            request.Action,
            RequireText(request.Actor, nameof(request.Actor)),
            recordedAtUtc,
            NormalizeOptional(request.Notes),
            NormalizeOptional(request.CorrelationId),
            request.EvidenceLinks);

    private static JournalEntryLifecycleTransitionDto BuildSubmitTransition(
        ManualJournalEntryStatusDto fromStatus,
        SubmitManualJournalEntryApprovalRequest request,
        DateTimeOffset recordedAtUtc)
        => new(
            $"manual-je-transition-{Guid.NewGuid():N}",
            fromStatus,
            ManualJournalEntryStatusDto.Submitted,
            JournalEntryLifecycleActionDto.Submit,
            RequireText(request.Actor, nameof(request.Actor)),
            recordedAtUtc,
            NormalizeOptional(request.Notes),
            NormalizeOptional(request.CorrelationId),
            request.EvidenceLinks);

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException(
                $"Reviewed automation cannot {action}; a human operator approval is required.");
        }
    }

    private static void EnsurePeriodUnlocked(bool periodIsLocked, string action)
    {
        if (periodIsLocked)
        {
            throw new InvalidOperationException(
                $"Cannot {action} because the accounting period is locked after close.");
        }
    }

    private static void EnsureRequestedLedgerBookMatchesDraft(
        Guid? requestedLedgerBookId,
        ManualJournalEntryDraftDto draft)
    {
        if (!requestedLedgerBookId.HasValue)
        {
            return;
        }

        if (draft.LedgerBookId == requestedLedgerBookId.Value)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Manual journal entry '{draft.JournalEntryId:D}' belongs to ledger book '{draft.LedgerBookId?.ToString("D") ?? "unscoped"}', not requested ledger book '{requestedLedgerBookId.Value:D}'.");
    }

    private static IReadOnlyDictionary<string, ChartOfAccountsNodeDto> BuildChartByPath(IReadOnlyList<ChartOfAccountsNodeDto> chart)
        => chart
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static item => item.IsArchived).First(),
                StringComparer.OrdinalIgnoreCase);

    private async Task<ManualJournalEntryDraftDto> NormalizeAndValidateAsync(
        ManualJournalEntryDraftDto draft,
        bool allowIncomplete,
        CancellationToken ct,
        bool periodIsLocked = false)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var fundProfileId = NormalizeFundProfileId(draft.FundProfileId);
        var configuration = await _configurationService.GetWorkspaceAsync(fundProfileId, draft.LedgerBookId, ct, draft.TenantId, draft.CompanyId).ConfigureAwait(false);
        var chartByPath = BuildChartByPath(configuration.ChartOfAccounts);
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        var lines = new List<ManualJournalEntryLineDto>(draft.Lines.Count);
        var attachments = NormalizeAttachments(draft.EvidenceAttachments, draft.PreparedBy);
        var evidenceLinks = MergeEvidenceLinks(draft.EvidenceLinks, attachments.Select(item => item.Uri).ToArray());
        var entryType = Enum.IsDefined(draft.EntryType)
            ? draft.EntryType
            : ManualJournalEntryTypeDto.General;
        var treasuryContext = NormalizeTreasuryContext(draft.TreasuryContext, draft.AccountingDate);
        var headerDimensions = NormalizeDimensionSet(
            draft.Dimensions,
            fundId: draft.FundNodeId ?? fundProfileId,
            entityId: draft.EntityId,
            investorId: treasuryContext?.InvestorId,
            capitalAccountId: treasuryContext?.CapitalAccountId);

        if (!Enum.IsDefined(draft.EntryType))
        {
            issues.Add(Issue("manual-je.entry-type-invalid", AccountingConfigurationValidationSeverityDto.Critical, "Manual journal entry type is not supported.", "entryType", "Select a supported entry type before submitting approval."));
        }

        if (periodIsLocked)
        {
            issues.Add(Issue(
                "manual-je.period-locked",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Manual journal entry is in a locked accounting period.",
                draft.PeriodId ?? draft.AccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "Create a reversal or rebook workflow in an unlocked adjustment period."));
        }

        if (!draft.LedgerBookId.HasValue)
        {
            issues.Add(Issue("manual-je.book-missing", AccountingConfigurationValidationSeverityDto.Critical, "Ledger book is required before a manual journal entry can be approved.", "ledgerBookId", "Select the book that owns this journal entry."));
        }
        else
        {
            await ValidateLedgerBookPeriodScopeAsync(
                draft,
                draft.LedgerBookId.Value,
                issues,
                ct).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(draft.Currency))
        {
            issues.Add(Issue("manual-je.currency-missing", AccountingConfigurationValidationSeverityDto.Critical, "Journal currency is required.", "currency", "Select the journal entry currency."));
        }

        if (!allowIncomplete && draft.Lines.Count < 2)
        {
            issues.Add(Issue("manual-je.lines-minimum", AccountingConfigurationValidationSeverityDto.Critical, "At least two journal lines are required for approval submission.", "lines", "Add debit and credit lines."));
        }

        ValidateRequiredDimensions(headerDimensions, allowIncomplete, "manual-je.dimensions", issues);

        if (!allowIncomplete && evidenceLinks.Count == 0)
        {
            issues.Add(Issue("manual-je.evidence-missing", AccountingConfigurationValidationSeverityDto.Critical, "At least one source document or evidence link is required before approval submission.", "evidence", "Attach source support or link retained evidence before submitting approval."));
        }

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.DisplayName) || string.IsNullOrWhiteSpace(attachment.Uri))
            {
                issues.Add(Issue("manual-je.evidence-invalid", AccountingConfigurationValidationSeverityDto.Critical, "Evidence attachments require a display name and route or path.", attachment.AttachmentId, "Complete the attachment label and evidence route before submitting approval."));
            }
        }

        foreach (var line in draft.Lines)
        {
            var lineDimensions = NormalizeDimensionSet(
                line.Dimensions,
                fundId: headerDimensions?.FundId,
                entityId: line.EntityId ?? headerDimensions?.EntityId,
                investorId: headerDimensions?.InvestorId,
                capitalAccountId: headerDimensions?.CapitalAccountId,
                instrumentId: line.SecurityId ?? headerDimensions?.InstrumentId,
                taxLotId: line.TaxLotId ?? headerDimensions?.TaxLotId,
                costCenterId: headerDimensions?.CostCenterId,
                counterpartyId: headerDimensions?.CounterpartyId,
                fallbackExternalDimensions: headerDimensions?.ExternalGlDimensions);
            var normalizedLine = line with
            {
                LineId = string.IsNullOrWhiteSpace(line.LineId) ? Guid.NewGuid().ToString("N") : line.LineId.Trim(),
                Currency = string.IsNullOrWhiteSpace(line.Currency) ? draft.Currency : line.Currency.Trim(),
                AccountPath = line.AccountPath?.Trim() ?? string.Empty,
                Description = NormalizeOptional(line.Description),
                EvidenceLink = NormalizeOptional(line.EvidenceLink),
                EntityId = NormalizeOptional(line.EntityId) ?? lineDimensions?.EntityId,
                FundAllocationId = NormalizeOptional(line.FundAllocationId),
                SecurityDisplayName = NormalizeOptional(line.SecurityDisplayName),
                TaxLotId = NormalizeOptional(line.TaxLotId) ?? lineDimensions?.TaxLotId,
                Dimensions = lineDimensions
            };

            if (normalizedLine.Amount <= 0m)
            {
                issues.Add(Issue("manual-je.line-amount", AccountingConfigurationValidationSeverityDto.Critical, "Manual journal lines must use positive amounts.", normalizedLine.LineId, "Enter a positive debit or credit amount."));
            }

            if (!chartByPath.TryGetValue(normalizedLine.AccountPath, out var account))
            {
                issues.Add(Issue("manual-je.account-missing", AccountingConfigurationValidationSeverityDto.Critical, $"GL account '{normalizedLine.AccountPath}' was not found.", normalizedLine.LineId, "Choose an active GL account from accounting configuration."));
            }
            else if (account.IsArchived)
            {
                issues.Add(Issue("manual-je.account-archived", AccountingConfigurationValidationSeverityDto.Critical, $"GL account '{account.Path}' is archived.", normalizedLine.LineId, "Choose an active GL account."));
            }

            if (!string.Equals(normalizedLine.Currency, draft.Currency, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Issue("manual-je.currency-mismatch", AccountingConfigurationValidationSeverityDto.Critical, "Line currency must match the journal entry currency for approval submission.", normalizedLine.LineId, "Use one currency per manual JE draft."));
            }

            if (normalizedLine.SecurityId.HasValue && !await SecurityExistsAsync(normalizedLine.SecurityId.Value, ct).ConfigureAwait(false))
            {
                issues.Add(Issue("manual-je.security-missing", AccountingConfigurationValidationSeverityDto.Critical, $"Security Master id '{normalizedLine.SecurityId:D}' was not found.", normalizedLine.LineId, "Choose a resolved Security Master instrument or clear the line security."));
            }

            ValidateRequiredDimensions(lineDimensions, allowIncomplete, normalizedLine.LineId, issues);

            lines.Add(normalizedLine);
        }

        ValidateTreasuryContext(treasuryContext, entryType, allowIncomplete, issues);

        var totalDebits = lines.Where(line => line.Side == AccountingTemplateLineSideDto.Debit).Sum(line => line.Amount);
        var totalCredits = lines.Where(line => line.Side == AccountingTemplateLineSideDto.Credit).Sum(line => line.Amount);
        var imbalance = totalDebits - totalCredits;
        if (imbalance != 0m)
        {
            issues.Add(Issue("manual-je.unbalanced", AccountingConfigurationValidationSeverityDto.Critical, $"Manual journal entry is unbalanced: debits={totalDebits}, credits={totalCredits}.", draft.JournalEntryId.ToString("D"), "Adjust lines so debits equal credits."));
        }

        return draft with
        {
            JournalEntryId = draft.JournalEntryId == Guid.Empty ? Guid.NewGuid() : draft.JournalEntryId,
            FundProfileId = fundProfileId,
            Currency = string.IsNullOrWhiteSpace(draft.Currency) ? "USD" : draft.Currency.Trim().ToUpperInvariant(),
            Memo = draft.Memo?.Trim() ?? string.Empty,
            PreparedBy = NormalizeOptional(draft.PreparedBy) ?? "unknown",
            Lines = lines,
            EvidenceLinks = evidenceLinks,
            EvidenceAttachments = attachments,
            TotalDebits = totalDebits,
            TotalCredits = totalCredits,
            Imbalance = imbalance,
            EntryType = entryType,
            TreasuryContext = treasuryContext,
            Dimensions = headerDimensions,
            ValidationIssues = issues.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private async Task ValidateLedgerBookPeriodScopeAsync(
        ManualJournalEntryDraftDto draft,
        Guid ledgerBookId,
        List<AccountingConfigurationValidationIssueDto> issues,
        CancellationToken ct)
    {
        if (_journalStore is null ||
            string.IsNullOrWhiteSpace(draft.PeriodId) ||
            !Guid.TryParse(draft.PeriodId, out var periodId))
        {
            return;
        }

        var period = await _journalStore.GetPeriodAsync(periodId, ct).ConfigureAwait(false);
        if (period is null)
        {
            issues.Add(Issue(
                "manual-je.period-missing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Ledger period '{periodId:D}' was not found.",
                "periodId",
                "Select an accounting period from the active ledger book before approval submission."));
            return;
        }

        if (period.LedgerBookId != ledgerBookId)
        {
            issues.Add(Issue(
                "manual-je.period-book-mismatch",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Ledger period '{periodId:D}' belongs to ledger book '{period.LedgerBookId?.ToString("D") ?? "unscoped"}', not '{ledgerBookId:D}'.",
                "periodId",
                "Select a period that belongs to the journal entry ledger book."));
        }

        // Closing entries are the governed exception: they must post into the (closed) period
        // being finalized, so the closed-period bar does not apply to them. The posting guard and
        // the ClosingEntry posting kind carry the governance for this path.
        if (draft.EntryType != ManualJournalEntryTypeDto.ClosingEntry &&
            !string.Equals(period.Status, "Open", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Issue(
                "manual-je.period-closed",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Ledger period '{periodId:D}' is {period.Status} and cannot accept manual journal approval workflow changes.",
                "periodId",
                "Create the journal entry in an open adjustment period or use governed late-adjustment workflow."));
        }
    }

    private async Task<bool> SecurityExistsAsync(Guid securityId, CancellationToken ct)
    {
        if (_securityMasterQueryService is null)
        {
            return true;
        }

        var detail = await _securityMasterQueryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        return detail is not null;
    }

    private async Task AppendAuditAsync(
        ManualJournalEntryDraftDto draft,
        string action,
        string actor,
        string? correlationId,
        IReadOnlyList<string> evidenceLinks,
        IReadOnlyList<string>? reportGroupPrincipalIds,
        CancellationToken ct)
    {
        var hash = Hash(draft);
        await _auditStore.AppendAsync(
            new AccountingActionAuditEventDto(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                RequireText(actor, nameof(actor)),
                action,
                draft.FundProfileId,
                draft.LedgerBookId,
                NormalizeOptional(correlationId),
                hash,
                hash,
                draft.ValidationIssues,
                evidenceLinks,
                draft.CompanyId,
                NormalizePrincipalIds(reportGroupPrincipalIds),
                draft.TenantId),
            ct).ConfigureAwait(false);
    }

    private static AccountingConfigurationValidationIssueDto Issue(
        string code,
        AccountingConfigurationValidationSeverityDto severity,
        string message,
        string? targetId,
        string? suggestedAction)
        => new(code, severity, message, targetId, suggestedAction);

    private static string Hash(ManualJournalEntryDraftDto draft)
    {
        var json = JsonSerializer.Serialize(draft);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static IReadOnlyList<string> MergeEvidenceLinks(
        IReadOnlyList<string> existing,
        IReadOnlyList<string>? incoming)
        => existing.Concat(incoming ?? [])
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .Select(link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<ManualJournalEntryEvidenceAttachmentDto> NormalizeAttachments(
        IReadOnlyList<ManualJournalEntryEvidenceAttachmentDto>? attachments,
        string? fallbackActor)
    {
        var actor = NormalizeOptional(fallbackActor) ?? "unknown";
        return (attachments ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.DisplayName) || !string.IsNullOrWhiteSpace(item.Uri))
            .Select(item => item with
            {
                AttachmentId = string.IsNullOrWhiteSpace(item.AttachmentId) ? Guid.NewGuid().ToString("N") : item.AttachmentId.Trim(),
                DisplayName = item.DisplayName?.Trim() ?? string.Empty,
                EvidenceKind = string.IsNullOrWhiteSpace(item.EvidenceKind) ? "SourceDocument" : item.EvidenceKind.Trim(),
                Uri = item.Uri?.Trim() ?? string.Empty,
                SourceSystem = string.IsNullOrWhiteSpace(item.SourceSystem) ? "ManualUpload" : item.SourceSystem.Trim(),
                AddedAtUtc = item.AddedAtUtc == default ? DateTimeOffset.UtcNow : item.AddedAtUtc,
                AddedBy = string.IsNullOrWhiteSpace(item.AddedBy) ? actor : item.AddedBy.Trim(),
                LineId = NormalizeOptional(item.LineId),
                Description = NormalizeOptional(item.Description)
            })
            .GroupBy(item => item.AttachmentId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.AddedAtUtc)
            .ThenBy(item => item.AttachmentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static TreasuryLedgerContextDto? NormalizeTreasuryContext(
        TreasuryLedgerContextDto? context,
        DateOnly accountingDate)
    {
        if (context is null)
        {
            return null;
        }

        return context with
        {
            EffectiveDate = context.EffectiveDate ?? accountingDate,
            IdempotencyKey = NormalizeOptional(context.IdempotencyKey),
            FundEventId = NormalizeOptional(context.FundEventId),
            FundEventType = NormalizeOptional(context.FundEventType),
            CapitalAccountId = NormalizeOptional(context.CapitalAccountId),
            InvestorId = NormalizeOptional(context.InvestorId),
            PaymentIntentId = NormalizeOptional(context.PaymentIntentId),
            SettlementReference = NormalizeOptional(context.SettlementReference)
        };
    }

    private static LedgerDimensionSetDto? NormalizeDimensionSet(
        LedgerDimensionSetDto? dimensions,
        string? fundId = null,
        string? entityId = null,
        string? sleeveId = null,
        string? strategyId = null,
        string? investorId = null,
        string? capitalAccountId = null,
        Guid? instrumentId = null,
        string? taxLotId = null,
        string? costCenterId = null,
        string? counterpartyId = null,
        IReadOnlyDictionary<string, string>? fallbackExternalDimensions = null)
    {
        var externalDimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in fallbackExternalDimensions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            var key = NormalizeOptional(item.Key);
            var value = NormalizeOptional(item.Value);
            if (key is not null && value is not null)
            {
                externalDimensions[key] = value;
            }
        }

        foreach (var item in dimensions?.ExternalGlDimensions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            var key = NormalizeOptional(item.Key);
            var value = NormalizeOptional(item.Value);
            if (key is not null && value is not null)
            {
                externalDimensions[key] = value;
            }
        }

        var normalized = new LedgerDimensionSetDto(
            FundId: NormalizeOptional(dimensions?.FundId) ?? NormalizeOptional(fundId),
            EntityId: NormalizeOptional(dimensions?.EntityId) ?? NormalizeOptional(entityId),
            SleeveId: NormalizeOptional(dimensions?.SleeveId) ?? NormalizeOptional(sleeveId),
            StrategyId: NormalizeOptional(dimensions?.StrategyId) ?? NormalizeOptional(strategyId),
            InvestorId: NormalizeOptional(dimensions?.InvestorId) ?? NormalizeOptional(investorId),
            CapitalAccountId: NormalizeOptional(dimensions?.CapitalAccountId) ?? NormalizeOptional(capitalAccountId),
            InstrumentId: dimensions?.InstrumentId ?? instrumentId,
            TaxLotId: NormalizeOptional(dimensions?.TaxLotId) ?? NormalizeOptional(taxLotId),
            CostCenterId: NormalizeOptional(dimensions?.CostCenterId) ?? NormalizeOptional(costCenterId),
            CounterpartyId: NormalizeOptional(dimensions?.CounterpartyId) ?? NormalizeOptional(counterpartyId),
            ExternalGlDimensions: externalDimensions
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
        {
            PositionId = dimensions?.PositionId
        };

        return HasAnyDimension(normalized) ? normalized : null;
    }

    private static bool HasAnyDimension(LedgerDimensionSetDto dimensions)
        => !string.IsNullOrWhiteSpace(dimensions.FundId) ||
           !string.IsNullOrWhiteSpace(dimensions.EntityId) ||
           !string.IsNullOrWhiteSpace(dimensions.SleeveId) ||
           !string.IsNullOrWhiteSpace(dimensions.StrategyId) ||
           !string.IsNullOrWhiteSpace(dimensions.InvestorId) ||
           !string.IsNullOrWhiteSpace(dimensions.CapitalAccountId) ||
           dimensions.InstrumentId.HasValue ||
           dimensions.PositionId.HasValue ||
           !string.IsNullOrWhiteSpace(dimensions.TaxLotId) ||
           !string.IsNullOrWhiteSpace(dimensions.CostCenterId) ||
           !string.IsNullOrWhiteSpace(dimensions.CounterpartyId) ||
           dimensions.ExternalGlDimensions.Count > 0;

    private static void ValidateRequiredDimensions(
        LedgerDimensionSetDto? dimensions,
        bool allowIncomplete,
        string targetId,
        List<AccountingConfigurationValidationIssueDto> issues)
    {
        var severity = allowIncomplete
            ? AccountingConfigurationValidationSeverityDto.Warning
            : AccountingConfigurationValidationSeverityDto.Critical;
        if (string.IsNullOrWhiteSpace(dimensions?.FundId))
        {
            issues.Add(Issue(
                "manual-je.dimension-fund-missing",
                severity,
                "Manual journal entry requires a fund dimension.",
                targetId,
                "Attach the fund dimension before approval submission."));
        }

        if (string.IsNullOrWhiteSpace(dimensions?.EntityId))
        {
            issues.Add(Issue(
                "manual-je.dimension-entity-missing",
                severity,
                "Manual journal entry requires an entity dimension.",
                targetId,
                "Attach the legal entity dimension before approval submission."));
        }
    }

    private static void ValidateTreasuryContext(
        TreasuryLedgerContextDto? context,
        ManualJournalEntryTypeDto entryType,
        bool allowIncomplete,
        List<AccountingConfigurationValidationIssueDto> issues)
    {
        var requiresPrivateCapitalContext = RequiresPrivateCapitalTreasuryContext(entryType);
        if (allowIncomplete && context is null)
        {
            return;
        }

        if (context is null)
        {
            if (requiresPrivateCapitalContext)
            {
                issues.Add(Issue("manual-je.treasury-context-missing", AccountingConfigurationValidationSeverityDto.Critical, "Private-capital manual journal entries require treasury ledger context before approval submission.", "treasuryContext", "Attach effective date, idempotency, fund event, and capital account context."));
            }

            return;
        }

        var hasAnyContext =
            context.EffectiveDate is not null ||
            !string.IsNullOrWhiteSpace(context.IdempotencyKey) ||
            !string.IsNullOrWhiteSpace(context.FundEventId) ||
            !string.IsNullOrWhiteSpace(context.FundEventType) ||
            !string.IsNullOrWhiteSpace(context.CapitalAccountId) ||
            !string.IsNullOrWhiteSpace(context.InvestorId) ||
            !string.IsNullOrWhiteSpace(context.PaymentIntentId) ||
            !string.IsNullOrWhiteSpace(context.SettlementReference);
        if (allowIncomplete && !hasAnyContext)
        {
            return;
        }

        if (!allowIncomplete && context.EffectiveDate is null)
        {
            issues.Add(Issue("manual-je.treasury-effective-date-missing", AccountingConfigurationValidationSeverityDto.Critical, "Treasury ledger context requires an effective date before approval submission.", "treasuryContext.effectiveDate", "Set the business-effective date used for balance reconstruction."));
        }

        if (!allowIncomplete && string.IsNullOrWhiteSpace(context.IdempotencyKey))
        {
            issues.Add(Issue("manual-je.treasury-idempotency-missing", AccountingConfigurationValidationSeverityDto.Critical, "Treasury ledger context requires an idempotency key before approval submission.", "treasuryContext.idempotencyKey", "Use a stable source-event key for retry-safe posting."));
        }

        var hasFundEventContext = requiresPrivateCapitalContext ||
            !string.IsNullOrWhiteSpace(context.FundEventId) ||
            !string.IsNullOrWhiteSpace(context.FundEventType) ||
            !string.IsNullOrWhiteSpace(context.CapitalAccountId) ||
            !string.IsNullOrWhiteSpace(context.InvestorId);
        if (!allowIncomplete && hasFundEventContext)
        {
            RequireTreasuryContextText(issues, context.FundEventId, "manual-je.fund-event-missing", "fund event id", "treasuryContext.fundEventId");
            RequireTreasuryContextText(issues, context.FundEventType, "manual-je.fund-event-type-missing", "fund event type", "treasuryContext.fundEventType");
            RequireTreasuryContextText(issues, context.CapitalAccountId, "manual-je.capital-account-missing", "capital account id", "treasuryContext.capitalAccountId");
        }
    }

    private static bool RequiresPrivateCapitalTreasuryContext(ManualJournalEntryTypeDto entryType)
        => entryType is ManualJournalEntryTypeDto.CapitalCall
            or ManualJournalEntryTypeDto.Distribution
            or ManualJournalEntryTypeDto.Subscription
            or ManualJournalEntryTypeDto.Redemption
            or ManualJournalEntryTypeDto.LpTransfer
            or ManualJournalEntryTypeDto.ManagementFee;

    private static bool CanSubmitManualJournalEntry(ManualJournalEntryStatusDto status)
        => status is ManualJournalEntryStatusDto.Draft
            or ManualJournalEntryStatusDto.NeedsFix
            or ManualJournalEntryStatusDto.Rejected;

    private static void RequireTreasuryContextText(
        List<AccountingConfigurationValidationIssueDto> issues,
        string? value,
        string code,
        string label,
        string targetId)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        issues.Add(Issue(
            code,
            AccountingConfigurationValidationSeverityDto.Critical,
            $"Treasury ledger context requires {label} before approval submission.",
            targetId,
            $"Attach {label} to the retained fund-event evidence."));
    }

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? DefaultFundProfileId : value.Trim();

    private static string NormalizeCurrency(string? value)
        => string.IsNullOrWhiteSpace(value) ? "USD" : value.Trim().ToUpperInvariant();

    private static string RequireText(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizePrincipalIds(IReadOnlyList<string>? values)
        => values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];
}
