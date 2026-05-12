import {
  CheckCircle2,
  DatabaseZap,
  FileOutput,
  Plus,
  RadioTower,
  RefreshCcw,
  ShieldCheck,
  TimerReset,
  XCircle
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";
import { Link, useLocation } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogCloseButton, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import { workspaceForPath } from "@/lib/workspace";
import {
  useDataOperationsViewModel
} from "@/screens/data-operations-screen.view-model";
import type { DataOperationsWorkspaceResponse } from "@/types";
import type {
  BackfillResultCardState,
  DataOperationsEmptyState,
  DataOperationsLoadingState,
  DataOperationsRouteFocusCardState,
  ProviderSetupNextActionState
} from "@/screens/data-operations-screen.view-model";

interface DataOperationsScreenProps {
  data: DataOperationsWorkspaceResponse | null;
}

export function DataOperationsScreen({ data }: DataOperationsScreenProps) {
  const { pathname } = useLocation();
  const workspace = workspaceForPath(pathname);
  const vm = useDataOperationsViewModel(data, pathname);

  if (!data) {
    return <DataOperationsLoadingPanel state={vm.loadingState} />;
  }

  return (
    <div className="space-y-8">
      <section className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap gap-2">
          <Button asChild variant="outline" size="sm">
            <Link to="/data/quotes" aria-label="Open live quotes and order book viewer">
              <RadioTower className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">Live quotes</span>
            </Link>
          </Button>
          <Button asChild variant="outline" size="sm">
            <Link to="/data/watchlist" aria-label="Open symbol watchlist">
              <Plus className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">Watchlist</span>
            </Link>
          </Button>
        </div>
      </section>

      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.metrics.map((metric) => <MetricCard key={metric.id} {...metric} />)}
      </section>

      <section className="grid gap-4 xl:grid-cols-[1.25fr_0.75fr]">
        <Card>
          <CardHeader>
            <div className="eyebrow-label">{workspace.label} Lane</div>
            <CardTitle className="flex items-center gap-2">
              <DatabaseZap className="h-5 w-5 text-primary" />
              Data operations command deck
            </CardTitle>
            <CardDescription>
              Monitor provider posture, historical repairs, and export readiness from the Data workspace.
              Security Master coverage now routes through Accounting where reconciliation and reporting evidence are reviewed.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-3">
            <DataHighlight
              icon={RadioTower}
              title="Provider posture"
              description="Track source health, trust evidence, and recovery actions before downstream workflows depend on the feed."
            />
            <DataHighlight
              icon={TimerReset}
              title="Backfill repair"
              description="Preview and run historical repair jobs with operator-visible status, ranges, and result evidence."
            />
            <DataHighlight
              icon={FileOutput}
              title="Export readiness"
              description="Keep generated packages and report handoff cues tied to the provider and backfill state that produced them."
            />
          </CardContent>
        </Card>

        <RouteFocusCard
          state={vm.routeFocusCard}
        />
      </section>

      <section className="grid gap-4 xl:grid-cols-3">
        <Card aria-labelledby="data-provider-health-title">
          <CardHeader>
            <div className="flex items-center justify-between gap-3">
              <div>
                <CardTitle id="data-provider-health-title">Provider health</CardTitle>
                <CardDescription>Current data-source posture for security, market, and export coverage.</CardDescription>
              </div>
              <Button size="sm" variant="outline" onClick={vm.openProviderSetup} aria-label="Configure a new data provider">
                <Plus className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                Add provider
              </Button>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {vm.providerSection.hasRows ? vm.providerSection.rows.map((provider) => (
              <div
                key={provider.provider}
                role="group"
                className={cn("rounded-lg border p-3", providerToneClass[provider.statusTone])}
                aria-label={provider.ariaLabel}
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="font-semibold">{provider.provider}</span>
                  <div className="flex items-center gap-2">
                    <Badge
                      variant={provider.statusTone === "danger" ? "danger" : provider.statusTone === "warning" ? "warning" : "success"}
                      dot
                    >
                      {provider.status}
                    </Badge>
                    <span className={cn("font-mono text-xs", providerStatusTextClass[provider.statusTone])}>
                      {provider.latencyText}
                    </span>
                  </div>
                </div>
                <p className="mt-2 text-sm text-muted-foreground">{provider.capability}</p>
                <p className="mt-1 text-xs text-muted-foreground">{provider.note}</p>
                <div className="mt-3 grid grid-cols-2 gap-2" aria-label={`${provider.provider} trust evidence`}>
                  {provider.trustFields.map((field) => (
                    <FieldTile key={field.id} field={field} />
                  ))}
                </div>
                <div className="mt-3 rounded-md border border-border/60 bg-background/40 px-3 py-2">
                  <div className="eyebrow-label">Recommended action</div>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{provider.recommendedActionText}</p>
                  <p className="mt-2 font-mono text-[11px] text-muted-foreground">Reason: {provider.reasonCodeText}</p>
                </div>
              </div>
            )) : (
              <ProviderEmptyState state={vm.providerSection.emptyState} onSetup={vm.openProviderSetup} />
            )}
          </CardContent>
        </Card>

        <Card aria-labelledby="data-backfill-queue-title">
          <CardHeader>
            <div className="flex items-center justify-between gap-3">
              <div>
                <CardTitle id="data-backfill-queue-title">Backfill queue</CardTitle>
                <CardDescription>Run or inspect historical repairs that support market-data quality and export readiness.</CardDescription>
              </div>
              <Button size="sm" onClick={vm.openBackfillDialog}>
                Trigger backfill
              </Button>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {vm.backfillSection.hasRows ? vm.backfillSection.rows.map((backfill) => (
              <button
                key={backfill.jobId}
                id={backfill.rowId}
                type="button"
                aria-label={backfill.ariaLabel}
                aria-pressed={backfill.selected}
                aria-controls={backfill.detailPanelId}
                aria-expanded={backfill.expanded}
                aria-describedby={`${backfill.rowId}-detail`}
                className={cn(
                  "w-full rounded-lg border px-3 py-3 text-left text-sm transition-colors duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                  backfill.selected ? "border-primary/50 bg-primary/10" : "border-border/70 bg-secondary/25"
                )}
                onClick={() => vm.selectBackfill(backfill.jobId)}
              >
                <div className="flex items-center justify-between gap-3">
                  <span className="font-mono font-semibold">{backfill.jobId}</span>
                  <div className="flex items-center gap-2">
                    <Badge
                      variant={backfill.status === "Review" ? "warning" : backfill.status === "Running" ? "default" : "outline"}
                    >
                      {backfill.status}
                    </Badge>
                    <span className="font-mono text-xs text-muted-foreground">{backfill.progress}</span>
                  </div>
                </div>
                <p className="mt-1 text-muted-foreground">{backfill.scope}</p>
                <div className="mt-2 h-1 rounded-full bg-border/70">
                  <div className="h-1 rounded-full bg-primary transition-all" style={{ width: backfill.progress }} />
                </div>
                <span id={`${backfill.rowId}-detail`} className="sr-only">{backfill.detailDescription}</span>
              </button>
            )) : (
              <EmptyState state={vm.backfillSection.emptyState} />
            )}
          </CardContent>
        </Card>

        <Card aria-labelledby="data-recent-exports-title">
          <CardHeader>
            <CardTitle id="data-recent-exports-title">Recent exports</CardTitle>
            <CardDescription>Latest package and reporting outputs tied to data operations evidence.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {vm.exportSection.hasRows ? vm.exportSection.rows.map((item) => (
              <div
                key={item.exportId}
                role="group"
                className={cn("rounded-md border p-3", exportToneClass[item.statusTone])}
                aria-label={item.ariaLabel}
              >
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <span className="font-semibold">{item.profile}</span>
                    <p className="mt-1 text-sm text-muted-foreground">{item.summaryText}</p>
                  </div>
                  <Badge variant={item.statusVariant} dot>{item.statusLabel}</Badge>
                </div>
                <dl className="mt-3 grid gap-2 sm:grid-cols-2">
                  {item.detailFields.map((field) => (
                    <FieldTile key={field.id} field={field} />
                  ))}
                </dl>
                <div className="mt-3 rounded-md border border-border/60 bg-background/45 px-3 py-2">
                  <div className="eyebrow-label">Next action</div>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{item.actionText}</p>
                </div>
              </div>
            )) : (
              <EmptyState state={vm.exportSection.emptyState} />
            )}
          </CardContent>
        </Card>
      </section>

      <ProviderSetupDialog vm={vm} />
      <BackfillTriggerDialog vm={vm} />
    </div>
  );
}

