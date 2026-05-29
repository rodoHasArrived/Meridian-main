import { Activity, ArrowRight, ExternalLink, GitBranch, KeyRound, LoaderCircle, MonitorCheck, RefreshCcw, Save, Search, ShieldCheck, Trash2, User } from "lucide-react";
import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldSupportText, joinDescribedByIds } from "@/components/ui/field-support";
import { Input } from "@/components/ui/input";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { approveSecurityAssetProfile, assignLedgerMapping, createRolePermissionProfile, createSecurityMasterEntry, deleteProviderCredentials, draftSecurityAssetProfile, getSecurityAssetProfileLineage, putProviderCredentials, rollbackSecurityAssetProfile, testProviderConnection, upsertOperationsApprovalPolicyRule, upsertOperationsCloseCalendarItem, verifyProviderConnection } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { cn } from "@/lib/utils";
import {
  buildSettingsScreenViewModel,
  useAlpacaConnectionFormViewModel,
  useSettingsRecentEventsSelectionViewModel,
  type SettingsAlpacaCredentialFieldState,
  type SettingsProfileAuthenticationStep,
  type SettingsRecentEventDetail,
  type SettingsRecentEventTableRow
} from "@/screens/settings-screen.view-model";
import { PROVIDER_KIND_CATALOG } from "@/screens/data-operations-screen.view-model";
import type {
  BrokerageConnectionStatus,
  DataOperationsWorkspaceResponse,
  FeatureCapabilitySettingsResponse,
  GovernanceWorkspaceResponse,
  PortfolioWorkspaceResponse,
  ProviderConnectionRow,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  LedgerMappingWorkbench,
  OperationsApprovalPolicyMatrix,
  OperationsApprovalPolicyMatrixRow,
  OperationsCloseCalendar,
  OperationsCloseCalendarItem,
  RolePermissionCatalog,
  SecurityAssetProfileDefinition,
  SecurityAssetProfileFieldDefinition,
  SecurityAssetProfileGovernanceResult,
  SecurityAssetProfileLineage,
  ResearchWorkspaceResponse,
  SessionInfo,
  SystemOverviewResponse,
  TradingWorkspaceResponse,
  WorkspaceKey
} from "@/types";

interface SettingsScreenProps {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  research?: ResearchWorkspaceResponse | null;
  trading?: TradingWorkspaceResponse | null;
  portfolio?: PortfolioWorkspaceResponse | null;
  dataOperations?: DataOperationsWorkspaceResponse | null;
  governance?: GovernanceWorkspaceResponse | null;
  reporting?: GovernanceWorkspaceResponse | null;
  brokerageConnection?: BrokerageConnectionStatus | null;
  providerConnections?: ProviderConnectionRow[] | null;
  providerRoutingConnections?: ProviderRoutingConnection[] | null;
  providerRoutingBindings?: ProviderRoutingBinding[] | null;
  providerRoutingTrustSnapshots?: ProviderRoutingTrustSnapshot[] | null;
  featureCapabilities?: FeatureCapabilitySettingsResponse | null;
  rolePermissionCatalog?: RolePermissionCatalog | null;
  securityAssetProfiles?: SecurityAssetProfileDefinition[] | null;
  ledgerMappingWorkbench?: LedgerMappingWorkbench | null;
  operationsApprovalPolicyMatrix?: OperationsApprovalPolicyMatrix | null;
  operationsCloseCalendar?: OperationsCloseCalendar | null;
  providerRoutingRefreshing?: boolean;
  onFeatureCapabilityToggle?: (capabilityKey: string, isEnabled: boolean) => Promise<void> | void;
  onRefresh?: () => Promise<void> | void;
  onProviderRoutingRefresh?: () => Promise<void> | void;
  loading?: boolean;
  error?: string | null;
  workspaceErrors?: Partial<Record<WorkspaceKey, string>>;
}

type ProviderInlineField = "apiKey" | "apiSecret" | "endpoint";
type ProviderInlineBusyAction = "test" | "save" | "verify" | "clear" | null;

interface LedgerMappingAssignmentState {
  accountId: string;
  ledgerGroupId: string;
  rationale: string;
  busy: boolean;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger";
}

interface RolePermissionProfileState {
  profileName: string;
  displayName: string;
  description: string;
  baseRole: string;
  permissionNames: string[];
  rationale: string;
  busy: boolean;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}

interface ApprovalPolicyRuleState {
  policyKey: string;
  requiredPermission: string;
  submitterRole: string;
  reviewerRole: string;
  requiredDistinctApprovals: number;
  requiresIndependentReviewer: boolean;
  requiresReportPack: boolean;
  requiresChecklistControlApprovals: boolean;
  evidenceRequirement: string;
  severity: string;
  rationale: string;
  busy: boolean;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}

interface CloseCalendarItemState {
  workflowId: string;
  taskId: string;
  dueDate: string;
  owner: string;
  rationale: string;
  busy: boolean;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}

interface AssetProfileDraftState {
  starterProfileId: string;
  profileId: string;
  name: string;
  category: string;
  subType: string;
  rationale: string;
  busyAction: "draft" | "approve" | "lineage" | "rollback" | null;
  rollbackTargetVersion: number;
  lineage: SecurityAssetProfileLineage | null;
  result: SecurityAssetProfileGovernanceResult | null;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}

interface ProfileBackedSecurityState {
  profileId: string;
  displayName: string;
  internalCode: string;
  currency: string;
  fieldValues: Record<string, string>;
  rationale: string;
  busy: boolean;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}

interface ProviderInlineFieldDefinition {
  field: ProviderInlineField;
  label: string;
  type: "password" | "url";
  placeholder: string;
  helpText: string;
  required: boolean;
}

interface ProviderInlineState {
  editing: boolean;
  values: Record<ProviderInlineField, string>;
  environment: "paper" | "live" | "sandbox" | "custom";
  liveAcknowledged: boolean;
  dirty: boolean;
  busyAction: ProviderInlineBusyAction;
  statusMessage: string | null;
  statusDetails: string[];
  statusTone: "default" | "success" | "warning" | "danger";
  verificationFailed: boolean;
  testLatencyLabel: string | null;
}

const systemToneClass = {
  default: "border-border/70",
  success: "border-success/30",
  warning: "border-warning/30",
  danger: "border-danger/30"
} as const;

const eventToneClass = {
  default: "border-border/70 bg-secondary/25",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10"
} as const;

const itemToneClass = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
} as const;

const diagnosticToneClass = {
  default: "border-border/70 bg-secondary/30",
  success: "border-success/30 bg-success/10",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10"
} as const;

const formReadinessTextClass = {
  default: "text-muted-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
} as const;

const requirementToneClass = {
  success: "border-success/30 bg-success/10 text-success",
  warning: "border-warning/35 bg-warning/10 text-warning",
  muted: "border-border/70 bg-secondary/25 text-muted-foreground"
} as const;

const environmentOptionClass = {
  paper: {
    selected: "border-paper/40 bg-paper/10 text-paper",
    idle: "border-border/70 bg-secondary/25 text-foreground hover:border-paper/35 hover:bg-paper/10",
    badge: "border-paper/30 bg-paper/10 text-paper"
  },
  live: {
    selected: "border-live-env/40 bg-live-env/10 text-live-env",
    idle: "border-border/70 bg-secondary/25 text-foreground hover:border-live-env/35 hover:bg-live-env/10",
    badge: "border-live-env/35 bg-live-env/10 text-live-env"
  }
} as const;

function AlpacaCredentialField({
  field,
  value,
  onValueChange,
  leadingIcon
}: {
  field: SettingsAlpacaCredentialFieldState;
  value: string;
  onValueChange: (value: string) => void;
  leadingIcon: ReactNode;
}) {
  const disabledReasonId = `${field.id}-disabled-reason`;
  return (
    <label htmlFor={field.id} className="grid gap-1 text-xs font-medium text-muted-foreground">
      {field.label}
      <Input
        id={field.id}
        type={field.type}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
        autoComplete={field.autoComplete}
        placeholder={field.placeholder}
        leadingIcon={leadingIcon}
        disabled={field.disabled}
        error={field.error}
        aria-describedby={joinDescribedByIds(field.describedBy, field.disabledReason ? disabledReasonId : undefined)}
      />
      <FieldSupportText
        helpId={field.helpId}
        helpText={field.helpText}
        helpClassName={cn("text-[11px] leading-4", field.error ? "text-danger" : "text-muted-foreground")}
        disabledReason={field.disabledReason}
        disabledReasonId={field.disabledReason ? disabledReasonId : undefined}
        disabledReasonClassName="text-[11px] leading-4"
      />
    </label>
  );
}

const setupStepToneClass = {
  success: "border-success/30 bg-success/10",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10",
  muted: "border-border/70 bg-secondary/25"
} as const;

const recentEventColumns: DenseDataTableColumn<SettingsRecentEventTableRow>[] = [
  {
    id: "status",
    label: "Status",
    render: (event) => <Badge variant={event.badgeVariant}>{event.statusCode}</Badge>
  },
  {
    id: "message",
    label: "Message",
    className: "min-w-[14rem]",
    render: (event) => <span className="text-foreground">{event.message}</span>
  },
  {
    id: "source",
    label: "Source",
    className: "font-mono text-muted-foreground",
    render: (event) => event.source
  },
  {
    id: "timestamp",
    label: "Timestamp",
    className: "font-mono text-muted-foreground",
    render: (event) => event.timestamp
  }
];

