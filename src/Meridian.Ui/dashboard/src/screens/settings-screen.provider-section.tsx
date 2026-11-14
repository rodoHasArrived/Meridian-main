// Provider connection center section of the Settings screen: its private state/handlers hook and
// presentational component.
import { SeverityBadge } from "@/components/operations";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { createProviderIntegrationReconciliationHandoff, getProviderIntegrationConnectionMonitor, getProviderIntegrationConnectionSyncPlan, getProviderIntegrationConnectionSyncRuns, getProviderIntegrationIdentityResolution, getProviderIntegrationPromotionReadiness, getProviderIntegrationQuarantineReview, getProviderIntegrationReconciliationHandoffHistory, getProviderIntegrationStagingReview, importProviderIntegrationOpenApi, replayProviderIntegrationQuarantineRecords, resolveProviderIntegrationQuarantineRecord, runDueProviderIntegrationSync } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { cn } from "@/lib/utils";
import { settingsProviderAdvancedRoute, settingsProviderSetupRoute, WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { type SettingsMutationConfirmation } from "@/screens/settings-mutation-confirm-dialog";
import { type SettingsRouteViewId } from "@/screens/settings-route-state";
import { humanizeSettingsIdentifier } from "@/screens/settings-screen.operations-form-options";
import { type SettingsProviderConnectionRow, type SettingsScreenViewModel } from "@/screens/settings-screen.view-model";
import type { FeatureCapabilitySettingsResponse, ProviderIntegrationQuarantinedRecord, ProviderIntegrationQuarantineResolutionAction, SessionInfo } from "@/types";
import { ArrowRight, MonitorCheck, RefreshCcw, Search } from "lucide-react";
import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { OwnedProviderCredentialPanel } from "./settings-owned-provider-credentials";
import type { ProviderOpenApiImportState, ProviderRuntimeEvidenceState, ProviderRuntimePhase } from "./settings-provider-integration";
import { createProviderOpenApiImportState, FilterSelect, parseProviderOpenApiCapabilities, ProviderIntegrationRuntimePanel, providerRuntimeErrorDetail, providerRuntimeHandoffEvidenceId, providerRuntimeLatestQuarantineDecision, providerRuntimeQuarantineActionLabel, providerRuntimeQuarantineActionNote, providerRuntimeReplaySyncRunId, providerRuntimeStateKey, providerRuntimeValue, SettingsFieldRow } from "./settings-provider-integration";
import { toSeverity } from "@/design-system/status-styles";

const emptyProviderRuntimeEvidenceState: ProviderRuntimeEvidenceState = {
  phase: "idle",
  message: null,
  details: [],
  monitor: null,
  syncRuns: null,
  syncPlan: null,
  staging: null,
  identity: null,
  promotion: null,
  handoff: null,
  quarantine: null
};

function filterProviderRow(
  row: { displayName: string; recommendedAction: string; affectedWorkflowsLabel: string; capabilityGroup: "brokerage" | "data" | "accounting"; healthTone: string; verificationStatus: "verified" | "pending" | "failed" },
  search: string,
  capabilityFilter: "all" | "brokerage" | "data" | "accounting",
  healthFilter: "all" | "healthy" | "warning" | "blocked",
  verificationFilter: "all" | "verified" | "unverified"
): boolean {
  if (search) {
    const content = `${row.displayName} ${row.recommendedAction} ${row.affectedWorkflowsLabel}`.toLowerCase();
    if (!content.includes(search)) {
      return false;
    }
  }

  if (capabilityFilter === "brokerage" && row.capabilityGroup !== "brokerage") {
    return false;
  }
  if (capabilityFilter === "data" && row.capabilityGroup !== "data") {
    return false;
  }
  if (capabilityFilter === "accounting" && row.capabilityGroup !== "accounting") {
    return false;
  }

  if (healthFilter === "healthy" && row.healthTone !== "success") return false;
  if (healthFilter === "warning" && row.healthTone !== "warning") return false;
  if (healthFilter === "blocked" && row.healthTone !== "danger") return false;

  const verified = row.verificationStatus === "verified";
  if (verificationFilter === "verified" && !verified) return false;
  if (verificationFilter === "unverified" && verified) return false;

  return true;
}

function providerRiskScore(row: { healthTone: string; credentialTone: string; verificationStatus: "verified" | "pending" | "failed"; fallbackStatus: "active" | "available" | "missing" }): number {
  const healthScore = row.healthTone === "danger" ? 100 : row.healthTone === "warning" ? 70 : 20;
  const credentialScore = row.credentialTone === "danger" ? 40 : row.credentialTone === "warning" ? 20 : 0;
  const verificationScore = row.verificationStatus === "failed" ? 30 : row.verificationStatus === "pending" ? 20 : 0;
  const fallbackScore = row.fallbackStatus === "active" ? 10 : row.fallbackStatus === "missing" ? 20 : 0;
  return healthScore + credentialScore + verificationScore + fallbackScore;
}

interface UseSettingsProviderSectionStateParams {
  activeProviderId: string | null;
  activeTaskView: SettingsRouteViewId;
  allProviderRows: SettingsProviderConnectionRow[];
  featureCapabilities: FeatureCapabilitySettingsResponse | null;
  legacyEmbeddedSettingsView: boolean;
  providerOpenApiImportState: Record<string, ProviderOpenApiImportState>;
  session: SessionInfo | null;
  setProviderOpenApiImportState: React.Dispatch<React.SetStateAction<Record<string, ProviderOpenApiImportState>>>;
  vm: SettingsScreenViewModel;
}

export function useSettingsProviderSectionState({ activeProviderId, activeTaskView, allProviderRows, featureCapabilities, legacyEmbeddedSettingsView, providerOpenApiImportState, session, setProviderOpenApiImportState, vm }: UseSettingsProviderSectionStateParams) {
  const [providerSearch, setProviderSearch] = useState("");
  const [providerCapabilityFilter, setProviderCapabilityFilter] = useState<"all" | "brokerage" | "data" | "accounting">("all");
  const [providerHealthFilter, setProviderHealthFilter] = useState<"all" | "healthy" | "warning" | "blocked">("all");
  const [providerVerificationFilter, setProviderVerificationFilter] = useState<"all" | "verified" | "unverified">("all");
  const [providerSort, setProviderSort] = useState<"risk" | "name">("risk");
  const [providerRuntimeState, setProviderRuntimeState] = useState<Record<string, ProviderRuntimeEvidenceState>>({});
  const providerInlineFlag = featureCapabilities?.capabilities.find((capability) => (
    capability.capabilityKey === "desktop.settings.provider-connection-center-inline-management"
  ));
  const inlineProviderManagementEnabled = providerInlineFlag?.isEnabled ?? true;
  const activeProviderDisplayName = activeProviderId
    ? allProviderRows.find((row) => row.providerId.toLowerCase() === activeProviderId.toLowerCase())?.displayName
      ?? humanizeSettingsIdentifier(activeProviderId)
    : null;
  const providerRiskScores = useMemo(
    () => Object.fromEntries(allProviderRows.map((row) => [row.providerId, providerRiskScore(row)])),
    [allProviderRows]
  );
  const showProviderFilters = activeTaskView === "providers" || legacyEmbeddedSettingsView;
  const showProviderAdvancedControls = activeTaskView === "provider-advanced" || legacyEmbeddedSettingsView;
  const filteredProviderGroups = useMemo(() => {
    const search = providerSearch.trim().toLowerCase();
    return vm.providerConnectionCenter.groups.map((group) => {
      const rows = group.rows
        .filter((row) => filterProviderRow(row, search, providerCapabilityFilter, providerHealthFilter, providerVerificationFilter))
        .sort((left, right) => {
          if (providerSort === "name") {
            return left.displayName.localeCompare(right.displayName);
          }
          const riskScore = (providerRiskScores[right.providerId] ?? 0) - (providerRiskScores[left.providerId] ?? 0);
          return riskScore !== 0 ? riskScore : left.displayName.localeCompare(right.displayName);
        });
      return {
        ...group,
        rows
      };
    });
  }, [
    providerCapabilityFilter,
    providerHealthFilter,
    providerRiskScores,
    providerSearch,
    providerSort,
    providerVerificationFilter,
    vm.providerConnectionCenter.groups
  ]);
  const visibleProviderGroups = useMemo(() => {
    if (!activeProviderId || activeTaskView === "providers" || legacyEmbeddedSettingsView) {
      return filteredProviderGroups;
    }

    return filteredProviderGroups
      .map((group) => ({
        ...group,
        rows: group.rows.filter((row) => row.providerId.toLowerCase() === activeProviderId.toLowerCase())
      }))
      .filter((group) => group.rows.length > 0);
  }, [activeProviderId, activeTaskView, filteredProviderGroups, legacyEmbeddedSettingsView]);
  const loadProviderRuntimeEvidence = async (row: SettingsProviderConnectionRow) => {
    const stateKey = providerRuntimeStateKey(row);
    const connectionId = row.integrationConnectionId;

    setProviderRuntimeState((current) => ({
      ...current,
      [stateKey]: {
        ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
        phase: "loading",
        message: null,
        details: []
      }
    }));

    const evaluatedAt = new Date().toISOString();
    const [
      monitorResult,
      syncRunsResult,
      syncPlanResult,
      stagingResult,
      identityResult,
      promotionResult,
      handoffResult,
      quarantineResult
    ] = await Promise.allSettled([
      getProviderIntegrationConnectionMonitor(connectionId, 5),
      getProviderIntegrationConnectionSyncRuns(connectionId, 5),
      getProviderIntegrationConnectionSyncPlan(connectionId, evaluatedAt),
      getProviderIntegrationStagingReview(connectionId, 5),
      getProviderIntegrationIdentityResolution(connectionId, 5),
      getProviderIntegrationPromotionReadiness(connectionId, 5),
      getProviderIntegrationReconciliationHandoffHistory(connectionId),
      getProviderIntegrationQuarantineReview(connectionId, 5)
    ]);

    const details = [
      providerRuntimeErrorDetail(monitorResult, "Connection monitor"),
      providerRuntimeErrorDetail(syncRunsResult, "Sync-run history"),
      providerRuntimeErrorDetail(syncPlanResult, "Sync plan"),
      providerRuntimeErrorDetail(stagingResult, "Staging review"),
      providerRuntimeErrorDetail(identityResult, "Identity resolution"),
      providerRuntimeErrorDetail(promotionResult, "Promotion readiness"),
      providerRuntimeErrorDetail(handoffResult, "Reconciliation handoff history"),
      providerRuntimeErrorDetail(quarantineResult, "Quarantine review")
    ].filter((detail): detail is string => Boolean(detail));
    const failedCount = details.length;
    const phase: ProviderRuntimePhase = failedCount === 8 ? "error" : "loaded";
    const warningSuffix = failedCount === 1 ? "warning" : "warnings";

    setProviderRuntimeState((current) => ({
      ...current,
      [stateKey]: {
        phase,
        message: failedCount === 0
          ? "Provider integration runtime evidence loaded."
          : failedCount === 8
            ? "Provider integration runtime evidence unavailable."
            : `Provider integration runtime loaded with ${failedCount} ${warningSuffix}.`,
        details,
        monitor: providerRuntimeValue(monitorResult),
        syncRuns: providerRuntimeValue(syncRunsResult),
        syncPlan: providerRuntimeValue(syncPlanResult),
        staging: providerRuntimeValue(stagingResult),
        identity: providerRuntimeValue(identityResult),
        promotion: providerRuntimeValue(promotionResult),
        handoff: providerRuntimeValue(handoffResult),
        quarantine: providerRuntimeValue(quarantineResult)
      }
    }));
  };
  const updateProviderOpenApiImportState = (
    row: SettingsProviderConnectionRow,
    updater: (state: ProviderOpenApiImportState) => ProviderOpenApiImportState
  ) => {
    const stateKey = providerRuntimeStateKey(row);
    setProviderOpenApiImportState((current) => ({
      ...current,
      [stateKey]: updater(current[stateKey] ?? createProviderOpenApiImportState(row))
    }));
  };
  const submitProviderOpenApiImport = async (row: SettingsProviderConnectionRow) => {
    const stateKey = providerRuntimeStateKey(row);
    const formState = providerOpenApiImportState[stateKey] ?? createProviderOpenApiImportState(row);
    const importedAt = new Date();
    const capabilities = parseProviderOpenApiCapabilities(formState.capabilities);

    if (!formState.manifestId.trim() || !formState.openApiDocumentJson.trim() || capabilities.length === 0) {
      updateProviderOpenApiImportState(row, (state) => ({
        ...state,
        message: "Manifest id, capability list, and OpenAPI JSON are required before importing a draft.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    updateProviderOpenApiImportState(row, (state) => ({
      ...state,
      busy: true,
      message: "Importing OpenAPI draft manifest.",
      details: [],
      tone: "default"
    }));

    try {
      const result = await importProviderIntegrationOpenApi({
        manifestId: formState.manifestId.trim(),
        providerId: row.providerId,
        displayName: formState.displayName.trim() || row.displayName,
        environment: formState.environment.trim() || "paper",
        authType: formState.authType,
        tokenUrl: formState.tokenUrl.trim() || null,
        scopes: formState.scopes.split(",").map((scope) => scope.trim()).filter(Boolean),
        capabilities,
        openApiDocumentJson: formState.openApiDocumentJson,
        importedBy: session?.displayName ?? "settings-operator",
        importedAt: importedAt.toISOString(),
        expectedManifestReference: formState.result?.manifestReference?.manifestId === formState.manifestId.trim()
          ? formState.result.manifestReference
          : undefined,
        changeReason: formState.changeReason.trim() || "Imported from the Settings Provider Connection Center."
      });

      updateProviderOpenApiImportState(row, (state) => ({
        ...state,
        busy: false,
        result,
        message: result.message ?? `OpenAPI draft imported for ${result.manifest.manifestId}.`,
        details: [
          `${result.manifest.endpoints.length} endpoints seeded.`,
          `${result.readiness.requiredEvidence.length} readiness evidence requirements.`,
          ...result.issues.map((issue) => issue.message)
        ],
        tone: result.readiness.isReady && result.issues.length === 0 ? "success" : "warning"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider integration OpenAPI import failed.");
      updateProviderOpenApiImportState(row, (state) => ({
        ...state,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };
  const runProviderRuntimeDueSync = async (row: SettingsProviderConnectionRow) => {
    const stateKey = providerRuntimeStateKey(row);
    const requestedAt = new Date();

    setProviderRuntimeState((current) => ({
      ...current,
      [stateKey]: {
        ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
        phase: "loading",
        message: "Provider integration due-sync is running.",
        details: []
      }
    }));

    try {
      const result = await runDueProviderIntegrationSync(row.integrationConnectionId, {
        connectionId: row.integrationConnectionId,
        requestedAt: requestedAt.toISOString(),
        requestedBy: session?.displayName ?? "settings-operator",
        maxPages: 2,
        pathParametersByCapability: {},
        queryParametersByCapability: {}
      });

      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: `Due-sync completed: ${result.startedCount} started / ${result.skippedCount} skipped.`,
          details: result.items.flatMap((item) => item.issues.map((issue) => issue.message))
        }
      }));
      await loadProviderRuntimeEvidence(row);
    } catch (error) {
      const display = describeApiError(error, "Provider integration due-sync failed.");
      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: display.summary,
          details: display.details
        }
      }));
    }
  };
  const createProviderRuntimeHandoff = async (row: SettingsProviderConnectionRow) => {
    const stateKey = providerRuntimeStateKey(row);
    const currentState = providerRuntimeState[stateKey];
    const readyRows = currentState?.promotion?.rows.filter((promotionRow) => promotionRow.status === "ReadyForReconciliation") ?? [];
    const alreadyHandedOff = new Set((currentState?.handoff?.records ?? []).map((record) => record.stagingRecordId));
    const stagingRecordIds = readyRows
      .map((promotionRow) => promotionRow.stagingRecordId)
      .filter((stagingRecordId) => !alreadyHandedOff.has(stagingRecordId));
    const requestedAt = new Date();

    if (stagingRecordIds.length === 0) {
      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: "No promotion-ready staging rows are available for reconciliation handoff.",
          details: []
        }
      }));
      return;
    }

    setProviderRuntimeState((current) => ({
      ...current,
      [stateKey]: {
        ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
        phase: "loading",
        message: "Creating provider integration reconciliation handoff.",
        details: []
      }
    }));

    try {
      const result = await createProviderIntegrationReconciliationHandoff({
        connectionId: row.integrationConnectionId,
        stagingRecordIds,
        requestedBy: session?.displayName ?? "settings-operator",
        requestedAt: requestedAt.toISOString(),
        approvalEvidenceId: providerRuntimeHandoffEvidenceId(row.integrationConnectionId, requestedAt),
        note: "Approved from the Settings Provider Connection Center promotion readiness panel.",
        recentRunLimit: 5
      });

      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: `Reconciliation handoff ${result.accepted ? "created" : "reviewed"}: ${result.acceptedRecordCount} accepted / ${result.duplicateRecordCount} duplicate.`,
          details: result.issues.map((issue) => issue.message)
        }
      }));
      await loadProviderRuntimeEvidence(row);
    } catch (error) {
      const display = describeApiError(error, "Provider integration reconciliation handoff failed.");
      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: display.summary,
          details: display.details
        }
      }));
    }
  };
  const replayProviderRuntimeQuarantine = async (row: SettingsProviderConnectionRow) => {
    const stateKey = providerRuntimeStateKey(row);
    const currentState = providerRuntimeState[stateKey];
    const replayEligibleRecords = currentState?.quarantine?.records.filter((record) =>
      !providerRuntimeLatestQuarantineDecision(currentState.quarantine?.decisions, record)
    ) ?? [];
    const replaySeed = replayEligibleRecords[0] ?? null;
    const manifestId = currentState?.monitor?.manifestId ?? null;
    if (!currentState || !replaySeed || !manifestId) {
      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: "Quarantine replay is unavailable until runtime evidence includes monitor data and undecided quarantine records.",
          details: [],
        }
      }));
      return;
    }

    const quarantineRecordIds = replayEligibleRecords
      .filter((record) => record.syncRunId === replaySeed.syncRunId && record.capability === replaySeed.capability)
      .map((record) => record.quarantineRecordId);
    const requestedAt = new Date();

    setProviderRuntimeState((current) => ({
      ...current,
      [stateKey]: {
        ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
        phase: "loading",
        message: "Provider integration quarantine replay is running.",
        details: []
      }
    }));

    try {
      const replayResult = await replayProviderIntegrationQuarantineRecords({
        replaySyncRunId: providerRuntimeReplaySyncRunId(row.integrationConnectionId, requestedAt),
        sourceSyncRunId: replaySeed.syncRunId,
        manifestId,
        connectionId: row.integrationConnectionId,
        capability: replaySeed.capability,
        quarantineRecordIds,
        requestedBy: session?.displayName ?? "settings-operator",
        requestedAt: requestedAt.toISOString(),
        mode: "Original"
      });

      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: `Quarantine replay completed: ${replayResult.recordsAccepted} accepted / ${replayResult.recordsRequarantined} requarantined.`,
          details: replayResult.issues.map((issue) => issue.message)
        }
      }));
      await loadProviderRuntimeEvidence(row);
    } catch (error) {
      const display = describeApiError(error, "Provider integration quarantine replay failed.");
      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: display.summary,
          details: display.details
        }
      }));
    }
  };
  const resolveProviderRuntimeQuarantineRecord = async (
    row: SettingsProviderConnectionRow,
    record: ProviderIntegrationQuarantinedRecord,
    action: ProviderIntegrationQuarantineResolutionAction
  ) => {
    const stateKey = providerRuntimeStateKey(row);
    const reviewedAt = new Date();
    const actionLabel = providerRuntimeQuarantineActionLabel(action);

    setProviderRuntimeState((current) => ({
      ...current,
      [stateKey]: {
        ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
        phase: "loading",
        message: `Recording ${actionLabel.toLowerCase()} decision for ${record.quarantineRecordId}.`,
        details: []
      }
    }));

    try {
      const result = await resolveProviderIntegrationQuarantineRecord({
        connectionId: row.integrationConnectionId,
        syncRunId: record.syncRunId,
        quarantineRecordId: record.quarantineRecordId,
        action,
        reviewedBy: session?.displayName ?? "settings-operator",
        reviewedAt: reviewedAt.toISOString(),
        note: providerRuntimeQuarantineActionNote(action)
      });

      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: result.message ?? `${actionLabel} decision recorded for ${record.quarantineRecordId}.`,
          details: []
        }
      }));
      await loadProviderRuntimeEvidence(row);
    } catch (error) {
      const display = describeApiError(error, `Provider integration quarantine ${actionLabel.toLowerCase()} decision failed.`);
      setProviderRuntimeState((current) => ({
        ...current,
        [stateKey]: {
          ...(current[stateKey] ?? emptyProviderRuntimeEvidenceState),
          phase: "loaded",
          message: display.summary,
          details: display.details
        }
      }));
    }
  };

  return { activeProviderDisplayName, createProviderRuntimeHandoff, inlineProviderManagementEnabled, loadProviderRuntimeEvidence, providerCapabilityFilter, providerHealthFilter, providerRuntimeState, providerSearch, providerSort, providerVerificationFilter, replayProviderRuntimeQuarantine, resolveProviderRuntimeQuarantineRecord, runProviderRuntimeDueSync, setProviderCapabilityFilter, setProviderHealthFilter, setProviderSearch, setProviderSort, setProviderVerificationFilter, showProviderAdvancedControls, showProviderFilters, submitProviderOpenApiImport, updateProviderOpenApiImportState, visibleProviderGroups };
}

