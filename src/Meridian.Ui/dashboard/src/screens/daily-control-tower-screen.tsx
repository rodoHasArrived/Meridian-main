import { useEffect, useState } from "react";
import { ArrowRight } from "lucide-react";
import { Link } from "react-router-dom";
import type { AppShellTrustStripState, AppShellWorkflowContinuityViewModel } from "@/app-shell.view-model";
import { Button } from "@/components/ui/button";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { PanelSurface } from "@/components/ui/panel-surface";
import { DetailRail } from "@/components/meridian/detail-rail";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import type { OperationalTrustTone } from "@/components/meridian/operational-trust-summary";
import { OperatorTriageQueue } from "@/components/meridian/operator-triage-queue";
import { DesignSystemStatus } from "@/design-system/status";
import { useQueueKeyboard } from "@/lib/use-queue-keyboard";
import { SeverityBadge } from "@/components/operations";
import { buildDailyControlTowerModel } from "@/lib/daily-control-tower";
import { appendOperatingScopeToRoute } from "@/app-shell.operating-scope";
import { badgeVariantToSeverityStatus } from "@/lib/shared-tone-mappings";

export interface DailyControlTowerScreenProps {
  viewModel: AppShellWorkflowContinuityViewModel;
  trustStrip: AppShellTrustStripState;
  onEditOperatingScope?: () => void;
  onRefresh?: () => void;
  refreshing?: boolean;
}

