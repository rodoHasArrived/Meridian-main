import { Activity, ArrowRight, ExternalLink, KeyRound, LoaderCircle, MonitorCheck, ShieldCheck, Trash2, User } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { buildSettingsScreenViewModel, useAlpacaConnectionFormViewModel } from "@/screens/settings-screen.view-model";
import type {
  BrokerageConnectionStatus,
  DataOperationsWorkspaceResponse,
  GovernanceWorkspaceResponse,
  PortfolioWorkspaceResponse,
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
  onRefresh?: () => Promise<void> | void;
  loading?: boolean;
  error?: string | null;
  workspaceErrors?: Partial<Record<WorkspaceKey, string>>;
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

const setupStepToneClass = {
  success: "border-success/30 bg-success/10",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10",
  muted: "border-border/70 bg-secondary/25"
} as const;

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
  onRefresh,
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
    loading,
    error,
    workspaceErrors
  });
  const alpacaForm = useAlpacaConnectionFormViewModel({
    onRefresh,
    canClear: vm.alpacaConnectionPanel.canClear
  });

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
        <Card id="diagnostic-endpoints" className="panel-surface scroll-mt-6">
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <div className="eyebrow-label">Settings lane</div>
                <CardTitle className="mt-2 flex items-center gap-2">
                  <User className="h-5 w-5 text-primary" />
                  {vm.sessionTitle}
                </CardTitle>
                <CardDescription className="mt-2">
                  Active operator session context and environment routing for the current workstation shell.
                </CardDescription>
              </div>
              <Badge variant={sessionVariant(session?.environment)}>
                {session ? session.environment : "Unavailable"}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap gap-2">
              <SettingsChip label="Commands" value={session ? String(session.commandCount) : "—"} />
              <SettingsChip label="Role" value={session?.role ?? "—"} />
              <SettingsChip label="Workspace" value={session?.activeWorkspace ?? "—"} />
            </div>
            {vm.hasSession ? (
              <dl className="grid gap-2">
                {vm.sessionItems.map((item) => (
                  <SettingsFieldRow key={item.label} label={item.label} value={item.value} tone={item.tone} />
                ))}
              </dl>
            ) : (
              <p className="rounded-md border border-border/70 bg-secondary/25 px-4 py-4 text-center text-sm text-muted-foreground">
                Session data is unavailable. Reconnect to the Meridian API.
              </p>
            )}
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
              <label htmlFor="alpaca-key-id" className="grid gap-1 text-xs font-medium text-muted-foreground">
                Key ID
                <Input
                  id="alpaca-key-id"
                  value={alpacaForm.keyId}
                  onChange={(event) => alpacaForm.setKeyId(event.target.value)}
                  autoComplete="off"
                  placeholder="ALPACA_KEY_ID"
                  leadingIcon={<KeyRound className="h-4 w-4" />}
                  disabled={!alpacaForm.canEdit}
                  error={alpacaForm.keyIdError}
                  aria-describedby={`${alpacaForm.fieldHelpIds.keyId} ${alpacaForm.formPanelId}`}
                />
                <span id={alpacaForm.fieldHelpIds.keyId} className={cn("text-[11px] leading-4", alpacaForm.keyIdError ? "text-danger" : "text-muted-foreground")}>
                  {alpacaForm.keyIdHelpText}
                </span>
              </label>
              <label htmlFor="alpaca-secret-key" className="grid gap-1 text-xs font-medium text-muted-foreground">
                Secret key
                <Input
                  id="alpaca-secret-key"
                  type="password"
                  value={alpacaForm.secretKey}
                  onChange={(event) => alpacaForm.setSecretKey(event.target.value)}
                  autoComplete="off"
                  placeholder="ALPACA_SECRET_KEY"
                  leadingIcon={<ShieldCheck className="h-4 w-4" />}
                  disabled={!alpacaForm.canEdit}
                  error={alpacaForm.secretKeyError}
                  aria-describedby={`${alpacaForm.fieldHelpIds.secretKey} ${alpacaForm.formPanelId}`}
                />
                <span id={alpacaForm.fieldHelpIds.secretKey} className={cn("text-[11px] leading-4", alpacaForm.secretKeyError ? "text-danger" : "text-muted-foreground")}>
                  {alpacaForm.secretKeyHelpText}
                </span>
              </label>
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
                      className={cn(
                        "relative grid min-h-[4.75rem] cursor-pointer gap-1 rounded-md border px-3 py-2 transition-colors",
                        option.isSelected ? environmentOptionClass[option.tone].selected : environmentOptionClass[option.tone].idle,
                        option.disabled && "cursor-not-allowed opacity-60"
                      )}
                    >
                      <input
                        type="radio"
                        name="alpaca-environment"
                        value={option.value}
                        checked={option.isSelected}
                        disabled={option.disabled}
                        onChange={() => alpacaForm.setEnvironment(option.value)}
                        aria-label={option.ariaLabel}
                        aria-describedby={`${option.descriptionId} ${alpacaForm.fieldHelpIds.environment} ${alpacaForm.formPanelId}`}
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
                    </label>
                  ))}
                </div>
                <span id={alpacaForm.fieldHelpIds.environment} className="text-[11px] leading-4 text-muted-foreground">
                  {alpacaForm.environmentHelpText}
                </span>
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
                  aria-describedby={`${alpacaForm.liveAcknowledgement.descriptionId} ${alpacaForm.formPanelId}`}
                  className="mt-0.5 h-4 w-4 shrink-0 accent-[hsl(var(--live-env))]"
                />
                <span className="min-w-0">
                  <span className="block font-semibold text-foreground">{alpacaForm.liveAcknowledgement.label}</span>
                  <span id={alpacaForm.liveAcknowledgement.descriptionId} className="mt-1 block text-xs leading-5 text-muted-foreground">
                    {alpacaForm.liveAcknowledgement.detail}
                  </span>
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
                <span role={alpacaForm.statusRole} className={alpacaForm.statusClassName}>{alpacaForm.actionMessage}</span>
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
        <Card className="panel-surface">
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
            {vm.recentEventsSection.rows.length > 0 ? (
              <div role="list" aria-label={vm.recentEventsSection.listLabel} className="space-y-2">
                {vm.recentEventsSection.rows.map((event) => (
                  <div
                    key={event.id}
                    role="group"
                    aria-label={event.ariaLabel}
                    className={cn(
                      "grid gap-3 rounded-md border px-3 py-3 sm:grid-cols-[auto_minmax(0,1fr)_auto]",
                      eventToneClass[event.tone]
                    )}
                  >
                    <Badge variant={event.badgeVariant} className="w-fit">
                      {event.statusCode}
                    </Badge>
                    <div className="min-w-0">
                      <p className="text-sm text-foreground">{event.message}</p>
                      <p className="mt-1 font-mono text-xs text-muted-foreground">{event.source} · {event.id}</p>
                    </div>
                    <span className="font-mono text-xs text-muted-foreground sm:text-right">{event.timestamp}</span>
                  </div>
                ))}
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

function capabilityTone(tone: "success" | "warning" | "danger" | "outline"): keyof typeof diagnosticToneClass {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "default";
}

function sessionVariant(environment: SessionInfo["environment"] | undefined): "outline" | "paper" | "live" | "research" {
  if (environment === "paper") return "paper";
  if (environment === "live") return "live";
  if (environment === "research") return "research";
  return "outline";
}