export function SettingsScreen({
  session,
  overview,
  research = null,
  trading = null,
  portfolio = null,
  dataOperations = null,
  governance = null,
  reporting = null,
  brokerageConnection = null,
  providerConnections = null,
  providerRoutingConnections = null,
  providerRoutingBindings = null,
  providerRoutingTrustSnapshots = null,
  featureCapabilities = null,
  rolePermissionCatalog = null,
  securityAssetProfiles = null,
  ledgerMappingWorkbench = null,
  operationsApprovalPolicyMatrix = null,
  operationsCloseCalendar = null,
  providerRoutingRefreshing = false,
  onFeatureCapabilityToggle,
  onRefresh,
  onProviderRoutingRefresh,
  loading = false,
  error = null,
  workspaceErrors = {}
}: SettingsScreenProps) {
  const vm = buildSettingsScreenViewModel({
    session,
    overview,
    research,
    trading,
    portfolio,
    dataOperations,
    governance,
    reporting,
    brokerageConnection,
    providerConnections,
    providerRoutingConnections,
    providerRoutingBindings,
    providerRoutingTrustSnapshots,
    featureCapabilities,
    rolePermissionCatalog,
    securityAssetProfiles,
    ledgerMappingWorkbench,
    operationsApprovalPolicyMatrix,
    operationsCloseCalendar,
    providerRoutingRefreshing,
    loading,
    error,
    workspaceErrors
  });
  const alpacaForm = useAlpacaConnectionFormViewModel({
    onRefresh,
    canClear: vm.alpacaConnectionPanel.canClear
  });
  const recentEventsVm = useSettingsRecentEventsSelectionViewModel(vm.recentEventsSection);
  const [providerSearch, setProviderSearch] = useState("");
  const [providerCapabilityFilter, setProviderCapabilityFilter] = useState<"all" | "brokerage" | "data">("all");
  const [providerHealthFilter, setProviderHealthFilter] = useState<"all" | "healthy" | "warning" | "blocked">("all");
  const [providerVerificationFilter, setProviderVerificationFilter] = useState<"all" | "verified" | "unverified">("all");
  const [providerSort, setProviderSort] = useState<"risk" | "name">("risk");
  const [providerInlineState, setProviderInlineState] = useState<Record<string, ProviderInlineState>>({});
  const ledgerMappingDraft = useMemo(
    () => buildLedgerMappingAssignmentDraft(ledgerMappingWorkbench),
    [ledgerMappingWorkbench]
  );
  const roleProfileDraft = useMemo(
    () => buildRolePermissionProfileDraft(rolePermissionCatalog),
    [rolePermissionCatalog]
  );
  const approvalPolicyDraft = useMemo(
    () => buildApprovalPolicyRuleDraft(operationsApprovalPolicyMatrix),
    [operationsApprovalPolicyMatrix]
  );
  const closeCalendarDraft = useMemo(
    () => buildCloseCalendarItemDraft(operationsCloseCalendar),
    [operationsCloseCalendar]
  );
  const approvedAssetProfiles = useMemo(
    () => (securityAssetProfiles ?? []).filter((profile) => profile.status === "Approved"),
    [securityAssetProfiles]
  );
  const firstApprovedAssetProfile = approvedAssetProfiles[0] ?? null;
  const ledgerMappingDraftSignature = `${ledgerMappingDraft.accountOptions.map((option) => option.value).join("|")}::${ledgerMappingDraft.ledgerGroupOptions.map((option) => option.value).join("|")}`;
  const roleProfileDraftSignature = `${roleProfileDraft.baseRoleOptions.map((option) => option.value).join("|")}::${roleProfileDraft.permissionOptions.map((option) => option.value).join("|")}`;
  const approvalPolicyDraftSignature = approvalPolicyDraft.rows.map((row) => [
    row.policyKey,
    row.requiredPermission,
    row.submitterRole,
    row.reviewerRole,
    row.requiredDistinctApprovals,
    row.requiresIndependentReviewer,
    row.requiresReportPack,
    row.requiresChecklistControlApprovals,
    row.evidenceRequirement,
    row.severity
  ].join(":")).join("|");
  const closeCalendarDraftSignature = closeCalendarDraft.items.map((item) => [
    item.workflowId,
    item.nextDueTaskId ?? "",
    item.nextDueDate ?? "",
    item.nextDueOwner ?? "",
    item.periodId,
    item.version
  ].join(":")).join("|");
  const assetProfileSignature = approvedAssetProfiles.map((profile) => `${profile.profileId}:${profile.version}`).join("|");
  const [ledgerMappingAssignment, setLedgerMappingAssignment] = useState<LedgerMappingAssignmentState>(() => ({
    accountId: "",
    ledgerGroupId: "",
    rationale: "Assign ledger group for governed posting, reconciliation, reporting, and close control.",
    busy: false,
    message: null,
    details: [],
    tone: "default"
  }));
  const [rolePermissionProfile, setRolePermissionProfile] = useState<RolePermissionProfileState>(() => ({
    profileName: "",
    displayName: "",
    description: "",
    baseRole: "",
    permissionNames: [],
    rationale: "Create a scoped authority profile for governed fund operations.",
    busy: false,
    message: null,
    details: [],
    tone: "default"
  }));
  const [approvalPolicyRule, setApprovalPolicyRule] = useState<ApprovalPolicyRuleState>(() => ({
    policyKey: "",
    requiredPermission: "",
    submitterRole: "",
    reviewerRole: "",
    requiredDistinctApprovals: 1,
    requiresIndependentReviewer: true,
    requiresReportPack: true,
    requiresChecklistControlApprovals: true,
    evidenceRequirement: "",
    severity: "Critical",
    rationale: "Update approval policy for governed fund-operations close control.",
    busy: false,
    message: null,
    details: [],
    tone: "default"
  }));
  const [closeCalendarItem, setCloseCalendarItem] = useState<CloseCalendarItemState>(() => ({
    workflowId: "",
    taskId: "",
    dueDate: "",
    owner: "",
    rationale: "Update account close calendar ownership and due-date governance.",
    busy: false,
    message: null,
    details: [],
    tone: "default"
  }));
  const [assetProfileDraft, setAssetProfileDraft] = useState<AssetProfileDraftState>(() => (
    createAssetProfileDraftState(firstApprovedAssetProfile)
  ));
  const [profileBackedSecurity, setProfileBackedSecurity] = useState<ProfileBackedSecurityState>(() => (
    createProfileBackedSecurityState(firstApprovedAssetProfile)
  ));
  const providerInlineFlag = featureCapabilities?.capabilities.find((capability) => (
    capability.capabilityKey === "desktop.settings.provider-connection-center-inline-management"
  ));
  const inlineProviderManagementEnabled = providerInlineFlag?.isEnabled ?? true;
  const allProviderRows = useMemo(
    () => vm.providerConnectionCenter.groups.flatMap((group) => group.rows),
    [vm.providerConnectionCenter.groups]
  );
  const providerRowIdsSignature = useMemo(
    () => allProviderRows.map((row) => row.providerId).sort().join("|"),
    [allProviderRows]
  );
  const providerFieldDefinitions = useMemo(
    () => Object.fromEntries(allProviderRows.map((row) => [row.providerId, buildProviderFieldDefinitions(row)])),
    [allProviderRows]
  );
  const providerRiskScores = useMemo(
    () => Object.fromEntries(allProviderRows.map((row) => [row.providerId, providerRiskScore(row)])),
    [allProviderRows]
  );

  useEffect(() => {
    setLedgerMappingAssignment((current) => ({
      ...current,
      accountId: ledgerMappingDraft.accountOptions.some((option) => option.value === current.accountId)
        ? current.accountId
        : ledgerMappingDraft.accountOptions[0]?.value ?? "",
      ledgerGroupId: ledgerMappingDraft.ledgerGroupOptions.some((option) => option.value === current.ledgerGroupId)
        ? current.ledgerGroupId
        : ledgerMappingDraft.ledgerGroupOptions[0]?.value ?? "",
      message: null,
      details: [],
      tone: "default"
    }));
  }, [ledgerMappingDraftSignature]);

  useEffect(() => {
    setRolePermissionProfile((current) => {
      const currentBaseRole = roleProfileDraft.baseRoleOptions.some((option) => option.value === current.baseRole)
        ? current.baseRole
        : roleProfileDraft.defaultBaseRole;
      const validPermissions = new Set(roleProfileDraft.permissionOptions.map((option) => option.value));
      const retainedPermissions = current.permissionNames.filter((permission) => validPermissions.has(permission));
      return {
        ...current,
        baseRole: currentBaseRole,
        permissionNames: retainedPermissions.length > 0 ? retainedPermissions : roleProfileDraft.defaultPermissionNames,
        message: null,
        details: [],
        tone: "default"
      };
    });
  }, [roleProfileDraftSignature]);

  useEffect(() => {
    setApprovalPolicyRule((current) => {
      const selected = approvalPolicyDraft.rows.find((row) => row.policyKey === current.policyKey) ??
        approvalPolicyDraft.rows[0];
      if (!selected) {
        return {
          ...current,
          policyKey: "",
          message: null,
          details: [],
          tone: "default"
        };
      }

      return {
        ...current,
        policyKey: selected.policyKey,
        requiredPermission: selected.requiredPermission,
        submitterRole: selected.submitterRole,
        reviewerRole: selected.reviewerRole,
        requiredDistinctApprovals: selected.requiredDistinctApprovals,
        requiresIndependentReviewer: selected.requiresIndependentReviewer,
        requiresReportPack: selected.requiresReportPack,
        requiresChecklistControlApprovals: selected.requiresChecklistControlApprovals,
        evidenceRequirement: selected.evidenceRequirement,
        severity: selected.severity,
        message: null,
        details: [],
        tone: "default"
      };
    });
  }, [approvalPolicyDraftSignature]);

  useEffect(() => {
    setCloseCalendarItem((current) => {
      const selected = closeCalendarDraft.items.find((item) => item.workflowId === current.workflowId) ??
        closeCalendarDraft.items[0];
      if (!selected) {
        return {
          ...current,
          workflowId: "",
          taskId: "",
          dueDate: "",
          owner: "",
          message: null,
          details: [],
          tone: "default"
        };
      }

      return {
        ...current,
        workflowId: selected.workflowId,
        taskId: selected.nextDueTaskId ?? "",
        dueDate: selected.nextDueDate ?? "",
        owner: selected.nextDueOwner ?? "",
        message: null,
        details: [],
        tone: "default"
      };
    });
  }, [closeCalendarDraftSignature]);

  useEffect(() => {
    setAssetProfileDraft((current) => {
      const selected = approvedAssetProfiles.find((profile) => profile.profileId === current.starterProfileId) ??
        firstApprovedAssetProfile;
      if (!selected) {
        return createAssetProfileDraftState(null);
      }

      if (current.starterProfileId === selected.profileId && current.profileId.trim()) {
        return current;
      }

      return createAssetProfileDraftState(selected);
    });

    setProfileBackedSecurity((current) => {
      const selected = approvedAssetProfiles.find((profile) => profile.profileId === current.profileId) ??
        firstApprovedAssetProfile;
      if (!selected) {
        return createProfileBackedSecurityState(null);
      }

      return {
        ...current,
        profileId: selected.profileId,
        fieldValues: buildProfileFieldValueState(selected, current.fieldValues),
        message: null,
        details: [],
        tone: "default"
      };
    });
  }, [assetProfileSignature, firstApprovedAssetProfile, approvedAssetProfiles]);

  useEffect(() => {
    if (!inlineProviderManagementEnabled) {
      return;
    }

    setProviderInlineState((current) => {
      let changed = false;
      const next = { ...current };
      for (const row of allProviderRows) {
        if (next[row.providerId]) {
          continue;
        }
        next[row.providerId] = createProviderInlineState(row);
        changed = true;
      }
      return changed ? next : current;
    });
  }, [inlineProviderManagementEnabled, providerRowIdsSignature]);

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

  const updateProviderInlineState = (providerId: string, updater: (state: ProviderInlineState) => ProviderInlineState) => {
    setProviderInlineState((current) => {
      const previous = current[providerId];
      if (!previous) {
        return current;
      }
      return {
        ...current,
        [providerId]: updater(previous)
      };
    });
  };

  const toggleProviderEdit = (providerId: string) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      editing: !state.editing,
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
  };

  const updateProviderField = (providerId: string, field: ProviderInlineField, value: string) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      dirty: true,
      values: {
        ...state.values,
        [field]: value
      },
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
  };

  const updateProviderEnvironment = (providerId: string, value: ProviderInlineState["environment"]) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      dirty: true,
      environment: value,
      liveAcknowledged: value === "live" ? state.liveAcknowledged : false,
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
  };

  const updateProviderLiveAcknowledgement = (providerId: string, checked: boolean) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      dirty: true,
      liveAcknowledged: checked,
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
  };

  const runProviderTest = async (providerId: string) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      busyAction: "test",
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
    try {
      const result = await testProviderConnection(providerId);
      updateProviderInlineState(providerId, (state) => ({
        ...state,
        busyAction: null,
        testLatencyLabel: result.latency ?? null,
        statusMessage: result.success ? "Connection test passed." : "Connection test failed.",
        statusDetails: [result.message].filter(Boolean),
        statusTone: result.success ? "success" : "danger"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider connection test failed.");
      updateProviderInlineState(providerId, (state) => ({
        ...state,
        busyAction: null,
        statusMessage: display.summary,
        statusDetails: display.details,
        statusTone: "danger"
      }));
    }
  };

  const saveProviderDraft = async (row: (typeof allProviderRows)[number]) => {
    const state = providerInlineState[row.providerId];
    if (!state) {
      return;
    }
    const definitions = providerFieldDefinitions[row.providerId] ?? buildProviderFieldDefinitions(row);
    const missingField = definitions.find((definition) => (
      definition.required && !state.values[definition.field].trim()
    ));
    if (missingField) {
      updateProviderInlineState(row.providerId, (current) => ({
        ...current,
        statusMessage: `${missingField.label} is required before save.`,
        statusDetails: [],
        statusTone: "warning"
      }));
      return;
    }
    if (state.environment === "live" && !state.liveAcknowledged) {
      updateProviderInlineState(row.providerId, (current) => ({
        ...current,
        statusMessage: "Acknowledge live routing before saving live credentials.",
        statusDetails: [],
        statusTone: "warning"
      }));
      return;
    }

    updateProviderInlineState(row.providerId, (current) => ({
      ...current,
      busyAction: "save",
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));

    try {
      const result = await putProviderCredentials(row.providerId, {
        credentials: definitions.reduce<Record<string, string | null>>((acc, definition) => {
          const value = state.values[definition.field].trim();
          acc[definition.field] = value.length > 0 ? value : null;
          return acc;
        }, {}),
        environment: state.environment,
        requestedBy: session?.displayName ?? "settings-screen"
      });
      await onRefresh?.();
      await onProviderRoutingRefresh?.();
      updateProviderInlineState(row.providerId, (current) => ({
        ...current,
        busyAction: null,
        dirty: false,
        editing: false,
        verificationFailed: false,
        statusMessage: `Credentials saved (${result.credentialState}).`,
        statusDetails: result.warnings ?? [],
        statusTone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Saving provider credentials failed.");
      updateProviderInlineState(row.providerId, (current) => ({
        ...current,
        busyAction: null,
        statusMessage: display.summary,
        statusDetails: display.details,
        statusTone: "danger"
      }));
    }
  };

  const runProviderVerification = async (providerId: string) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      busyAction: "verify",
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
    try {
      const result = await verifyProviderConnection(providerId);
      await onRefresh?.();
      await onProviderRoutingRefresh?.();
      updateProviderInlineState(providerId, (state) => ({
        ...state,
        busyAction: null,
        verificationFailed: !result.success,
        statusMessage: result.success ? "Verification succeeded." : "Verification failed.",
        statusDetails: [
          result.lastError,
          ...(result.warnings ?? [])
        ].filter((value): value is string => Boolean(value)),
        statusTone: result.success ? "success" : "danger"
      }));
    } catch (error) {
      const display = describeApiError(error, "Provider verification failed.");
      updateProviderInlineState(providerId, (state) => ({
        ...state,
        busyAction: null,
        verificationFailed: true,
        statusMessage: display.summary,
        statusDetails: display.details,
        statusTone: "danger"
      }));
    }
  };

  const clearProviderCredentials = async (providerId: string) => {
    updateProviderInlineState(providerId, (state) => ({
      ...state,
      busyAction: "clear",
      statusMessage: null,
      statusDetails: [],
      statusTone: "default"
    }));
    try {
      const result = await deleteProviderCredentials(providerId);
      await onRefresh?.();
      await onProviderRoutingRefresh?.();
      updateProviderInlineState(providerId, (state) => ({
        ...state,
        busyAction: null,
        dirty: false,
        values: { apiKey: "", apiSecret: "", endpoint: "" },
        statusMessage: "Credentials cleared.",
        statusDetails: result.warnings ?? [],
        statusTone: "success",
        verificationFailed: result.verificationState === "Failed"
      }));
    } catch (error) {
      const display = describeApiError(error, "Clearing provider credentials failed.");
      updateProviderInlineState(providerId, (state) => ({
        ...state,
        busyAction: null,
        statusMessage: display.summary,
        statusDetails: display.details,
        statusTone: "danger"
      }));
    }
  };

  const submitLedgerMappingAssignment = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!ledgerMappingAssignment.accountId || !ledgerMappingAssignment.ledgerGroupId) {
      setLedgerMappingAssignment((current) => ({
        ...current,
        message: "Choose an account and ledger group before saving.",
        details: [],
        tone: "danger"
      }));
      return;
    }

    setLedgerMappingAssignment((current) => ({
      ...current,
      busy: true,
      message: null,
      details: [],
      tone: "default"
    }));

    try {
      const result = await assignLedgerMapping({
        accountId: ledgerMappingAssignment.accountId,
        ledgerGroupId: ledgerMappingAssignment.ledgerGroupId,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: ledgerMappingAssignment.rationale,
        correlationId: `settings-ledger-map-${Date.now()}`
      });
      await onRefresh?.();
      setLedgerMappingAssignment((current) => ({
        ...current,
        busy: false,
        message: `Ledger mapping saved for ${result.account.accountCode}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Ledger mapping assignment failed.");
      setLedgerMappingAssignment((current) => ({
        ...current,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const toggleRoleProfilePermission = (permissionName: string, checked: boolean) => {
    setRolePermissionProfile((current) => ({
      ...current,
      permissionNames: checked
        ? Array.from(new Set([...current.permissionNames, permissionName]))
        : current.permissionNames.filter((name) => name !== permissionName),
      message: null,
      details: [],
      tone: "default"
    }));
  };

  const submitRolePermissionProfile = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const profileName = rolePermissionProfile.profileName.trim();
    const displayName = rolePermissionProfile.displayName.trim() || profileName;
    if (!roleProfileDraft.canSave || !profileName || !displayName || rolePermissionProfile.permissionNames.length === 0) {
      setRolePermissionProfile((current) => ({
        ...current,
        message: "Profile name, display name, base role, and at least one permission are required.",
        details: roleProfileDraft.disabledReason ? [roleProfileDraft.disabledReason] : [],
        tone: "warning"
      }));
      return;
    }

    setRolePermissionProfile((current) => ({
      ...current,
      busy: true,
      message: null,
      details: [],
      tone: "default"
    }));

    try {
      const result = await createRolePermissionProfile({
        profileName,
        displayName,
        description: rolePermissionProfile.description.trim() || null,
        baseRole: rolePermissionProfile.baseRole,
        permissionNames: rolePermissionProfile.permissionNames,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: rolePermissionProfile.rationale,
        correlationId: `settings-role-profile-${Date.now()}`
      });
      await onRefresh?.();
      setRolePermissionProfile((current) => ({
        ...current,
        busy: false,
        message: `Role profile saved for ${result.profile.displayName}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Role profile save failed.");
      setRolePermissionProfile((current) => ({
        ...current,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const selectApprovalPolicyRule = (policyKey: string) => {
    const selected = approvalPolicyDraft.rows.find((row) => row.policyKey === policyKey);
    if (!selected) {
      return;
    }

    setApprovalPolicyRule((current) => ({
      ...current,
      policyKey: selected.policyKey,
      requiredPermission: selected.requiredPermission,
      submitterRole: selected.submitterRole,
      reviewerRole: selected.reviewerRole,
      requiredDistinctApprovals: selected.requiredDistinctApprovals,
      requiresIndependentReviewer: selected.requiresIndependentReviewer,
      requiresReportPack: selected.requiresReportPack,
      requiresChecklistControlApprovals: selected.requiresChecklistControlApprovals,
      evidenceRequirement: selected.evidenceRequirement,
      severity: selected.severity,
      message: null,
      details: [],
      tone: "default"
    }));
  };

  const submitApprovalPolicyRule = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const selected = approvalPolicyDraft.rows.find((row) => row.policyKey === approvalPolicyRule.policyKey);
    if (!approvalPolicyDraft.canSave || !selected) {
      setApprovalPolicyRule((current) => ({
        ...current,
        message: "Choose an approval policy rule before saving.",
        details: approvalPolicyDraft.disabledReason ? [approvalPolicyDraft.disabledReason] : [],
        tone: "warning"
      }));
      return;
    }

    if (approvalPolicyRule.requiredDistinctApprovals < 1) {
      setApprovalPolicyRule((current) => ({
        ...current,
        message: "Required approvals must be at least 1.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setApprovalPolicyRule((current) => ({
      ...current,
      busy: true,
      message: null,
      details: [],
      tone: "default"
    }));

    try {
      const result = await upsertOperationsApprovalPolicyRule({
        ...selected,
        requiredPermission: approvalPolicyRule.requiredPermission,
        submitterRole: approvalPolicyRule.submitterRole,
        reviewerRole: approvalPolicyRule.reviewerRole,
        requiredDistinctApprovals: approvalPolicyRule.requiredDistinctApprovals,
        requiresIndependentReviewer: approvalPolicyRule.requiresIndependentReviewer,
        requiresReportPack: approvalPolicyRule.requiresReportPack,
        requiresChecklistControlApprovals: approvalPolicyRule.requiresChecklistControlApprovals,
        evidenceRequirement: approvalPolicyRule.evidenceRequirement,
        severity: approvalPolicyRule.severity,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: approvalPolicyRule.rationale,
        correlationId: `settings-approval-policy-${Date.now()}`
      });
      await onRefresh?.();
      setApprovalPolicyRule((current) => ({
        ...current,
        busy: false,
        message: `Approval policy saved for ${result.rule.action}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Approval policy save failed.");
      setApprovalPolicyRule((current) => ({
        ...current,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const selectCloseCalendarItem = (workflowId: string) => {
    const selected = closeCalendarDraft.items.find((item) => item.workflowId === workflowId);
    if (!selected) {
      return;
    }

    setCloseCalendarItem((current) => ({
      ...current,
      workflowId: selected.workflowId,
      taskId: selected.nextDueTaskId ?? "",
      dueDate: selected.nextDueDate ?? "",
      owner: selected.nextDueOwner ?? "",
      message: null,
      details: [],
      tone: "default"
    }));
  };

  const submitCloseCalendarItem = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const selected = closeCalendarDraft.items.find((item) => item.workflowId === closeCalendarItem.workflowId);
    if (!closeCalendarDraft.canSave || !selected) {
      setCloseCalendarItem((current) => ({
        ...current,
        message: "Choose an account close workflow before saving.",
        details: closeCalendarDraft.disabledReason ? [closeCalendarDraft.disabledReason] : [],
        tone: "warning"
      }));
      return;
    }

    if (!closeCalendarItem.taskId.trim() || !closeCalendarItem.dueDate || !closeCalendarItem.owner.trim()) {
      setCloseCalendarItem((current) => ({
        ...current,
        message: "Task, due date, owner, and rationale are required.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setCloseCalendarItem((current) => ({
      ...current,
      busy: true,
      message: null,
      details: [],
      tone: "default"
    }));

    try {
      const result = await upsertOperationsCloseCalendarItem({
        workflowId: selected.workflowId,
        taskId: closeCalendarItem.taskId,
        dueDate: closeCalendarItem.dueDate,
        owner: closeCalendarItem.owner,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: closeCalendarItem.rationale,
        correlationId: `settings-close-calendar-${Date.now()}`
      });
      await onRefresh?.();
      setCloseCalendarItem((current) => ({
        ...current,
        busy: false,
        message: `Close calendar saved for ${result.item.periodId}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Close calendar save failed.");
      setCloseCalendarItem((current) => ({
        ...current,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const selectedDraftStarterProfile = approvedAssetProfiles.find((profile) => profile.profileId === assetProfileDraft.starterProfileId) ??
    firstApprovedAssetProfile;
  const selectedProfileBackedSecurityProfile = approvedAssetProfiles.find((profile) => profile.profileId === profileBackedSecurity.profileId) ??
    firstApprovedAssetProfile;

  const selectAssetProfileStarter = (profileId: string) => {
    const selected = approvedAssetProfiles.find((profile) => profile.profileId === profileId);
    setAssetProfileDraft(createAssetProfileDraftState(selected ?? null));
  };

  const selectProfileBackedSecurityProfile = (profileId: string) => {
    const selected = approvedAssetProfiles.find((profile) => profile.profileId === profileId);
    setProfileBackedSecurity((current) => ({
      ...createProfileBackedSecurityState(selected ?? null),
      displayName: current.displayName,
      internalCode: current.internalCode,
      currency: current.currency || "USD"
    }));
  };

  const updateProfileBackedSecurityField = (fieldKey: string, value: string) => {
    setProfileBackedSecurity((current) => ({
      ...current,
      fieldValues: {
        ...current.fieldValues,
        [fieldKey]: value
      },
      message: null,
      details: [],
      tone: "default"
    }));
  };

  const submitAssetProfileDraft = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!selectedDraftStarterProfile) {
      setAssetProfileDraft((current) => ({
        ...current,
        message: "Choose an approved starter profile before drafting.",
        details: vm.assetProfileGovernancePanel.createDisabledReason ? [vm.assetProfileGovernancePanel.createDisabledReason] : [],
        tone: "warning"
      }));
      return;
    }

    const profileId = normalizeAssetProfileId(assetProfileDraft.profileId);
    const name = assetProfileDraft.name.trim();
    const category = assetProfileDraft.category.trim();
    const rationale = assetProfileDraft.rationale.trim();
    if (!profileId || !name || !category || !rationale) {
      setAssetProfileDraft((current) => ({
        ...current,
        message: "Profile id, name, category, and rationale are required.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setAssetProfileDraft((current) => ({ ...current, busyAction: "draft", message: null, details: [], tone: "default" }));
    try {
      const result = await draftSecurityAssetProfile({
        profileId,
        name,
        category,
        subType: assetProfileDraft.subType.trim() || null,
        fields: selectedDraftStarterProfile.fields,
        identifierPreferences: selectedDraftStarterProfile.identifierPreferences,
        lifecycleStates: selectedDraftStarterProfile.lifecycleStates,
        accountingImpactHints: selectedDraftStarterProfile.accountingImpactHints,
        dateOrderRules: selectedDraftStarterProfile.dateOrderRules,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale,
        correlationId: `settings-asset-profile-draft-${Date.now()}`
      });
      await onRefresh?.();
      setAssetProfileDraft((current) => ({
        ...current,
        profileId,
        name,
        category,
        busyAction: null,
        rollbackTargetVersion: result.profile.version,
        lineage: result.lineage,
        result,
        message: `Draft saved for ${result.profile.name} v${result.profile.version}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Asset profile draft failed.");
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const approveAssetProfileDraft = async () => {
    const profileId = normalizeAssetProfileId(assetProfileDraft.profileId);
    const version = assetProfileDraft.result?.profile.version ?? assetProfileDraft.rollbackTargetVersion;
    if (!profileId || version < 1) {
      setAssetProfileDraft((current) => ({
        ...current,
        message: "Draft a profile version before approval.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setAssetProfileDraft((current) => ({ ...current, busyAction: "approve", message: null, details: [], tone: "default" }));
    try {
      const result = await approveSecurityAssetProfile({
        profileId,
        version,
        effectiveFrom: todayDateOnly(),
        approvalReference: `settings:${profileId}:v${version}`,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: assetProfileDraft.rationale.trim() || "Approve asset profile for governed Security Master creation.",
        correlationId: `settings-asset-profile-approve-${Date.now()}`
      });
      await onRefresh?.();
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        lineage: result.lineage,
        result,
        message: `Approved ${result.profile.name} v${result.profile.version}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`,
          `Reference ${result.auditEvent.approvalReference ?? "n/a"}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Asset profile approval failed.");
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const loadAssetProfileLineage = async () => {
    const profileId = assetProfileDraft.starterProfileId || assetProfileDraft.profileId;
    if (!profileId) {
      return;
    }

    setAssetProfileDraft((current) => ({ ...current, busyAction: "lineage", message: null, details: [], tone: "default" }));
    try {
      const lineage = await getSecurityAssetProfileLineage(profileId);
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        lineage,
        rollbackTargetVersion: lineage.versions[0]?.version ?? current.rollbackTargetVersion,
        message: `Loaded ${lineage.versions.length} version${lineage.versions.length === 1 ? "" : "s"} for ${lineage.profileId}.`,
        details: lineage.auditEvents.slice(-3).map((event) => `Audit ${event.auditId}: ${event.eventType}`),
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Asset profile lineage load failed.");
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const rollbackAssetProfile = async () => {
    const profileId = assetProfileDraft.starterProfileId || assetProfileDraft.profileId;
    if (!profileId || assetProfileDraft.rollbackTargetVersion < 1) {
      setAssetProfileDraft((current) => ({
        ...current,
        message: "Choose a profile and target version before rollback.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setAssetProfileDraft((current) => ({ ...current, busyAction: "rollback", message: null, details: [], tone: "default" }));
    try {
      const result = await rollbackSecurityAssetProfile({
        profileId,
        targetVersion: assetProfileDraft.rollbackTargetVersion,
        effectiveFrom: todayDateOnly(),
        approvalReference: `settings:${profileId}:rollback:${assetProfileDraft.rollbackTargetVersion}`,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: assetProfileDraft.rationale.trim() || "Rollback asset profile to an approved prior version.",
        correlationId: `settings-asset-profile-rollback-${Date.now()}`
      });
      await onRefresh?.();
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        lineage: result.lineage,
        result,
        message: `Rolled back ${result.profile.name} into v${result.profile.version}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Asset profile rollback failed.");
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  const submitProfileBackedSecurity = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const selected = selectedProfileBackedSecurityProfile;
    if (!selected) {
      setProfileBackedSecurity((current) => ({
        ...current,
        message: "Choose an approved asset profile before creating a security.",
        details: vm.assetProfileGovernancePanel.createDisabledReason ? [vm.assetProfileGovernancePanel.createDisabledReason] : [],
        tone: "warning"
      }));
      return;
    }

    const missingFields = selected.fields
      .filter((field) => field.isRequired && !profileBackedSecurity.fieldValues[field.key]?.trim())
      .map((field) => field.label);
    if (!profileBackedSecurity.displayName.trim() || !profileBackedSecurity.internalCode.trim() || missingFields.length > 0) {
      setProfileBackedSecurity((current) => ({
        ...current,
        message: "Display name, internal code, and required profile fields are required.",
        details: missingFields,
        tone: "warning"
      }));
      return;
    }

    setProfileBackedSecurity((current) => ({ ...current, busy: true, message: null, details: [], tone: "default" }));
    try {
      const securityId = createBrowserGuid();
      const effectiveFrom = new Date().toISOString();
      const profileFields = buildProfileFieldPayload(selected.fields, profileBackedSecurity.fieldValues);
      const result = await createSecurityMasterEntry({
        securityId,
        assetClass: "CustomAsset",
        commonTerms: {
          displayName: profileBackedSecurity.displayName.trim(),
          currency: profileBackedSecurity.currency.trim().toUpperCase() || "USD"
        },
        assetSpecificTerms: {
          schemaVersion: 3,
          category: selected.category,
          subType: selected.subType,
          customProfileId: selected.profileId,
          profileVersion: selected.version,
          profileFields,
          profileApproval: {
            approvedBy: selected.approvedBy,
            approvedAtUtc: selected.approvedAtUtc,
            approvalReference: `profile:${selected.profileId}:v${selected.version}`
          },
          evidenceLinks: []
        },
        identifiers: [
          {
            kind: "InternalCode",
            value: profileBackedSecurity.internalCode.trim(),
            isPrimary: true,
            validFrom: effectiveFrom
          }
        ],
        effectiveFrom,
        sourceSystem: "Meridian.Settings.AssetProfiles",
        updatedBy: session?.displayName ?? "settings-screen",
        sourceRecordId: `asset-profile:${selected.profileId}:v${selected.version}`,
        reason: profileBackedSecurity.rationale.trim()
      });
      await onRefresh?.();
      setProfileBackedSecurity((current) => ({
        ...current,
        busy: false,
        message: `Security created for ${result.displayName}.`,
        details: [
          `Security ${result.securityId}`,
          `Profile ${selected.profileId} v${selected.version}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Profile-backed security creation failed.");
      setProfileBackedSecurity((current) => ({
        ...current,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  return (
    <div className="space-y-8">
      <section
        role="region"
        aria-label="Settings workbench context"
        className="panel-surface-strong flex flex-wrap items-center justify-between gap-3 px-4 py-4"
      >
        <div className="min-w-0">
          <div className="eyebrow-label">Settings lane</div>
          <h2 className="mt-2 font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            Operator control posture
          </h2>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            Session context, bootstrap health, and diagnostic reachability stay visible from one operator-facing
            control surface.
          </p>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          {vm.headerChips.map((chip) => (
            <SettingsChip key={chip.label} label={chip.label} value={chip.value} />
          ))}
        </div>
      </section>

      <section className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
        <Card
          id="profile-authentication"
          role="region"
          aria-label={vm.profileAuthenticationPanel.regionLabel}
          className={cn("panel-surface scroll-mt-6 border", diagnosticToneClass[vm.profileAuthenticationPanel.statusTone])}
        >
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <div className="eyebrow-label">Profile and authentication</div>
                <CardTitle className="mt-2 flex items-center gap-2">
                  <User className="h-5 w-5 text-primary" />
                  {vm.profileAuthenticationPanel.title}
                </CardTitle>
                <CardDescription className="mt-2">{vm.profileAuthenticationPanel.summary}</CardDescription>
              </div>
              <Badge
                variant={vm.profileAuthenticationPanel.badgeVariant}
                dot={vm.profileAuthenticationPanel.statusTone === "success"}
              >
                {vm.profileAuthenticationPanel.statusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 lg:grid-cols-[minmax(0,0.78fr)_minmax(0,1fr)]">
              <div className="rounded-md border border-border/70 bg-background/35 px-4 py-4">
                <div className="flex items-start gap-3">
                  <div
                    aria-hidden="true"
                    className="grid h-12 w-12 shrink-0 place-items-center rounded-md border border-primary/35 bg-primary/12 font-mono text-sm font-semibold text-primary"
                  >
                    {vm.profileAuthenticationPanel.avatarInitials}
                  </div>
                  <div className="min-w-0">
                    <div className="break-words text-sm font-semibold text-foreground">
                      {vm.profileAuthenticationPanel.operatorName}
                    </div>
                    <div className="mt-1 break-words text-xs text-muted-foreground">
                      {vm.profileAuthenticationPanel.roleLabel}
                    </div>
                    <div className="mt-3 flex flex-wrap gap-2">
                      <SettingsChip label="Mode" value={vm.profileAuthenticationPanel.environmentLabel} />
                      <SettingsChip label="Workspace" value={vm.profileAuthenticationPanel.workspaceLabel} />
                    </div>
                  </div>
                </div>
                <dl className="mt-4 grid gap-2">
                  <SettingsFieldRow label="Command trail" value={vm.profileAuthenticationPanel.commandCountLabel} tone="muted" />
                  <SettingsFieldRow
                    label="Authority"
                    value={vm.profileAuthenticationPanel.authorityLabel}
                    tone={vm.profileAuthenticationPanel.statusTone === "danger" ? "danger" : vm.profileAuthenticationPanel.statusTone === "warning" ? "warning" : "default"}
                  />
                </dl>
                <p className="mt-3 text-xs leading-5 text-muted-foreground">
                  {vm.profileAuthenticationPanel.authorityDetail}
                </p>
              </div>

              <div className="grid gap-3">
                <dl className="grid gap-2 sm:grid-cols-2" aria-label="Profile authentication facts">
                  {vm.profileAuthenticationPanel.facts.map((fact) => (
                    <SettingsFieldRow key={fact.id} label={fact.label} value={fact.value} tone={fact.tone} />
                  ))}
                </dl>
                {vm.profileAuthenticationPanel.notice ? (
                  <div
                    role={vm.profileAuthenticationPanel.notice.role}
                    className={cn("rounded-md border px-3 py-3", diagnosticToneClass[vm.profileAuthenticationPanel.notice.tone])}
                  >
                    <div className="text-sm font-semibold text-foreground">
                      {vm.profileAuthenticationPanel.notice.title}
                    </div>
                    <p className="mt-1 text-xs leading-5 text-muted-foreground">
                      {vm.profileAuthenticationPanel.notice.detail}
                    </p>
                  </div>
                ) : null}
              </div>
            </div>

            <div role="list" aria-label={vm.profileAuthenticationPanel.stepsAriaLabel} className="grid gap-2">
              <h3 className="text-xs font-semibold uppercase text-muted-foreground">
                {vm.profileAuthenticationPanel.stepsTitle}
              </h3>
              {vm.profileAuthenticationPanel.steps.map((step) => (
                <ProfileAuthenticationStepRow key={step.id} step={step} />
              ))}
            </div>
          </CardContent>
        </Card>

        <Card className={cn("panel-surface border", systemToneClass[vm.systemTone])}>
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <div className="eyebrow-label">System posture</div>
                <CardTitle className="mt-2 flex items-center gap-2">
                  <MonitorCheck className="h-5 w-5 text-primary" />
                  {vm.systemTitle}
                </CardTitle>
                <CardDescription className="mt-2">{vm.systemSummary}</CardDescription>
              </div>
              <Badge variant={systemVariant(vm.systemTone)} dot={vm.systemTone === "success"}>
                {overview?.systemStatus ?? "Unavailable"}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Providers" value={overview ? `${overview.providersOnline} / ${overview.providersTotal}` : "—"} />
              <SettingsChip label="Runs" value={overview ? String(overview.activeRuns) : "—"} />
              <SettingsChip label="Positions" value={overview ? String(overview.openPositions) : "—"} />
              <SettingsChip label="Storage" value={overview?.storageHealth ?? "—"} />
            </div>
            {vm.hasOverview ? (
              <dl className="grid gap-2">
                {vm.systemItems.map((item) => (
                  <SettingsFieldRow key={item.label} label={item.label} value={item.value} tone={item.tone} />
                ))}
              </dl>
            ) : (
              <p className="rounded-md border border-border/70 bg-secondary/25 px-4 py-4 text-center text-sm text-muted-foreground">
                System overview is unavailable. Check the API connection.
              </p>
            )}
          </CardContent>
        </Card>
      </section>

      <Card id="fund-operations-control-center" className="panel-surface scroll-mt-6 border border-border/70">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Configurable operations</div>
              <CardTitle className="mt-2 flex items-center gap-2 text-base">
                <ShieldCheck className="h-4 w-4 text-primary" />
                {vm.operationsControlCenter.title}
              </CardTitle>
              <CardDescription className="mt-2">{vm.operationsControlCenter.summary}</CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <SettingsChip label="Loaded" value={vm.operationsControlCenter.loadedCountLabel} />
              <SettingsChip label="Review" value={vm.operationsControlCenter.reviewCountLabel} />
              <Badge
                variant={vm.operationsControlCenter.statusVariant}
                dot={vm.operationsControlCenter.statusVariant === "success"}
              >
                {vm.operationsControlCenter.statusLabel}
              </Badge>
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4">
          <div className="grid gap-3 xl:grid-cols-4" role="list" aria-label={vm.operationsControlCenter.listLabel}>
            {vm.operationsControlCenter.cards.map((card) => (
              <article
                key={card.id}
                role="listitem"
                className={cn(
                  "grid gap-3 rounded-md border px-3 py-3",
                  diagnosticToneClass[capabilityTone(card.statusVariant === "default" ? "outline" : card.statusVariant)]
                )}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <h3 className="text-sm font-semibold text-foreground">{card.title}</h3>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{card.description}</p>
                  </div>
                  <Badge variant={card.statusVariant} className="shrink-0">
                    {card.statusLabel}
                  </Badge>
                </div>
                <dl className="grid gap-2 sm:grid-cols-2 xl:grid-cols-1 2xl:grid-cols-2">
                  {card.metrics.map((metric) => (
                    <SettingsFieldRow key={`${card.id}-${metric.label}`} label={metric.label} value={metric.value} tone={metric.tone} />
                  ))}
                </dl>
                <p className="text-xs leading-5 text-foreground/75">{card.detail}</p>
                <div className="mt-auto flex flex-wrap gap-2">
                  {card.routeHref.startsWith("/api/") ? (
                    <Button asChild variant="outline" size="sm">
                      <a href={card.routeHref} target="_blank" rel="noreferrer" aria-label={card.routeAriaLabel}>
                        {card.routeLabel}
                        <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
                      </a>
                    </Button>
                  ) : (
                    <Button asChild variant="outline" size="sm">
                      <Link to={card.routeHref} aria-label={card.routeAriaLabel}>
                        {card.routeLabel}
                        <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                      </Link>
                    </Button>
                  )}
                  <a
                    href={card.endpointHref}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex items-center gap-2 rounded-md border border-border/60 px-2 py-1 text-[11px] font-mono text-muted-foreground transition-colors hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                    aria-label={`Open raw endpoint for ${card.title}`}
                  >
                    GET
                    <ExternalLink className="h-3 w-3" aria-hidden="true" />
                  </a>
                </div>
              </article>
            ))}
          </div>
          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitLedgerMappingAssignment}
            aria-label="Assign ledger mapping"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Assign ledger mapping</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Save an account-level ledger group assignment with actor, rationale, and correlation evidence.
                </p>
              </div>
              <Badge variant={ledgerMappingDraft.canAssign ? "warning" : "outline"}>
                {ledgerMappingDraft.statusLabel}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,1.2fr)_auto]">
              <FilterSelect
                label="Account"
                value={ledgerMappingAssignment.accountId}
                onChange={(value) => setLedgerMappingAssignment((current) => ({ ...current, accountId: value, message: null }))}
                options={ledgerMappingDraft.accountOptions}
                disabled={!ledgerMappingDraft.canAssign || ledgerMappingAssignment.busy}
              />
              <FilterSelect
                label="Ledger group"
                value={ledgerMappingAssignment.ledgerGroupId}
                onChange={(value) => setLedgerMappingAssignment((current) => ({ ...current, ledgerGroupId: value, message: null }))}
                options={ledgerMappingDraft.ledgerGroupOptions}
                disabled={!ledgerMappingDraft.canAssign || ledgerMappingAssignment.busy}
              />
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={ledgerMappingAssignment.rationale}
                  onChange={(event) => setLedgerMappingAssignment((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  placeholder="Reason for the ledger mapping change"
                  disabled={!ledgerMappingDraft.canAssign || ledgerMappingAssignment.busy}
                  aria-label="Ledger mapping rationale"
                />
              </label>
              <div className="flex items-end">
                <Button
                  type="submit"
                  size="sm"
                  disabled={!ledgerMappingDraft.canAssign || ledgerMappingAssignment.busy}
                  busy={ledgerMappingAssignment.busy}
                  busyLabel="Saving mapping"
                  disabledReason={ledgerMappingDraft.disabledReason}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Save mapping
                </Button>
              </div>
            </div>
            {ledgerMappingAssignment.message ? (
              <div
                role={ledgerMappingAssignment.tone === "danger" ? "alert" : "status"}
                className={cn("rounded-md border px-3 py-2 text-xs leading-5", diagnosticToneClass[ledgerMappingAssignment.tone])}
              >
                <div className="font-semibold text-foreground">{ledgerMappingAssignment.message}</div>
                {ledgerMappingAssignment.details.length > 0 ? (
                  <ul className="mt-1 list-disc space-y-1 pl-5 text-muted-foreground">
                    {ledgerMappingAssignment.details.map((detail) => (
                      <li key={detail}>{detail}</li>
                    ))}
                  </ul>
                ) : null}
              </div>
            ) : null}
          </form>
          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitRolePermissionProfile}
            aria-label="Create role profile"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Create role profile</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Save a reusable authority profile with base role, explicit grants, actor, rationale, and audit evidence.
                </p>
              </div>
              <Badge variant={roleProfileDraft.canSave ? "warning" : "outline"}>
                {roleProfileDraft.statusLabel}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,0.9fr)_minmax(0,0.75fr)]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Profile name
                <Input
                  value={rolePermissionProfile.profileName}
                  onChange={(event) => setRolePermissionProfile((current) => ({
                    ...current,
                    profileName: event.target.value,
                    displayName: !current.displayName || current.displayName === current.profileName
                      ? event.target.value
                      : current.displayName,
                    message: null
                  }))}
                  placeholder="Month-end Close Reviewer"
                  disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
                  aria-label="Role profile name"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Display name
                <Input
                  value={rolePermissionProfile.displayName}
                  onChange={(event) => setRolePermissionProfile((current) => ({
                    ...current,
                    displayName: event.target.value,
                    message: null
                  }))}
                  placeholder="Close Reviewer"
                  disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
                  aria-label="Role profile display name"
                />
              </label>
              <FilterSelect
                label="Base role"
                value={rolePermissionProfile.baseRole}
                onChange={(value) => setRolePermissionProfile((current) => {
                  const baseRole = rolePermissionCatalog?.roles.find((role) => role.role === value);
                  return {
                    ...current,
                    baseRole: value,
                    permissionNames: baseRole?.permissions ?? current.permissionNames,
                    message: null
                  };
                })}
                options={roleProfileDraft.baseRoleOptions}
                disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
              />
            </div>
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Description
              <Input
                value={rolePermissionProfile.description}
                onChange={(event) => setRolePermissionProfile((current) => ({
                  ...current,
                  description: event.target.value,
                  message: null
                }))}
                placeholder="Scope this profile to close review and fund operations evidence."
                disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
                aria-label="Role profile description"
              />
            </label>
            <fieldset
              className="grid gap-2 rounded-md border border-border/60 bg-secondary/15 px-3 py-3"
              disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
            >
              <legend className="px-1 text-xs font-semibold text-foreground">Permissions</legend>
              <div className="grid max-h-56 gap-2 overflow-auto pr-1 sm:grid-cols-2 xl:grid-cols-3">
                {roleProfileDraft.permissionOptions.map((permission) => (
                  <label
                    key={permission.value}
                    className="flex min-w-0 items-start gap-2 rounded-sm border border-border/60 bg-background/40 px-2 py-2 text-xs text-muted-foreground"
                  >
                    <input
                      type="checkbox"
                      checked={rolePermissionProfile.permissionNames.includes(permission.value)}
                      onChange={(event) => toggleRoleProfilePermission(permission.value, event.target.checked)}
                      className="mt-0.5 h-4 w-4 shrink-0 accent-[hsl(var(--primary))]"
                    />
                    <span className="min-w-0">
                      <span className="block font-medium text-foreground">{permission.label}</span>
                      <span className="block break-words text-[11px] leading-4">{permission.group}</span>
                    </span>
                  </label>
                ))}
              </div>
            </fieldset>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={rolePermissionProfile.rationale}
                  onChange={(event) => setRolePermissionProfile((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  placeholder="Reason for the authority-profile change"
                  disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
                  aria-label="Role profile rationale"
                />
              </label>
              <div className="flex items-end">
                <Button
                  type="submit"
                  size="sm"
                  disabled={!roleProfileDraft.canSave || rolePermissionProfile.busy}
                  busy={rolePermissionProfile.busy}
                  busyLabel="Saving role profile"
                  disabledReason={roleProfileDraft.disabledReason}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Save profile
                </Button>
              </div>
            </div>
            {rolePermissionProfile.message ? (
              <div
                role={rolePermissionProfile.tone === "danger" ? "alert" : "status"}
                className={cn("rounded-md border px-3 py-2 text-xs leading-5", diagnosticToneClass[rolePermissionProfile.tone])}
              >
                <div className="font-semibold text-foreground">{rolePermissionProfile.message}</div>
                {rolePermissionProfile.details.length > 0 ? (
                  <ul className="mt-1 list-disc space-y-1 pl-5 text-muted-foreground">
                    {rolePermissionProfile.details.map((detail) => (
                      <li key={detail}>{detail}</li>
                    ))}
                  </ul>
                ) : null}
              </div>
            ) : null}
          </form>
          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitApprovalPolicyRule}
            aria-label="Configure approval policy rule"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Configure approval policy</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Update close approval rules with reviewer separation, required evidence, distinct approvals, and audit rationale.
                </p>
              </div>
              <Badge variant={approvalPolicyDraft.canSave ? "warning" : "outline"}>
                {approvalPolicyDraft.statusLabel}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1.2fr)_minmax(0,0.8fr)_minmax(0,0.8fr)_minmax(0,0.55fr)]">
              <FilterSelect
                label="Policy rule"
                value={approvalPolicyRule.policyKey}
                onChange={selectApprovalPolicyRule}
                options={approvalPolicyDraft.policyOptions}
                disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
              />
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Required permission
                <Input
                  value={approvalPolicyRule.requiredPermission}
                  onChange={(event) => setApprovalPolicyRule((current) => ({ ...current, requiredPermission: event.target.value, message: null }))}
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy required permission"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Reviewer role
                <Input
                  value={approvalPolicyRule.reviewerRole}
                  onChange={(event) => setApprovalPolicyRule((current) => ({ ...current, reviewerRole: event.target.value, message: null }))}
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy reviewer role"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Approvals
                <Input
                  type="number"
                  min={1}
                  value={String(approvalPolicyRule.requiredDistinctApprovals)}
                  onChange={(event) => setApprovalPolicyRule((current) => ({
                    ...current,
                    requiredDistinctApprovals: Math.max(0, Number.parseInt(event.target.value, 10) || 0),
                    message: null
                  }))}
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy required distinct approvals"
                />
              </label>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,0.8fr)_minmax(0,0.8fr)_minmax(0,1.2fr)]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Submitter role
                <Input
                  value={approvalPolicyRule.submitterRole}
                  onChange={(event) => setApprovalPolicyRule((current) => ({ ...current, submitterRole: event.target.value, message: null }))}
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy submitter role"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Severity
                <Input
                  value={approvalPolicyRule.severity}
                  onChange={(event) => setApprovalPolicyRule((current) => ({ ...current, severity: event.target.value, message: null }))}
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy severity"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Evidence requirement
                <Input
                  value={approvalPolicyRule.evidenceRequirement}
                  onChange={(event) => setApprovalPolicyRule((current) => ({ ...current, evidenceRequirement: event.target.value, message: null }))}
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy evidence requirement"
                />
              </label>
            </div>
            <div className="flex flex-wrap gap-2">
              {([
                {
                  key: "requiresIndependentReviewer",
                  label: "Independent reviewer",
                  checked: approvalPolicyRule.requiresIndependentReviewer
                },
                {
                  key: "requiresReportPack",
                  label: "Report pack",
                  checked: approvalPolicyRule.requiresReportPack
                },
                {
                  key: "requiresChecklistControlApprovals",
                  label: "Checklist control approvals",
                  checked: approvalPolicyRule.requiresChecklistControlApprovals
                }
              ] satisfies Array<{
                key: "requiresIndependentReviewer" | "requiresReportPack" | "requiresChecklistControlApprovals";
                label: string;
                checked: boolean;
              }>).map((option) => (
                <label
                  key={option.key}
                  className="flex items-center gap-2 rounded-md border border-border/60 bg-secondary/20 px-2 py-2 text-xs text-muted-foreground"
                >
                  <input
                    type="checkbox"
                    checked={option.checked}
                    onChange={(event) => setApprovalPolicyRule((current) => ({
                      ...current,
                      [option.key]: event.target.checked,
                      message: null
                    }))}
                    className="h-4 w-4 shrink-0 accent-[hsl(var(--primary))]"
                    disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  />
                  <span>{option.label}</span>
                </label>
              ))}
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={approvalPolicyRule.rationale}
                  onChange={(event) => setApprovalPolicyRule((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  placeholder="Reason for the approval policy change"
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  aria-label="Approval policy rationale"
                />
              </label>
              <div className="flex items-end">
                <Button
                  type="submit"
                  size="sm"
                  disabled={!approvalPolicyDraft.canSave || approvalPolicyRule.busy}
                  busy={approvalPolicyRule.busy}
                  busyLabel="Saving approval policy"
                  disabledReason={approvalPolicyDraft.disabledReason}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Save policy
                </Button>
              </div>
            </div>
            {approvalPolicyRule.message ? (
              <div
                role={approvalPolicyRule.tone === "danger" ? "alert" : "status"}
                className={cn("rounded-md border px-3 py-2 text-xs leading-5", diagnosticToneClass[approvalPolicyRule.tone])}
              >
                <div className="font-semibold text-foreground">{approvalPolicyRule.message}</div>
                {approvalPolicyRule.details.length > 0 ? (
                  <ul className="mt-1 list-disc space-y-1 pl-5 text-muted-foreground">
                    {approvalPolicyRule.details.map((detail) => (
                      <li key={detail}>{detail}</li>
                    ))}
                  </ul>
                ) : null}
              </div>
            ) : null}
          </form>
          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitCloseCalendarItem}
            aria-label="Configure account close calendar"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Configure account close calendar</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Assign the next close task owner and due date with an audit rationale.
                </p>
              </div>
              <Badge variant={closeCalendarDraft.canSave ? "warning" : "outline"}>
                {closeCalendarDraft.statusLabel}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1.2fr)_minmax(0,0.8fr)_minmax(0,0.65fr)_minmax(0,0.85fr)]">
              <FilterSelect
                label="Close workflow"
                value={closeCalendarItem.workflowId}
                onChange={selectCloseCalendarItem}
                options={closeCalendarDraft.workflowOptions}
                disabled={!closeCalendarDraft.canSave || closeCalendarItem.busy}
              />
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Task
                <Input
                  value={closeCalendarItem.taskId}
                  onChange={(event) => setCloseCalendarItem((current) => ({ ...current, taskId: event.target.value, message: null }))}
                  disabled={!closeCalendarDraft.canSave || closeCalendarItem.busy}
                  aria-label="Close calendar task"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Due date
                <Input
                  type="date"
                  value={closeCalendarItem.dueDate}
                  onChange={(event) => setCloseCalendarItem((current) => ({ ...current, dueDate: event.target.value, message: null }))}
                  disabled={!closeCalendarDraft.canSave || closeCalendarItem.busy}
                  aria-label="Close calendar due date"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Owner
                <Input
                  value={closeCalendarItem.owner}
                  onChange={(event) => setCloseCalendarItem((current) => ({ ...current, owner: event.target.value, message: null }))}
                  disabled={!closeCalendarDraft.canSave || closeCalendarItem.busy}
                  aria-label="Close calendar owner"
                />
              </label>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={closeCalendarItem.rationale}
                  onChange={(event) => setCloseCalendarItem((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  placeholder="Reason for the calendar change"
                  disabled={!closeCalendarDraft.canSave || closeCalendarItem.busy}
                  aria-label="Close calendar rationale"
                />
              </label>
              <div className="flex items-end">
                <Button
                  type="submit"
                  size="sm"
                  disabled={!closeCalendarDraft.canSave || closeCalendarItem.busy}
                  busy={closeCalendarItem.busy}
                  busyLabel="Saving close calendar"
                  disabledReason={closeCalendarDraft.disabledReason}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Save calendar
                </Button>
              </div>
            </div>
            {closeCalendarItem.message ? (
              <div
                role={closeCalendarItem.tone === "danger" ? "alert" : "status"}
                className={cn("rounded-md border px-3 py-2 text-xs leading-5", diagnosticToneClass[closeCalendarItem.tone])}
              >
                <div className="font-semibold text-foreground">{closeCalendarItem.message}</div>
                {closeCalendarItem.details.length > 0 ? (
                  <ul className="mt-1 list-disc space-y-1 pl-5 text-muted-foreground">
                    {closeCalendarItem.details.map((detail) => (
                      <li key={detail}>{detail}</li>
                    ))}
                  </ul>
                ) : null}
              </div>
            ) : null}
          </form>
        </CardContent>
      </Card>

      <Card id="asset-profile-governance" className="panel-surface scroll-mt-6 border border-border/70">
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Security Master</div>
              <CardTitle className="mt-2 flex items-center gap-2 text-base">
                <GitBranch className="h-4 w-4 text-primary" />
                {vm.assetProfileGovernancePanel.title}
              </CardTitle>
              <CardDescription className="mt-2">{vm.assetProfileGovernancePanel.summary}</CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <SettingsChip label="Approved" value={vm.assetProfileGovernancePanel.approvedCountLabel} />
              <SettingsChip label="Projected" value={vm.assetProfileGovernancePanel.projectedFieldCountLabel} />
              <SettingsChip label="Close IDs" value={vm.assetProfileGovernancePanel.closeIdentifierCountLabel} />
              <Badge variant={vm.assetProfileGovernancePanel.statusVariant} dot={vm.assetProfileGovernancePanel.statusVariant === "success"}>
                {vm.assetProfileGovernancePanel.statusLabel}
              </Badge>
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4">
          <div className="grid gap-3 xl:grid-cols-5" role="list" aria-label={vm.assetProfileGovernancePanel.listLabel}>
            {vm.assetProfileGovernancePanel.rows.map((row) => (
              <article
                key={`${row.profileId}-${row.versionLabel}`}
                role="listitem"
                className={cn("grid gap-3 rounded-md border px-3 py-3", diagnosticToneClass[capabilityTone(row.statusVariant)])}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <h3 className="text-sm font-semibold text-foreground">{row.name}</h3>
                    <p className="mt-1 break-words font-mono text-[11px] text-muted-foreground">{row.profileId}</p>
                  </div>
                  <Badge variant={row.statusVariant}>{row.versionLabel}</Badge>
                </div>
                <dl className="grid gap-2">
                  <SettingsFieldRow label="Category" value={row.categoryLabel} tone="default" />
                  <SettingsFieldRow label="Fields" value={row.fieldCountLabel} tone="muted" />
                  <SettingsFieldRow label="Projection" value={row.projectedFieldLabel} tone="muted" />
                  <SettingsFieldRow label="Close ID" value={row.requiredCloseIdentifierLabel} tone="warning" />
                </dl>
                <p className="text-xs leading-5 text-foreground/75">{row.accountingImpactLabel}</p>
              </article>
            ))}
          </div>

          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitAssetProfileDraft}
            aria-label="Draft asset profile"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Draft asset profile</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Copy approved no-code fields, close identifiers, lifecycle states, and accounting hints into a governed draft.
                </p>
              </div>
              <Badge variant={selectedDraftStarterProfile ? "warning" : "outline"}>
                {assetProfileDraft.busyAction ? "Working" : selectedDraftStarterProfile ? "Draft ready" : "Unavailable"}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)_minmax(0,1fr)_minmax(0,0.8fr)]">
              <FilterSelect
                label="Starter"
                value={assetProfileDraft.starterProfileId}
                onChange={selectAssetProfileStarter}
                options={approvedAssetProfiles.map((profile) => ({ value: profile.profileId, label: profile.name }))}
                disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
              />
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Profile id
                <Input
                  value={assetProfileDraft.profileId}
                  onChange={(event) => setAssetProfileDraft((current) => ({ ...current, profileId: event.target.value, message: null }))}
                  disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                  aria-label="Asset profile id"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Name
                <Input
                  value={assetProfileDraft.name}
                  onChange={(event) => setAssetProfileDraft((current) => ({ ...current, name: event.target.value, message: null }))}
                  disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                  aria-label="Asset profile name"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Category
                <Input
                  value={assetProfileDraft.category}
                  onChange={(event) => setAssetProfileDraft((current) => ({ ...current, category: event.target.value, message: null }))}
                  disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                  aria-label="Asset profile category"
                />
              </label>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,0.75fr)_minmax(0,1fr)_auto_auto_auto]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Subtype
                <Input
                  value={assetProfileDraft.subType}
                  onChange={(event) => setAssetProfileDraft((current) => ({ ...current, subType: event.target.value, message: null }))}
                  disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                  aria-label="Asset profile subtype"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={assetProfileDraft.rationale}
                  onChange={(event) => setAssetProfileDraft((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                  aria-label="Asset profile rationale"
                />
              </label>
              <div className="flex items-end">
                <Button type="submit" size="sm" disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null} busy={assetProfileDraft.busyAction === "draft"} busyLabel="Saving draft">
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Save draft
                </Button>
              </div>
              <div className="flex items-end">
                <Button type="button" variant="outline" size="sm" onClick={approveAssetProfileDraft} disabled={assetProfileDraft.busyAction !== null || !assetProfileDraft.result} busy={assetProfileDraft.busyAction === "approve"} busyLabel="Approving">
                  <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
                  Approve
                </Button>
              </div>
              <div className="flex items-end">
                <Button type="button" variant="outline" size="sm" onClick={loadAssetProfileLineage} disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null} busy={assetProfileDraft.busyAction === "lineage"} busyLabel="Loading lineage">
                  <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
                  Lineage
                </Button>
              </div>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,0.35fr)_auto_minmax(0,1fr)]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rollback version
                <Input
                  type="number"
                  min={1}
                  value={String(assetProfileDraft.rollbackTargetVersion)}
                  onChange={(event) => setAssetProfileDraft((current) => ({
                    ...current,
                    rollbackTargetVersion: Math.max(1, Number.parseInt(event.target.value, 10) || 1),
                    message: null
                  }))}
                  disabled={assetProfileDraft.busyAction !== null}
                  aria-label="Asset profile rollback target version"
                />
              </label>
              <div className="flex items-end">
                <Button type="button" variant="outline" size="sm" onClick={rollbackAssetProfile} disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null} busy={assetProfileDraft.busyAction === "rollback"} busyLabel="Rolling back">
                  <GitBranch className="h-3.5 w-3.5" aria-hidden="true" />
                  Rollback
                </Button>
              </div>
              <div className="flex items-end text-xs text-muted-foreground">
                {assetProfileDraft.lineage ? `${assetProfileDraft.lineage.versions.length} lineage version${assetProfileDraft.lineage.versions.length === 1 ? "" : "s"}` : "Lineage not loaded"}
              </div>
            </div>
            {assetProfileDraft.message ? (
              <div
                role={assetProfileDraft.tone === "danger" ? "alert" : "status"}
                className={cn("rounded-md border px-3 py-2 text-xs leading-5", diagnosticToneClass[assetProfileDraft.tone])}
              >
                <div className="font-semibold text-foreground">{assetProfileDraft.message}</div>
                {assetProfileDraft.details.length > 0 ? (
                  <ul className="mt-1 list-disc space-y-1 pl-5 text-muted-foreground">
                    {assetProfileDraft.details.map((detail) => <li key={detail}>{detail}</li>)}
                  </ul>
                ) : null}
              </div>
            ) : null}
          </form>

          <form
            className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
            onSubmit={submitProfileBackedSecurity}
            aria-label="Create profile-backed security"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-foreground">Create profile-backed security</h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Pin the Security Master record to the approved profile version used at creation.
                </p>
              </div>
              <Badge variant={selectedProfileBackedSecurityProfile ? "success" : "outline"}>
                {selectedProfileBackedSecurityProfile ? `v${selectedProfileBackedSecurityProfile.version}` : "No profile"}
              </Badge>
            </div>
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,0.75fr)_minmax(0,0.45fr)]">
              <FilterSelect
                label="Profile"
                value={profileBackedSecurity.profileId}
                onChange={selectProfileBackedSecurityProfile}
                options={approvedAssetProfiles.map((profile) => ({ value: profile.profileId, label: profile.name }))}
                disabled={!vm.assetProfileGovernancePanel.canCreateSecurity || profileBackedSecurity.busy}
              />
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Display name
                <Input
                  value={profileBackedSecurity.displayName}
                  onChange={(event) => setProfileBackedSecurity((current) => ({ ...current, displayName: event.target.value, message: null }))}
                  disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                  aria-label="Profile-backed security display name"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Internal code
                <Input
                  value={profileBackedSecurity.internalCode}
                  onChange={(event) => setProfileBackedSecurity((current) => ({ ...current, internalCode: event.target.value, message: null }))}
                  disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                  aria-label="Profile-backed security internal code"
                />
              </label>
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Currency
                <Input
                  value={profileBackedSecurity.currency}
                  onChange={(event) => setProfileBackedSecurity((current) => ({ ...current, currency: event.target.value.toUpperCase(), message: null }))}
                  disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                  aria-label="Profile-backed security currency"
                />
              </label>
            </div>
            {selectedProfileBackedSecurityProfile ? (
              <div className="grid gap-3 lg:grid-cols-3">
                {selectedProfileBackedSecurityProfile.fields.map((field) => (
                  <AssetProfileFieldInput
                    key={field.key}
                    field={field}
                    value={profileBackedSecurity.fieldValues[field.key] ?? ""}
                    disabled={profileBackedSecurity.busy}
                    onChange={(value) => updateProfileBackedSecurityField(field.key, value)}
                  />
                ))}
              </div>
            ) : null}
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto]">
              <label className="grid gap-1 text-xs font-medium text-muted-foreground">
                Rationale
                <Input
                  value={profileBackedSecurity.rationale}
                  onChange={(event) => setProfileBackedSecurity((current) => ({ ...current, rationale: event.target.value, message: null }))}
                  disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                  aria-label="Profile-backed security rationale"
                />
              </label>
              <div className="flex items-end">
                <Button
                  type="submit"
                  size="sm"
                  disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                  busy={profileBackedSecurity.busy}
                  busyLabel="Creating security"
                  disabledReason={vm.assetProfileGovernancePanel.createDisabledReason ?? undefined}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden="true" />
                  Create security
                </Button>
              </div>
            </div>
            {profileBackedSecurity.message ? (
              <div
                role={profileBackedSecurity.tone === "danger" ? "alert" : "status"}
                className={cn("rounded-md border px-3 py-2 text-xs leading-5", diagnosticToneClass[profileBackedSecurity.tone])}
              >
                <div className="font-semibold text-foreground">{profileBackedSecurity.message}</div>
                {profileBackedSecurity.details.length > 0 ? (
                  <ul className="mt-1 list-disc space-y-1 pl-5 text-muted-foreground">
                    {profileBackedSecurity.details.map((detail) => <li key={detail}>{detail}</li>)}
                  </ul>
                ) : null}
              </div>
            ) : null}
          </form>
        </CardContent>
      </Card>

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
                {inlineProviderManagementEnabled
                  ? " Inline credential editing, verification, and runtime impact checks are enabled."
                  : " Inline editing is disabled by capability flag."}
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
              <Badge
                variant={vm.providerConnectionCenter.statusVariant}
                dot={vm.providerConnectionCenter.statusVariant === "success"}
              >
                {vm.providerConnectionCenter.statusLabel}
              </Badge>
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4">
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
                  { value: "data", label: "Data" }
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
          <div className="grid gap-4 xl:grid-cols-2">
          {filteredProviderGroups.map((group) => (
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
                      {inlineProviderManagementEnabled ? (
                        <ProviderInlineActionPanel
                          row={row}
                          state={providerInlineState[row.providerId] ?? createProviderInlineState(row)}
                          fieldDefinitions={providerFieldDefinitions[row.providerId] ?? buildProviderFieldDefinitions(row)}
                          onToggleEdit={() => toggleProviderEdit(row.providerId)}
                          onFieldChange={(field, value) => updateProviderField(row.providerId, field, value)}
                          onEnvironmentChange={(value) => updateProviderEnvironment(row.providerId, value)}
                          onLiveAcknowledgementChange={(value) => updateProviderLiveAcknowledgement(row.providerId, value)}
                          onTest={() => void runProviderTest(row.providerId)}
                          onSave={() => void saveProviderDraft(row)}
                          onVerify={() => void runProviderVerification(row.providerId)}
                          onClear={() => void clearProviderCredentials(row.providerId)}
                        />
                      ) : null}
                      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                        <div className="min-w-0">
                          <div className="flex flex-wrap items-center gap-2">
                            <h4 className="text-sm font-semibold text-foreground">{row.displayName}</h4>
                            <Badge variant="outline">{row.capabilityLabel}</Badge>
                            <Badge variant={toneVariant(row.healthTone)} dot={row.healthTone === "success"}>
                              {row.healthLabel}
                            </Badge>
                            {inlineProviderManagementEnabled ? (
                              <Badge variant={providerDraftStatusVariant(providerInlineState[row.providerId], row)}>
                                {providerDraftStatusLabel(providerInlineState[row.providerId], row)}
                              </Badge>
                            ) : null}
                          </div>
                          <p className="mt-2 text-xs leading-5 text-muted-foreground">{row.recommendedAction}</p>
                        </div>
                        <div className="flex flex-wrap items-center gap-2">
                          <Button asChild variant="outline" size="sm" className="shrink-0">
                            <Link to={row.actionHref} aria-label={row.actionAriaLabel}>
                              {row.actionLabel}
                              <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                            </Link>
                          </Button>
                        </div>
                      </div>
                      <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                        <SettingsFieldRow label="Credential" value={row.credentialLabel} tone={row.credentialTone} />
                        <SettingsFieldRow label="Verification" value={row.verificationLabel} tone={row.credentialTone} />
                        <SettingsFieldRow label="Source" value={row.sourceLabel} tone="muted" />
                        <SettingsFieldRow label="Environment" value={row.environmentLabel} tone="muted" />
                        <SettingsFieldRow label="Masked key" value={row.maskedKeyPreviewLabel} tone="muted" />
                        <SettingsFieldRow label="Last good heartbeat" value={row.lastHeartbeatLabel} tone="muted" />
                        <SettingsFieldRow label="Failover" value={row.fallbackLabel} tone={row.fallbackLabel === "Fallback active" ? "warning" : "muted"} />
                        <SettingsFieldRow label="Routing bindings" value={row.routingBindingsLabel} tone="muted" />
                        <SettingsFieldRow label="Trust score" value={row.trustScoreLabel} tone={row.healthTone} />
                        <SettingsFieldRow label="Production gate" value={row.productionStateLabel} tone={row.productionStateLabel === "Production ready" ? "success" : "warning"} />
                        <SettingsFieldRow label="Affected workflows" value={row.affectedWorkflowsLabel} tone="default" />
                      </dl>
                      {inlineProviderManagementEnabled ? (
                        <ProviderReadinessChecklist
                          row={row}
                          state={providerInlineState[row.providerId] ?? createProviderInlineState(row)}
                          fieldDefinitions={providerFieldDefinitions[row.providerId] ?? buildProviderFieldDefinitions(row)}
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

      <Card
        id="alpaca-provider-setup"
        className={cn("panel-surface scroll-mt-6 border", diagnosticToneClass[vm.alpacaConnectionPanel.statusTone])}
      >
        <CardHeader>
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <div className="eyebrow-label">Brokerage connection</div>
              <CardTitle className="mt-2 flex items-center gap-2 text-base">
                <KeyRound className="h-4 w-4 text-primary" />
                Alpaca paper API keys
              </CardTitle>
              <CardDescription className="mt-2">{vm.alpacaConnectionPanel.statusDetail}</CardDescription>
            </div>
            <Badge variant={vm.alpacaConnectionPanel.badgeVariant} dot={vm.alpacaConnectionPanel.statusTone === "success"}>
              {vm.alpacaConnectionPanel.stateLabel}
            </Badge>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,0.8fr)]">
          <form className="grid gap-3" onSubmit={alpacaForm.connect} noValidate aria-describedby={alpacaForm.formPanelId}>
            <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_10rem]">
              <AlpacaCredentialField
                field={alpacaForm.keyIdField}
                value={alpacaForm.keyId}
                onValueChange={alpacaForm.setKeyId}
                leadingIcon={<KeyRound className="h-4 w-4" />}
              />
              <AlpacaCredentialField
                field={alpacaForm.secretKeyField}
                value={alpacaForm.secretKey}
                onValueChange={alpacaForm.setSecretKey}
                leadingIcon={<ShieldCheck className="h-4 w-4" />}
              />
              <fieldset className="grid gap-1 text-xs font-medium text-muted-foreground">
                <legend>{alpacaForm.environmentLegend}</legend>
                <div
                  className="grid gap-2 sm:grid-cols-2"
                  role="radiogroup"
                  aria-label={alpacaForm.environmentLegend}
                  aria-describedby={`${alpacaForm.fieldHelpIds.environment} ${alpacaForm.formPanelId}`}
                >
                  {alpacaForm.environmentOptions.map((option) => (
                    <label
                      key={option.id}
                      htmlFor={option.id}
                      className={cn(
                        "relative grid min-h-[4.75rem] cursor-pointer gap-1 rounded-md border px-3 py-2 transition-colors",
                        option.isSelected ? environmentOptionClass[option.tone].selected : environmentOptionClass[option.tone].idle,
                        option.disabled && "cursor-not-allowed opacity-60"
                      )}
                    >
                      <input
                        id={option.id}
                        type="radio"
                        name="alpaca-environment"
                        value={option.value}
                        checked={option.isSelected}
                        disabled={option.disabled}
                        onChange={() => alpacaForm.setEnvironment(option.value)}
                        aria-label={option.ariaLabel}
                        aria-describedby={joinDescribedByIds(
                          option.descriptionId,
                          alpacaForm.fieldHelpIds.environment,
                          option.disabledReasonId,
                          alpacaForm.formPanelId
                        )}
                        className="peer sr-only"
                      />
                      <span className="pointer-events-none absolute inset-0 rounded-md peer-focus-visible:ring-2 peer-focus-visible:ring-primary/40" aria-hidden="true" />
                      <span className="flex items-center justify-between gap-2">
                        <span className="font-semibold text-foreground">{option.label}</span>
                        <span className={cn("rounded-sm border px-2 py-0.5 font-mono text-[10px] uppercase", environmentOptionClass[option.tone].badge)}>
                          {option.badgeLabel}
                        </span>
                      </span>
                      <span id={option.descriptionId} className="text-[11px] font-normal leading-4 text-muted-foreground">
                        {option.description}
                      </span>
                      <span className="break-all font-mono text-[10px] font-normal leading-4 text-muted-foreground">
                        {option.endpointLabel}
                      </span>
                    </label>
                  ))}
                </div>
                <FieldSupportText
                  helpId={alpacaForm.fieldHelpIds.environment}
                  helpText={alpacaForm.environmentHelpText}
                  helpClassName="text-[11px] leading-4"
                  disabledReason={alpacaForm.environmentOptions[0]?.disabledReason}
                  disabledReasonId={alpacaForm.environmentOptions[0]?.disabledReasonId ?? undefined}
                  disabledReasonClassName="text-[11px] leading-4"
                />
              </fieldset>
            </div>
            {alpacaForm.liveAcknowledgement.visible ? (
              <label
                htmlFor={alpacaForm.liveAcknowledgement.id}
                className={cn(
                  "flex items-start gap-3 rounded-md border border-live-env/35 bg-live-env/10 px-3 py-3 text-sm text-live-env",
                  alpacaForm.liveAcknowledgement.disabled && "opacity-60"
                )}
              >
                <input
                  id={alpacaForm.liveAcknowledgement.id}
                  type="checkbox"
                  checked={alpacaForm.liveAcknowledgement.checked}
                  disabled={alpacaForm.liveAcknowledgement.disabled}
                  required={alpacaForm.liveAcknowledgement.required}
                  onChange={(event) => alpacaForm.setLiveAcknowledged(event.target.checked)}
                  aria-label={alpacaForm.liveAcknowledgement.ariaLabel}
                  aria-describedby={joinDescribedByIds(
                    alpacaForm.liveAcknowledgement.descriptionId,
                    alpacaForm.liveAcknowledgement.disabledReasonId,
                    alpacaForm.formPanelId
                  )}
                  className="mt-0.5 h-4 w-4 shrink-0 accent-[hsl(var(--live-env))]"
                />
                <span className="min-w-0">
                  <span className="block font-semibold text-foreground">{alpacaForm.liveAcknowledgement.label}</span>
                  <span id={alpacaForm.liveAcknowledgement.descriptionId} className="mt-1 block text-xs leading-5 text-muted-foreground">
                    {alpacaForm.liveAcknowledgement.detail}
                  </span>
                  <FieldSupportText
                    disabledReason={alpacaForm.liveAcknowledgement.disabledReason}
                    disabledReasonId={alpacaForm.liveAcknowledgement.disabledReasonId ?? undefined}
                    disabledReasonClassName="mt-1 block"
                  />
                </span>
              </label>
            ) : null}
            <div
              id={alpacaForm.formPanelId}
              role={alpacaForm.formPanelRole}
              aria-live={alpacaForm.formPanelAriaLive}
              className={cn("rounded-md border px-3 py-3", diagnosticToneClass[alpacaForm.formPanelTone])}
            >
              <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                <div className="min-w-0">
                  <div className={cn("text-sm font-semibold", formReadinessTextClass[alpacaForm.formPanelTone])}>
                    {alpacaForm.formPanelTitle}
                  </div>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{alpacaForm.formPanelDetail}</p>
                </div>
                <div className="flex flex-wrap gap-2" aria-label="Alpaca credential requirements">
                  {alpacaForm.requirements.map((requirement) => (
                    <span
                      key={requirement.id}
                      className={cn("inline-flex items-center gap-2 rounded-sm border px-2 py-1 text-[11px] font-medium", requirementToneClass[requirement.tone])}
                    >
                      <span className="text-muted-foreground">{requirement.label}</span>
                      <span className="font-mono">{requirement.value}</span>
                    </span>
                  ))}
                </div>
              </div>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Button
                type="submit"
                size="sm"
                disabled={!alpacaForm.canSubmit}
                busy={alpacaForm.submitBusy}
                busyLabel="Testing Alpaca"
                disabledReason={alpacaForm.submitDisabledReason}
              >
                <ShieldCheck className="h-4 w-4" aria-hidden="true" />
                {alpacaForm.submitLabel}
              </Button>
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={alpacaForm.clear}
                disabled={alpacaForm.clearDisabledReason !== null}
                busy={alpacaForm.clearBusy}
                busyLabel="Clearing Alpaca"
                disabledReason={alpacaForm.clearDisabledReason}
              >
                <Trash2 className="h-4 w-4" aria-hidden="true" />
                {alpacaForm.clearLabel}
              </Button>
              {alpacaForm.actionMessage ? (
                <div aria-live={alpacaForm.statusRole === "alert" ? "assertive" : "polite"} className={alpacaForm.statusClassName}>
                  <div>{alpacaForm.actionMessage}</div>
                  {alpacaForm.statusDetails.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                      {alpacaForm.statusDetails.map((detail) => (
                        <li key={detail}>{detail}</li>
                      ))}
                    </ul>
                  ) : null}
                </div>
              ) : null}
            </div>
          </form>

          <div className="grid gap-2">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Provider" value={vm.alpacaConnectionPanel.providerLabel} />
              <SettingsChip label="Environment" value={vm.alpacaConnectionPanel.environmentLabel} />
            </div>
            <dl className="grid gap-2">
              <SettingsFieldRow label="Key ID" value={vm.alpacaConnectionPanel.maskedKeyIdLabel} tone="muted" />
              <SettingsFieldRow label="Account" value={vm.alpacaConnectionPanel.accountLabel} tone={vm.alpacaConnectionPanel.statusTone === "success" ? "success" : "muted"} />
              <SettingsFieldRow label="Verified" value={vm.alpacaConnectionPanel.verifiedAtLabel} tone="muted" />
            </dl>
            {vm.alpacaConnectionPanel.warnings.length > 0 ? (
              <div className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning">
                {vm.alpacaConnectionPanel.warnings[0]}
              </div>
            ) : null}
            <div
              role="list"
              aria-label={vm.alpacaConnectionPanel.setupChecklistAriaLabel}
              className="grid gap-2"
            >
              <div className="min-w-0">
                <h3 className="text-xs font-semibold uppercase text-muted-foreground">
                  {vm.alpacaConnectionPanel.setupChecklistTitle}
                </h3>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  {vm.alpacaConnectionPanel.setupChecklistDetail}
                </p>
              </div>
              {vm.alpacaConnectionPanel.setupChecklist.map((step) => (
                <div
                  key={step.id}
                  role="listitem"
                  className={cn("rounded-md border px-3 py-2", setupStepToneClass[step.tone])}
                >
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="text-sm font-medium text-foreground">{step.label}</div>
                      <p className="mt-1 text-xs leading-5 text-muted-foreground">{step.detail}</p>
                    </div>
                    <Badge variant={step.badgeVariant} className="shrink-0">
                      {step.statusLabel}
                    </Badge>
                  </div>
                  {step.actionHref && step.actionLabel ? (
                    <Button asChild variant="outline" size="sm" className="mt-3">
                      <Link to={step.actionHref} aria-label={step.actionAriaLabel ?? step.actionLabel}>
                        {step.actionLabel}
                        <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                      </Link>
                    </Button>
                  ) : null}
                </div>
              ))}
            </div>
          </div>
        </CardContent>
      </Card>

      <section className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
        <Card id="diagnostic-endpoints" className="panel-surface scroll-mt-6">
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <div className="eyebrow-label">Event posture</div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Activity className="h-4 w-4 text-primary" />
                  {vm.recentEventsSection.title}
                </CardTitle>
                <CardDescription className="mt-2">{vm.recentEventsSection.description}</CardDescription>
              </div>
              <Badge variant={recentEventsVariant(vm.recentEventsSection.state)} dot={vm.recentEventsSection.state === "ready"}>
                {vm.recentEventsSection.statusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Count" value={vm.recentEventsSection.countLabel} />
              <SettingsChip label="Heartbeat" value={overview?.lastHeartbeatUtc ?? "—"} />
              <SettingsChip label="Stream" value={vm.recentEventsSection.state} />
            </div>
            {recentEventsVm.rows.length > 0 ? (
              <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_minmax(260px,0.48fr)]">
                <DenseDataTable
                  columns={recentEventColumns}
                  rows={recentEventsVm.rows}
                  getRowId={(event) => event.id}
                  getRowAriaLabel={(event) => event.ariaLabel}
                  getRowSelectAriaLabel={(event) => event.selectAriaLabel}
                  getRowAriaControls={(event) => event.detailPanelId}
                  getRowAriaExpanded={(event) => event.expanded}
                  getRowClassName={(event) => eventToneClass[event.tone]}
                  onRowSelect={(event) => recentEventsVm.selectRow(event.id)}
                  selectedRowId={recentEventsVm.selectedRowId}
                  emptyText={vm.recentEventsSection.statusDetail}
                  ariaLabel={recentEventsVm.tableLabel}
                  caption={recentEventsVm.tableCaption}
                />
                <RecentEventDetailPanel
                  id={recentEventsVm.detailPanelId}
                  title={recentEventsVm.detailPanelTitle}
                  description={recentEventsVm.detailPanelDescription}
                  emptyText={recentEventsVm.detailPanelEmptyText}
                  ariaLabel={recentEventsVm.detailPanelAriaLabel}
                  detail={recentEventsVm.selectedDetail}
                />
              </div>
            ) : (
              <div
                role={vm.recentEventsSection.state === "unavailable" ? "alert" : "status"}
                className={cn(
                  "rounded-md border px-4 py-4",
                  vm.recentEventsSection.state === "unavailable"
                    ? "border-danger/35 bg-danger/10"
                    : "border-border/70 bg-secondary/25"
                )}
              >
                <div className="text-sm font-semibold text-foreground">{vm.recentEventsSection.statusLabel}</div>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">{vm.recentEventsSection.statusDetail}</p>
              </div>
            )}
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <div className="eyebrow-label">API posture</div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <ExternalLink className="h-4 w-4 text-primary" />
                  Diagnostic endpoints
                </CardTitle>
                <CardDescription className="mt-2">{vm.diagnosticSummary}</CardDescription>
              </div>
              <Badge variant={vm.diagnosticStatusVariant} dot={vm.diagnosticStatusVariant === "success"}>
                {vm.diagnosticStatusLabel}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Loaded" value={vm.diagnosticCounts.loadedLabel} />
              <SettingsChip label="Failed" value={vm.diagnosticCounts.failedLabel} />
              <SettingsChip label="Checking" value={vm.diagnosticCounts.checkingLabel} />
            </div>
            <div className="grid gap-3 md:grid-cols-2" role="list" aria-label={vm.diagnosticListLabel}>
              {vm.diagnosticLinks.map((link) => (
                <div key={link.href} role="listitem">
                  <a
                    href={link.href}
                    target="_blank"
                    rel="noreferrer"
                    aria-label={link.ariaLabel}
                    className={cn(
                      "group flex h-full flex-col gap-2 rounded-lg border px-4 py-3 transition-colors hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40",
                      diagnosticToneClass[link.tone]
                    )}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-semibold text-foreground transition-colors group-hover:text-primary">
                        {link.label}
                      </span>
                      <span className="inline-flex items-center gap-2">
                        <Badge variant={link.badgeVariant} className="shrink-0">
                          {link.statusLabel}
                        </Badge>
                        {link.isLoading ? (
                          <LoaderCircle className="h-3 w-3 shrink-0 animate-spin text-warning" aria-hidden="true" />
                        ) : (
                          <ExternalLink className="h-3 w-3 shrink-0 text-muted-foreground" aria-hidden="true" />
                        )}
                      </span>
                    </div>
                    <p className="text-xs leading-5 text-muted-foreground">{link.description}</p>
                    <p className="text-xs leading-5 text-foreground/75">{link.statusDetail}</p>
                    <span className="mt-1 font-mono text-[10px] text-muted-foreground">{link.href}</span>
                  </a>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      </section>

      <Card id="runtime-feature-capabilities" className="panel-surface scroll-mt-6">
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <div className="eyebrow-label">Runtime controls</div>
              <CardTitle className="flex items-center gap-2 text-base">
                <MonitorCheck className="h-4 w-4 text-primary" />
                {vm.runtimeCapabilitySection.title}
              </CardTitle>
              <CardDescription className="mt-2">{vm.runtimeCapabilitySection.summary}</CardDescription>
            </div>
            <Badge variant={vm.runtimeCapabilitySection.statusVariant} dot={vm.runtimeCapabilitySection.statusVariant === "success"}>
              {vm.runtimeCapabilitySection.statusLabel}
            </Badge>
          </div>
        </CardHeader>
        <CardContent>
          {vm.runtimeCapabilitySection.toggles.length === 0 ? (
            <p className="text-sm text-muted-foreground">{vm.runtimeCapabilitySection.description}</p>
          ) : (
            <div className="grid gap-3 md:grid-cols-2" role="list" aria-label={vm.runtimeCapabilitySection.listLabel}>
              {vm.runtimeCapabilitySection.toggles.map((capability) => (
                <div
                  key={capability.capabilityKey}
                  role="listitem"
                  className={cn("rounded-lg border px-4 py-4", diagnosticToneClass[capability.statusVariant === "success" ? "success" : "warning"])}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="font-mono text-[10px] text-muted-foreground">{capability.capabilityKey}</div>
                      <h3 className="mt-2 text-sm font-semibold text-foreground">{capability.displayName}</h3>
                      <p className="mt-2 text-xs leading-5 text-muted-foreground">{capability.description}</p>
                    </div>
                    <Badge variant={capability.statusVariant} className="shrink-0">
                      {capability.statusLabel}
                    </Badge>
                  </div>
                  <div className="mt-3 flex flex-wrap gap-2">
                    <SettingsChip label="Default" value={capability.defaultLabel} />
                    <SettingsChip label="Config" value={capability.overrideLabel} />
                  </div>
                  <label className={cn("mt-4 flex items-start gap-3 text-sm", !capability.canToggle && "opacity-70")}>
                    <input
                      type="checkbox"
                      checked={capability.isEnabled}
                      disabled={!capability.canToggle || !onFeatureCapabilityToggle}
                      onChange={(event) => {
                        void onFeatureCapabilityToggle?.(capability.capabilityKey, event.target.checked);
                      }}
                      aria-label={capability.ariaLabel}
                      className="mt-0.5 h-4 w-4 shrink-0 accent-[hsl(var(--primary))]"
                    />
                    <span className="min-w-0">
                      <span className="block font-medium text-foreground">
                        {capability.canToggle ? "Allow this browser workstation capability" : "Required capability"}
                      </span>
                      {capability.disabledReason ? (
                        <span className="mt-1 block text-xs leading-5 text-muted-foreground">{capability.disabledReason}</span>
                      ) : null}
                    </span>
                  </label>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <Card id="backend-capability-coverage" className="panel-surface scroll-mt-6">
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <div className="eyebrow-label">Backend reachability</div>
              <CardTitle className="flex items-center gap-2 text-base">
                <ExternalLink className="h-4 w-4 text-primary" />
                Capability coverage
              </CardTitle>
              <CardDescription className="mt-2">{vm.backendCapabilitySummary}</CardDescription>
            </div>
            <Badge variant={vm.backendCapabilityStatusVariant} dot={vm.backendCapabilityStatusVariant === "success"}>
              {vm.backendCapabilityStatusLabel}
            </Badge>
          </div>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 xl:grid-cols-2" role="list" aria-label={vm.backendCapabilityListLabel}>
            {vm.backendCapabilityGroups.map((group) => (
              <div
                key={group.id}
                role="listitem"
                className={cn("rounded-lg border px-4 py-4", diagnosticToneClass[capabilityTone(group.statusVariant)])}
              >
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="eyebrow-label">{group.workspaceLabel} · {group.route}</div>
                    <h3 className="mt-2 text-sm font-semibold text-foreground">{group.title}</h3>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{group.description}</p>
                  </div>
                  <Badge variant={group.statusVariant} className="shrink-0">
                    {group.statusLabel}
                  </Badge>
                </div>
                <div className="mt-3 flex flex-wrap gap-2">
                  <SettingsChip label="Mapped" value={group.endpointCountLabel} />
                  <SettingsChip label="Loaded" value={group.loadedCountLabel} />
                </div>
                <p className="mt-3 text-xs leading-5 text-foreground/75">{group.statusDetail}</p>
                <div className="mt-4 grid gap-2 sm:grid-cols-2">
                  {group.endpoints.map((endpoint) => endpoint.isBrowserNavigable ? (
                    <a
                      key={endpoint.id}
                      href={endpoint.href}
                      target="_blank"
                      rel="noreferrer"
                      aria-label={endpoint.ariaLabel}
                      className="flex min-w-0 items-start gap-2 rounded-md border border-border/60 bg-background/45 px-3 py-2 text-xs transition-colors hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                    >
                      <EndpointReference endpoint={endpoint} />
                    </a>
                  ) : (
                    <div
                      key={endpoint.id}
                      role="group"
                      aria-label={endpoint.ariaLabel}
                      className="flex min-w-0 items-start gap-2 rounded-md border border-border/60 bg-secondary/20 px-3 py-2 text-xs"
                    >
                      <EndpointReference endpoint={endpoint} />
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function EndpointReference({
  endpoint
}: {
  endpoint: {
    method: string;
    label: string;
    href: string;
    interactionLabel: string;
  };
}) {
  return (
    <>
      <Badge variant="outline" className="shrink-0">{endpoint.method}</Badge>
      <span className="min-w-0">
        <span className="block font-semibold text-foreground">{endpoint.label}</span>
        <span className="mt-1 block break-all font-mono text-[10px] leading-4 text-muted-foreground">
          {endpoint.href}
        </span>
        <span className="mt-1 inline-flex rounded-sm border border-border/60 px-1.5 py-0.5 text-[10px] uppercase text-muted-foreground">
          {endpoint.interactionLabel}
        </span>
      </span>
    </>
  );
}

function ProfileAuthenticationStepRow({ step }: { step: SettingsProfileAuthenticationStep }) {
  return (
    <div role="listitem" className={cn("rounded-md border px-3 py-2", setupStepToneClass[step.tone])}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="text-sm font-medium text-foreground">{step.label}</div>
          <p className="mt-1 text-xs leading-5 text-muted-foreground">{step.detail}</p>
        </div>
        <Badge variant={step.badgeVariant} className="shrink-0">
          {step.statusLabel}
        </Badge>
      </div>
      {step.actionHref && step.actionLabel ? (
        <Button asChild variant="outline" size="sm" className="mt-3">
          <Link to={step.actionHref} aria-label={step.actionAriaLabel ?? step.actionLabel}>
            {step.actionLabel}
            <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
          </Link>
        </Button>
      ) : null}
    </div>
  );
}

function RecentEventDetailPanel({
  id,
  title,
  description,
  emptyText,
  ariaLabel,
  detail
}: {
  id: string;
  title: string;
  description: string;
  emptyText: string;
  ariaLabel: string;
  detail: SettingsRecentEventDetail | null;
}) {
  return (
    <aside
      id={id}
      role="complementary"
      aria-label={ariaLabel}
      aria-live="polite"
      className="row-detail-panel h-fit min-w-0"
    >
      <div className="head">{title}</div>
      <div className="body">
        {detail ? (
          <div role="region" aria-label={detail.ariaLabel} className="space-y-3">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="eyebrow-label">{detail.eyebrow}</div>
                <h3 className="mt-2 break-words text-sm font-semibold text-foreground">{detail.title}</h3>
                <p className="mt-1 break-words font-mono text-xs text-muted-foreground">{detail.subtitle}</p>
              </div>
              <Badge variant={detail.statusVariant} className="shrink-0">
                {detail.statusLabel}
              </Badge>
            </div>
            <p className="text-sm leading-6 text-muted-foreground">{detail.description}</p>
            <dl className="grid gap-2 sm:grid-cols-2 xl:grid-cols-1 2xl:grid-cols-2">
              {detail.fields.map((field) => (
                <div key={field.label} className="rounded-sm border border-border/60 bg-background/35 px-2.5 py-2">
                  <dt className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{field.label}</dt>
                  <dd className={cn("mt-1 break-words font-mono text-xs", itemToneClass[field.tone])}>
                    {field.value}
                  </dd>
                </div>
              ))}
            </dl>
          </div>
        ) : (
          <div role="status" className="rounded-md border border-dashed border-border/70 bg-secondary/20 px-3 py-3">
            <div className="text-sm font-semibold text-foreground">{description}</div>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">{emptyText}</p>
          </div>
        )}
      </div>
    </aside>
  );
}

function SettingsChip({ label, value }: { label: string; value: string }) {
  return (
    <div className="toolbar-chip" aria-label={`${label} ${value}`}>
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground">{value}</span>
    </div>
  );
}

function SettingsFieldRow({
  label,
  value,
  tone
}: {
  label: string;
  value: string;
  tone: keyof typeof itemToneClass;
}) {
  return (
    <div className="grid grid-cols-[minmax(0,0.7fr)_minmax(0,1fr)] items-start gap-3 rounded-md border border-border/60 bg-secondary/25 px-3 py-2">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className={cn("text-right font-mono text-xs", itemToneClass[tone])}>{value}</dd>
    </div>
  );
}

function buildLedgerMappingAssignmentDraft(workbench: LedgerMappingWorkbench | null): {
  canAssign: boolean;
  statusLabel: string;
  disabledReason: string | null;
  accountOptions: Array<{ value: string; label: string }>;
  ledgerGroupOptions: Array<{ value: string; label: string }>;
} {
  if (!workbench) {
    return {
      canAssign: false,
      statusLabel: "Workbench unavailable",
      disabledReason: "Ledger mapping workbench payload has not loaded.",
      accountOptions: [],
      ledgerGroupOptions: []
    };
  }

  const accountOptions = workbench.accounts
    .filter((account) => account.mapping.requiresUserMapping)
    .map((account) => ({
      value: account.accountId,
      label: `${account.accountCode} - ${account.displayName}`
    }));
  const ledgerGroupOptions = workbench.ledgerGroups
    .filter((group) => group.ledgerGroupId.toLowerCase() !== "unassigned")
    .map((group) => ({
      value: group.ledgerGroupId,
      label: group.displayName ? `${group.displayName} (${group.ledgerGroupId})` : group.ledgerGroupId
    }));

  if (accountOptions.length === 0) {
    return {
      canAssign: false,
      statusLabel: "No unmapped accounts",
      disabledReason: "All accounts in the loaded workbench already have ledger mappings.",
      accountOptions,
      ledgerGroupOptions
    };
  }

  if (ledgerGroupOptions.length === 0) {
    return {
      canAssign: false,
      statusLabel: "No ledger groups",
      disabledReason: "Create or load at least one ledger group before assigning an account mapping.",
      accountOptions,
      ledgerGroupOptions
    };
  }

  return {
    canAssign: true,
    statusLabel: `${accountOptions.length} ready`,
    disabledReason: null,
    accountOptions,
    ledgerGroupOptions
  };
}

function buildRolePermissionProfileDraft(catalog: RolePermissionCatalog | null): {
  canSave: boolean;
  statusLabel: string;
  disabledReason: string | null;
  baseRoleOptions: Array<{ value: string; label: string }>;
  permissionOptions: Array<{ value: string; label: string; group: string }>;
  defaultBaseRole: string;
  defaultPermissionNames: string[];
} {
  if (!catalog) {
    return {
      canSave: false,
      statusLabel: "Catalog unavailable",
      disabledReason: "Role and permission catalog payload has not loaded.",
      baseRoleOptions: [],
      permissionOptions: [],
      defaultBaseRole: "",
      defaultPermissionNames: []
    };
  }

  const builtInRoles = catalog.roles.filter((role) => role.isBuiltIn);
  const defaultRole = builtInRoles.find((role) => role.role === "Accounting") ?? builtInRoles[0];
  const baseRoleOptions = builtInRoles.map((role) => ({
    value: role.role,
    label: `${role.displayName} (${role.role})`
  }));
  const permissionOptions = catalog.permissions.map((permission) => ({
    value: permission.name,
    label: permission.name,
    group: permission.group
  }));

  if (baseRoleOptions.length === 0) {
    return {
      canSave: false,
      statusLabel: "No base roles",
      disabledReason: "At least one built-in base role is required before creating a custom profile.",
      baseRoleOptions,
      permissionOptions,
      defaultBaseRole: "",
      defaultPermissionNames: []
    };
  }

  if (permissionOptions.length === 0) {
    return {
      canSave: false,
      statusLabel: "No permissions",
      disabledReason: "At least one permission is required before creating a custom profile.",
      baseRoleOptions,
      permissionOptions,
      defaultBaseRole: defaultRole?.role ?? "",
      defaultPermissionNames: []
    };
  }

  return {
    canSave: true,
    statusLabel: `${permissionOptions.length} grants`,
    disabledReason: null,
    baseRoleOptions,
    permissionOptions,
    defaultBaseRole: defaultRole?.role ?? baseRoleOptions[0]?.value ?? "",
    defaultPermissionNames: defaultRole?.permissions ?? []
  };
}

function buildApprovalPolicyRuleDraft(matrix: OperationsApprovalPolicyMatrix | null): {
  canSave: boolean;
  statusLabel: string;
  disabledReason: string | null;
  rows: OperationsApprovalPolicyMatrixRow[];
  policyOptions: Array<{ value: string; label: string }>;
} {
  if (!matrix) {
    return {
      canSave: false,
      statusLabel: "Matrix unavailable",
      disabledReason: "Approval policy matrix payload has not loaded.",
      rows: [],
      policyOptions: []
    };
  }

  if (matrix.rows.length === 0) {
    return {
      canSave: false,
      statusLabel: "No rules",
      disabledReason: "At least one approval policy rule is required before configuration can be saved.",
      rows: [],
      policyOptions: []
    };
  }

  return {
    canSave: true,
    statusLabel: `${matrix.rows.length} rules`,
    disabledReason: null,
    rows: matrix.rows,
    policyOptions: matrix.rows.map((row) => ({
      value: row.policyKey,
      label: `${row.action} (${row.policyKey})`
    }))
  };
}

function buildCloseCalendarItemDraft(calendar: OperationsCloseCalendar | null): {
  canSave: boolean;
  statusLabel: string;
  disabledReason: string | null;
  items: OperationsCloseCalendarItem[];
  workflowOptions: Array<{ value: string; label: string }>;
} {
  if (!calendar) {
    return {
      canSave: false,
      statusLabel: "Calendar unavailable",
      disabledReason: "Account close calendar payload has not loaded.",
      items: [],
      workflowOptions: []
    };
  }

  const configurableItems = calendar.items.filter((item) => item.nextDueTaskId && item.nextDueDate);
  if (configurableItems.length === 0) {
    return {
      canSave: false,
      statusLabel: "No open tasks",
      disabledReason: "At least one account close workflow with a next due task is required before the calendar can be configured.",
      items: [],
      workflowOptions: []
    };
  }

  return {
    canSave: true,
    statusLabel: `${configurableItems.length} workflows`,
    disabledReason: null,
    items: configurableItems,
    workflowOptions: configurableItems.map((item) => ({
      value: item.workflowId,
      label: `${item.periodId}: ${item.nextDueLabel ?? item.nextDueTaskId}`
    }))
  };
}

function AssetProfileFieldInput({
  field,
  value,
  disabled,
  onChange
}: {
  field: SecurityAssetProfileFieldDefinition;
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  if (field.fieldType === "Boolean") {
    return (
      <label className="flex min-h-16 items-center gap-2 rounded-md border border-border/60 bg-secondary/15 px-3 py-2 text-xs font-medium text-muted-foreground">
        <input
          type="checkbox"
          checked={value === "true"}
          onChange={(event) => onChange(event.target.checked ? "true" : "false")}
          className="h-4 w-4 shrink-0 accent-[hsl(var(--primary))]"
          disabled={disabled}
        />
        <span>{field.label}{field.isRequired ? " *" : ""}</span>
      </label>
    );
  }

  if (field.fieldType === "Enum") {
    return (
      <FilterSelect
        label={`${field.label}${field.isRequired ? " *" : ""}`}
        value={value}
        onChange={onChange}
        options={[
          { value: "", label: "Select value" },
          ...field.allowedValues.map((allowed) => ({ value: allowed, label: allowed }))
        ]}
        disabled={disabled}
      />
    );
  }

  return (
    <label className="grid gap-1 text-xs font-medium text-muted-foreground">
      {field.label}{field.isRequired ? " *" : ""}
      <Input
        type={assetProfileInputType(field)}
        min={field.minValue ?? undefined}
        max={field.maxValue ?? undefined}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        disabled={disabled}
        aria-label={`Profile field ${field.label}`}
      />
    </label>
  );
}

function assetProfileInputType(field: SecurityAssetProfileFieldDefinition): "text" | "number" | "date" {
  if (field.fieldType === "Date") return "date";
  if (field.fieldType === "Decimal" || field.fieldType === "Integer") return "number";
  return "text";
}

function createAssetProfileDraftState(profile: SecurityAssetProfileDefinition | null): AssetProfileDraftState {
  const profileId = profile ? `${profile.profileId}-variant` : "";
  return {
    starterProfileId: profile?.profileId ?? "",
    profileId,
    name: profile ? `${profile.name} Variant` : "",
    category: profile?.category ?? "",
    subType: profile?.subType ?? "",
    rationale: "Draft asset profile from approved starter template.",
    busyAction: null,
    rollbackTargetVersion: profile?.version ?? 1,
    lineage: null,
    result: null,
    message: null,
    details: [],
    tone: "default"
  };
}

function createProfileBackedSecurityState(profile: SecurityAssetProfileDefinition | null): ProfileBackedSecurityState {
  return {
    profileId: profile?.profileId ?? "",
    displayName: "",
    internalCode: "",
    currency: "USD",
    fieldValues: profile ? buildProfileFieldValueState(profile, {}) : {},
    rationale: "Create profile-backed custom asset with approved Security Master profile version.",
    busy: false,
    message: null,
    details: [],
    tone: "default"
  };
}

function buildProfileFieldValueState(
  profile: SecurityAssetProfileDefinition,
  previous: Record<string, string>
): Record<string, string> {
  return Object.fromEntries(profile.fields.map((field) => [
    field.key,
    previous[field.key] ?? defaultProfileFieldValue(field)
  ]));
}

function defaultProfileFieldValue(field: SecurityAssetProfileFieldDefinition): string {
  if (field.fieldType === "Boolean") return "false";
  return "";
}

function buildProfileFieldPayload(
  fields: SecurityAssetProfileFieldDefinition[],
  values: Record<string, string>
): Record<string, unknown> {
  return fields.reduce<Record<string, unknown>>((acc, field) => {
    const raw = values[field.key]?.trim() ?? "";
    if (!raw && !field.isRequired) {
      return acc;
    }

    switch (field.fieldType) {
      case "Decimal":
        acc[field.key] = Number.parseFloat(raw);
        break;
      case "Integer":
        acc[field.key] = Number.parseInt(raw, 10);
        break;
      case "Boolean":
        acc[field.key] = raw === "true";
        break;
      default:
        acc[field.key] = raw;
        break;
    }
    return acc;
  }, {});
}

function normalizeAssetProfileId(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9-]+/g, "-").replace(/^-+|-+$/g, "");
}

function todayDateOnly(): string {
  return new Date().toISOString().slice(0, 10);
}

function createBrowserGuid(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID();
  }

  return "10000000-1000-4000-8000-100000000000".replace(/[018]/g, (character) => {
    const value = Number(character);
    return (value ^ (Math.random() * 16 >> (value / 4))).toString(16);
  });
}

function FilterSelect({
  label,
  value,
  onChange,
  options,
  disabled = false
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: Array<{ value: string; label: string }>;
  disabled?: boolean;
}) {
  return (
    <label className="grid gap-1 text-xs font-medium text-muted-foreground">
      {label}
      <select
        value={value}
        onChange={(event) => onChange(event.target.value)}
        disabled={disabled}
        className="h-9 rounded-md border border-border/70 bg-background px-2 text-sm text-foreground"
        aria-label={label}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}

function ProviderInlineActionPanel({
  row,
  state,
  fieldDefinitions,
  onToggleEdit,
  onFieldChange,
  onEnvironmentChange,
  onLiveAcknowledgementChange,
  onTest,
  onSave,
  onVerify,
  onClear
}: {
  row: { providerId: string; displayName: string; affectedWorkflowsLabel: string; productionStateLabel: string; fallbackLabel: string };
  state: ProviderInlineState;
  fieldDefinitions: ProviderInlineFieldDefinition[];
  onToggleEdit: () => void;
  onFieldChange: (field: ProviderInlineField, value: string) => void;
  onEnvironmentChange: (value: ProviderInlineState["environment"]) => void;
  onLiveAcknowledgementChange: (value: boolean) => void;
  onTest: () => void;
  onSave: () => void;
  onVerify: () => void;
  onClear: () => void;
}) {
  const busy = state.busyAction !== null;
  return (
    <section className="mb-3 grid gap-3 rounded-md border border-border/70 bg-secondary/20 px-3 py-3" aria-label={`${row.displayName} inline provider actions`}>
      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={onToggleEdit}
          disabled={busy}
          aria-label={state.editing ? `Close ${row.displayName} editor` : `Edit ${row.displayName} credentials`}
        >
          {state.editing ? "Close editor" : "Edit"}
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={onTest}
          busy={state.busyAction === "test"}
          disabled={busy}
          busyLabel="Testing provider"
          aria-label={`Test ${row.displayName} connection`}
        >
          Test
        </Button>
        <Button
          type="button"
          size="sm"
          onClick={onSave}
          busy={state.busyAction === "save"}
          disabled={busy || (state.environment === "live" && !state.liveAcknowledged)}
          busyLabel="Saving draft"
          aria-label={`Save ${row.displayName} credentials`}
        >
          <Save className="h-3.5 w-3.5" aria-hidden="true" />
          Save
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={onVerify}
          busy={state.busyAction === "verify"}
          disabled={busy}
          busyLabel="Verifying provider"
          aria-label={`Re-verify ${row.displayName} connection`}
        >
          Re-verify
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={onClear}
          busy={state.busyAction === "clear"}
          disabled={busy}
          busyLabel="Clearing credentials"
          aria-label={`Clear ${row.displayName} credentials`}
        >
          <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
          Clear credentials
        </Button>
      </div>
      {state.editing ? (
        <div className="grid gap-3 md:grid-cols-2">
          {fieldDefinitions.map((field) => (
            <label key={`${row.providerId}-${field.field}`} className="grid gap-1 text-xs font-medium text-muted-foreground">
              {field.label}
              <Input
                type={field.type}
                value={state.values[field.field]}
                onChange={(event) => onFieldChange(field.field, event.target.value)}
                placeholder={field.placeholder}
                autoComplete={field.type === "password" ? "new-password" : "off"}
                disabled={busy}
                aria-label={`${row.displayName} ${field.label}`}
              />
              <span className="text-[11px] leading-4 text-muted-foreground">{field.helpText}</span>
            </label>
          ))}
          <label className="grid gap-1 text-xs font-medium text-muted-foreground">
            Environment
            <select
              value={state.environment}
              onChange={(event) => onEnvironmentChange(event.target.value as ProviderInlineState["environment"])}
              className="h-9 rounded-md border border-border/70 bg-background px-2 text-sm text-foreground"
              disabled={busy}
              aria-label={`${row.displayName} environment`}
            >
              <option value="paper">Paper</option>
              <option value="live">Live</option>
              <option value="sandbox">Sandbox</option>
              <option value="custom">Custom</option>
            </select>
          </label>
          {state.environment === "live" ? (
            <label className="flex items-start gap-2 rounded-md border border-live-env/35 bg-live-env/10 px-2 py-2 text-xs text-live-env">
              <input
                type="checkbox"
                checked={state.liveAcknowledged}
                onChange={(event) => onLiveAcknowledgementChange(event.target.checked)}
                className="mt-0.5 h-4 w-4 shrink-0 accent-[hsl(var(--live-env))]"
                disabled={busy}
              />
              <span>I understand this save updates live provider routing credentials.</span>
            </label>
          ) : null}
        </div>
      ) : null}
      <div className="rounded-sm border border-border/60 bg-background/40 px-2 py-2 text-xs text-muted-foreground">
        Impact summary: {row.affectedWorkflowsLabel} · Production gate {row.productionStateLabel} · Failover {row.fallbackLabel}
      </div>
      {state.testLatencyLabel ? <div className="text-xs text-muted-foreground">Latest test latency: {state.testLatencyLabel}</div> : null}
      {state.statusMessage ? (
        <div className={cn("text-xs", itemToneClass[state.statusTone])}>
          <div>{state.statusMessage}</div>
          {state.statusDetails.length > 0 ? (
            <ul className="mt-1 list-disc space-y-1 pl-4 text-[11px] text-muted-foreground">
              {state.statusDetails.map((detail) => <li key={detail}>{detail}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

function ProviderReadinessChecklist({
  row,
  state,
  fieldDefinitions
}: {
  row: { credentialStatus: "present" | "missing" | "not-required"; verificationStatus: "verified" | "pending" | "failed"; fallbackStatus: "active" | "available" | "missing"; displayName: string };
  state: ProviderInlineState;
  fieldDefinitions: ProviderInlineFieldDefinition[];
}) {
  const credentialsReady = state.dirty
    ? fieldDefinitions
      .filter((definition) => definition.required)
      .every((definition) => state.values[definition.field].trim().length > 0)
    : row.credentialStatus !== "missing";
  const verified = !state.verificationFailed && row.verificationStatus === "verified";
  const fallbackSet = row.fallbackStatus !== "missing";
  const checks = [
    { label: "Credentials present", ready: credentialsReady },
    { label: "Verified recently", ready: verified },
    { label: "Fallback set", ready: fallbackSet }
  ];
  return (
    <div className="mt-3 rounded-md border border-border/60 bg-secondary/15 px-3 py-2" aria-label={`${row.displayName} readiness checks`}>
      <div className="text-xs font-semibold text-foreground">Readiness checks</div>
      <div className="mt-2 flex flex-wrap gap-2">
        {checks.map((check) => (
          <Badge key={check.label} variant={check.ready ? "success" : "warning"}>
            {check.label}: {check.ready ? "Ready" : "Review"}
          </Badge>
        ))}
      </div>
    </div>
  );
}

function createProviderInlineState(row: {
  environmentLabel: string;
  credentialStatus: "present" | "missing" | "not-required";
  verificationStatus: "verified" | "pending" | "failed";
}): ProviderInlineState {
  return {
    editing: false,
    values: {
      apiKey: "",
      apiSecret: "",
      endpoint: ""
    },
    environment: normalizeInlineEnvironment(row.environmentLabel),
    liveAcknowledged: false,
    dirty: false,
    busyAction: null,
    statusMessage: null,
    statusDetails: [],
    statusTone: "default",
    verificationFailed: row.verificationStatus === "failed" || row.credentialStatus === "missing",
    testLatencyLabel: null
  };
}

function normalizeInlineEnvironment(label: string): ProviderInlineState["environment"] {
  const value = label.trim().toLowerCase();
  if (value === "live") return "live";
  if (value === "sandbox") return "sandbox";
  if (value === "custom") return "custom";
  return "paper";
}

function buildProviderFieldDefinitions(row: { providerId: string; displayName: string }): ProviderInlineFieldDefinition[] {
  const normalizedId = row.providerId.toLowerCase();
  const idTokens = normalizedId.split(/[^a-z0-9]+/).filter(Boolean);
  const displayTokens = row.displayName.toLowerCase().split(/[^a-z0-9]+/).filter(Boolean);
  const fromCatalog = PROVIDER_KIND_CATALOG.find((provider) => (
    idTokens.includes(provider.kind) || displayTokens.includes(provider.kind)
  ));
  const fields: ProviderInlineFieldDefinition[] = [];

  if (fromCatalog?.needsApiKey !== false) {
    fields.push({
      field: "apiKey",
      label: "API key",
      type: "password",
      placeholder: "Stored server-side and masked after save",
      helpText: "Required for provider credential verification.",
      required: true
    });
  }
  if (fromCatalog?.needsApiSecret) {
    fields.push({
      field: "apiSecret",
      label: "API secret",
      type: "password",
      placeholder: "Paste secure secret for this provider",
      helpText: "Secret is cleared from browser state after save.",
      required: true
    });
  }
  if (fromCatalog?.needsEndpoint) {
    fields.push({
      field: "endpoint",
      label: "Endpoint URL",
      type: "url",
      placeholder: "https://api.provider.com",
      helpText: "Used when provider requires a custom endpoint.",
      required: true
    });
  }

  if (fields.length === 0) {
    fields.push({
      field: "apiKey",
      label: "Credential reference",
      type: "password",
      placeholder: "Optional credential or token",
      helpText: "Provider metadata marks credentials as optional.",
      required: false
    });
  }

  return fields;
}

function filterProviderRow(
  row: { displayName: string; recommendedAction: string; affectedWorkflowsLabel: string; capabilityGroup: "brokerage" | "data"; healthTone: string; verificationStatus: "verified" | "pending" | "failed" },
  search: string,
  capabilityFilter: "all" | "brokerage" | "data",
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

function providerDraftStatusLabel(state: ProviderInlineState | undefined, row: { verificationStatus: "verified" | "pending" | "failed" }): string {
  if (!state) return "Active";
  if (state.busyAction === "save") return "Saving";
  if (state.dirty) return "Unsaved";
  if (state.verificationFailed || row.verificationStatus === "failed") return "Verification failed";
  return "Active";
}

function providerDraftStatusVariant(state: ProviderInlineState | undefined, row: { verificationStatus: "verified" | "pending" | "failed" }): "outline" | "success" | "warning" | "danger" {
  const label = providerDraftStatusLabel(state, row);
  if (label === "Saving") return "warning";
  if (label === "Unsaved") return "warning";
  if (label === "Verification failed") return "danger";
  return "success";
}

function recentEventsVariant(state: "ready" | "empty" | "unavailable"): "default" | "outline" | "danger" {
  if (state === "unavailable") return "danger";
  if (state === "empty") return "outline";
  return "default";
}

function systemVariant(tone: keyof typeof systemToneClass): "outline" | "success" | "warning" | "danger" {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "outline";
}

function toneVariant(tone: keyof typeof itemToneClass): "outline" | "success" | "warning" | "danger" {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "outline";
}

function capabilityTone(tone: "default" | "success" | "warning" | "danger" | "outline"): keyof typeof diagnosticToneClass {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "default";
}