/** Provider connection center: filters, provider groups, runtime evidence, and OpenAPI import. */
interface SettingsProviderSectionProps {
  vm: SettingsScreenViewModel;
  showProviderSetupControls: boolean;
  inlineProviderManagementEnabled: ReturnType<typeof useSettingsProviderSectionState>["inlineProviderManagementEnabled"];
  showProviderAdvancedControls: ReturnType<typeof useSettingsProviderSectionState>["showProviderAdvancedControls"];
  onProviderRoutingRefresh: (() => Promise<void> | void) | undefined;
  showProviderFilters: ReturnType<typeof useSettingsProviderSectionState>["showProviderFilters"];
  providerSearch: ReturnType<typeof useSettingsProviderSectionState>["providerSearch"];
  setProviderSearch: ReturnType<typeof useSettingsProviderSectionState>["setProviderSearch"];
  providerCapabilityFilter: ReturnType<typeof useSettingsProviderSectionState>["providerCapabilityFilter"];
  setProviderCapabilityFilter: ReturnType<typeof useSettingsProviderSectionState>["setProviderCapabilityFilter"];
  providerHealthFilter: ReturnType<typeof useSettingsProviderSectionState>["providerHealthFilter"];
  setProviderHealthFilter: ReturnType<typeof useSettingsProviderSectionState>["setProviderHealthFilter"];
  providerVerificationFilter: ReturnType<typeof useSettingsProviderSectionState>["providerVerificationFilter"];
  setProviderVerificationFilter: ReturnType<typeof useSettingsProviderSectionState>["setProviderVerificationFilter"];
  providerSort: ReturnType<typeof useSettingsProviderSectionState>["providerSort"];
  setProviderSort: ReturnType<typeof useSettingsProviderSectionState>["setProviderSort"];
  activeProviderDisplayName: ReturnType<typeof useSettingsProviderSectionState>["activeProviderDisplayName"];
  visibleProviderGroups: ReturnType<typeof useSettingsProviderSectionState>["visibleProviderGroups"];
  session: SessionInfo | null;
  onRefresh: (() => Promise<void> | void) | undefined;
  activeTaskView: SettingsRouteViewId;
  providerRuntimeState: ReturnType<typeof useSettingsProviderSectionState>["providerRuntimeState"];
  providerOpenApiImportState: Record<string, ProviderOpenApiImportState>;
  updateProviderOpenApiImportState: ReturnType<typeof useSettingsProviderSectionState>["updateProviderOpenApiImportState"];
  submitProviderOpenApiImport: ReturnType<typeof useSettingsProviderSectionState>["submitProviderOpenApiImport"];
  loadProviderRuntimeEvidence: ReturnType<typeof useSettingsProviderSectionState>["loadProviderRuntimeEvidence"];
  setMutationConfirmation: React.Dispatch<React.SetStateAction<SettingsMutationConfirmation | null>>;
  runProviderRuntimeDueSync: ReturnType<typeof useSettingsProviderSectionState>["runProviderRuntimeDueSync"];
  createProviderRuntimeHandoff: ReturnType<typeof useSettingsProviderSectionState>["createProviderRuntimeHandoff"];
  replayProviderRuntimeQuarantine: ReturnType<typeof useSettingsProviderSectionState>["replayProviderRuntimeQuarantine"];
  resolveProviderRuntimeQuarantineRecord: ReturnType<typeof useSettingsProviderSectionState>["resolveProviderRuntimeQuarantineRecord"];
}

