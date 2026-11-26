namespace Meridian.Wpf.ViewModels;

public sealed partial class SecurityMasterViewModel
{
    private void ClearSelectedSecurityAssuranceState()
    {
        _passportEditor?.CashFlowScenarios?.DraftCheck.SetRetainedTarget(null, null);
        // The contextual editor targets the previously-selected security, so close it when the
        // selection changes; reopening for the new security re-hydrates and resets its inputs.
        IsPassportEditorOpen = false;
        SelectedTrustSnapshot = null;
        SelectedInstrumentPassport = null;
        TrustSnapshotErrorText = string.Empty;
        _selectedEconomicDefinition = null;
        _selectedTradingParameters = null;
        _latestHistoryEvent = null;
        _conflictAssessmentById.Clear();
        ProvenanceCandidates.Clear();
        FilteredConflicts.Clear();
        RecommendedActions.Clear();
        DownstreamImpactLinks.Clear();
        CompanyProfileFields.Clear();
        CompanyCoverageFields.Clear();
        InstrumentPassportFields.Clear();
        ValidationIssues.Clear();
        ChangeHistoryItems.Clear();
        ScheduleBookFields.Clear();
        ScheduleBookEvents.Clear();
        ScheduleBookFactorHistory.Clear();
        ScheduleBookProvenanceHistory.Clear();
        OpenLotReadModelFields.Clear();
        OpenLotRows.Clear();
        OpenLotProvenanceHistory.Clear();
        PrintSections.Clear();
        PrintChecklistItems.Clear();
        PrintEvidenceItems.Clear();
        TrustScoreText = "Trust score unavailable.";
        TrustSummaryText = "Select a security to review trust posture.";
        GoldenCopySourceText = "No golden copy selected.";
        GoldenCopyRuleText = "Preserve winner unless the current winner is blank or equivalent.";
        TradingParametersStatusText = "Trading parameter posture appears after selection.";
        ProvenanceSummaryText = "Winning-source provenance appears after selection.";
        DownstreamImpactSummaryText = "Downstream impact appears after selection.";
        ConflictFilterSummaryText = "Conflict filter summary unavailable.";
        SelectedConflictRecommendedActionText = "Select a conflict to see the recommended action.";
        SelectedConflictWinnerText = "Select a conflict to compare the current winner.";
        _selectedConflictImpactText = "Select a conflict to review downstream impact.";
        CorporateActionReadinessText = "Corporate-action readiness appears after selection.";
        CorporateActionImpactSummaryText = "Corporate-action impact appears after selection.";
        BulkPreviewSummaryText = "Preview low-risk bulk resolutions to inspect scope.";
        CanApplyRecommendedConflictResolution = false;
        CanApplyBulkRecommendedResolutions = false;

        RaisePropertyChanged(nameof(SelectedTrustPostureText));
        RaisePropertyChanged(nameof(SelectedProvenanceText));
        RaisePropertyChanged(nameof(SelectedTradingParameterCoverageText));
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
        RaisePropertyChanged(nameof(ConflictFilterSummaryText));
        RaisePropertyChanged(nameof(SelectedConflictRecommendedActionText));
        RaisePropertyChanged(nameof(SelectedConflictWinnerText));
        RaisePropertyChanged(nameof(SelectedConflictImpactText));
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
        RaisePropertyChanged(nameof(BulkPreviewSummaryText));
        RaisePropertyChanged(nameof(ActionInspectorLeadText));
        RaisePropertyChanged(nameof(ActionInspectorDetailText));
        RaisePropertyChanged(nameof(ShowBackfillTradingParamsAction));
        RaisePropertyChanged(nameof(ShowAnyDownstreamAction));
        RaisePropertyChanged(nameof(HasPortfolioImpactLink));
        RaisePropertyChanged(nameof(HasLedgerImpactLink));
        RaisePropertyChanged(nameof(HasReconciliationImpactLink));
        RaisePropertyChanged(nameof(HasReportPackImpactLink));
    }
}
