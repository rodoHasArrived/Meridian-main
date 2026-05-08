import {
  ArrowUpRight,
  Building2,
  CheckCircle2,
  DatabaseZap,
  Download,
  FileOutput,
  FileText,
  Landmark,
  Plus,
  Printer,
  RadioTower,
  Search,
  ShieldCheck,
  Sparkles,
  TimerReset,
  XCircle
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import type { KeyboardEvent, ReactNode } from "react";
import { Link, useLocation } from "react-router-dom";
import { MetricCard } from "@/components/meridian/metric-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogCloseButton, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import { workspaceForPath } from "@/lib/workspace";
import {
  resolveSecurityMasterTabKeyCommand,
  useDataOperationsViewModel
} from "@/screens/data-operations-screen.view-model";
import type { DataOperationsWorkspaceResponse } from "@/types";
import type {
  BackfillResultCardState,
  DataOperationsBackfillDetailState,
  DataOperationsEmptyState
} from "@/screens/data-operations-screen.view-model";
import type {
  SecurityMasterAuditItem,
  SecurityMasterCompanySection,
  SecurityMasterCorporateActionRow,
  SecurityMasterDetailField,
  SecurityMasterOwnershipBar,
  SecurityMasterPrintChecklistItem,
  SecurityMasterRationaleItem,
  SecurityMasterSelectedState,
  SecurityMasterTimelineEvent,
  SecurityMasterVenueRow,
  SecurityMasterWorkspaceState
} from "@/screens/data-operations-screen.security-master";
import { SECURITY_MASTER_DEFAULT_QUERY } from "@/screens/data-operations-screen.security-master";

interface DataOperationsScreenProps {
  data: DataOperationsWorkspaceResponse | null;
}

export function DataOperationsScreen({ data }: DataOperationsScreenProps) {
  const { pathname } = useLocation();
  const workspace = workspaceForPath(pathname);
  const vm = useDataOperationsViewModel(data, pathname);

  if (!data) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Loading {workspace.label}</CardTitle>
          <CardDescription>Waiting for provider posture, security coverage, and backfill queue state.</CardDescription>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-muted-foreground">
            If this takes more than a few seconds, check your API connection in{" "}
            <span className="font-medium text-foreground">Settings</span> or use the refresh button in the toolbar.
          </p>
        </CardContent>
      </Card>
    );
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
              Security Master command deck
            </CardTitle>
            <CardDescription>
              Search, validate, and publish Meridian-ready security profiles without leaving the Data workspace.
              Company facts, corporate actions, and print/export evidence all stay on one operator surface.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 md:grid-cols-3">
            <DataHighlight
              icon={Search}
              title="Search & resolve"
              description="Match issuers, venues, and identifiers with a workstation-ready score and review trail."
            />
            <DataHighlight
              icon={Building2}
              title="Company context"
              description="Fold issuer profile, venue coverage, and ownership posture into the same evidence lane."
            />
            <DataHighlight
              icon={Printer}
              title="Packet evidence"
              description="Publish print/export packets with provider, backfill, and downstream handoff cues still visible."
            />
          </CardContent>
        </Card>

        <RouteFocusCard
          workstream={vm.workstream}
          selectedBackfillDetail={vm.selectedBackfillDetail}
          backfillDetailEmptyState={vm.backfillDetailEmptyState}
          selectedSecurity={vm.securityMaster.selectedSecurity}
          openPrintPacket={() => vm.selectSecurityMasterTab("print")}
        />
      </section>

      <section className="grid gap-4 xl:grid-cols-[0.95fr_1.45fr]">
        <SecurityMasterSearchPanel
          state={vm.securityMaster}
          onQueryChange={vm.updateSecurityMasterQuery}
          onSelect={vm.selectSecurityMaster}
          onToggleStatusFilter={vm.toggleSecurityMasterStatusFilter}
        />
        <SecurityMasterDetailCard
          state={vm.securityMaster}
          onSelectTab={vm.selectSecurityMasterTab}
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
              <ProviderEmptyState onSetup={vm.openProviderSetup} />
            )}
          </CardContent>
        </Card>

        <Card aria-labelledby="data-backfill-queue-title">
          <CardHeader>
            <div className="flex items-center justify-between gap-3">
              <div>
                <CardTitle id="data-backfill-queue-title">Backfill queue</CardTitle>
                <CardDescription>Run or inspect historical repairs that support the Security Master lane.</CardDescription>
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
            <div className="mt-5 flex justify-end gap-2">
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
                  onChange={(e) => vm.updateProviderForm(vm.providerSetupDialogState.displayNameField.field, e.target.value)}
                />
              </label>

              {vm.providerSetupDialogState.credentialFields.map((field) => (
                <label key={field.id} htmlFor={field.id} className="grid gap-1 text-sm">
                  {field.label}
                  <input
                    id={field.id}
                    type={field.type}
                    className="rounded-md border border-border bg-background px-3 py-2 font-mono focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                    value={field.value}
                    aria-label={field.ariaLabel}
                    placeholder={field.placeholder ?? undefined}
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
                          ? "border-primary/40 bg-primary/8"
                          : "border-border/70 bg-secondary/20 hover:bg-secondary/35"
                      )}
                    >
                      <input
                        type="checkbox"
                        className="mt-0.5 shrink-0 accent-[hsl(var(--primary))]"
                        checked={cap.selected}
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
              className="min-h-11 rounded-md border border-border bg-background px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              value={vm.form.provider}
              aria-label={vm.dialogState.providerField.ariaLabel}
              placeholder={vm.dialogState.providerField.placeholder}
              onChange={(event) => vm.updateBackfillForm("provider", event.target.value)}
            />
          </label>
          <label htmlFor={vm.dialogState.symbolsField.id} className="grid gap-1 text-sm">
            {vm.dialogState.symbolsField.label}
            <input
              id={vm.dialogState.symbolsField.id}
              className="min-h-12 rounded-md border border-border bg-background px-3 py-2 font-mono focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
              placeholder={vm.dialogState.symbolsField.placeholder}
              value={vm.form.symbols}
              aria-label={vm.dialogState.symbolsField.ariaLabel}
              aria-describedby={vm.dialogState.symbolsField.describedBy}
              aria-invalid={vm.validationError !== null}
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
                className="min-h-11 rounded-md border border-border bg-background px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                value={vm.form.from}
                aria-label={vm.dialogState.fromField.ariaLabel}
                onChange={(event) => vm.updateBackfillForm("from", event.target.value)}
              />
            </label>
            <label htmlFor={vm.dialogState.toField.id} className="grid gap-1 text-sm">
              {vm.dialogState.toField.label}
              <input
                id={vm.dialogState.toField.id}
                type="date"
                className="min-h-11 rounded-md border border-border bg-background px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                value={vm.form.to}
                aria-label={vm.dialogState.toField.ariaLabel}
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
  workstream,
  selectedBackfillDetail,
  backfillDetailEmptyState,
  selectedSecurity,
  openPrintPacket
}: {
  workstream: "overview" | "backfills";
  selectedBackfillDetail: DataOperationsBackfillDetailState | null;
  backfillDetailEmptyState: DataOperationsEmptyState | null;
  selectedSecurity: SecurityMasterSelectedState | null;
  openPrintPacket: () => void;
}) {
  if (workstream === "backfills") {
    return selectedBackfillDetail ? (
      <Card
        id={selectedBackfillDetail.id}
        className="bg-panel-strong text-slate-50"
        role="region"
        aria-label={selectedBackfillDetail.ariaLabel}
      >
        <CardHeader>
          <div className="eyebrow-label text-slate-300">Backfill Detail</div>
          <CardTitle>Backfill queue focus</CardTitle>
          <CardDescription className="text-slate-300">{selectedBackfillDetail.description}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-2 text-sm">
          {selectedBackfillDetail.rows.map((row) => (
            <DetailRow key={row.id} label={row.label} value={row.value} />
          ))}
        </CardContent>
      </Card>
    ) : (
      <Card>
        <CardHeader>
          <div className="eyebrow-label">Backfill Detail</div>
          <CardTitle>{backfillDetailEmptyState?.title ?? "Backfill queue focus"}</CardTitle>
          <CardDescription>{backfillDetailEmptyState?.description ?? "No backfill selected."}</CardDescription>
        </CardHeader>
      </Card>
    );
  }

  return (
    <Card className="bg-panel-strong text-slate-50">
      <CardHeader>
        <div className="eyebrow-label text-slate-300">Lane evidence</div>
        <CardTitle>Print/export evidence</CardTitle>
        <CardDescription className="text-slate-300">
          Keep the current Security Master packet, distribution plan, and downstream export cues visible while you work.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3 text-sm">
        {selectedSecurity ? (
          <>
            <div className="rounded-lg border border-slate-700/60 bg-slate-950/40 p-3">
              <div className="eyebrow-label text-slate-300">Packet</div>
              <div className="mt-1 font-mono text-sm text-slate-50">{selectedSecurity.printPacketId}</div>
              <p className="mt-2 text-sm text-slate-300">{selectedSecurity.printSummary}</p>
            </div>
            <DetailRow label="Distribution" value={selectedSecurity.printDistribution} />
            <DetailRow label="Generated" value={selectedSecurity.printGeneratedAt} />
            <DetailRow label="Export targets" value={`${selectedSecurity.exportEvidence.length} downstream lanes`} />
            <Button variant="outline" className="w-full border-slate-600/70 text-slate-50 hover:bg-slate-800/60" onClick={openPrintPacket}>
              Open print packet
            </Button>
          </>
        ) : (
          <p className="text-sm text-slate-300">Select a security to inspect its print/export packet.</p>
        )}
      </CardContent>
    </Card>
  );
}

