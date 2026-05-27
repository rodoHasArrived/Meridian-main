import { Activity, ArrowRight, ExternalLink, KeyRound, LoaderCircle, MonitorCheck, RefreshCcw, Save, Search, ShieldCheck, Trash2, User } from "lucide-react";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldSupportText, joinDescribedByIds } from "@/components/ui/field-support";
import { Input } from "@/components/ui/input";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { deleteProviderCredentials, putProviderCredentials, testProviderConnection, verifyProviderConnection } from "@/lib/api";
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
  const providerInlineFlag = featureCapabilities?.capabilities.find((capability) => (
    capability.capabilityKey === "desktop.settings.provider-connection-center-inline-management"
  ));
  const inlineProviderManagementEnabled = providerInlineFlag?.isEnabled ?? true;
  const allProviderRows = useMemo(
    () => vm.providerConnectionCenter.groups.flatMap((group) => group.rows),
    [vm.providerConnectionCenter.groups]
  );

  useEffect(() => {
    if (!inlineProviderManagementEnabled) {
      return;
    }

    setProviderInlineState((current) => {
      const next = { ...current };
      for (const row of allProviderRows) {
        if (next[row.providerId]) {
          continue;
        }
        next[row.providerId] = createProviderInlineState(row);
      }
      return next;
    });
  }, [allProviderRows, inlineProviderManagementEnabled]);

  const filteredProviderGroups = useMemo(() => {
    const search = providerSearch.trim().toLowerCase();
    return vm.providerConnectionCenter.groups.map((group) => {
      const rows = group.rows
        .filter((row) => filterProviderRow(row, search, providerCapabilityFilter, providerHealthFilter, providerVerificationFilter))
        .sort((left, right) => {
          if (providerSort === "name") {
            return left.displayName.localeCompare(right.displayName);
          }
          const riskScore = providerRiskScore(right) - providerRiskScore(left);
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
    const definitions = buildProviderFieldDefinitions(row);
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
                          fieldDefinitions={buildProviderFieldDefinitions(row)}
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

function FilterSelect({
  label,
  value,
  onChange,
  options
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: Array<{ value: string; label: string }>;
}) {
  return (
    <label className="grid gap-1 text-xs font-medium text-muted-foreground">
      {label}
      <select
        value={value}
        onChange={(event) => onChange(event.target.value)}
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
        <Button type="button" variant="outline" size="sm" onClick={onToggleEdit} disabled={busy}>
          {state.editing ? "Close editor" : "Edit"}
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={onTest} busy={state.busyAction === "test"} disabled={busy} busyLabel="Testing provider">
          Test
        </Button>
        <Button type="button" size="sm" onClick={onSave} busy={state.busyAction === "save"} disabled={busy || (state.environment === "live" && !state.liveAcknowledged)} busyLabel="Saving draft">
          <Save className="h-3.5 w-3.5" aria-hidden="true" />
          Save
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={onVerify} busy={state.busyAction === "verify"} disabled={busy} busyLabel="Verifying provider">
          Re-verify
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={onClear} busy={state.busyAction === "clear"} disabled={busy} busyLabel="Clearing credentials">
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
  state
}: {
  row: { credentialLabel: string; verificationLabel: string; fallbackLabel: string; displayName: string };
  state: ProviderInlineState;
}) {
  const credentialsReady = state.dirty ? Boolean(state.values.apiKey || state.values.apiSecret || state.values.endpoint) : row.credentialLabel !== "Missing";
  const verified = !state.verificationFailed && /verified|certified/i.test(row.verificationLabel);
  const fallbackSet = !/not declared|none/i.test(row.fallbackLabel);
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
  maskedKeyPreviewLabel: string;
  credentialLabel: string;
  verificationLabel: string;
}): ProviderInlineState {
  return {
    editing: false,
    values: {
      apiKey: row.maskedKeyPreviewLabel === "Masked after save" ? "" : row.maskedKeyPreviewLabel,
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
    verificationFailed: /failed/i.test(row.verificationLabel) || /invalid/i.test(row.credentialLabel),
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
  const fromCatalog = PROVIDER_KIND_CATALOG.find((provider) => (
    normalizedId.includes(provider.kind) || row.displayName.toLowerCase().includes(provider.kind)
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
  row: { displayName: string; recommendedAction: string; affectedWorkflowsLabel: string; capabilityLabel: string; healthTone: string; verificationLabel: string },
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

  if (capabilityFilter === "brokerage" && !row.capabilityLabel.toLowerCase().includes("brokerage")) {
    return false;
  }
  if (capabilityFilter === "data" && row.capabilityLabel.toLowerCase().includes("brokerage")) {
    return false;
  }

  if (healthFilter === "healthy" && row.healthTone !== "success") return false;
  if (healthFilter === "warning" && row.healthTone !== "warning") return false;
  if (healthFilter === "blocked" && row.healthTone !== "danger") return false;

  const verified = /verified|certified|ready/i.test(row.verificationLabel);
  if (verificationFilter === "verified" && !verified) return false;
  if (verificationFilter === "unverified" && verified) return false;

  return true;
}

function providerRiskScore(row: { healthTone: string; credentialTone: string; verificationLabel: string; fallbackLabel: string }): number {
  const healthScore = row.healthTone === "danger" ? 100 : row.healthTone === "warning" ? 70 : 20;
  const credentialScore = row.credentialTone === "danger" ? 40 : row.credentialTone === "warning" ? 20 : 0;
  const verificationScore = /failed|pending|not verified/i.test(row.verificationLabel) ? 25 : 0;
  const fallbackScore = /fallback active/i.test(row.fallbackLabel) ? 10 : 0;
  return healthScore + credentialScore + verificationScore + fallbackScore;
}

function providerDraftStatusLabel(state: ProviderInlineState | undefined, row: { verificationLabel: string }): string {
  if (!state) return "Active";
  if (state.busyAction === "save") return "Saving";
  if (state.dirty) return "Unsaved";
  if (state.verificationFailed || /failed/i.test(row.verificationLabel)) return "Verification failed";
  return "Active";
}

function providerDraftStatusVariant(state: ProviderInlineState | undefined, row: { verificationLabel: string }): "outline" | "success" | "warning" | "danger" {
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

function capabilityTone(tone: "success" | "warning" | "danger" | "outline"): keyof typeof diagnosticToneClass {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "default";
}
