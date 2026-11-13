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
    private void RaiseSelectionDerivedStateChanged()
    {
        RaisePropertyChanged(nameof(HasSelectedSecurity));
        RaisePropertyChanged(nameof(SelectedAssetClass));
        RaisePropertyChanged(nameof(SelectedCurrency));
        RaisePropertyChanged(nameof(SelectedStatusBadge));
        RaisePropertyChanged(nameof(SelectedIdentifier));
        RaisePropertyChanged(nameof(SelectionSummaryText));
        RaisePropertyChanged(nameof(SelectionLifecycleText));
        RaisePropertyChanged(nameof(SelectedSecurityConflictSummaryText));
        RaisePropertyChanged(nameof(SelectedTrustPostureText));
        RaisePropertyChanged(nameof(SelectedProvenanceText));
        RaisePropertyChanged(nameof(SelectedTradingParameterCoverageText));
        RaisePropertyChanged(nameof(CorporateActionSummaryText));
        RaisePropertyChanged(nameof(LatestHistoryEventText));
        RaisePropertyChanged(nameof(TrustScoreText));
        RaisePropertyChanged(nameof(TrustSummaryText));
        RaisePropertyChanged(nameof(InstrumentPassportSummaryText));
        RaisePropertyChanged(nameof(InstrumentPassportProviderConfidenceText));
        RaisePropertyChanged(nameof(GoldenCopySourceText));
        RaisePropertyChanged(nameof(GoldenCopyRuleText));
        RaisePropertyChanged(nameof(TradingParametersStatusText));
        RaisePropertyChanged(nameof(ProvenanceSummaryText));
        RaisePropertyChanged(nameof(DownstreamImpactSummaryText));
        RaisePropertyChanged(nameof(CorporateActionReadinessText));
        RaisePropertyChanged(nameof(CorporateActionImpactSummaryText));
        RaiseScheduleAndOpenLotStateChanged();
        RaisePropertyChanged(nameof(CompanyCardTitle));
        RaisePropertyChanged(nameof(CompanyCardSubtitle));
        RaisePropertyChanged(nameof(CompanyCardDescription));
        RaisePropertyChanged(nameof(PrintPacketIdText));
        RaisePropertyChanged(nameof(PrintGeneratedAtText));
        RaisePropertyChanged(nameof(PrintDistributionText));
        RaisePropertyChanged(nameof(PrintSummaryText));
        RaisePropertyChanged(nameof(SearchDeckLeadText));
        RaisePropertyChanged(nameof(SearchDeckDetailText));
        RaisePropertyChanged(nameof(ActionInspectorLeadText));
        RaisePropertyChanged(nameof(ActionInspectorDetailText));
        RaisePropertyChanged(nameof(ActionInspectorNoActionText));
        RaisePropertyChanged(nameof(ShowBackfillTradingParamsAction));
        RaisePropertyChanged(nameof(ShowAnyDownstreamAction));
        RaisePropertyChanged(nameof(HasPortfolioImpactLink));
        RaisePropertyChanged(nameof(HasLedgerImpactLink));
        RaisePropertyChanged(nameof(HasReconciliationImpactLink));
        RaisePropertyChanged(nameof(HasReportPackImpactLink));
        AlignConflictLaneToSelectedSecurity();
        NotifySelectionCommandsChanged();
    }

    private void RaiseScheduleAndOpenLotStateChanged()
    {
        RaisePropertyChanged(nameof(HasTrustSnapshotError));
        RaisePropertyChanged(nameof(ValidationIssuesStatusText));
        RaisePropertyChanged(nameof(HasChangeHistoryItems));
        RaisePropertyChanged(nameof(ChangeHistoryStatusText));
        RaisePropertyChanged(nameof(HasScheduleBookEvents));
        RaisePropertyChanged(nameof(HasOpenLotRows));
        RaisePropertyChanged(nameof(ScheduleBookStatusText));
        RaisePropertyChanged(nameof(OpenLotReadModelStatusText));
    }

    // The contextual passport editor is reachable once the selected security's trust snapshot has
    // loaded (it carries the securityId and the optimistic-concurrency version the editor needs).
    private bool CanOpenPassportEditor => SelectedTrustSnapshot is not null && !IsTrustSnapshotLoading;

    private void OnOpenPassportEditor()
    {
        // Defensive: although these are non-nullable contract members, a partially-deserialized snapshot
        // could surface nulls, and the editor must not open against an absent economic definition.
        var snapshot = SelectedTrustSnapshot;
        if (snapshot?.EconomicDefinition is not { } economic)
        {
            return;
        }

        // Reuse the existing editor only to keep unsaved work for the *same* security alive (a saved draft
        // or typed-but-unsaved input), and only when no write is in flight. Otherwise build a fresh editor:
        // a different security, a refreshed version on an empty editor, or an in-flight write started
        // against a prior security must never bleed into this passport.
        var preserveInProgressWork = PassportEditor.SecurityId == economic.SecurityId
            && PassportEditor.HasUnsavedWork
            && !PassportEditor.IsBusy;
        if (!preserveInProgressWork)
        {
            PassportEditor = new SecurityPassportEditorViewModel(_workstationSecurityMasterApiClient, _cashFlowClient)
            {
                Parameter = new SecurityPassportEditorParameter(
                    SecurityId: economic.SecurityId,
                    Version: economic.Version,
                    Symbol: snapshot.Security?.DisplayName ?? string.Empty,
                    AssetClass: economic.AssetClass,
                    TrustPosture: snapshot.TrustPosture?.Tone.ToString() ?? string.Empty,
                    FundProfileId: snapshot.DownstreamImpact?.IsScoped == true ? snapshot.DownstreamImpact.FundProfileId : null)
            };
        }

        IsPassportEditorOpen = true;
    }

    private void NotifySelectionCommandsChanged()
    {
        EditSelectedCommand.NotifyCanExecuteChanged();
        DeactivateSelectedCommand.NotifyCanExecuteChanged();
        LoadCorporateActionsCommand.NotifyCanExecuteChanged();
        ShowRecordCorpActionCommand.NotifyCanExecuteChanged();
        RefreshSelectedTrustSnapshotCommand.NotifyCanExecuteChanged();
        CopySelectedIdentifierCommand.NotifyCanExecuteChanged();
        OpenSelectedSecurityPortfolioImpactCommand.NotifyCanExecuteChanged();
        OpenSelectedSecurityLedgerImpactCommand.NotifyCanExecuteChanged();
        OpenSelectedSecurityReconciliationImpactCommand.NotifyCanExecuteChanged();
        OpenSelectedSecurityReportPackImpactCommand.NotifyCanExecuteChanged();
    }

    private bool TryBeginSelectedSecurityLoad(
        Guid securityId,
        CancellationToken ct,
        out int loadRevision,
        out CancellationTokenSource loadCts)
    {
        loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationTokenSource? previousLoadCts;
        lock (_selectedSecurityLoadGate)
        {
            if (_disposed)
            {
                loadRevision = 0;
                loadCts.Dispose();
                return false;
            }

            if (_selectedSecurityLoadCts is not null && _selectedSecurityLoadTargetId == securityId)
            {
                loadRevision = _selectedSecurityLoadRevision;
                loadCts.Dispose();
                return false;
            }

            loadRevision = ++_selectedSecurityLoadRevision;
            previousLoadCts = _selectedSecurityLoadCts;
            _selectedSecurityLoadCts = loadCts;
            _selectedSecurityLoadTargetId = securityId;
        }

        CancelAndDisposeSelectedSecurityLoad(previousLoadCts);
        return true;
    }

    private bool CancelSelectedSecurityLoad()
    {
        CancellationTokenSource? loadCts;
        lock (_selectedSecurityLoadGate)
        {
            _selectedSecurityLoadRevision++;
            loadCts = _selectedSecurityLoadCts;
            _selectedSecurityLoadCts = null;
            _selectedSecurityLoadTargetId = null;
        }

        CancelAndDisposeSelectedSecurityLoad(loadCts);
        return loadCts is not null;
    }

    private bool CanCommitSelectedSecurityLoad(
        Guid securityId,
        int loadRevision,
        CancellationToken loadToken)
    {
        lock (_selectedSecurityLoadGate)
        {
            return !_disposed &&
                   !loadToken.IsCancellationRequested &&
                   loadRevision == _selectedSecurityLoadRevision &&
                   _selectedSecurityLoadTargetId == securityId;
        }
    }

    private bool CanCompleteSelectedSecurityLoad(
        Guid securityId,
        int loadRevision,
        CancellationTokenSource loadCts)
    {
        lock (_selectedSecurityLoadGate)
        {
            return !_disposed &&
                   loadRevision == _selectedSecurityLoadRevision &&
                   ReferenceEquals(_selectedSecurityLoadCts, loadCts) &&
                   _selectedSecurityLoadTargetId == securityId;
        }
    }

    private void CompleteSelectedSecurityLoad(
        int loadRevision,
        CancellationTokenSource loadCts)
    {
        lock (_selectedSecurityLoadGate)
        {
            if (loadRevision == _selectedSecurityLoadRevision &&
                ReferenceEquals(_selectedSecurityLoadCts, loadCts))
            {
                _selectedSecurityLoadCts = null;
                _selectedSecurityLoadTargetId = null;
            }
        }

        loadCts.Dispose();
    }

    private static void CancelAndDisposeSelectedSecurityLoad(CancellationTokenSource? loadCts)
    {
        if (loadCts is null)
        {
            return;
        }

        try
        {
            loadCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The superseded load already completed and disposed its source.
        }

        loadCts.Dispose();
    }

    // ── Search ──────────────────────────────────────────────────────────────
    public async Task SearchAsync(CancellationToken ct = default)
    {
        var query = SearchQuery.Trim();
        if (string.IsNullOrEmpty(query))
        {
            _searchSection.HasSearchAttempted = false;
            RaiseSearchDerivedStateChanged();
            StatusText = "Enter a query and press Search.";
            return;
        }

        _searchSection.HasSearchAttempted = true;
        RaiseSearchDerivedStateChanged();

        if (!_securityMasterRuntimeStatus.IsAvailable)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            Results.Clear();
            SelectedSecurity = null;
            HistoryText = string.Empty;
            ClearSelectedSecurityAssuranceState();
            StatusText = _securityMasterRuntimeStatus.AvailabilityDescription;
            return;
        }

        _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linked = _cts.Token;

        Results.Clear();
        SelectedSecurity = null;
        HistoryText = string.Empty;
        ClearSelectedSecurityAssuranceState();
        IsLoading = true;
        StatusText = "Searching…";

        try
        {
            var endpoint = $"/api/workstation/security-master/securities" +
                           $"?query={Uri.EscapeDataString(query)}&take=50&activeOnly={ActiveOnly}";

            var results = (await ApiClientService.Instance
                .GetWithResponseAsync<SecurityMasterWorkstationDto[]>(endpoint, linked)
                .ConfigureAwait(false)).DataOrLoggedNull("Search security master securities");

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Results.Clear();
                if (results is { Length: > 0 })
                {
                    foreach (var r in results)
                        Results.Add(r);
                    StatusText = $"{results.Length} result{(results.Length == 1 ? "" : "s")} found.";
                }
                else
                {
                    StatusText = "No securities matched the query.";
                }
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Search cancelled.";
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Security Master search failed", ex);
            StatusText = "Search failed. Check connection to backend.";
            _notificationService.ShowNotification("Security Master", "Search failed.", NotificationType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadInstrumentPassportAsync(
        Guid securityId,
        string? fundProfileId,
        string? displayName,
        int loadRevision,
        CancellationToken loadToken)
    {
        try
        {
            var passport = await _workstationSecurityMasterApiClient
                .GetInstrumentPassportAsync(securityId, fundProfileId, loadToken)
                .ConfigureAwait(false);

            if (!CanCommitSelectedSecurityLoad(securityId, loadRevision, loadToken))
            {
                return;
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!CanCommitSelectedSecurityLoad(securityId, loadRevision, loadToken))
                {
                    return;
                }

                if (passport is not null && passport.SecurityId != securityId)
                {
                    InstrumentPassportErrorText = "Instrument passport identity did not match the current selection.";
                    return;
                }

                ApplyInstrumentPassport(passport);
                StatusText = passport is null
                    ? string.Concat("Loaded trust snapshot for ", SecurityMasterTextHelpers.FirstNonEmpty(displayName, securityId.ToString("D")), ".")
                    : string.Concat("Loaded trust snapshot and instrument passport for ", SecurityMasterTextHelpers.FirstNonEmpty(displayName, passport.Identity.DisplayName), ".");
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!CanCommitSelectedSecurityLoad(securityId, loadRevision, loadToken))
            {
                return;
            }

            _loggingService.LogError($"Security Master instrument passport load failed for {securityId}", ex);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!CanCommitSelectedSecurityLoad(securityId, loadRevision, loadToken))
                {
                    return;
                }

                ApplyInstrumentPassport(null);
                InstrumentPassportErrorText = "Instrument passport failed to load.";
                StatusText = string.Concat(
                    "Loaded trust snapshot for ",
                    SecurityMasterTextHelpers.FirstNonEmpty(displayName, securityId.ToString("D")),
                    ", but instrument passport evidence failed to load.");
                _notificationService.ShowNotification("Security Master", "Instrument passport load failed.", NotificationType.Warning);
            });
        }
    }

    private void ApplyTrustSnapshot(SecurityMasterTrustSnapshotDto snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var preferredConflictId = SelectedConflict?.ConflictId;
        SyncLegacySelectionStateFromSnapshot(snapshot);

        SelectedTrustSnapshot = snapshot;
        _conflictAssessmentById.Clear();
        foreach (var assessment in snapshot.ConflictAssessments)
        {
            _conflictAssessmentById[assessment.Conflict.ConflictId] = assessment;
        }

        ProvenanceCandidates.Clear();
        foreach (var candidate in snapshot.ProvenanceCandidates
                     .OrderByDescending(candidate => candidate.IsWinningSource)
                     .ThenByDescending(candidate => candidate.AsOf))
        {
            ProvenanceCandidates.Add(candidate);
        }

        DownstreamImpactLinks.Clear();
        foreach (var link in snapshot.DownstreamImpact.Links.OrderBy(SecurityMasterPresentation.GetImpactLinkOrder))
        {
            DownstreamImpactLinks.Add(link);
        }

        TrustScoreText = $"{snapshot.TrustPosture.TrustScore}/100 - {snapshot.TrustPosture.Tone}";
        TrustSummaryText = snapshot.TrustPosture.Summary;
        GoldenCopySourceText = SecurityMasterPresentation.FormatGoldenCopySourceText(snapshot.EconomicDefinition);
        GoldenCopyRuleText = snapshot.TrustPosture.GoldenCopyRule;
        TradingParametersStatusText = snapshot.TrustPosture.TradingParametersStatus;
        ProvenanceSummaryText = SecurityMasterPresentation.BuildProvenanceSummaryText(snapshot);
        DownstreamImpactSummaryText = snapshot.DownstreamImpact.Summary;
        CorporateActionReadinessText = snapshot.TrustPosture.CorporateActionReadiness;
        CorporateActionImpactSummaryText = SecurityMasterPresentation.BuildCorporateActionImpactSummary(snapshot);
        PopulateCompanyPresentation(snapshot);
        PopulateValidationPresentation(snapshot);
        PopulateChangeHistoryPresentation(snapshot);
        PopulateScheduleBookPresentation(snapshot);
        PopulateOpenLotReadModelPresentation(snapshot);
        PopulatePrintPresentation(snapshot);

        RebuildFilteredConflicts(preferredConflictId);
        RebuildRecommendedActions();
        UpdateSelectedConflictAssessmentState();
        RaiseSelectionDerivedStateChanged();
        RaiseConflictDerivedStateChanged();
    }

    private void ApplyInstrumentPassport(InstrumentPassportDto? passport)
    {
        SelectedInstrumentPassport = passport;
        if (passport is null)
        {
            InstrumentPassportFields.Clear();
            return;
        }

        var activeProviderCount = passport.ProviderConfidence.Count(item => item.IsActive);
        var primaryProvider = passport.ProviderConfidence.FirstOrDefault(item => item.IsPrimary)
            ?? passport.ProviderConfidence.FirstOrDefault();
        var fields = new List<SecurityMasterPresentationField>
        {
            new SecurityMasterPresentationField("Display name", passport.Identity.DisplayName),
            new SecurityMasterPresentationField("Security ID", passport.SecurityId.ToString("D")),
            new SecurityMasterPresentationField("Asset class", SecurityMasterTextHelpers.FirstNonEmpty(passport.Identity.AssetClass, passport.EconomicDefinition.AssetClass, "Unavailable")),
            new SecurityMasterPresentationField("Trust", $"{passport.TrustPosture.Tone} - {passport.TrustPosture.Summary}"),
            new SecurityMasterPresentationField("Identifiers", passport.IdentifierSummary.Summary),
            new SecurityMasterPresentationField("Provider confidence", $"{activeProviderCount} active / {passport.ProviderConfidence.Count} total"),
            new SecurityMasterPresentationField("Primary provider", primaryProvider is null ? "Unavailable" : $"{primaryProvider.Provider} {primaryProvider.MappingKind} {primaryProvider.Symbol}"),
            new SecurityMasterPresentationField("Pricing", $"{passport.Pricing.Status}: {passport.Pricing.Summary}"),
            new SecurityMasterPresentationField("Usage", passport.Usage.Summary),
            new SecurityMasterPresentationField("Retrieved", FormatHelpers.FormatUtcTimestamp(passport.RetrievedAtUtc))
        };

        if (passport.OperatingModel is { } operatingModel)
        {
            fields.Add(new SecurityMasterPresentationField("Operating model", $"{operatingModel.Status}: {operatingModel.Summary}"));
            foreach (var stage in operatingModel.Stages)
            {
                fields.Add(new SecurityMasterPresentationField(
                    stage.Title,
                    $"{stage.Status}: {stage.Summary} Evidence {stage.EvidenceCount}; blockers {stage.BlockingIssueCount}."));
            }

            var applicableEntitlements = operatingModel.EntitlementApplicability
                .Count(static item => item.IsApplicable && item.IsMostSpecific);
            fields.Add(new SecurityMasterPresentationField(
                "Entitlement applicability",
                $"{applicableEntitlements} most-specific applicable entitlement(s)."));
            fields.Add(new SecurityMasterPresentationField(
                "Manual-change approval",
                $"{operatingModel.ManualChangeApproval.Status}: {operatingModel.ManualChangeApproval.PolicyKey} via {operatingModel.ManualChangeApproval.Gate}; {operatingModel.ManualChangeApproval.Summary}"));
        }

        if (passport.ReferenceDataWorkbench is { } workbench)
        {
            fields.Add(new SecurityMasterPresentationField("Reference-data workbench", $"{workbench.Status}: {workbench.Summary}"));
            foreach (var section in workbench.Sections)
            {
                fields.Add(new SecurityMasterPresentationField(
                    section.Title,
                    $"{section.Status}: {section.Summary} Evidence {section.EvidenceCount}; blockers {section.BlockingIssueCount}."));
            }

            var enabledHandoffs = workbench.OperationsHandoffs.Count(static handoff => handoff.IsEnabled);
            fields.Add(new SecurityMasterPresentationField(
                "Operations handoff",
                $"{enabledHandoffs} enabled / {workbench.OperationsHandoffs.Count} total handoff(s)."));
        }

        if (passport.OperationsWorkbench is { } operationsWorkbench)
        {
            fields.Add(new SecurityMasterPresentationField("Operations workbench", $"{operationsWorkbench.Status}: {operationsWorkbench.Summary}"));
            foreach (var readiness in operationsWorkbench.Readiness)
            {
                fields.Add(new SecurityMasterPresentationField(
                    readiness.Label,
                    $"{readiness.Status}: {readiness.Summary} Evidence {readiness.EvidenceCount}; blockers {readiness.BlockingIssueCount}; next action {readiness.NextAction}"));
            }

            foreach (var panel in operationsWorkbench.Panels)
            {
                fields.Add(new SecurityMasterPresentationField(
                    panel.Title,
                    $"{panel.Status}: {panel.Summary} {panel.Items.Count} item(s)."));
            }

            foreach (var handoff in operationsWorkbench.Handoffs)
            {
                var impactedOutputs = handoff.ImpactedOutputs.Count == 0
                    ? "Security Master"
                    : string.Join(", ", handoff.ImpactedOutputs);
                var linkedCases = handoff.LinkedCases.Count == 0
                    ? "none"
                    : string.Join(", ", handoff.LinkedCases);
                fields.Add(new SecurityMasterPresentationField(
                    $"Handoff: {handoff.Title}",
                    $"{handoff.Status}: owner {SecurityMasterTextHelpers.FirstNonEmpty(handoff.Owner, "Security Master steward")}; blocker {SecurityMasterTextHelpers.FirstNonEmpty(handoff.BlockerReason, handoff.Detail)}; outputs {impactedOutputs}; linked cases {linkedCases}; route {SecurityMasterTextHelpers.FirstNonEmpty(handoff.Route, handoff.Target)}"));
            }
        }

        ReplaceCollection(InstrumentPassportFields, fields);
    }

    private void NavigateToFundOperations(string pageTag, FundOperationsTab tab)
    {
        _navigationService.NavigateTo(
            pageTag,
            new FundOperationsNavigationContext(
                Tab: tab,
                FundProfileId: GetCurrentFundProfileId()));
    }

    private bool HasActiveImpactLink(string target)
        => DownstreamImpactLinks.Any(link =>
            link.IsActive &&
            string.Equals(link.Target, target, StringComparison.OrdinalIgnoreCase));

    private string? GetCurrentFundProfileId()
        => string.IsNullOrWhiteSpace(_fundContextService.CurrentFundProfile?.FundProfileId)
            ? _fundContextService.LastSelectedFundProfileId
            : _fundContextService.CurrentFundProfile?.FundProfileId;

    private void CopySelectedIdentifier()
    {
        if (string.IsNullOrWhiteSpace(SelectedIdentifier))
        {
            return;
        }

        try
        {
            Clipboard.SetText(SelectedIdentifier);
            _notificationService.ShowNotification("Security Master", "Selected identifier copied to clipboard.", NotificationType.Success);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to copy selected identifier", ex);
            _notificationService.ShowNotification("Security Master", "Failed to copy the selected identifier.", NotificationType.Error);
        }
    }

    private void PopulateCompanyPresentation(SecurityMasterTrustSnapshotDto snapshot)
    {
        var identity = snapshot.Identity;
        var economic = snapshot.EconomicDefinition;

        ReplaceCollection(CompanyProfileFields,
        [
            new SecurityMasterPresentationField("Legal name", SecurityMasterTextHelpers.FirstNonEmpty(identity.IssuerName, snapshot.Security.DisplayName, "Unavailable")),
            new SecurityMasterPresentationField("Primary listing", SecurityMasterTextHelpers.FirstNonEmpty(identity.PrimaryListingMic, "Not supplied")),
            new SecurityMasterPresentationField("Country of risk", SecurityMasterTextHelpers.FirstNonEmpty(identity.CountryOfRisk, economic.RiskCountry, snapshot.Security.Classification.RiskCountry, "Not supplied")),
            new SecurityMasterPresentationField("Settlement cycle", identity.SettlementCycleDays is int days ? $"T+{days}" : "Not supplied"),
            new SecurityMasterPresentationField("Asset family", SecurityMasterTextHelpers.FirstNonEmpty(economic.AssetFamily, economic.AssetClass, "Not supplied")),
            new SecurityMasterPresentationField("Issuer type", SecurityMasterTextHelpers.FirstNonEmpty(economic.IssuerType, snapshot.Security.Classification.IssuerType, "Not supplied"))
        ]);

        ReplaceCollection(CompanyCoverageFields,
        [
            new SecurityMasterPresentationField("Trust posture", $"{snapshot.TrustPosture.Tone} • {snapshot.TrustPosture.TrustScore}/100"),
            new SecurityMasterPresentationField("Validation", SecurityMasterPresentation.BuildValidationSummaryText(snapshot)),
            new SecurityMasterPresentationField("Identifier coverage", SecurityMasterPresentation.BuildIdentifierCoverageSummaryText(snapshot)),
            new SecurityMasterPresentationField("Schedule model", SecurityMasterPresentation.BuildScheduleSummaryText(snapshot)),
            new SecurityMasterPresentationField("Lot model", SecurityMasterPresentation.BuildLotModelSummaryText(snapshot)),
            new SecurityMasterPresentationField("Trading readiness", snapshot.TrustPosture.TradingParametersStatus),
            new SecurityMasterPresentationField("Schema compatibility", SecurityMasterPresentation.BuildSchemaCompatibilitySummaryText(snapshot)),
            new SecurityMasterPresentationField("Corporate actions", snapshot.TrustPosture.CorporateActionReadiness),
            new SecurityMasterPresentationField("Downstream scope", snapshot.DownstreamImpact.IsScoped
                ? $"Scoped to {snapshot.DownstreamImpact.FundProfileId}"
                : "Operator-wide review"),
            new SecurityMasterPresentationField("Impact severity", snapshot.DownstreamImpact.Severity.ToString()),
            new SecurityMasterPresentationField("Latest audit", LatestHistoryEventText)
        ]);
    }

    private void PopulateValidationPresentation(SecurityMasterTrustSnapshotDto snapshot)
    {
        ReplaceCollection(
            ValidationIssues,
            snapshot.ValidationReport?.Issues is { } issues
                ? issues
                    .OrderByDescending(static issue => issue.Severity)
                    .ThenBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase)
                : []);
        RaiseScheduleAndOpenLotStateChanged();
    }

    private void PopulateChangeHistoryPresentation(SecurityMasterTrustSnapshotDto snapshot)
    {
        ReplaceCollection(
            ChangeHistoryItems,
            snapshot.ChangeHistory is { } changeHistory
                ? changeHistory
                    .OrderByDescending(static item => item.ChangedAtUtc)
                    .ThenByDescending(static item => item.StreamVersion)
                : []);
        RaiseScheduleAndOpenLotStateChanged();
    }

    private void PopulateScheduleBookPresentation(SecurityMasterTrustSnapshotDto snapshot)
    {
        var scheduleBook = snapshot.ScheduleBook;
        if (scheduleBook is null)
        {
            ScheduleBookFields.Clear();
            ScheduleBookEvents.Clear();
            ScheduleBookFactorHistory.Clear();
            ScheduleBookProvenanceHistory.Clear();
            RaiseScheduleAndOpenLotStateChanged();
            return;
        }

        ReplaceCollection(ScheduleBookFields,
        [
            new SecurityMasterPresentationField("Summary", scheduleBook.Summary),
            new SecurityMasterPresentationField("Currency", scheduleBook.Currency),
            new SecurityMasterPresentationField("Current factor", SecurityMasterPresentation.FormatNullableDecimal(scheduleBook.CurrentFactor)),
            new SecurityMasterPresentationField("Current factor date", SecurityMasterPresentation.FormatNullableDate(scheduleBook.CurrentFactorDate)),
            new SecurityMasterPresentationField("Next lifecycle date", SecurityMasterPresentation.FormatNullableDate(scheduleBook.NextLifecycleDate)),
            new SecurityMasterPresentationField("Cash-flow schedule", scheduleBook.SupportsCashflowSchedule ? "Supported" : "Unavailable"),
            new SecurityMasterPresentationField("Factor history", scheduleBook.SupportsFactorHistory ? "Supported" : "Unavailable"),
            new SecurityMasterPresentationField("Economic terms", scheduleBook.HasEconomicScheduleTerms ? "Present" : "Not present"),
            new SecurityMasterPresentationField("Source", SecurityMasterTextHelpers.FirstNonEmpty(scheduleBook.SourceSummary, "Source summary unavailable"))
        ]);
        AppendSchedulePolicyEvidence(snapshot);

        ReplaceCollection(ScheduleBookEvents, scheduleBook.Events.OrderBy(item => item.EffectiveDate));
        ReplaceCollection(ScheduleBookFactorHistory, scheduleBook.FactorHistory.OrderByDescending(item => item.EffectiveDate));
        ReplaceCollection(ScheduleBookProvenanceHistory, scheduleBook.ProvenanceHistory.OrderByDescending(item => item.SourceAsOfUtc ?? DateTimeOffset.MinValue));
        RaiseScheduleAndOpenLotStateChanged();
    }

    private void PopulateOpenLotReadModelPresentation(SecurityMasterTrustSnapshotDto snapshot)
    {
        var readModel = snapshot.OpenLotReadModel;
        if (readModel is null)
        {
            OpenLotReadModelFields.Clear();
            OpenLotRows.Clear();
            OpenLotProvenanceHistory.Clear();
            RaiseScheduleAndOpenLotStateChanged();
            return;
        }

        ReplaceCollection(OpenLotReadModelFields,
        [
            new SecurityMasterPresentationField("Summary", readModel.Summary),
            new SecurityMasterPresentationField("Quantity model", readModel.QuantityModel),
            new SecurityMasterPresentationField("Lot size", SecurityMasterPresentation.FormatNullableDecimal(readModel.LotSize)),
            new SecurityMasterPresentationField("Contract multiplier", SecurityMasterPresentation.FormatNullableDecimal(readModel.ContractMultiplier)),
            new SecurityMasterPresentationField("Current factor", SecurityMasterPresentation.FormatNullableDecimal(readModel.CurrentFactor)),
            new SecurityMasterPresentationField("Current factor date", SecurityMasterPresentation.FormatNullableDate(readModel.CurrentFactorDate)),
            new SecurityMasterPresentationField("As of", FormatHelpers.FormatUtcTimestamp(readModel.AsOfUtc)),
            new SecurityMasterPresentationField("Face value", readModel.UsesFaceValue ? "Uses face value" : "Quantity only"),
            new SecurityMasterPresentationField("Factor-adjusted exposure", readModel.SupportsFactorAdjustedExposure ? "Supported" : "Unavailable"),
            new SecurityMasterPresentationField("Resolved ID required", readModel.RequiresResolvedSecurityId ? "Required" : "Not required")
        ]);

        ReplaceCollection(OpenLotRows, readModel.Lots.OrderByDescending(item => item.TradeDate));
        ReplaceCollection(OpenLotProvenanceHistory, readModel.ProvenanceHistory.OrderByDescending(item => item.AsOfUtc));
        RaiseScheduleAndOpenLotStateChanged();
    }

    private void PopulatePrintPresentation(SecurityMasterTrustSnapshotDto snapshot)
    {
        var projection = SecurityMasterPrintProjectionService.BuildProjection(
            snapshot,
            GoldenCopySourceText,
            LatestHistoryEventText,
            PrintDistributionText);
        ReplaceCollection(PrintSections, projection.Sections);
        ReplaceCollection(PrintChecklistItems, projection.ChecklistItems);
        ReplaceCollection(PrintEvidenceItems, projection.EvidenceItems);
    }
}