function SecurityMasterSearchPanel({
  state,
  onQueryChange,
  onSelect,
  onToggleStatusFilter
}: {
  state: SecurityMasterWorkspaceState;
  onQueryChange: (value: string) => void;
  onSelect: (securityId: string) => void;
  onToggleStatusFilter: () => void;
}) {
  const selectedSecurity = state.selectedSecurity;

  return (
    <Card>
      <CardHeader>
        <div className="eyebrow-label">Security Master</div>
        <CardTitle className="flex items-center gap-2">
          <Search className="h-5 w-5 text-primary" />
          Search and resolve instruments
        </CardTitle>
        <CardDescription>
          Meridian-ready search results keep venue coverage, identifiers, and packet evidence visible before you drill into company or corporate action detail.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="rounded-xl border border-border/70 bg-panel-strong p-4 text-slate-50">
          <div className="flex flex-col gap-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <div className="eyebrow-label text-slate-300">Command search</div>
                <div className="mt-1 text-sm text-slate-300">
                  Search score stays primary while filters and packet posture remain visible.
                </div>
              </div>
              <Badge variant="outline" className="border-slate-700/80 bg-slate-950/50 text-slate-300">
                Ctrl K
              </Badge>
            </div>
            <label htmlFor="security-master-search" className="grid gap-2 text-sm">
              <span className="font-mono text-[11px] uppercase tracking-[0.14em] text-slate-300">Search securities</span>
              <div className="flex flex-col gap-3 rounded-xl border border-slate-700/80 bg-slate-950/55 p-3 shadow-[var(--shadow-panel)]">
                <div className="flex items-center gap-3">
                  <Search className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                  <input
                    id="security-master-search"
                    className="min-h-10 flex-1 bg-transparent font-mono text-sm text-slate-50 placeholder:text-slate-500 focus-visible:outline-none"
                    value={state.searchValue}
                    placeholder="Search by issuer, ticker, or ISIN"
                    onChange={(event) => onQueryChange(event.target.value)}
                  />
                  {state.searchValue.trim() ? (
                    <Button size="sm" variant="ghost" className="text-slate-300 hover:bg-slate-800/70 hover:text-slate-50" onClick={() => onQueryChange("")}>
                      Clear
                    </Button>
                  ) : null}
                </div>
                <div className="flex flex-wrap items-center gap-2 text-[11px] text-slate-300">
                  <Badge variant="outline" className="border-slate-700/80 bg-slate-950/50 text-slate-300">
                    {state.searchMetaLabel}
                  </Badge>
                  {state.searchSummaryBadges.map((badge) => (
                    <Badge
                      key={badge.id}
                      variant="outline"
                      aria-label={badge.ariaLabel}
                      className="border-slate-700/80 bg-slate-950/50 text-slate-300"
                    >
                      {badge.label} · {badge.value}
                    </Badge>
                  ))}
                </div>
              </div>
            </label>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button size="sm" variant="outline" onClick={onToggleStatusFilter}>
            {state.statusChipLabel}
          </Button>
          <span className="text-xs text-muted-foreground">
            Sorted by search score · coverage and packet posture visible in detail panel
          </span>
        </div>

        {state.hasResults ? (
          <div className="space-y-3">
            <div className="flex items-center justify-between gap-3 text-xs uppercase tracking-[0.14em] text-muted-foreground">
              <span>{state.resultCountLabel}</span>
              <span>Issuer · type · venue · ISIN · score</span>
            </div>
            <div className="grid gap-3 rounded-t-xl border border-border/70 bg-secondary/20 px-4 py-3 text-[11px] uppercase tracking-[0.14em] text-muted-foreground sm:grid-cols-[minmax(0,1.6fr)_minmax(110px,0.8fr)_minmax(120px,0.85fr)_minmax(150px,0.9fr)_minmax(90px,0.5fr)]">
              <span>Name · ticker</span>
              <span>Type</span>
              <span>Market</span>
              <span>ISIN</span>
              <span className="text-right">Score</span>
            </div>
            <div className="space-y-2" aria-label="Security master search results">
              {state.results.map((result) => (
                <button
                  key={result.securityId}
                  type="button"
                  aria-label={result.ariaLabel}
                  aria-pressed={result.selected}
                  className={cn(
                    "w-full rounded-xl border px-4 py-4 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                    result.selected
                      ? "border-primary/50 bg-primary/8 shadow-[inset_3px_0_0_hsl(var(--primary)),0_0_0_1px_rgba(42,178,212,0.08)]"
                      : "border-border/70 bg-secondary/20 hover:bg-secondary/35"
                  )}
                  onClick={() => onSelect(result.securityId)}
                >
                  <div className="grid gap-3 sm:grid-cols-[minmax(0,1.5fr)_minmax(110px,0.8fr)_minmax(120px,0.85fr)_minmax(150px,0.9fr)_minmax(90px,0.5fr)]">
                    <div className="min-w-0">
                      <div className="flex items-center gap-2 truncate font-semibold text-foreground">
                        <span className={cn(
                          "h-2.5 w-2.5 rounded-full",
                          result.statusVariant === "success"
                            ? "bg-success shadow-[0_0_0_3px_rgba(38,191,134,0.15)]"
                            : result.statusVariant === "warning"
                              ? "bg-warning shadow-[0_0_0_3px_rgba(214,158,56,0.15)]"
                              : "bg-muted-foreground/60 shadow-[0_0_0_3px_rgba(124,138,155,0.12)]"
                        )} />
                        <span className="truncate">{result.displayName}</span>
                      </div>
                      <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                        <span className="font-mono text-primary">{result.ticker}</span>
                        <span>{result.issuer}</span>
                      </div>
                    </div>
                    <div className="text-sm text-muted-foreground">{result.assetType}</div>
                    <div className="text-sm text-muted-foreground">
                      <div>{result.market}</div>
                      <div className="mt-1 text-xs">{result.country}</div>
                    </div>
                    <div className="font-mono text-xs text-muted-foreground">{result.isin}</div>
                    <div className="text-right">
                      <Badge variant={result.statusVariant} dot>{result.status}</Badge>
                      <div className="mt-2 font-mono text-sm text-foreground">{result.scoreText}</div>
                      <div className="mt-1 h-1 rounded-full bg-border/70">
                        <div className="h-1 rounded-full bg-primary" style={{ width: result.scoreWidth }} />
                      </div>
                      <div className="mt-2 text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
                        {result.selected ? "In focus" : "Open"}
                      </div>
                    </div>
                  </div>
                </button>
              ))}
            </div>
            {selectedSecurity ? (
              <div className="rounded-xl border border-border/70 bg-secondary/15 p-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <div className="eyebrow-label">Selected row detail</div>
                    <div className="mt-1 text-sm font-semibold text-foreground">{selectedSecurity.titleCode} · {selectedSecurity.title}</div>
                    <p className="mt-2 text-sm text-muted-foreground">
                      {selectedSecurity.subtitle}. Packet {selectedSecurity.printPacketId} routes to {selectedSecurity.exportEvidence.length} downstream lanes.
                    </p>
                  </div>
                  <Badge variant={selectedSecurity.statusVariant} dot>{selectedSecurity.status}</Badge>
                </div>
              </div>
            ) : null}
          </div>
        ) : state.emptyState ? (
          <div className="space-y-3">
            <EmptyState state={state.emptyState} />
            <div className="flex flex-wrap gap-2">
              <Button size="sm" variant="outline" onClick={() => onQueryChange(SECURITY_MASTER_DEFAULT_QUERY)}>
                Reset to default search
              </Button>
              {state.statusFilter === "active" ? (
                <Button size="sm" variant="outline" onClick={onToggleStatusFilter}>
                  Show all statuses
                </Button>
              ) : null}
            </div>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

function SecurityMasterDetailCard({
  state,
  onSelectTab
}: {
  state: SecurityMasterWorkspaceState;
  onSelectTab: (tab: "overview" | "company" | "corporate-actions" | "print") => void;
}) {
  const selected = state.selectedSecurity;
  const activeTab = state.tabs.find((tab) => tab.selected)?.id ?? "overview";
  const packetStatus = selected?.printPacketState ?? null;
  const handleTabKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const nextTab = resolveSecurityMasterTabKeyCommand(state.tabs, activeTab, event.key);
    if (!nextTab) {
      return;
    }

    event.preventDefault();
    onSelectTab(nextTab);
    window.requestAnimationFrame(() => {
      document.getElementById(`security-master-tab-${nextTab}`)?.focus();
    });
  };

  return (
    <Card className="overflow-hidden">
      {selected ? (
        <>
          <CardHeader className="border-b border-border/70 bg-secondary/20">
            <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
              <div>
                <div className="eyebrow-label">
                  Reference data / {selected.assetType} / {selected.subtitle} / {activeTab.replace("-", " ")}
                </div>
                <div className="mt-2 flex flex-wrap items-center gap-3">
                  <CardTitle className="text-2xl">{selected.title}</CardTitle>
                  <span className="rounded-md bg-primary/10 px-2.5 py-1 font-mono text-sm text-primary">{selected.titleCode}</span>
                </div>
                <p className="mt-2 text-sm text-muted-foreground">{selected.subtitle}</p>
                <p className="mt-3 max-w-3xl text-sm leading-6 text-muted-foreground">{selected.description}</p>
                <div className="mt-3 flex flex-wrap gap-2">
                  <Badge variant={selected.statusVariant} dot>{selected.status}</Badge>
                  <Badge variant="outline">{selected.assetType}</Badge>
                  <Badge variant="default">Score {selected.scoreText}</Badge>
                  {packetStatus ? <Badge variant={packetStatus.statusVariant}>{packetStatus.statusLabel}</Badge> : null}
                </div>
              </div>
              <div className="space-y-3 lg:max-w-xs">
                <div className="rounded-xl border border-border/70 bg-background/80 p-4">
                  <div className="eyebrow-label">Packet posture</div>
                  <div className="mt-2 font-mono text-sm text-foreground">{selected.printPacketId}</div>
                  <div className="mt-2 text-sm text-muted-foreground">
                    {selected.exportEvidence.length} export lanes · {selected.printChecklist.length} sign-off steps
                  </div>
                </div>
                <div className="flex flex-wrap gap-2">
                  <Button size="sm" variant="outline" onClick={() => onSelectTab("company")}>
                    <Building2 className="h-3.5 w-3.5" />
                    Company view
                  </Button>
                  <Button size="sm" variant="outline" onClick={() => onSelectTab("corporate-actions")}>
                    <TimerReset className="h-3.5 w-3.5" />
                    Corporate actions
                  </Button>
                  <Button size="sm" onClick={() => onSelectTab("print")}>
                    <FileOutput className="h-3.5 w-3.5" />
                    Print packet
                  </Button>
                </div>
              </div>
            </div>
          </CardHeader>

          <CardContent className="p-0">
            <div className="grid gap-px border-b border-border/70 bg-border/70 sm:grid-cols-2 xl:grid-cols-6">
              {selected.summaryFields.map((field) => (
                <div key={field.id} className="bg-background px-4 py-3">
                  <div className="eyebrow-label">{field.label}</div>
                  <div className="mt-1 font-mono text-sm text-foreground">{field.value}</div>
                </div>
              ))}
            </div>

            <div className="border-b border-border/70 bg-secondary/20">
              <div
                role="tablist"
                aria-label="Security master detail views"
                className="flex flex-wrap gap-1 px-4 py-2"
                onKeyDown={handleTabKeyDown}
              >
                {state.tabs.map((tab) => (
                  <button
                    key={tab.id}
                    id={`security-master-tab-${tab.id}`}
                    type="button"
                    role="tab"
                    aria-selected={tab.selected}
                    aria-controls={tab.panelId}
                    aria-label={tab.selectAriaLabel}
                    tabIndex={tab.tabIndex}
                    className={cn(
                      "inline-flex items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                      tab.selected ? "bg-primary/10 text-primary" : "text-muted-foreground hover:bg-secondary/60 hover:text-foreground"
                    )}
                    onClick={() => onSelectTab(tab.id)}
                  >
                    <span>{tab.label}</span>
                    {tab.badge ? <span className="font-mono text-[11px]">{tab.badge}</span> : null}
                  </button>
                ))}
              </div>
            </div>

            {activeTab === "overview" ? (
              <SecurityOverviewPanel selected={selected} />
            ) : activeTab === "company" ? (
              <SecurityCompanyPanel selected={selected} />
            ) : activeTab === "corporate-actions" ? (
              <SecurityCorporateActionsPanel selected={selected} />
            ) : (
              <SecurityPrintPanel selected={selected} />
            )}
          </CardContent>
        </>
      ) : (
        <CardContent className="p-6">
          <EmptyState
            state={{
              title: "No security selected",
              description: "Search the Security Master and choose a result to inspect its company, corporate action, and packet evidence views."
            }}
          />
        </CardContent>
      )}
    </Card>
  );
}

function SecurityOverviewPanel({ selected }: { selected: SecurityMasterSelectedState }) {
  return (
    <div
      id="security-master-panel-overview"
      role="tabpanel"
      aria-labelledby="security-master-tab-overview"
      className="grid gap-4 p-4 xl:grid-cols-[1fr_320px]"
    >
      <div className="space-y-4">
        <div className="grid gap-3 md:grid-cols-4">
          <SummaryPill label="Identifier groups" value={`${selected.identifierSections.length}`} />
          <SummaryPill label="Active venues" value={`${selected.venues.length}`} />
          <SummaryPill label="Audit steps" value={`${selected.auditTrail.length}`} />
          <SummaryPill label="Packet" value={selected.printPacketId} mono />
        </div>
        {selected.identifierSections.map((section) => (
          <div key={section.id} className="rounded-xl border border-border/70 bg-background/80">
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border/60 bg-secondary/20 px-4 py-3">
              <div className="flex items-center gap-3">
                <span className="font-mono text-xs text-primary">{section.badge}</span>
                <div className="font-semibold text-foreground">{section.title}</div>
              </div>
              <span className="text-xs text-muted-foreground">{section.meta}</span>
            </div>
            <dl className="grid gap-px bg-border/60 sm:grid-cols-2">
              {section.rows.map((row) => (
                <div key={row.id} className="bg-background px-4 py-3">
                  <dt className="text-xs text-muted-foreground">{row.label}</dt>
                  <dd className="mt-1 break-all font-mono text-sm text-foreground">{row.value}</dd>
                </div>
              ))}
            </dl>
          </div>
        ))}
      </div>

      <div className="space-y-4">
        <RailCard title="Audit trail" icon={ShieldCheck}>
          <SecurityAuditList items={selected.auditTrail} />
        </RailCard>
        <RailCard title="Active venues" icon={Landmark}>
          <SecurityVenueList venues={selected.venues} />
        </RailCard>
        <RailCard title="Match rationale" icon={Sparkles}>
          <SecurityRationaleList items={selected.rationale} />
        </RailCard>
      </div>
    </div>
  );
}

function SecurityCompanyPanel({ selected }: { selected: SecurityMasterSelectedState }) {
  return (
    <div
      id="security-master-panel-company"
      role="tabpanel"
      aria-labelledby="security-master-tab-company"
      className="grid gap-4 p-4 xl:grid-cols-[1fr_320px]"
    >
      <div className="space-y-4">
        <div className="grid gap-3 md:grid-cols-4">
          {selected.companyMetrics.map((field) => (
            <SummaryPill key={field.id} label={field.label} value={field.value} tone={field.tone} mono />
          ))}
        </div>
        <div className="rounded-xl border border-border/70 bg-background/80 p-5">
          <div className="flex items-start gap-4">
            <div className="flex h-14 w-14 items-center justify-center rounded-xl border border-primary/20 bg-primary/10 font-display text-xl font-semibold text-primary">
              {selected.companyCard.logoText}
            </div>
            <div className="min-w-0">
              <h3 className="text-lg font-semibold text-foreground">{selected.companyCard.title}</h3>
              <p className="mt-1 text-sm text-muted-foreground">{selected.companyCard.subtitle}</p>
              <p className="mt-3 text-sm leading-6 text-muted-foreground">{selected.companyCard.description}</p>
              <div className="mt-3 flex flex-wrap gap-2">
                {selected.companyTags.map((tag) => <Badge key={tag} variant="outline">{tag}</Badge>)}
              </div>
            </div>
          </div>
        </div>

        {selected.companySections.map((section) => (
          <SecuritySectionCard key={section.id} section={section} />
        ))}
      </div>

      <div className="space-y-4">
        <RailCard title="Company metrics" icon={Building2}>
          <MetricFieldList fields={selected.companyMetrics} />
        </RailCard>
        <RailCard title="Ownership posture" icon={Landmark}>
          <OwnershipBars bars={selected.ownership} />
        </RailCard>
        <RailCard title="Venue distribution" icon={ArrowUpRight}>
          <SecurityVenueList venues={selected.venues} />
        </RailCard>
      </div>
    </div>
  );
}

function SecurityCorporateActionsPanel({ selected }: { selected: SecurityMasterSelectedState }) {
  return (
    <div
      id="security-master-panel-corporate-actions"
      role="tabpanel"
      aria-labelledby="security-master-tab-corporate-actions"
      className="grid gap-4 p-4 xl:grid-cols-[1fr_320px]"
    >
      <div className="space-y-4">
        <div className="rounded-xl border border-primary/25 bg-gradient-to-br from-primary/15 via-primary/5 to-transparent p-5">
          <div className="grid gap-4 md:grid-cols-[1fr_auto] md:items-center">
            <div>
              <div className="eyebrow-label text-primary">{selected.upcomingActionLabel}</div>
              <h3 className="mt-2 text-lg font-semibold text-foreground">{selected.upcomingActionTitle}</h3>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{selected.upcomingActionDescription}</p>
            </div>
            <div className="rounded-xl border border-primary/25 bg-background/80 px-4 py-3 text-center">
              <div className="font-mono text-2xl text-foreground">{selected.upcomingActionCountdown}</div>
              <div className="mt-1 text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{selected.upcomingActionCountdownLabel}</div>
            </div>
          </div>
        </div>

        <div className="rounded-xl border border-border/70 bg-background/80 p-5">
          <div className="flex items-center justify-between gap-3">
            <div className="font-semibold text-foreground">Event timeline</div>
            <div className="text-xs text-muted-foreground">Sequence from declaration through packet evidence</div>
          </div>
          <div className="mt-5 grid gap-3 sm:grid-cols-5">
            {selected.corporateTimeline.map((event) => (
              <TimelineStep key={event.id} event={event} />
            ))}
          </div>
        </div>

        <div className="overflow-hidden rounded-xl border border-border/70 bg-background/80">
          <div className="grid gap-3 border-b border-border/70 bg-secondary/20 px-4 py-3 text-xs uppercase tracking-[0.14em] text-muted-foreground sm:grid-cols-[120px_1.3fr_110px_110px_100px_90px]">
            <span>Type</span>
            <span>Event</span>
            <span>Announced</span>
            <span>Effective</span>
            <span className="text-right">Amount</span>
            <span>Status</span>
          </div>
          <div className="divide-y divide-border/60">
            {selected.corporateActions.map((row) => (
              <CorporateActionRow key={row.id} row={row} />
            ))}
          </div>
        </div>
      </div>

      <div className="space-y-4">
        <RailCard title="Corporate action posture" icon={TimerReset}>
          <MetricFieldList fields={selected.corporateActionStats} />
        </RailCard>
        <RailCard title="Linked filings" icon={FileText}>
          <div className="space-y-2">
            {selected.filings.map((filing) => (
              <div key={filing.id} className="rounded-lg border border-border/60 bg-secondary/20 px-3 py-3">
                <div className="flex items-center justify-between gap-3">
                  <span className="font-mono text-xs text-primary">{filing.form}</span>
                  <ArrowUpRight className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
                </div>
                <div className="mt-2 text-sm text-foreground">{filing.description}</div>
                <div className="mt-1 text-xs text-muted-foreground">{filing.meta}</div>
              </div>
            ))}
          </div>
        </RailCard>
      </div>
    </div>
  );
}

function SecurityPrintPanel({ selected }: { selected: SecurityMasterSelectedState }) {
  const packet = selected.printPacketState;

  return (
    <div
      id="security-master-panel-print"
      role="tabpanel"
      aria-labelledby="security-master-tab-print"
      className="grid gap-4 p-4 xl:grid-cols-[1fr_320px]"
    >
      <div className="space-y-4">
        <div className="rounded-xl border border-border/70 bg-panel-strong p-5 text-slate-50">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <div className="eyebrow-label text-slate-300">Print packet</div>
              <h3 className="mt-2 text-lg font-semibold">{selected.printPacketId}</h3>
              <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-300">{selected.printSummary}</p>
            </div>
            <Badge variant={packet.statusVariant} dot>{packet.statusLabel}</Badge>
          </div>
          <dl className="mt-4 grid gap-3 sm:grid-cols-3">
            <DetailRow label="Generated" value={selected.printGeneratedAt} inverted />
            <DetailRow label="Distribution" value={selected.printDistribution} inverted />
            <DetailRow label="Sections" value={`${selected.printSections.length}`} inverted />
          </dl>
        </div>

        <div className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
          <div className="rounded-xl border border-border/70 bg-background/80 p-5">
            <div className="flex items-center justify-between gap-3">
              <div className="font-semibold text-foreground">Print preview</div>
              <Badge variant="outline">Page-ready layout</Badge>
            </div>
            <div className="mt-4 rounded-xl border border-border/60 bg-secondary/10 p-4">
              <div className="security-master-print-preview">
                <div className="security-master-print-sheet">
                  <div className="security-master-print-sheet__eyebrow">Meridian // security master</div>
                  <div className="security-master-print-sheet__title">{selected.title}</div>
                  <div className="security-master-print-sheet__subtitle">{selected.titleCode} · {selected.subtitle}</div>
                  <div className="security-master-print-sheet__grid">
                    {packet.previewFields.map((field) => (
                      <div key={field.id} className="security-master-print-sheet__cell">
                        <span>{field.label}</span>
                        <strong>{field.value}</strong>
                      </div>
                    ))}
                  </div>
                  <div className="security-master-print-sheet__section-list">
                    {selected.printSections.map((section) => (
                      <div key={section.id} className="security-master-print-sheet__section">
                        <span>{section.title}</span>
                        <strong>{section.description}</strong>
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            </div>
          </div>
          <div
            role="status"
            aria-label={packet.statusAriaLabel}
            className="rounded-xl border border-border/70 bg-background/80 p-5"
          >
            <div className="font-semibold text-foreground">Packet readiness</div>
            <div className="mt-4 grid gap-3 sm:grid-cols-3 xl:grid-cols-1">
              {packet.readinessPills.map((pill) => (
                <SummaryPill key={pill.id} label={pill.label} value={pill.value} tone={pill.tone} mono />
              ))}
            </div>
            <div className="mt-4 rounded-lg border border-border/60 bg-secondary/20 px-4 py-3">
              <div className="eyebrow-label">Routing guidance</div>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">
                {packet.routingGuidance}
              </p>
            </div>
          </div>
        </div>

        <div className="rounded-xl border border-border/70 bg-background/80 p-5" aria-label={packet.contentsLabel}>
          <div className="font-semibold text-foreground">Packet contents</div>
          <div className="mt-4 grid gap-3">
            {selected.printSections.map((section) => (
              <div key={section.id} className="rounded-lg border border-border/60 bg-secondary/20 px-4 py-3">
                <div className="font-semibold text-foreground">{section.title}</div>
                <p className="mt-1 text-sm text-muted-foreground">{section.description}</p>
              </div>
            ))}
          </div>
        </div>

        <div className="rounded-xl border border-border/70 bg-background/80 p-5" aria-label={packet.checklistLabel}>
          <div className="font-semibold text-foreground">Sign-off checklist</div>
          <div className="mt-4 space-y-2">
            {selected.printChecklist.map((item) => (
              <ChecklistRow key={item.id} item={item} />
            ))}
          </div>
        </div>
      </div>

      <div className="space-y-4">
        <RailCard title="Export evidence" icon={Download}>
          <div className="space-y-2" aria-label={packet.exportLabel}>
            {selected.exportEvidence.map((item) => (
              <div key={item.id} className="rounded-lg border border-border/60 bg-secondary/20 px-3 py-3">
                <div className="font-semibold text-foreground">{item.title}</div>
                <p className="mt-1 text-sm text-muted-foreground">{item.detail}</p>
                <div className="mt-2 font-mono text-xs text-primary">{item.destination}</div>
              </div>
            ))}
          </div>
        </RailCard>
        <RailCard title="Packet routing" icon={Printer}>
          <div className="rounded-lg border border-border/60 bg-secondary/20 px-3 py-3 text-sm text-muted-foreground">
            Export the packet to preserve operator evidence, then attach it to the research, accounting, and reporting handoff bundles.
          </div>
        </RailCard>
      </div>
    </div>
  );
}

function SecuritySectionCard({ section }: { section: SecurityMasterCompanySection }) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/80">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border/60 bg-secondary/20 px-4 py-3">
        <div className="flex items-center gap-3">
          <span className="font-mono text-xs text-primary">{section.badge}</span>
          <div className="font-semibold text-foreground">{section.title}</div>
        </div>
        <span className="text-xs text-muted-foreground">{section.meta}</span>
      </div>
      <dl className="grid gap-px bg-border/60">
        {section.rows.map((row) => (
          <div key={row.id} className="grid gap-2 bg-background px-4 py-3 sm:grid-cols-[minmax(0,0.55fr)_minmax(0,1fr)]">
            <dt className="text-xs text-muted-foreground">{row.label}</dt>
            <dd className="break-words text-sm text-foreground">{row.value}</dd>
          </div>
        ))}
      </dl>
    </div>
  );
}

function SecurityAuditList({ items }: { items: SecurityMasterAuditItem[] }) {
  return (
    <div className="space-y-3">
      {items.map((item) => (
        <div key={item.id} className="grid grid-cols-[12px_1fr] gap-3">
          <div className={cn("mt-1.5 h-3 w-3 rounded-full border", item.current ? "border-success bg-success/30" : "border-border/70 bg-secondary/60")} />
          <div>
            <div className="text-sm text-foreground">{item.label}</div>
            <div className="mt-1 text-xs text-muted-foreground">{item.meta}</div>
          </div>
        </div>
      ))}
    </div>
  );
}

function SecurityVenueList({ venues }: { venues: SecurityMasterVenueRow[] }) {
  return (
    <div className="space-y-2">
      {venues.map((venue) => (
        <div key={venue.id} className="grid grid-cols-[1fr_auto_auto] items-center gap-3 rounded-lg border border-border/60 bg-secondary/20 px-3 py-2">
          <div>
            <div className="text-sm text-foreground">{venue.venue}</div>
            <div className="mt-1 font-mono text-[11px] text-muted-foreground">{venue.mic}</div>
          </div>
          <span className="font-mono text-xs text-primary">{venue.ticker}</span>
          {venue.primary ? <Badge variant="success">Primary</Badge> : <span className="text-xs text-muted-foreground">Alt</span>}
        </div>
      ))}
    </div>
  );
}

function SecurityRationaleList({ items }: { items: SecurityMasterRationaleItem[] }) {
  return (
    <div className="space-y-3">
      {items.map((item) => (
        <div key={item.id} className="rounded-lg border border-border/60 bg-secondary/20 px-3 py-3">
          <div className="flex items-start justify-between gap-3">
            <div className="text-sm text-foreground">{item.label}</div>
            <span className="font-mono text-[11px] text-muted-foreground">{item.weight}</span>
          </div>
          <div className="mt-1 text-xs text-muted-foreground">{item.detail}</div>
        </div>
      ))}
    </div>
  );
}

function MetricFieldList({ fields }: { fields: SecurityMasterDetailField[] }) {
  return (
    <div className="grid gap-2">
      {fields.map((field) => (
        <div key={field.id} className="rounded-lg border border-border/60 bg-secondary/20 px-3 py-3">
          <div className="text-xs text-muted-foreground">{field.label}</div>
          <div className={cn(
            "mt-1 font-mono text-sm",
            field.tone === "success" ? "text-success" : field.tone === "warning" ? "text-warning" : "text-foreground"
          )}>
            {field.value}
          </div>
        </div>
      ))}
    </div>
  );
}

function OwnershipBars({ bars }: { bars: SecurityMasterOwnershipBar[] }) {
  return (
    <div className="space-y-3">
      {bars.map((bar) => (
        <div key={bar.id} className="grid grid-cols-[90px_1fr_44px] items-center gap-3">
          <div className="text-xs text-muted-foreground">{bar.label}</div>
          <div className="h-2 rounded-full bg-border/70">
            <div
              className={cn(
                "h-2 rounded-full",
                bar.tone === "primary"
                  ? "bg-primary"
                  : bar.tone === "success"
                    ? "bg-success"
                    : bar.tone === "warning"
                      ? "bg-warning"
                      : "bg-muted-foreground/60"
              )}
              style={{ width: `${bar.percent}%` }}
            />
          </div>
          <div className="text-right font-mono text-xs text-foreground">{bar.percent}%</div>
        </div>
      ))}
    </div>
  );
}

function TimelineStep({ event }: { event: SecurityMasterTimelineEvent }) {
  return (
    <div className="rounded-lg border border-border/60 bg-secondary/20 px-3 py-3 text-center">
      <div className={cn(
        "mx-auto mb-2 h-3 w-3 rounded-full border",
        event.current ? "border-primary bg-primary/40" : event.done ? "border-success bg-success/30" : "border-border bg-background"
      )}
      />
      <div className={cn("text-[11px] uppercase tracking-[0.14em]", event.current ? "text-primary" : "text-muted-foreground")}>{event.label}</div>
      <div className="mt-1 font-mono text-sm text-foreground">{event.date}</div>
    </div>
  );
}

function CorporateActionRow({ row }: { row: SecurityMasterCorporateActionRow }) {
  return (
    <div className="grid gap-3 px-4 py-4 text-sm sm:grid-cols-[120px_1.3fr_110px_110px_100px_90px]">
      <div>
        <div className="font-mono text-xs text-primary">{row.type}</div>
      </div>
      <div>
        <div className="font-semibold text-foreground">{row.description}</div>
        <div className="mt-1 text-xs text-muted-foreground">{row.note}</div>
      </div>
      <div className="font-mono text-xs text-muted-foreground">{row.announcedOn}</div>
      <div className="font-mono text-xs text-muted-foreground">{row.effectiveOn}</div>
      <div className="text-right font-mono text-sm text-foreground">{row.amount}</div>
      <div>
        <Badge variant={row.status === "Confirmed" ? "success" : row.status === "Pending" ? "warning" : "outline"}>
          {row.status}
        </Badge>
      </div>
    </div>
  );
}

function ChecklistRow({ item }: { item: SecurityMasterPrintChecklistItem }) {
  return (
    <div className="flex items-center justify-between gap-3 rounded-lg border border-border/60 bg-secondary/20 px-3 py-3">
      <div>
        <div className="text-sm text-foreground">{item.label}</div>
        <div className="mt-1 text-xs text-muted-foreground">{item.owner}</div>
      </div>
      <Badge variant={item.status === "Ready" ? "success" : item.status === "Review" ? "warning" : "outline"}>
        {item.status}
      </Badge>
    </div>
  );
}

function RailCard({
  title,
  icon: Icon,
  children
}: {
  title: string;
  icon: LucideIcon;
  children: ReactNode;
}) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/80 p-4">
      <div className="mb-4 flex items-center gap-2 font-semibold text-foreground">
        <Icon className="h-4 w-4 text-primary" />
        {title}
      </div>
      {children}
    </div>
  );
}

function SummaryPill({
  label,
  value,
  tone,
  mono = false
}: {
  label: string;
  value: string;
  tone?: "default" | "success" | "warning";
  mono?: boolean;
}) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/80 px-4 py-3">
      <div className="eyebrow-label">{label}</div>
      <div
        className={cn(
          "mt-2 text-sm font-semibold text-foreground",
          mono && "font-mono",
          tone === "success" ? "text-success" : tone === "warning" ? "text-warning" : "text-foreground"
        )}
      >
        {value}
      </div>
    </div>
  );
}