type DataOperationsVm = ReturnType<typeof useDataOperationsViewModel>;

function DataOperationsLoadingPanel({ state }: { state: DataOperationsLoadingState }) {
  return (
    <section
      role={state.role}
      aria-live={state.ariaLive}
      aria-busy={state.ariaBusy}
      aria-label={state.regionLabel}
      className="panel-surface-strong grid gap-4 px-4 py-4 lg:grid-cols-[1fr_auto]"
    >
      <div className="min-w-0">
        <div className="eyebrow-label">Data lane</div>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <span className="inline-flex h-2.5 w-2.5 animate-pulse rounded-full bg-primary" aria-hidden="true" />
          <h2 className="font-display text-[1.375rem] font-semibold leading-tight text-foreground">
            {state.title}
          </h2>
          <Badge variant="warning">{state.statusLabel}</Badge>
        </div>
        <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">{state.description}</p>
        <p className="mt-2 max-w-3xl text-sm leading-6 text-foreground">{state.detail}</p>
        <div className="mt-4 flex flex-wrap gap-2" aria-label="Data loading dependencies">
          {state.chips.map((chip) => (
            <span key={chip.label} className="toolbar-chip">
              <span className="text-muted-foreground">{chip.label}</span>
              <span className="font-mono text-warning">{chip.value}</span>
            </span>
          ))}
        </div>
      </div>
      <div className="flex flex-wrap items-start gap-2 lg:justify-end">
        {state.actions.map((action) => (
          <Button key={action.id} asChild variant={action.variant} size="sm">
            <Link to={action.href} aria-label={action.ariaLabel}>
              {action.id === "settings" ? (
                <DatabaseZap className="h-4 w-4" aria-hidden="true" />
              ) : (
                <RadioTower className="h-4 w-4" aria-hidden="true" />
              )}
              {action.label}
            </Link>
          </Button>
        ))}
        <RefreshCcw className="mt-2 h-4 w-4 animate-spin text-primary" aria-hidden="true" />
      </div>
    </section>
  );
}

