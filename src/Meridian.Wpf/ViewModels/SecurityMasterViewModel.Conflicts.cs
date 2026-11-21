using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using ISmQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ISmService = Meridian.Contracts.SecurityMaster.ISecurityMasterService;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed partial class SecurityMasterViewModel
{
    private void NotifyConflictWorkflowCommandsChanged()
    {
        RaiseConflictDerivedStateChanged();
        RaisePropertyChanged(nameof(SelectedConflictSummaryText));
        RaisePropertyChanged(nameof(CanResolveSelectedConflict));
        AcceptPrimaryConflictCommand.NotifyCanExecuteChanged();
        AcceptSecondaryConflictCommand.NotifyCanExecuteChanged();
        DismissConflictCommand.NotifyCanExecuteChanged();
        ApplyRecommendedConflictResolutionCommand.NotifyCanExecuteChanged();
        ApplyBulkRecommendedResolutionsCommand.NotifyCanExecuteChanged();
    }

    private void RaiseConflictDerivedStateChanged()
    {
        RaisePropertyChanged(nameof(ConflictSummaryText));
        RaisePropertyChanged(nameof(ConflictQueueSummaryText));
        RaisePropertyChanged(nameof(ConflictGroupCount));
        RaisePropertyChanged(nameof(HasConflictGroups));
        RaisePropertyChanged(nameof(HasSelectedConflictGroup));
        RaisePropertyChanged(nameof(HasSelectedConflictEntry));
        RaisePropertyChanged(nameof(SelectedConflictSummaryText));
        RaisePropertyChanged(nameof(SelectedConflictSeverityText));
        RaisePropertyChanged(nameof(SelectedConflictConfidenceText));
        RaisePropertyChanged(nameof(SelectedConflictImpactText));
        RaisePropertyChanged(nameof(SelectedConflictAutoResolveText));
        RaisePropertyChanged(nameof(SelectedConflictDetectedText));
        RaisePropertyChanged(nameof(ConflictFilterSummaryText));
        RaisePropertyChanged(nameof(SelectedConflictRecommendedActionText));
        RaisePropertyChanged(nameof(SelectedConflictWinnerText));
        RaisePropertyChanged(nameof(BulkPreviewSummaryText));
        RaisePropertyChanged(nameof(CanApplyRecommendedConflictResolution));
        RaisePropertyChanged(nameof(CanApplyBulkRecommendedResolutions));
        RaisePropertyChanged(nameof(AcceptPrimaryConflictLabel));
        RaisePropertyChanged(nameof(AcceptSecondaryConflictLabel));
        RaisePropertyChanged(nameof(SelectedSecurityConflictSummaryText));
        RaisePropertyChanged(nameof(SelectedTrustPostureText));
        RaisePropertyChanged(nameof(ActionInspectorLeadText));
        RaisePropertyChanged(nameof(ActionInspectorDetailText));
        RaisePropertyChanged(nameof(ActionInspectorNoActionText));
        RaisePropertyChanged(nameof(ShowFundReviewActions));
        RaisePropertyChanged(nameof(ShowReconciliationAction));
        RaisePropertyChanged(nameof(ShowCashFlowAction));
        RaisePropertyChanged(nameof(ShowReportPackAction));
        RaisePropertyChanged(nameof(ShowBackfillTradingParamsAction));
        RaisePropertyChanged(nameof(ShowAnyDownstreamAction));
        RaisePropertyChanged(nameof(HasPortfolioImpactLink));
        RaisePropertyChanged(nameof(HasLedgerImpactLink));
        RaisePropertyChanged(nameof(HasReconciliationImpactLink));
        RaisePropertyChanged(nameof(HasReportPackImpactLink));
    }

    private void AlignConflictLaneToSelectedSecurity()
    {
        if (SelectedSecurity is null || ConflictGroups.Count == 0)
        {
            return;
        }

        var matchingGroup = ConflictGroups.FirstOrDefault(group => group.SecurityId == SelectedSecurity.SecurityId);
        if (matchingGroup is null)
        {
            return;
        }

        if (SelectedConflictEntry is not null && SelectedConflictEntry.Conflict.SecurityId == matchingGroup.SecurityId)
        {
            if (SelectedConflictGroup != matchingGroup)
            {
                _suppressConflictLaneSelectionSync = true;
                try
                {
                    SelectedConflictGroup = matchingGroup;
                }
                finally
                {
                    _suppressConflictLaneSelectionSync = false;
                }
            }

            return;
        }

        _suppressConflictLaneSelectionSync = true;
        try
        {
            SelectedConflictGroup = matchingGroup;
            SelectedConflictEntry = matchingGroup.Conflicts.FirstOrDefault();
        }
        finally
        {
            _suppressConflictLaneSelectionSync = false;
        }
    }

    private void RebuildFilteredConflicts()
        => RebuildFilteredConflicts(SelectedConflict?.ConflictId);

    private void RebuildFilteredConflicts(Guid? preferredConflictId)
    {
        IEnumerable<SecurityMasterConflict> source = SelectedTrustSnapshot?.ConflictAssessments.Select(assessment => assessment.Conflict)
            ?? OpenConflicts;

        if (ShowOnlySelectedSecurityConflicts && SelectedSecurity is not null)
        {
            source = source.Where(conflict => conflict.SecurityId == SelectedSecurity.SecurityId);
        }

        var baseConflicts = source.ToArray();
        var filtered = baseConflicts
            .Where(conflict => !ShowHighImpactConflictsOnly ||
                               GetSelectedConflictAssessment(conflict.ConflictId)?.ImpactSeverity == SecurityMasterImpactSeverity.High)
            .Where(conflict => !ShowBulkEligibleConflictsOnly ||
                               GetSelectedConflictAssessment(conflict.ConflictId)?.IsBulkEligible == true)
            .OrderByDescending(conflict => SecurityMasterPresentation.GetImpactSeverityRank(GetSelectedConflictAssessment(conflict.ConflictId)?.ImpactSeverity ?? SecurityMasterImpactSeverity.Unknown))
            .ThenByDescending(conflict => conflict.DetectedAt)
            .ToArray();

        FilteredConflicts.Clear();
        foreach (var conflict in filtered)
        {
            FilteredConflicts.Add(conflict);
        }

        ConflictFilterSummaryText = BuildConflictFilterSummary(baseConflicts.Length, filtered.Length);
        BulkPreviewSummaryText = BuildBulkPreviewSummary();
        CanApplyBulkRecommendedResolutions = GetVisibleConflictAssessments().Any(assessment => assessment.IsBulkEligible);

        var nextSelectedConflict = filtered.FirstOrDefault(conflict => preferredConflictId.HasValue && conflict.ConflictId == preferredConflictId.Value)
            ?? filtered.FirstOrDefault();
        SelectedConflict = nextSelectedConflict;

        if (nextSelectedConflict is null)
        {
            UpdateSelectedConflictAssessmentState();
        }
    }

    private void RebuildRecommendedActions()
    {
        RecommendedActions.Clear();
        if (SelectedTrustSnapshot is null)
        {
            return;
        }

        var selectedConflictAssessment = GetSelectedConflictAssessment();
        if (selectedConflictAssessment is not null)
        {
            RecommendedActions.Add(SecurityMasterPresentation.CreateSelectedConflictAction(selectedConflictAssessment));
        }

        foreach (var action in SelectedTrustSnapshot.RecommendedActions
                     .Where(action => action.Kind != SecurityMasterRecommendedActionKind.ResolveSelectedConflict)
                     .OrderBy(action => SecurityMasterPresentation.GetRecommendedActionOrder(action.Kind)))
        {
            RecommendedActions.Add(action);
        }

        if (selectedConflictAssessment is null)
        {
            var defaultResolveAction = SelectedTrustSnapshot.RecommendedActions
                .FirstOrDefault(action => action.Kind == SecurityMasterRecommendedActionKind.ResolveSelectedConflict);
            if (defaultResolveAction is not null)
            {
                RecommendedActions.Insert(0, defaultResolveAction);
            }
        }

        RaisePropertyChanged(nameof(ActionInspectorLeadText));
        RaisePropertyChanged(nameof(ActionInspectorDetailText));
        RaisePropertyChanged(nameof(ActionInspectorNoActionText));
    }

    private void SyncLegacySelectionStateFromSnapshot(SecurityMasterTrustSnapshotDto snapshot)
    {
        if (SelectedSecurity?.SecurityId != snapshot.Security.SecurityId)
        {
            _suppressSelectedSecurityLoadCancellation = true;
            try
            {
                SelectedSecurity = snapshot.Security;
            }
            finally
            {
                _suppressSelectedSecurityLoadCancellation = false;
            }
        }

        _selectedEconomicDefinition = null;
        _selectedTradingParameters = null;
        _latestHistoryEvent = snapshot.History
            .OrderByDescending(item => item.EventTimestamp)
            .FirstOrDefault();

        HistoryText = SecurityMasterPresentation.BuildHistoryText(snapshot.History);

        CorporateActions.Clear();
        foreach (var action in snapshot.CorporateActions.OrderByDescending(action => action.ExDate))
        {
            CorporateActions.Add(action);
        }

        _conflictSecurityContextCache[snapshot.Security.SecurityId] = SecurityMasterPresentation.ToSecurityContext(snapshot.Security);
    }

    private void UpdateSelectedConflictAssessmentState()
    {
        var assessment = GetSelectedConflictAssessment();
        if (assessment is null)
        {
            SelectedConflictRecommendedActionText = "Select a conflict to see the recommended action.";
            SelectedConflictWinnerText = "Select a conflict to compare the current winner.";
            _selectedConflictImpactText = "Select a conflict to review downstream impact.";
            CanApplyRecommendedConflictResolution = false;
            RaisePropertyChanged(nameof(SelectedConflictImpactText));
            return;
        }

        SelectedConflictRecommendedActionText = assessment.Recommendation switch
        {
            SecurityMasterConflictRecommendationKind.DismissAsEquivalent =>
                "Dismiss as equivalent because the normalized values match.",
            SecurityMasterConflictRecommendationKind.Challenger =>
                $"Accept {assessment.ChallengerSource} because the current winning value is blank.",
            SecurityMasterConflictRecommendationKind.PreserveWinner =>
                $"Preserve {assessment.CurrentWinningSource} as the current winner.",
            _ =>
                "Manual review required before applying a resolution."
        };
        SelectedConflictWinnerText = assessment.RecommendedWinner;
        _selectedConflictImpactText = $"{assessment.ImpactSummary} {assessment.ImpactDetail}".Trim();
        CanApplyRecommendedConflictResolution = !string.IsNullOrWhiteSpace(assessment.RecommendedResolution);
        RaisePropertyChanged(nameof(SelectedConflictImpactText));
    }

    private SecurityMasterConflictAssessmentDto? GetSelectedConflictAssessment(Guid? conflictId = null)
    {
        var effectiveConflictId = conflictId ?? SelectedConflict?.ConflictId;
        return effectiveConflictId.HasValue && _conflictAssessmentById.TryGetValue(effectiveConflictId.Value, out var assessment)
            ? assessment
            : null;
    }

    private IEnumerable<SecurityMasterConflictAssessmentDto> GetVisibleConflictAssessments()
    {
        if (SelectedTrustSnapshot is null || FilteredConflicts.Count == 0)
        {
            return [];
        }

        var visibleIds = FilteredConflicts
            .Select(conflict => conflict.ConflictId)
            .ToHashSet();
        return SelectedTrustSnapshot.ConflictAssessments
            .Where(assessment => visibleIds.Contains(assessment.Conflict.ConflictId));
    }

    private void ResetConflictFilters()
    {
        var changed = ShowOnlySelectedSecurityConflicts != true ||
                      ShowHighImpactConflictsOnly ||
                      ShowBulkEligibleConflictsOnly;

        ShowOnlySelectedSecurityConflicts = true;
        ShowHighImpactConflictsOnly = false;
        ShowBulkEligibleConflictsOnly = false;

        if (!changed)
        {
            RebuildFilteredConflicts();
        }
    }

    private void PreviewBulkRecommendedResolutions()
    {
        BulkPreviewSummaryText = BuildBulkPreviewSummary();
        CanApplyBulkRecommendedResolutions = GetVisibleConflictAssessments().Any(assessment => assessment.IsBulkEligible);
    }

    private async Task ApplyBulkRecommendedResolutionsAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ConflictOperatorText))
        {
            _notificationService.ShowNotification("Security Master", "Resolved By is required for bulk resolution.", NotificationType.Warning);
            return;
        }

        var eligibleAssessments = GetVisibleConflictAssessments()
            .Where(assessment => assessment.IsBulkEligible)
            .ToArray();
        if (eligibleAssessments.Length == 0)
        {
            PreviewBulkRecommendedResolutions();
            _notificationService.ShowNotification("Security Master", "No low-risk conflicts qualify for bulk assist in the current filter.", NotificationType.Info);
            return;
        }

        try
        {
            var response = await _workstationSecurityMasterApiClient
                .BulkResolveConflictsAsync(
                    new BulkResolveSecurityMasterConflictsRequest(
                        ConflictIds: eligibleAssessments.Select(assessment => assessment.Conflict.ConflictId).ToArray(),
                        ResolvedBy: ConflictOperatorText.Trim(),
                        Reason: string.IsNullOrWhiteSpace(ConflictNoteText) ? null : ConflictNoteText.Trim(),
                        FundProfileId: GetCurrentFundProfileId()),
                    ct)
                .ConfigureAwait(false);

            if (!response.Success || response.Data is null)
            {
                var errorMessage = string.IsNullOrWhiteSpace(response.ErrorMessage)
                    ? "Bulk conflict resolution failed."
                    : $"Bulk conflict resolution failed: {response.ErrorMessage}";
                _notificationService.ShowNotification("Security Master", errorMessage, NotificationType.Error);
                return;
            }

            var result = response.Data;

            var summary = $"{result.Resolved} of {result.Requested} requested conflict(s) resolved.";
            if (result.Skipped > 0)
            {
                var skipDetail = string.Join("; ", result.SkippedReasons.Values.Take(2));
                summary = $"{summary} {result.Skipped} skipped{(string.IsNullOrWhiteSpace(skipDetail) ? "." : $": {skipDetail}.")}";
            }

            await RefreshOperatorWorkflowAsync(ct).ConfigureAwait(false);
            if (SelectedSecurity is not null)
            {
                await LoadSelectedTrustSnapshotAsync(SelectedSecurity.SecurityId, ct).ConfigureAwait(false);
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ConflictNoteText = string.Empty;
                BulkPreviewSummaryText = summary;
                _notificationService.ShowNotification(
                    "Security Master",
                    summary,
                    result.Resolved > 0 && result.Skipped == 0 ? NotificationType.Success : NotificationType.Warning);
            });
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected when the view is torn down mid-operation; benign.
            _loggingService.LogDebug(
                "Security Master operation cancelled.",
                ("view", GetType().Name));
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to apply bulk Security Master resolutions", ex);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                _notificationService.ShowNotification("Security Master", "Bulk conflict resolution failed.", NotificationType.Error));
        }
    }

    private async Task ApplyRecommendedConflictResolutionAsync(CancellationToken ct = default)
    {
        var assessment = GetSelectedConflictAssessment();
        if (assessment is null)
        {
            var recommendedAction = RecommendedActions
                .FirstOrDefault(action => action.Kind == SecurityMasterRecommendedActionKind.ResolveSelectedConflict && action.ConflictId.HasValue);
            assessment = recommendedAction?.ConflictId is Guid recommendedConflictId
                ? GetSelectedConflictAssessment(recommendedConflictId)
                : SelectedTrustSnapshot?.ConflictAssessments.FirstOrDefault();
        }

        if (assessment is null)
        {
            _notificationService.ShowNotification("Security Master", "No recommended conflict resolution is available.", NotificationType.Info);
            return;
        }

        if (string.IsNullOrWhiteSpace(assessment.RecommendedResolution))
        {
            _notificationService.ShowNotification("Security Master", "This conflict requires manual review.", NotificationType.Warning);
            return;
        }

        SelectedConflict = FilteredConflicts.FirstOrDefault(conflict => conflict.ConflictId == assessment.Conflict.ConflictId)
            ?? assessment.Conflict;
        await ResolveSelectedConflictAsync(assessment.RecommendedResolution, ct).ConfigureAwait(false);
    }

    private string BuildConflictFilterSummary(int baseCount, int filteredCount)
    {
        var activeFilters = new List<string>();
        if (ShowOnlySelectedSecurityConflicts)
        {
            activeFilters.Add("selected security");
        }

        if (ShowHighImpactConflictsOnly)
        {
            activeFilters.Add("high impact");
        }

        if (ShowBulkEligibleConflictsOnly)
        {
            activeFilters.Add("bulk eligible");
        }

        var filterText = activeFilters.Count == 0
            ? "all open conflicts"
            : string.Join(", ", activeFilters);

        return filteredCount == 0
            ? $"No conflicts match the current filter set ({filterText})."
            : $"{filteredCount} of {baseCount} conflict(s) shown for {filterText}.";
    }

    private string BuildBulkPreviewSummary()
    {
        if (SelectedTrustSnapshot is null)
        {
            return "Load a selected security to preview low-risk bulk resolutions.";
        }

        var visibleAssessments = GetVisibleConflictAssessments().ToArray();
        if (visibleAssessments.Length == 0)
        {
            return "No conflicts match the current filters.";
        }

        var eligibleCount = visibleAssessments.Count(assessment => assessment.IsBulkEligible);
        if (eligibleCount == 0)
        {
            var firstReason = visibleAssessments
                .Select(assessment => assessment.BulkIneligibilityReason)
                .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
            return string.IsNullOrWhiteSpace(firstReason)
                ? $"0 of {visibleAssessments.Length} filtered conflict(s) qualify for low-risk bulk assist."
                : $"0 of {visibleAssessments.Length} filtered conflict(s) qualify for low-risk bulk assist. {firstReason}";
        }

        return $"{eligibleCount} of {visibleAssessments.Length} filtered conflict(s) qualify for low-risk bulk assist.";
    }

    private async Task PrimeConflictSecurityContextCacheAsync(IReadOnlyList<SecurityMasterConflict> conflicts, CancellationToken ct)
    {
        if (SelectedSecurity is not null)
        {
            _conflictSecurityContextCache[SelectedSecurity.SecurityId] = SecurityMasterPresentation.ToSecurityContext(SelectedSecurity);
        }

        var missingSecurityIds = conflicts
            .Select(conflict => conflict.SecurityId)
            .Distinct()
            .Where(securityId => !_conflictSecurityContextCache.ContainsKey(securityId))
            .ToArray();
        if (missingSecurityIds.Length == 0)
        {
            return;
        }

        var lookupTasks = new List<Task<KeyValuePair<Guid, SecurityConflictSecurityContext>?>>(missingSecurityIds.Length);
        foreach (var securityId in missingSecurityIds)
        {
            lookupTasks.Add(LoadConflictSecurityContextAsync(securityId, ct));
        }

        var lookups = await Task.WhenAll(lookupTasks).ConfigureAwait(false);
        foreach (var lookup in lookups)
        {
            if (lookup.HasValue)
            {
                _conflictSecurityContextCache[lookup.Value.Key] = lookup.Value.Value;
            }
        }
    }

    private async Task<KeyValuePair<Guid, SecurityConflictSecurityContext>?> LoadConflictSecurityContextAsync(
        Guid securityId,
        CancellationToken ct)
    {
        try
        {
            var detailTask = _queryService.GetByIdAsync(securityId, ct);
            if (detailTask is null)
            {
                return null;
            }

            var detail = await detailTask.ConfigureAwait(false);
            return detail is null
                ? null
                : new KeyValuePair<Guid, SecurityConflictSecurityContext>(securityId, SecurityMasterPresentation.ToSecurityContext(detail));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private void RebuildConflictLaneGroups()
    {
        var groups = OpenConflicts
            .GroupBy(conflict => conflict.SecurityId)
            .Select(group =>
            {
                var context = GetConflictSecurityContext(group.Key);
                var entries = group
                    .Select(conflict => BuildConflictLaneEntry(conflict, context))
                    .OrderByDescending(entry => entry.SeverityRank)
                    .ThenBy(entry => entry.Conflict.DetectedAt)
                    .ToArray();
                var highestSeverity = entries.FirstOrDefault();
                var groupSummary = SecurityMasterPresentation.BuildConflictGroupSummary(entries);

                return new SecurityConflictLaneGroup(
                    SecurityId: group.Key,
                    SecurityLabel: context.DisplayName,
                    SecurityIdentifier: context.PrimaryIdentifier,
                    GroupSummary: groupSummary,
                    HighestSeverityLabel: highestSeverity?.SeverityLabel ?? "Review",
                    HighestSeverityTone: highestSeverity?.SeverityTone ?? "Info",
                    SafeAutoResolveCount: entries.Count(entry => entry.IsAutoResolveSafe),
                    Conflicts: entries);
            })
            .OrderByDescending(group => group.Conflicts.FirstOrDefault()?.SeverityRank ?? 0)
            .ThenByDescending(group => group.ConflictCount)
            .ThenBy(group => group.SecurityLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ConflictGroups.Clear();
        foreach (var group in groups)
        {
            ConflictGroups.Add(group);
        }
    }

    private SecurityConflictLaneEntry BuildConflictLaneEntry(
        SecurityMasterConflict conflict,
        SecurityConflictSecurityContext context)
    {
        var assessment = SecurityMasterPresentation.AssessConflict(conflict);

        return new SecurityConflictLaneEntry(
            Conflict: conflict,
            SecurityLabel: context.DisplayName,
            SecurityIdentifier: context.PrimaryIdentifier,
            FieldLabel: SecurityMasterPresentation.FormatFieldLabel(conflict.FieldPath),
            SeverityLabel: assessment.SeverityLabel,
            SeverityTone: assessment.SeverityTone,
            SeverityRank: assessment.SeverityRank,
            ConfidenceLabel: assessment.ConfidenceLabel,
            AutoResolveHint: assessment.AutoResolveHint,
            ImpactSummary: assessment.ImpactSummary,
            ImpactDetail: assessment.ImpactDetail,
            NextStepSummary: assessment.NextStepSummary,
            RoutesToFundReview: assessment.RoutesToFundReview,
            RoutesToReconciliation: assessment.RoutesToReconciliation,
            RoutesToCashFlow: assessment.RoutesToCashFlow,
            RoutesToReportPack: assessment.RoutesToReportPack,
            RequiresTradingBackfill: assessment.RequiresTradingBackfill,
            IsAutoResolveSafe: assessment.IsAutoResolveSafe);
    }

    private SecurityConflictSecurityContext GetConflictSecurityContext(Guid securityId)
    {
        if (_conflictSecurityContextCache.TryGetValue(securityId, out var cachedContext))
        {
            return cachedContext;
        }

        if (SelectedSecurity?.SecurityId == securityId)
        {
            return SecurityMasterPresentation.ToSecurityContext(SelectedSecurity);
        }

        var shortId = securityId.ToString("N")[..8].ToUpperInvariant();
        return new SecurityConflictSecurityContext(
            DisplayName: $"Security {shortId}",
            PrimaryIdentifier: $"SecurityId {shortId}",
            AssetClass: "Unknown");
    }

    private SecurityConflictLaneEntry? GetActiveConflictContextEntry()
    {
        if (SelectedConflictEntry is not null)
        {
            return SelectedConflictEntry;
        }

        if (SelectedSecurity is not null)
        {
            return ConflictGroups
                .FirstOrDefault(group => group.SecurityId == SelectedSecurity.SecurityId)?
                .Conflicts
                .FirstOrDefault();
        }

        return SelectedConflictGroup?.Conflicts.FirstOrDefault();
    }

    private int GetSelectedSecurityConflictCount()
        => SelectedSecurity is null
            ? 0
            : OpenConflicts.Count(conflict => conflict.SecurityId == SelectedSecurity.SecurityId);

    private IReadOnlyList<string> GetMissingTradingParameterFields()
    {
        if (SelectedSecurity is null)
        {
            return [];
        }

        var missingFields = new List<string>();
        if (_selectedTradingParameters?.LotSize is null or <= 0)
        {
            missingFields.Add("lot size");
        }

        if (_selectedTradingParameters?.TickSize is null or <= 0)
        {
            missingFields.Add("tick size");
        }

        if (string.IsNullOrWhiteSpace(_selectedTradingParameters?.TradingHoursUtc))
        {
            missingFields.Add("trading hours");
        }

        if (InstrumentTypeDescriptorCatalog.RequiresContractMultiplier(SelectedAssetClass) &&
            _selectedTradingParameters?.ContractMultiplier is null or <= 0)
        {
            missingFields.Add("contract multiplier");
        }

        return missingFields;
    }

    private async Task ResolveSelectedConflictAsync(string resolution, CancellationToken ct = default)
    {
        if (SelectedConflict is null || string.IsNullOrWhiteSpace(ConflictOperatorText))
        {
            return;
        }

        try
        {
            var selectedSecurityId = SelectedSecurity?.SecurityId;
            var updated = await _workflowClient
                .ResolveConflictAsync(
                    SelectedConflict.ConflictId,
                    resolution,
                    ConflictOperatorText.Trim(),
                    string.IsNullOrWhiteSpace(ConflictNoteText) ? null : ConflictNoteText.Trim(),
                    ct)
                .ConfigureAwait(false);

            if (updated is null)
            {
                _notificationService.ShowNotification("Security Master", "Conflict no longer exists.", NotificationType.Warning);
                return;
            }

            ConflictNoteText = string.Empty;
            await RefreshOperatorWorkflowAsync(ct).ConfigureAwait(false);
            if (selectedSecurityId.HasValue && updated.SecurityId == selectedSecurityId.Value)
            {
                await LoadSelectedTrustSnapshotAsync(selectedSecurityId.Value, ct).ConfigureAwait(false);
            }

            _notificationService.ShowNotification(
                "Security Master",
                resolution.Equals("Dismiss", StringComparison.OrdinalIgnoreCase)
                    ? "Conflict dismissed."
                    : "Conflict marked resolved.",
                NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to resolve Security Master conflict", ex);
            _notificationService.ShowNotification("Security Master", "Conflict resolution failed.", NotificationType.Error);
        }
    }
}