export function SettingsProviderSection({ vm, showProviderSetupControls, inlineProviderManagementEnabled, showProviderAdvancedControls, onProviderRoutingRefresh, showProviderFilters, providerSearch, setProviderSearch, providerCapabilityFilter, setProviderCapabilityFilter, providerHealthFilter, setProviderHealthFilter, providerVerificationFilter, setProviderVerificationFilter, providerSort, setProviderSort, activeProviderDisplayName, visibleProviderGroups, session, onRefresh, activeTaskView, providerRuntimeState, providerOpenApiImportState, updateProviderOpenApiImportState, submitProviderOpenApiImport, loadProviderRuntimeEvidence, setMutationConfirmation, runProviderRuntimeDueSync, createProviderRuntimeHandoff, replayProviderRuntimeQuarantine, resolveProviderRuntimeQuarantineRecord }: SettingsProviderSectionProps) {
  return (
    <Card id="provider-connection-center" className="panel-surface scroll-mt-6 border border-border/70">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div className="eyebrow-label">Provider management</div>
            <CardTitle className="mt-2 flex items-center gap-2 text-base">
              <MonitorCheck className="h-4 w-4 text-primary" />
              {vm.providerConnectionCenter.title}
            </CardTitle>
            <CardDescription className="mt-2">
              {vm.providerConnectionCenter.description}
              {showProviderSetupControls
                ? inlineProviderManagementEnabled
                  ? " Guided credential editing and verification are enabled for the selected provider."
                  : " Guided credential editing is disabled by capability flag."
                : showProviderAdvancedControls
                  ? " Advanced integration and runtime evidence are isolated on this route."
                  : " Choose guided setup or advanced controls for a single provider."}
            </CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {onProviderRoutingRefresh ? (
              <Button
                variant="outline"
                size="sm"
                onClick={() => void onProviderRoutingRefresh()}
                disabled={vm.providerConnectionCenter.refreshAction.disabled}
                disabledReason={vm.providerConnectionCenter.refreshAction.disabledReason}
                aria-label={vm.providerConnectionCenter.refreshAction.ariaLabel}
              >
                <RefreshCcw
                  className={cn(
                    "h-3.5 w-3.5",
                    vm.providerConnectionCenter.refreshAction.busy && "animate-spin"
                  )}
                  aria-hidden="true"
                />
                {vm.providerConnectionCenter.refreshAction.label}
              </Button>
            ) : null}
            <SeverityBadge
              status={toSeverity(vm.providerConnectionCenter.statusVariant)}
              label={vm.providerConnectionCenter.statusLabel}
            />
          </div>
        </div>
      </CardHeader>
      <CardContent className="grid gap-4">
        {showProviderFilters ? (
        <section className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3">
          <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_11rem_11rem_11rem_9rem]">
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Search providers
              <Input
                value={providerSearch}
                onChange={(event) => setProviderSearch(event.target.value)}
                placeholder="Search provider, workflow, or action"
                leadingIcon={<Search className="h-4 w-4" />}
                aria-label="Search providers in connection center"
              />
            </label>
            <FilterSelect
              label="Capability"
              value={providerCapabilityFilter}
              onChange={(value) => setProviderCapabilityFilter(value as typeof providerCapabilityFilter)}
              options={[
                { value: "all", label: "All" },
                { value: "brokerage", label: "Brokerage" },
                { value: "data", label: "Data" },
                { value: "accounting", label: "Accounting" }
              ]}
            />
            <FilterSelect
              label="Health"
              value={providerHealthFilter}
              onChange={(value) => setProviderHealthFilter(value as typeof providerHealthFilter)}
              options={[
                { value: "all", label: "All" },
                { value: "healthy", label: "Healthy" },
                { value: "warning", label: "Warning" },
                { value: "blocked", label: "Blocked" }
              ]}
            />
            <FilterSelect
              label="Verification"
              value={providerVerificationFilter}
              onChange={(value) => setProviderVerificationFilter(value as typeof providerVerificationFilter)}
              options={[
                { value: "all", label: "All" },
                { value: "verified", label: "Verified" },
                { value: "unverified", label: "Unverified" }
              ]}
            />
            <FilterSelect
              label="Sort"
              value={providerSort}
              onChange={(value) => setProviderSort(value as typeof providerSort)}
              options={[
                { value: "risk", label: "Risk" },
                { value: "name", label: "Name" }
              ]}
            />
          </div>
          <p className="text-xs leading-5 text-muted-foreground">
            Providers are sorted by risk by default so blocked and warning rows needing attention appear first.
          </p>
        </section>
        ) : (
          <div className="flex flex-wrap items-center gap-2 rounded-md border border-border/70 bg-background/35 px-3 py-3 text-xs text-muted-foreground">
            <Link to={WORKSTATION_ROUTE_CATALOG.settingsProviders} className="font-semibold text-primary hover:underline focus:outline-hidden focus:ring-2 focus:ring-primary/40">
              Back to provider overview
            </Link>
            <span aria-hidden="true">·</span>
            <span>{activeProviderDisplayName ? `Focused provider: ${activeProviderDisplayName}` : "Choose a provider from the overview."}</span>
          </div>
        )}
        <div className={cn("grid gap-4", visibleProviderGroups.length > 1 && "xl:grid-cols-2")}>
        {visibleProviderGroups.map((group) => (
          <section key={group.id} className="grid gap-3" aria-label={group.label}>
            <div className="min-w-0">
              <h3 className="text-sm font-semibold text-foreground">{group.label}</h3>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">{group.summary}</p>
            </div>
            {group.rows.length > 0 ? (
              <div className="grid gap-2">
                {group.rows.map((row) => (
                  <article
                    key={`${group.id}-${row.providerId}`}
                    id={row.rowAnchorId === "alpaca-provider-setup" ? undefined : row.rowAnchorId}
                    className="rounded-md border border-border/70 bg-background/35 px-3 py-3"
                  >
                    {showProviderSetupControls && inlineProviderManagementEnabled ? (
                      <OwnedProviderCredentialPanel row={row} session={session}
                        onRefresh={async () => { await onRefresh?.(); await onProviderRoutingRefresh?.(); }} />
                    ) : null}
                    <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                      <div className="min-w-0">
                        <div className="flex flex-wrap items-center gap-2">
                          <h4 className="text-sm font-semibold text-foreground">{row.displayName}</h4>
                          <Badge variant="outline">{row.capabilityLabel}</Badge>
                          <SeverityBadge
                            status={toSeverity(row.healthTone === "muted" ? "default" : row.healthTone)}
                            label={row.healthLabel}
                          />
                          {showProviderSetupControls && inlineProviderManagementEnabled ? (
                            <SeverityBadge
                              status={toSeverity(row.readinessTone)}
                              label={row.readinessLabel}
                            />
                          ) : null}
                        </div>
                        <p className="mt-2 text-xs leading-5 text-muted-foreground">{row.recommendedAction}</p>
                      </div>
                      <div className="flex flex-wrap items-center gap-2">
                        {activeTaskView !== "provider-setup" ? (
                          <Button asChild variant="outline" size="sm" className="shrink-0">
                            <Link to={settingsProviderSetupRoute(row.providerId)} aria-label={`Open guided setup for ${row.displayName}`}>
                              Guided setup
                              <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                            </Link>
                          </Button>
                        ) : null}
                        {activeTaskView !== "provider-advanced" ? (
                          <Button asChild variant="outline" size="sm" className="shrink-0">
                            <Link to={settingsProviderAdvancedRoute(row.providerId)} aria-label={`Open advanced controls for ${row.displayName}`}>
                              Advanced
                              <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                            </Link>
                          </Button>
                        ) : null}
                      </div>
                    </div>
                    <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                      <SettingsFieldRow label="Credential" value={row.credentialLabel} tone={row.credentialTone} />
                      <SettingsFieldRow label="Verification" value={row.verificationLabel} tone={row.verificationTone} />
                      <SettingsFieldRow label="Source" value={row.sourceLabel} tone={row.sourceTone} />
                      <SettingsFieldRow label="Environment" value={row.environmentLabel} tone="muted" />
                      <SettingsFieldRow label="Masked key" value={row.maskedKeyPreviewLabel} tone="muted" />
                      <SettingsFieldRow label="Last good heartbeat" value={row.lastHeartbeatLabel} tone={row.lastHeartbeatTone} />
                      <SettingsFieldRow label="Failover" value={row.fallbackLabel} tone={row.fallbackLabel === "Fallback active" ? "warning" : "muted"} />
                      <SettingsFieldRow label="Routing bindings" value={row.routingBindingsLabel} tone="muted" />
                      <SettingsFieldRow label="Trust score" value={row.trustScoreLabel} tone={row.healthTone} />
                      <SettingsFieldRow label="Production gate" value={row.productionStateLabel} tone={row.productionStateTone} />
                      <SettingsFieldRow label="Readiness" value={row.readinessLabel} tone={row.readinessTone} />
                      <SettingsFieldRow label="Affected workflows" value={row.affectedWorkflowsLabel} tone="default" />
                    </dl>
                    {showProviderAdvancedControls ? (
                    <ProviderIntegrationRuntimePanel
                      row={row}
                      state={providerRuntimeState[providerRuntimeStateKey(row)] ?? emptyProviderRuntimeEvidenceState}
                      operatorName={session?.displayName ?? "settings-operator"}
                      openApiImportState={providerOpenApiImportState[providerRuntimeStateKey(row)] ?? createProviderOpenApiImportState(row)}
                      onOpenApiImportStateChange={(updater) => updateProviderOpenApiImportState(row, updater)}
                      onImportOpenApi={() => void submitProviderOpenApiImport(row)}
                      onLoad={() => void loadProviderRuntimeEvidence(row)}
                      onRunDueSync={() => setMutationConfirmation({
                        id: `provider-sync-${row.providerId}`,
                        title: `Run due sync for ${row.displayName}?`,
                        description: "Run only the due, server-planned read-only capabilities. Blocked plan items remain blocked by shared provider policy.",
                        confirmLabel: "Confirm due sync",
                        confirmAriaLabel: `Confirm due provider integration sync for ${row.displayName}`,
                        run: () => runProviderRuntimeDueSync(row)
                      })}
                      onCreateHandoff={() => setMutationConfirmation({
                        id: `provider-handoff-${row.providerId}`,
                        title: `Create reconciliation handoff for ${row.displayName}?`,
                        description: "Create retained handoff evidence only for staging rows the shared promotion-readiness service marked ready.",
                        confirmLabel: "Confirm handoff",
                        confirmAriaLabel: `Confirm reconciliation handoff for ${row.displayName}`,
                        run: () => createProviderRuntimeHandoff(row)
                      })}
                      onReplayQuarantine={() => setMutationConfirmation({
                        id: `provider-replay-${row.providerId}`,
                        title: `Replay quarantined records for ${row.displayName}?`,
                        description: "Replay the retained eligible quarantine batch using its original manifest version. Mapping updates are not applied; remediation requires an explicitly selected newer version. Existing decisions and source evidence remain retained.",
                        confirmLabel: "Confirm replay",
                        confirmAriaLabel: `Confirm quarantine replay for ${row.displayName}`,
                        destructive: true,
                        run: () => replayProviderRuntimeQuarantine(row)
                      })}
                      onResolveQuarantineRecord={(record, action) => setMutationConfirmation({
                        id: `provider-quarantine-${record.quarantineRecordId}-${action}`,
                        title: `Record quarantine decision for ${row.displayName}?`,
                        description: `Record ${action} for retained quarantine record ${record.quarantineRecordId}. The server remains authoritative for allowed transitions and replay eligibility.`,
                        confirmLabel: "Confirm decision",
                        confirmAriaLabel: `Confirm ${action} for quarantine record ${record.quarantineRecordId}`,
                        destructive: action === "IgnoreProviderRecord",
                        run: () => resolveProviderRuntimeQuarantineRecord(row, record, action)
                      })}
                    />
                    ) : null}

                  </article>
                ))}
              </div>
            ) : (
              <p className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
                {group.emptyLabel}
              </p>
            )}
          </section>
        ))}
        </div>
      </CardContent>
    </Card>
  );
}