function ProviderEmptyState({
  state,
  onSetup
}: {
  state: DataOperationsEmptyState;
  onSetup: () => void;
}) {
  return (
    <div
      role="status"
      className="rounded-lg border border-dashed border-border/80 bg-secondary/20 px-3 py-4 text-sm text-muted-foreground"
    >
      <div className="font-semibold text-foreground">{state.title}</div>
      <p className="mt-1 leading-6">{state.description}</p>
      <Button type="button" variant="outline" size="sm" className="mt-3" onClick={onSetup}>
        <Plus className="h-3.5 w-3.5" aria-hidden="true" />
        Add provider
      </Button>
    </div>
  );
}

function EmptyState({ state }: { state: DataOperationsEmptyState }) {
  return (
    <div
      role="status"
      className="rounded-lg border border-dashed border-border/80 bg-secondary/20 px-3 py-4 text-sm text-muted-foreground"
    >
      <div className="font-semibold text-foreground">{state.title}</div>
      <p className="mt-1 leading-6">{state.description}</p>
    </div>
  );
}

function ProviderSetupDialog({ vm }: { vm: DataOperationsVm }) {
  return (
    <Dialog open={vm.providerSetupOpen} onOpenChange={(open) => { if (!open) vm.closeProviderSetup(); }}>
      <DialogContent aria-labelledby={vm.providerSetupDialogState.titleId} aria-describedby={vm.providerSetupDialogState.descriptionId}>
        <div className="flex items-start justify-between gap-4">
          <DialogHeader className="mb-0">
            <div className="eyebrow-label">Data providers</div>
            <DialogTitle id={vm.providerSetupDialogState.titleId}>Configure provider</DialogTitle>
            <DialogDescription id={vm.providerSetupDialogState.descriptionId}>
              Register a data or brokerage provider with Meridian. The backend will verify credentials on save.
            </DialogDescription>
          </DialogHeader>
          <DialogCloseButton
            label={vm.providerSetupDialogState.closeButtonLabel}
            disabled={vm.providerPhase === "submitting"}
            disabledReason={vm.providerSetupDialogState.closeButtonDisabledReason}
            onClick={vm.closeProviderSetup}
          />
        </div>

        {vm.providerPhase === "success" && vm.providerSetupResult ? (
          <div className="mt-5">
            <div className="flex items-center gap-3 rounded-lg border border-success/35 bg-success/10 px-4 py-4">
              <CheckCircle2 className="h-5 w-5 shrink-0 text-success" aria-hidden="true" />
              <div>
                <div className="font-semibold text-success">{vm.providerSetupResult.providerName} configured</div>
                <p className="mt-1 text-sm text-muted-foreground">{vm.providerSetupResult.message}</p>
              </div>
            </div>
            <div
              className="mt-4 rounded-lg border border-border/70 bg-secondary/25 px-3 py-3"
              role="region"
              aria-label={vm.providerSetupDialogState.successPanel.ariaLabel}
            >
              <div className="eyebrow-label">{vm.providerSetupDialogState.successPanel.title}</div>
              <div className="mt-3 grid gap-2 sm:grid-cols-2">
                {vm.providerSetupDialogState.successActions.map((action) => (
                  <ProviderSetupNextAction
                    key={action.id}
                    action={action}
                    onNavigate={vm.closeProviderSetup}
                  />
                ))}
              </div>
            </div>
            <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
              <Button variant="outline" onClick={vm.closeProviderSetup}>Done</Button>
              <Button onClick={vm.openProviderSetup}>Configure another</Button>
            </div>
          </div>
        ) : (
          <>
            <div className="mt-5 grid gap-4" role="group" aria-label={vm.providerSetupDialogState.formLabel}>
              <label htmlFor="provider-setup-kind" className="grid gap-1 text-sm">
                {vm.providerSetupDialogState.providerKindField.label}
                <select
                  id={vm.providerSetupDialogState.providerKindField.id}
                  className="rounded-md border border-border bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  value={vm.providerForm.kind}
                  aria-label={vm.providerSetupDialogState.providerKindField.ariaLabel}
                  disabled={vm.providerSetupDialogState.providerKindField.disabled}
                  title={vm.providerSetupDialogState.providerKindField.disabledReason ?? undefined}
                  onChange={(e) => vm.updateProviderForm("kind", e.target.value)}
                >
                  {vm.providerSetupDialogState.providerKindField.options.map((p) => (
                    <option key={p.value} value={p.value}>{p.label}</option>
                  ))}
                </select>
                <span className="text-xs text-muted-foreground">
                  {vm.providerSetupDialogState.providerKindField.description}
                </span>
              </label>

              <label htmlFor={vm.providerSetupDialogState.displayNameField.id} className="grid gap-1 text-sm">
                {vm.providerSetupDialogState.displayNameField.label}
                <input
                  id={vm.providerSetupDialogState.displayNameField.id}
                  className="rounded-md border border-border bg-background px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  value={vm.providerSetupDialogState.displayNameField.value}
                  aria-label={vm.providerSetupDialogState.displayNameField.ariaLabel}
                  disabled={vm.providerSetupDialogState.displayNameField.disabled}
                  title={vm.providerSetupDialogState.displayNameField.disabledReason ?? undefined}
                  onChange={(e) => vm.updateProviderForm(vm.providerSetupDialogState.displayNameField.field, e.target.value)}
                />
              </label>

              {vm.providerSetupDialogState.credentialFields.map((field) => (
                <label key={field.id} htmlFor={field.id} className="grid gap-1 text-sm">
                  {field.label}
                  <input
                    id={field.id}
                    type={field.type}
                    autoComplete={field.autoComplete}
                    className="rounded-md border border-border bg-background px-3 py-2 font-mono focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                    value={field.value}
                    aria-label={field.ariaLabel}
                    placeholder={field.placeholder ?? undefined}
                    disabled={field.disabled}
                    title={field.disabledReason ?? undefined}
                    onChange={(e) => vm.updateProviderForm(field.field, e.target.value)}
                  />
                </label>
              ))}

              <fieldset>
                <legend className="mb-2 text-sm">Capabilities</legend>
                <div className="grid gap-2 sm:grid-cols-2">
                  {vm.providerSetupDialogState.capabilityOptions.map((cap) => (
                    <label
                      key={cap.id}
                      className={cn(
                        "flex cursor-pointer items-start gap-3 rounded-md border px-3 py-2.5 transition-colors",
                        cap.selected
                          ? "border-primary/40 bg-primary/[0.08]"
                          : "border-border/70 bg-secondary/20 hover:bg-secondary/35"
                      )}
                    >
                      <input
                        type="checkbox"
                        className="mt-0.5 shrink-0 accent-[hsl(var(--primary))]"
                        checked={cap.selected}
                        disabled={cap.disabled}
                        title={cap.disabledReason ?? undefined}
                        onChange={() => vm.toggleProviderCapability(cap.id)}
                        aria-label={cap.label}
                      />
                      <div className="min-w-0">
                        <div className="text-sm font-medium">{cap.label}</div>
                        <div className="text-xs text-muted-foreground">{cap.description}</div>
                      </div>
                    </label>
                  ))}
                </div>
              </fieldset>
            </div>

            <div
              id="provider-setup-status"
              role="status"
              aria-live="polite"
              className="mt-4 rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-xs leading-5 text-muted-foreground"
            >
              {vm.providerSetupDialogState.statusLabel}
            </div>

            {vm.providerSetupError && (
              <div role="alert" className="mt-3 flex items-start gap-2 rounded-lg border border-danger/35 bg-danger/10 px-3 py-2.5 text-sm text-danger">
                <XCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                <span>{vm.providerSetupError}</span>
              </div>
            )}

            <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
              <Button variant="outline" onClick={vm.closeProviderSetup} disabled={vm.providerPhase === "submitting"}>
                Cancel
              </Button>
              <Button
                onClick={() => void vm.submitProviderSetup()}
                disabled={vm.providerSetupDialogState.submitAction.disabled}
                disabledReason={vm.providerSetupDialogState.submitAction.disabledReason}
                busy={vm.providerSetupDialogState.submitAction.busy}
                busyLabel={vm.providerSetupDialogState.submitAction.busyLabel}
                aria-label={vm.providerSetupDialogState.submitAction.ariaLabel}
              >
                {vm.providerSetupDialogState.submitAction.label}
              </Button>
            </div>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

function ProviderSetupNextAction({
  action,
  onNavigate
}: {
  action: ProviderSetupNextActionState;
  onNavigate: () => void;
}) {
  const Icon = providerSetupNextActionIcons[action.id];

  return (
    <Button asChild variant={action.variant} size="sm" className="justify-start">
      <Link to={action.href} aria-label={action.ariaLabel} onClick={onNavigate}>
        <Icon className="h-4 w-4" aria-hidden="true" />
        {action.label}
      </Link>
    </Button>
  );
}

const providerSetupNextActionIcons: Record<ProviderSetupNextActionState["id"], LucideIcon> = {
  "live-quotes": RadioTower,
  backfill: TimerReset,
  readiness: ShieldCheck,
  "security-master": DatabaseZap
};

function BackfillTriggerDialog({ vm }: { vm: DataOperationsVm }) {
  return (
    <Dialog open={vm.dialogOpen} onOpenChange={(open) => { if (!open) vm.closeBackfillDialog(); }}>
      <DialogContent
        aria-labelledby={vm.dialogState.titleId}
        aria-describedby={vm.dialogState.descriptionId}
        className="max-w-xl p-6"
      >
        <div className="flex items-start justify-between gap-4">
          <DialogHeader className="mb-0">
            <div className="eyebrow-label">Backfill</div>
            <DialogTitle id={vm.dialogState.titleId}>Trigger backfill</DialogTitle>
            <DialogDescription id={vm.dialogState.descriptionId}>
              Preview the request before writing historical bars.
            </DialogDescription>
          </DialogHeader>
          <DialogCloseButton
            label={vm.dialogState.closeButtonLabel}
            disabled={vm.busy}
            disabledReason={vm.dialogState.closeButtonDisabledReason}
            onClick={vm.closeBackfillDialog}
          />
        </div>

        <dl className="mt-5 grid gap-2 rounded-lg border border-border/80 bg-secondary/20 p-3 sm:grid-cols-3">
          {vm.dialogState.summaryItems.map((item) => (
            <div key={item.id} className="min-w-0">
              <dt className="font-mono text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{item.label}</dt>
              <dd className={cn("mt-1 truncate font-mono text-xs", item.tone === "warning" ? "text-warning" : "text-foreground")}>
                {item.value}
              </dd>
            </div>
          ))}
        </dl>

        <div className="mt-5 grid gap-4" role="group" aria-label={vm.dialogState.formLabel}>
          <label htmlFor={vm.dialogState.providerField.id} className="grid gap-1 text-sm">
            {vm.dialogState.providerField.label}
            <input
              id={vm.dialogState.providerField.id}
              className="min-h-11 rounded-md border border-border bg-background px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
              value={vm.form.provider}
              aria-label={vm.dialogState.providerField.ariaLabel}
              placeholder={vm.dialogState.providerField.placeholder}
              disabled={vm.dialogState.providerField.disabled}
              title={vm.dialogState.providerField.disabledReason ?? undefined}
              onChange={(event) => vm.updateBackfillForm("provider", event.target.value)}
            />
          </label>
          <label htmlFor={vm.dialogState.symbolsField.id} className="grid gap-1 text-sm">
            {vm.dialogState.symbolsField.label}
            <input
              id={vm.dialogState.symbolsField.id}
              className="min-h-12 rounded-md border border-border bg-background px-3 py-2 font-mono focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
              placeholder={vm.dialogState.symbolsField.placeholder}
              value={vm.form.symbols}
              aria-label={vm.dialogState.symbolsField.ariaLabel}
              aria-describedby={vm.dialogState.symbolsField.describedBy}
              aria-invalid={vm.validationError !== null}
              disabled={vm.dialogState.symbolsField.disabled}
              title={vm.dialogState.symbolsField.disabledReason ?? undefined}
              data-dialog-autofocus={vm.dialogState.symbolsField.autoFocus ? "" : undefined}
              onChange={(event) => vm.updateBackfillForm("symbols", event.target.value)}
            />
            <span id="backfill-symbols-help" className="text-xs text-muted-foreground">{vm.symbolsHelpText}</span>
          </label>
          <div className="grid gap-3 md:grid-cols-2">
            <label htmlFor={vm.dialogState.fromField.id} className="grid gap-1 text-sm">
              {vm.dialogState.fromField.label}
              <input
                id={vm.dialogState.fromField.id}
                type="date"
                className="min-h-11 rounded-md border border-border bg-background px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
                value={vm.form.from}
                aria-label={vm.dialogState.fromField.ariaLabel}
                disabled={vm.dialogState.fromField.disabled}
                title={vm.dialogState.fromField.disabledReason ?? undefined}
                onChange={(event) => vm.updateBackfillForm("from", event.target.value)}
              />
            </label>
            <label htmlFor={vm.dialogState.toField.id} className="grid gap-1 text-sm">
              {vm.dialogState.toField.label}
              <input
                id={vm.dialogState.toField.id}
                type="date"
                className="min-h-11 rounded-md border border-border bg-background px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-60"
                value={vm.form.to}
                aria-label={vm.dialogState.toField.ariaLabel}
                disabled={vm.dialogState.toField.disabled}
                title={vm.dialogState.toField.disabledReason ?? undefined}
                onChange={(event) => vm.updateBackfillForm("to", event.target.value)}
              />
            </label>
          </div>
        </div>

        <div
          id="backfill-form-status"
          role="status"
          aria-live="polite"
          className={cn(
            "mt-4 rounded-md border px-3 py-2 text-xs leading-5",
            vm.dialogState.formStatusTone === "danger"
              ? "border-danger/40 bg-danger/10 text-danger"
              : vm.dialogState.formStatusTone === "success"
                ? "border-success/35 bg-success/10 text-success"
                : vm.dialogState.formStatusTone === "warning"
                  ? "border-warning/35 bg-warning/10 text-warning"
                  : "border-border/70 bg-secondary/25 text-muted-foreground"
          )}
        >
          {vm.dialogState.formStatusLabel}
        </div>

        {vm.feedbackText && (
          <div
            id="backfill-form-feedback"
            role="alert"
            className={cn(
              "mt-4 rounded-lg border px-3 py-2 text-sm",
              vm.feedbackTone === "warning"
                ? "border-warning/40 bg-warning/10 text-warning"
                : "border-danger/40 bg-danger/10 text-danger"
            )}
          >
            {vm.feedbackText}
          </div>
        )}
        <span className="sr-only" aria-live="polite">{vm.statusAnnouncement}</span>
        {vm.previewResultCard && <BackfillResultCard state={vm.previewResultCard} />}
        {vm.runResultCard && <BackfillResultCard state={vm.runResultCard} />}

        <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button
            variant="outline"
            onClick={() => void vm.previewBackfill()}
            disabled={vm.dialogState.previewAction.disabled}
            disabledReason={vm.dialogState.previewAction.disabledReason}
            busy={vm.dialogState.previewAction.busy}
            busyLabel={vm.dialogState.previewAction.busyLabel}
            aria-label={vm.dialogState.previewAction.ariaLabel}
          >
            {vm.dialogState.previewAction.label}
          </Button>
          {vm.preview && (
            <Button
              onClick={() => void vm.runBackfill()}
              disabled={vm.dialogState.runAction.disabled}
              disabledReason={vm.dialogState.runAction.disabledReason}
              busy={vm.dialogState.runAction.busy}
              busyLabel={vm.dialogState.runAction.busyLabel}
              aria-label={vm.dialogState.runAction.ariaLabel}
            >
              {vm.dialogState.runAction.label}
            </Button>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function RouteFocusCard({
  state
}: {
  state: DataOperationsRouteFocusCardState;
}) {
  return (
    <Card id={state.id} role={state.role} aria-label={state.ariaLabel} className="panel-surface-strong">
      <CardHeader>
        <div className="eyebrow-label">{state.eyebrow}</div>
        <CardTitle>{state.title}</CardTitle>
        <CardDescription>{state.description}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3 text-sm">
        {state.rows.length > 0 ? (
          <dl className="space-y-2">
            {state.rows.map((row) => (
              <DetailRow key={row.id} label={row.label} value={row.value} />
            ))}
          </dl>
        ) : (
          <p role="status" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm leading-6 text-warning">
            {state.description}
          </p>
        )}
        {state.action ? (
          <Button asChild variant="outline" className="w-full justify-center">
            <Link to={state.action.href} aria-label={state.action.ariaLabel}>
              {state.action.label}
            </Link>
          </Button>
        ) : null}
      </CardContent>
    </Card>
  );
}


function DataHighlight({ icon: Icon, title, description }: { icon: LucideIcon; title: string; description: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-secondary/30 p-4">
      <Icon className="mb-3 h-5 w-5 text-primary" />
      <div className="font-semibold">{title}</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{description}</p>
    </div>
  );
}

function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-lg border border-border/70 bg-secondary/40 px-3 py-2">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-mono text-foreground">{value}</dd>
    </div>
  );
}

function FieldTile({ field }: { field: { id: string; label: string; value: string } }) {
  return (
    <div className="rounded-md border border-border/60 bg-background/45 px-2.5 py-2">
      <div className="eyebrow-label">{field.label}</div>
      <div className="mt-1 truncate font-mono text-xs text-foreground">{field.value}</div>
    </div>
  );
}

const providerToneClass: Record<"success" | "warning" | "danger", string> = {
  success: "border-border/70 bg-secondary/20",
  warning: "border-warning/35 bg-warning/5",
  danger: "border-danger/35 bg-danger/5",
};

const providerStatusTextClass: Record<"success" | "warning" | "danger", string> = {
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
};

const resultToneClass: Record<BackfillResultCardState["tone"], string> = {
  warning: "border-warning/35 bg-warning/10 text-warning",
  success: "border-success/35 bg-success/10 text-success",
  danger: "border-danger/35 bg-danger/10 text-danger"
};

const exportToneClass = {
  success: "border-success/30 bg-success/5",
  warning: "border-warning/30 bg-warning/5",
  paper: "border-paper/30 bg-paper/5"
} as const;

function BackfillResultCard({ state }: { state: BackfillResultCardState }) {
  return (
    <div
      role="status"
      aria-label={state.ariaLabel}
      className={cn("mt-4 rounded-md border p-3 text-sm", resultToneClass[state.tone])}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="font-semibold">{state.title}</div>
        <div className="font-mono text-xs">{state.statusLabel}</div>
      </div>
      <dl className="mt-3 grid gap-2 sm:grid-cols-2">
        {state.rows.map((row) => (
          <FieldTile key={row.id} field={row} />
        ))}
      </dl>
      {state.errorText && <p className="mt-3 text-xs leading-5">{state.errorText}</p>}
    </div>
  );
}