function EmptyState({ state }: { state: DataOperationsEmptyState }) {
  return (
    <div role="status" className="rounded-lg border border-dashed border-border/80 bg-secondary/20 px-3 py-4">
      <div className="text-sm font-semibold">{state.title}</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{state.description}</p>
    </div>
  );
}

function ProviderEmptyState({ onSetup }: { onSetup: () => void }) {
  return (
    <div role="status" className="rounded-lg border border-dashed border-border/80 bg-secondary/10 px-4 py-6 text-center">
      <RadioTower className="mx-auto mb-3 h-8 w-8 text-muted-foreground/40" aria-hidden="true" />
      <div className="text-sm font-semibold">No providers configured</div>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">
        Meridian needs at least one data provider to stream prices, run backfills, and power strategy execution.
        Configure a provider to unblock live data and backfill workflows.
      </p>
      <Button size="sm" className="mt-4" onClick={onSetup} aria-label="Open provider setup to configure your first data provider">
        <Plus className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
        Configure your first provider
      </Button>
    </div>
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

function DetailRow({ label, value, inverted = false }: { label: string; value: string; inverted?: boolean }) {
  return (
    <div className={cn(
      "flex items-center justify-between gap-4 rounded-lg border px-3 py-2",
      inverted ? "border-slate-700/60 bg-slate-950/40" : "border-border/70 bg-secondary/40"
    )}>
      <span className={cn(inverted ? "text-slate-300" : "text-muted-foreground")}>{label}</span>
      <span className={cn("font-mono", inverted ? "text-slate-50" : "text-foreground")}>{value}</span>
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