export function DailyControlTowerScreen({
  viewModel,
  trustStrip,
  onEditOperatingScope,
  onRefresh,
  refreshing = false
}: DailyControlTowerScreenProps) {
  const model = buildDailyControlTowerModel(viewModel, trustStrip);
  // Triage-in-place: the operator can inspect any queue row's evidence without
  // leaving the tower. Falls back to the top-ranked row until one is chosen
  // (or when the chosen row leaves the queue on refresh).
  const [selectedQueueItemId, setSelectedQueueItemId] = useState<string | null>(null);
  const selectedQueueRow =
    model.queueRows.find((row) => row.item.id === selectedQueueItemId) ?? model.queueRows[0] ?? null;
  const freshnessEvidence = selectedQueueRow?.proofPassportItems.find((item) => item.id === "freshness");
  const freshness = buildTowerFreshness(
    selectedQueueRow?.proof?.timestampIso,
    freshnessEvidence?.detail
  );
  const providerTrust = trustStrip.items.find((item) => item.id === "providers") ?? null;
  const evidencePanelId = "daily-control-tower-evidence-detail";

  useEffect(() => {
    if (selectedQueueItemId && !model.queueRows.some((row) => row.item.id === selectedQueueItemId)) {
      setSelectedQueueItemId(null);
    }
  }, [model.queueRows, selectedQueueItemId]);

  const queueColumns: DenseDataTableColumn<(typeof model.queueRows)[number]>[] = [
    {
      id: "item",
      label: "Item",
      className: "min-w-[18rem]",
      render: (row) => (
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-medium text-foreground">{row.item.label}</span>
            <SeverityBadge status={badgeVariantToSeverityStatus(row.badgeVariant)} label={row.statusLabel} />
          </div>
          <p className="text-xs leading-5 text-muted-foreground">{row.item.detail}</p>
        </div>
      )
    },
    { id: "workspace", label: "Workspace", render: (row) => row.item.workspaceLabel },
    { id: "output", label: "Affected output", render: (row) => row.outputLabel },
    {
      id: "action",
      label: "Next action",
      render: (row) => (
        <Link
          to={row.item.route}
          className="inline-flex items-center gap-1 font-medium text-primary hover:underline focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40"
          aria-label={row.item.ariaLabel}
        >
          <span>{row.item.actionLabel}</span>
          <ArrowRight className="h-3 w-3" aria-hidden="true" />
        </Link>
      )
    },
    {
      id: "evidence",
      label: "Evidence",
      render: (row) => row.proof?.label ?? "Evidence not linked"
    }
  ];

  const queueKeyboard = useQueueKeyboard({ rows: model.queueRows, selectedId: selectedQueueRow?.item.id ?? null,
    getId: (row: (typeof model.queueRows)[number]) => row.item.id,
    onSelect: (row: (typeof model.queueRows)[number]) => setSelectedQueueItemId(row.item.id) });

  return (
    <div className="space-y-3">
      <h2 className="sr-only">What needs an operator decision now</h2>
      <section aria-label="Priority decision" className="flex flex-wrap items-center gap-x-3 gap-y-2 border border-border px-3 py-2">
        <DesignSystemStatus status={model.statusTone === "blocked" ? "blocked" : model.statusTone === "review" ? "action" : model.statusTone === "ready" ? "ready" : "info"}>{model.statusLabel}</DesignSystemStatus>
        <h3 className="text-sm font-semibold">{model.decision.title}</h3>
        <span className="min-w-0 flex-1 text-xs text-muted-foreground">{model.decision.reason}</span>
        <Button asChild variant="outline" size="sm"><Link to={model.nextActionHref} aria-label={model.nextActionAriaLabel}>{model.nextActionLabel}<ArrowRight className="h-3.5 w-3.5" aria-hidden="true" /></Link></Button>
      </section>

      <section aria-label="Daily control tower confidence" className="flex flex-wrap items-center gap-x-4 gap-y-1 border-y border-border py-1.5 text-xs">
        <span className="inline-flex items-center gap-2"><span className="text-muted-foreground">Connectivity</span><DesignSystemStatus status={providerTrust?.tone === "blocked" ? "blocked" : providerTrust?.tone === "review" ? "action" : providerTrust?.tone === "ready" ? "ready" : "info"}>{providerTrust?.value ?? "Unavailable"}</DesignSystemStatus></span>
        <span title={viewModel.operatingScope.summary}><span className="text-muted-foreground">Scope </span>{viewModel.operatingScope.hasScope ? viewModel.operatingScope.summary : "All authorized scopes"}</span>
        <span><span className="text-muted-foreground">Evidence </span>{freshness.value === "Timestamp unavailable" ? "Timestamp unavailable" : freshness.tone === "ready" ? "Current" : "Stale update"}</span>
        <span>{model.queueRows.length} loaded workspace items</span>
        <details className="relative ml-auto">
          <summary className="cursor-pointer font-medium">Health details</summary>
          <div className="absolute right-0 z-20 mt-2 w-80 space-y-2 border border-border bg-card p-3 shadow-sm">
            <p>{freshness.value} · {model.evidenceTimelineItems.length} evidence events</p>
            {providerTrust?.href && providerTrust.actionLabel ? <Link className="block underline" to={appendOperatingScopeToRoute(providerTrust.href, viewModel.operatingScope)}>{providerTrust.actionLabel}</Link> : null}
            {onEditOperatingScope ? <Button type="button" variant="ghost" size="sm" onClick={onEditOperatingScope}>{viewModel.operatingScope.hasScope ? "Change scope filter" : "Set scope filter"}</Button> : null}
            {onRefresh ? <Button type="button" variant="ghost" size="sm" disabled={refreshing} aria-label={refreshing ? "Refreshing control tower evidence" : "Refresh control tower evidence"} onClick={onRefresh}>{refreshing ? "Refreshing" : "Refresh evidence"}</Button> : null}
          </div>
        </details>
      </section>

      <section aria-label="Finance queue">
        <OperatorTriageQueue key={viewModel.operatingScope.fundAccountId ?? "all"} fundAccountId={viewModel.operatingScope.fundAccountId} refreshKey={refreshing} fallback={model.queueRows.length > 0 ? (
          <div onKeyDownCapture={queueKeyboard} className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
            <DenseDataTable
              tableId="daily-control-tower-queue"
              columns={queueColumns}
              rows={model.queueRows}
              getRowId={(row) => row.item.id}
              getRowAriaLabel={(row) => `${row.item.label}. ${row.statusLabel}. ${row.item.detail}`}
              getRowSelectAriaLabel={(row) => row.item.label}
              getRowAriaControls={() => evidencePanelId}
              getRowAriaExpanded={(row) => row.item.id === selectedQueueRow?.item.id}
              getRowClassName={() => "align-top"}
              onRowSelect={(row) => setSelectedQueueItemId(row.item.id)}
              selectedRowId={selectedQueueRow?.item.id ?? null}
              emptyText={model.emptyQueueText}
              ariaLabel="Daily control tower finance queue"
              maxVisibleRows={null}
            />
            {selectedQueueRow ? (
              <DetailRail
                id={evidencePanelId}
                ariaLabel={`${selectedQueueRow.item.label} evidence summary`}
                title={selectedQueueRow.item.label}
                fields={selectedQueueRow.proofPassportItems.slice(0, 4).map((passportItem) => ({ label: passportItem.label,
                  value: <><span>{passportItem.value}</span><p className="mt-1 text-xs leading-5 text-muted-foreground">{passportItem.detail}</p></> }))}
              >
                <p className="my-3 text-sm leading-6 text-muted-foreground">{selectedQueueRow.proofPassportSummary}</p>
                {selectedQueueRow.proofPassportItems.length > 4 ? (
                  <TechnicalDetails
                    label="More evidence"
                    description="Downstream usage, blockers, evidence packets, and audit trail for the selected queue item."
                  >
                    <dl className="grid gap-2">
                      {selectedQueueRow.proofPassportItems.slice(4).map((passportItem) => (
                        <div key={passportItem.id} className="rounded border border-border bg-card p-2">
                          <dt className="eyebrow-label">{passportItem.label}</dt>
                          <dd className="mt-1 text-xs font-medium leading-5 text-foreground">{passportItem.value}</dd>
                          <dd className="mt-1 text-xs leading-5 text-muted-foreground">{passportItem.detail}</dd>
                        </div>
                      ))}
                    </dl>
                  </TechnicalDetails>
                ) : null}
              </DetailRail>
            ) : null}
          </div>
        ) : (
          <PanelSurface flat role="status" className="p-4 text-sm text-muted-foreground">
            {model.emptyQueueText}
          </PanelSurface>
        )} />
      </section>

    </div>
  );
}

const TOWER_FRESHNESS_WINDOW_MS = 24 * 60 * 60 * 1000;

function buildTowerFreshness(
  timestampIso: string | null | undefined,
  detail: string | null | undefined,
  nowMs = Date.now()
): { value: string; detail: string; tone: OperationalTrustTone } {
  const observedAtMs = Date.parse(timestampIso ?? "");
  if (!Number.isFinite(observedAtMs)) {
    return {
      value: "Timestamp unavailable",
      detail: detail ?? "No timestamped evidence is linked to the leading decision.",
      tone: "review"
    };
  }

  const observedAt = new Date(observedAtMs);
  const timestampLabel = new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
    timeZone: "UTC",
    timeZoneName: "short"
  }).format(observedAt).replace("24:", "00:");
  const stale = nowMs - observedAtMs > TOWER_FRESHNESS_WINDOW_MS;

  return {
    value: `${stale ? "Stale update" : "Current"} · ${timestampLabel}`,
    detail: detail ?? "Latest evidence for the leading decision.",
    tone: stale ? "review" : "ready"
  };
}
