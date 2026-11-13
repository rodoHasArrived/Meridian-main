// Accounting case workbench panel, extracted from accounting-screen.tsx.
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { cn } from "@/lib/utils";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { BreakAuditRebuildCheck } from "@/screens/accounting-screen.break-audit-rebuild";
import type { ReconciliationBreakRowViewModel, ReconciliationBreakDetailViewModel, ReconciliationDetailActionsViewModel, CloseCommandCenterViewState } from "@/screens/accounting-screen.view-model";
import { AccountingValue } from "@/screens/accounting-screen.accounting-value";

interface AccountingCaseWorkbenchProps {
  breakRows: ReconciliationBreakRowViewModel[];
  selectedBreakId: string | null;
  selectedBreakDetail: ReconciliationBreakDetailViewModel | null;
  closeCommandCenter: CloseCommandCenterViewState | null;
  detailActions: ReconciliationDetailActionsViewModel | null;
  onSelectBreak: (breakId: string) => void;
  onAssignBreak: (breakId: string) => void;
  onResolveBreak: (breakId: string) => void;
  onDismissBreak: (breakId: string) => void;
}

export function AccountingCaseWorkbench({
  breakRows,
  selectedBreakId,
  selectedBreakDetail,
  closeCommandCenter,
  detailActions,
  onSelectBreak,
  onAssignBreak,
  onResolveBreak,
  onDismissBreak
}: AccountingCaseWorkbenchProps) {
  const selectedBreak = breakRows.find((row) => row.breakId === selectedBreakId) ?? breakRows[0] ?? null;
  const selectedBlocker = closeCommandCenter?.blockerRows[0] ?? null;
  const queueEmpty = breakRows.length === 0 && !selectedBlocker;
  const dueWorkCount = closeCommandCenter?.blockerRows.length ?? 0;

  return (
    <section
      id="accounting-case-workbench"
      className="workspace-section-band"
      aria-labelledby="accounting-case-workbench-heading"
    >
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Today</p>
          <h3 id="accounting-case-workbench-heading" className="workspace-section-title">Accounting case workbench</h3>
          <p className="workspace-section-summary">
            Status -&gt; Queue -&gt; Selected item -&gt; Evidence -&gt; Action for breaks, approvals, and close tasks.
          </p>
        </div>
        <Badge variant={closeCommandCenter?.statusTone === "danger" ? "danger" : closeCommandCenter?.statusTone === "warning" ? "warning" : closeCommandCenter?.statusTone === "success" ? "success" : "outline"}>
          {closeCommandCenter?.statusLabel ?? "Loading"}
        </Badge>
      </div>

      <div className="grid gap-4 lg:grid-cols-[minmax(15rem,0.72fr)_minmax(0,1.1fr)_minmax(16rem,0.82fr)]">
        <Card className="panel-surface" role="region" aria-label="Accounting case queue">
          <CardHeader>
            <CardTitle className="text-base">Queue</CardTitle>
            <CardDescription>Breaks, approvals, and close tasks needing finance review.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <div className="grid gap-2 sm:grid-cols-3 lg:grid-cols-1">
              <AccountingValue label="Exceptions" value={String(breakRows.length)} tone={breakRows.length > 0 ? "text-warning" : "text-success"} />
              <AccountingValue label="Due work" value={String(dueWorkCount)} tone={dueWorkCount > 0 ? "text-warning" : "text-success"} />
              <AccountingValue label="Close" value={closeCommandCenter?.periodLabel ?? "Current period"} />
            </div>
            {queueEmpty ? (
              <div role="status" className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3 text-sm text-muted-foreground">
                No accounting cases need review. Open reconciliation or close evidence when new work arrives.
              </div>
            ) : (
              <div role="list" aria-label="Accounting case queue rows" className="grid gap-2">
                {breakRows.slice(0, 6).map((row) => (
                  <div role="listitem" key={row.breakId} className="grid">
                  <button
                    type="button"
                    className={cn(
                      "rounded-md border px-3 py-2 text-left transition-colors focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40",
                      row.breakId === selectedBreak?.breakId ? "border-primary/45 bg-primary/10" : "border-border/70 bg-secondary/20 hover:bg-secondary/35"
                    )}
                    aria-pressed={row.breakId === selectedBreak?.breakId}
                    onClick={() => onSelectBreak(row.breakId)}
                  >
                    <span className="block text-sm font-semibold text-foreground">{row.financeLabel}</span>
                    <span className="mt-1 block text-xs leading-5 text-muted-foreground">
                      {row.strategyName} · {row.varianceLabel} · {row.ownerLabel}
                    </span>
                  </button>
                  </div>
                ))}
                {selectedBlocker ? (
                  <Link
                    to={selectedBlocker.href ?? WORKSTATION_ROUTE_CATALOG.accountingApprovals}
                    className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-left text-sm text-warning transition-colors hover:bg-warning/15 focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40"
                  >
                    <span className="block font-semibold">{selectedBlocker.label}</span>
                    <span className="mt-1 block text-xs leading-5">{selectedBlocker.actionLabel}</span>
                  </Link>
                ) : null}
              </div>
            )}
          </CardContent>
        </Card>

        <Card className="panel-surface-strong" role="region" aria-label="Selected accounting case">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <CardTitle className="text-base">
                  {selectedBreakDetail?.title ?? selectedBlocker?.label ?? "No case selected"}
                </CardTitle>
                <CardDescription className="mt-2">
                  {selectedBreakDetail?.description ?? selectedBlocker?.detail ?? "Select a case to inspect facts, variance, affected books, reports, and next action."}
                </CardDescription>
              </div>
              {selectedBreakDetail ? <Badge variant={selectedBreakDetail.statusBadgeVariant}>{selectedBreakDetail.statusLabel}</Badge> : null}
            </div>
          </CardHeader>
          <CardContent className="space-y-4 text-sm">
            {selectedBreakDetail ? (
              <>
                <div className="grid gap-3 sm:grid-cols-2">
                  {selectedBreakDetail.fields.filter((field) => field.label !== "Run").slice(0, 6).map((field) => (
                    <AccountingValue key={field.label} label={field.label} value={field.value} />
                  ))}
                </div>
                {selectedBreakDetail.analysisText ? (
                  <div className="rounded-md border border-border/70 bg-background/55 px-3 py-3 text-muted-foreground">
                    {selectedBreakDetail.analysisText}
                  </div>
                ) : null}
                <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3">
                  <div className="text-xs font-semibold uppercase text-muted-foreground">Next action</div>
                  <p className="mt-1 leading-6 text-foreground">
                    {selectedBreakDetail.recommendedActionText ?? "Review the selected case evidence before taking action."}
                  </p>
                </div>
              </>
            ) : selectedBlocker ? (
              <div className="grid gap-3">
                <AccountingValue label="Status" value={selectedBlocker.statusLabel} />
                <AccountingValue label="Owner" value={selectedBlocker.ownerLabel ?? "Unassigned"} />
                <AccountingValue label="Impact" value={selectedBlocker.impactLabel} />
                <AccountingValue label="Due" value={selectedBlocker.dueLabel ?? "No due date"} />
              </div>
            ) : (
              <div role="status" className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3 text-muted-foreground">
                No selected accounting case.
              </div>
            )}
          </CardContent>
        </Card>

        <Card className="panel-surface" role="region" aria-label="Selected case evidence and actions">
          <CardHeader>
            <CardTitle className="text-base">Evidence and action</CardTitle>
            <CardDescription>Stored support, approvals, audit details, and the primary action for the selected case.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4 text-sm">
            <div className="grid gap-2">
              <AccountingValue label="Evidence status" value={selectedBlocker?.evidenceLabel ?? selectedBreakDetail?.subtitle ?? "Select a case"} />
              <AccountingValue label="Evidence workspace" value={detailActions ? "Packet available for review" : selectedBlocker?.evidenceLabel ?? "No stored support selected"} />
              <AccountingValue label="Affected output" value={selectedBlocker?.impactLabel ?? "Close and reconciliation"} />
            </div>
            <div className="flex flex-wrap gap-2">
              {selectedBreak?.canAssign ? (
                <Button type="button" size="sm" variant="outline" onClick={() => onAssignBreak(selectedBreak.breakId)}>
                  {selectedBreak.assignLabel}
                </Button>
              ) : null}
              {selectedBreak?.canResolve ? (
                <Button type="button" size="sm" onClick={() => onResolveBreak(selectedBreak.breakId)}>
                  {selectedBreak.resolveLabel}
                </Button>
              ) : null}
              {selectedBreak?.canDismiss ? (
                <Button type="button" size="sm" variant="ghost" onClick={() => onDismissBreak(selectedBreak.breakId)}>
                  {selectedBreak.dismissLabel}
                </Button>
              ) : null}
              {detailActions ? (
                <Button asChild size="sm" variant="outline">
                  <Link to={detailActions.evidencePacketHref} aria-label={detailActions.evidencePacketAriaLabel}>
                    {detailActions.evidencePacketLabel}
                  </Link>
                </Button>
              ) : null}
              {selectedBlocker?.href ? (
                <Button asChild size="sm" variant="outline">
                  <Link to={selectedBlocker.href}>{selectedBlocker.actionLabel}</Link>
                </Button>
              ) : null}
            </div>
            <TechnicalDetails label="Audit details">
              <div className="grid gap-2" role="group" aria-label="Audit detail values">
                <AccountingValue label="Case ID" value={selectedBreak?.breakId ?? selectedBlocker?.id ?? "No case"} />
                <AccountingValue label="Run reference" value={selectedBreak?.runId ?? "No run selected"} />
                <AccountingValue label="Route" value={selectedBreakDetail?.routingActionHref ?? selectedBlocker?.href ?? "No route"} />
                <AccountingValue label="Raw category" value={selectedBreakDetail?.rawCategoryLabel ?? "No category"} />
                {selectedBlocker?.sourceRecordIds?.length ? (
                  <AccountingValue label="Source records" value={selectedBlocker.sourceRecordIds.join(", ")} />
                ) : null}
                <AccountingValue label="Audit packet" value={detailActions?.auditPacketLabel ?? "Open after selecting a break"} />
              </div>
              <BreakAuditRebuildCheck breakId={selectedBreak?.breakId ?? null} />
            </TechnicalDetails>
          </CardContent>
        </Card>
      </div>
    </section>
  );
}
