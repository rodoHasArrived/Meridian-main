import { useEffect, useState } from "react";
import { ArrowRight } from "lucide-react";
import { Link } from "react-router-dom";
import type { AppShellTrustStripState, AppShellWorkflowContinuityViewModel } from "@/app-shell.view-model";
import { Button } from "@/components/ui/button";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { PanelSurface } from "@/components/ui/panel-surface";
import { ScreenLayout } from "@/components/ui/screen-layout";
import { KeyValueGrid } from "@/components/data/concrete";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { OperationalTrustSummary, type OperationalTrustTone } from "@/components/meridian/operational-trust-summary";
import { ReadinessPanel, SeverityBadge, WorkspaceSection } from "@/components/operations";
import { buildDailyControlTowerModel } from "@/lib/daily-control-tower";
import { appendOperatingScopeToRoute } from "@/app-shell.operating-scope";
import {
  badgeVariantToSeverityStatus,
  readinessToneToSeverityStatus
} from "@/lib/shared-tone-mappings";

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
  const [reviewAllScopes, setReviewAllScopes] = useState(false);
  const selectedQueueRow =
    model.queueRows.find((row) => row.item.id === selectedQueueItemId) ?? model.queueRows[0] ?? null;
  const freshnessEvidence = selectedQueueRow?.proofPassportItems.find((item) => item.id === "freshness");
  const freshness = buildTowerFreshness(
    selectedQueueRow?.proof?.timestampIso,
    freshnessEvidence?.detail
  );
  const providerTrust = trustStrip.items.find((item) => item.id === "providers") ?? null;
  const evidencePanelId = "daily-control-tower-evidence-detail";
  const queueVisible = viewModel.operatingScope.hasScope || reviewAllScopes;
  const trustTone: OperationalTrustTone = model.statusTone === "ready"
    ? "ready"
    : model.statusTone === "blocked"
      ? "blocked"
      : model.statusTone === "review"
        ? "review"
      : "unknown";

  useEffect(() => {
    if (selectedQueueItemId && !model.queueRows.some((row) => row.item.id === selectedQueueItemId)) {
      setSelectedQueueItemId(null);
    }
  }, [model.queueRows, selectedQueueItemId]);

  const queueColumns: DenseDataTableColumn<(typeof model.queueRows)[number]>[] = [
    {
      id: "item",
      label: "Blocked item",
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
    { id: "owner", label: "Owner", render: (row) => row.item.workspaceLabel },
    { id: "output", label: "Affected output", render: (row) => row.outputLabel },
    {
      id: "action",
      label: "Next action",
      render: (row) => (
        <Link
          to={row.item.route}
          className="inline-flex items-center gap-1 font-medium text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
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

  return (
    <ScreenLayout
      title="What needs an operator decision now"
      scope="Daily control tower"
      description="One priority, followed by a ranked finance queue with evidence available in place."
    >
      <ReadinessPanel
        state={readinessToneToSeverityStatus(model.statusTone)}
        statusLabel={model.statusLabel}
        title={model.decision.title}
        detail={model.decision.summary}
        actions={
          <Button asChild variant="outline" size="sm">
            <Link to={model.nextActionHref} aria-label={model.nextActionAriaLabel}>
              <span>{model.nextActionLabel}</span>
              <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
            </Link>
          </Button>
        }
      >
        <KeyValueGrid
          columns={2}
          items={[
            { label: model.decision.reasonLabel, value: model.decision.reason },
            { label: "Owner", value: model.ownerLabel },
            { label: "Output", value: model.outputLabel },
            { label: "Evidence", value: model.evidenceLabel }
          ]}
        />
      </ReadinessPanel>

      <OperationalTrustSummary
        label="Daily control tower confidence"
        source={{
          label: "Connectivity",
          value: providerTrust?.value ?? "Provider posture unavailable",
          detail: providerTrust?.detail ?? "Provider connectivity evidence has not loaded.",
          tone: providerTrust ? operationalTrustToneFromStrip(providerTrust.tone) : "unknown",
          action: providerTrust?.href && providerTrust.actionLabel ? (
            <Button asChild variant="outline" size="sm">
              <Link to={appendOperatingScopeToRoute(providerTrust.href, viewModel.operatingScope)}>
                {providerTrust.actionLabel}
              </Link>
            </Button>
          ) : null
        }}
        scope={{
          value: viewModel.operatingScope.summary,
          detail: viewModel.operatingScope.hasScope
            ? "Applied to compatible workstation routes."
            : "Choose a fund, account, run, provider, symbol, or date window.",
          tone: viewModel.operatingScope.hasScope ? "ready" : "review",
          action: onEditOperatingScope ? (
            <Button type="button" variant="outline" size="sm" onClick={onEditOperatingScope}>
              {viewModel.operatingScope.hasScope ? "Change scope" : "Set operating scope"}
            </Button>
          ) : null
        }}
        freshness={{
          value: freshness.value,
          detail: freshness.detail,
          tone: freshness.tone,
          action: onRefresh ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={refreshing}
              aria-label={refreshing ? "Refreshing control tower evidence" : "Refresh control tower evidence"}
              onClick={onRefresh}
            >
              {refreshing ? "Refreshing" : "Refresh evidence"}
            </Button>
          ) : null
        }}
        completeness={{
          value: `${model.queueRows.length} ranked items · ${model.evidenceTimelineItems.length} evidence events`,
          detail: "Finance queue and retained evidence coverage",
          tone: model.queueRows.length > 0 && model.evidenceTimelineItems.length > 0 ? "ready" : "review"
        }}
        blocker={model.statusTone !== "ready" ? {
          value: model.statusLabel,
          detail: model.decision.reason,
          tone: trustTone
        } : undefined}
      />

      <WorkspaceSection
        title="Finance queue"
        summary="Each row carries the evidence needed to move from source issue to downstream output."
      >
        {!queueVisible ? (
          <PanelSurface flat role="region" aria-label="Choose Control Tower scope" className="grid gap-4 p-4 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-center">
            <div>
              <h3 className="text-sm font-semibold text-foreground">Choose the scope for today’s queue</h3>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                Select a fund, account, provider, symbol, run, or date window to rank the most relevant decisions. You can still review the combined queue when cross-scope triage is required.
              </p>
            </div>
            <div className="flex flex-wrap gap-2">
              {onEditOperatingScope ? (
                <Button type="button" size="sm" onClick={onEditOperatingScope}>Set operating scope</Button>
              ) : null}
              <Button type="button" size="sm" variant="outline" onClick={() => setReviewAllScopes(true)}>
                Review all scopes
              </Button>
            </div>
          </PanelSurface>
        ) : model.queueRows.length > 0 ? (
          <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
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
              <DenseRowDetailPanel
                id={evidencePanelId}
                ariaLabel={`${selectedQueueRow.item.label} evidence summary`}
                selectedSourceLabel={selectedQueueRow.item.label}
                className="space-y-3 border-l border-border bg-background/50 p-4"
              >
                <div>
                  <p className="eyebrow-label">Selected queue evidence</p>
                  <h3 className="text-sm font-semibold text-foreground">{selectedQueueRow.item.label}</h3>
                  <p className="mt-1 text-sm leading-6 text-muted-foreground">{selectedQueueRow.proofPassportSummary}</p>
                </div>
                <dl className="grid gap-2">
                  {selectedQueueRow.proofPassportItems.slice(0, 4).map((passportItem) => (
                    <div key={passportItem.id} className="rounded border border-border bg-card p-2">
                      <dt className="eyebrow-label">{passportItem.label}</dt>
                      <dd className="mt-1 text-xs font-medium leading-5 text-foreground">{passportItem.value}</dd>
                      <dd className="mt-1 text-xs leading-5 text-muted-foreground">{passportItem.detail}</dd>
                    </div>
                  ))}
                </dl>
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
              </DenseRowDetailPanel>
            ) : null}
          </div>
        ) : (
          <PanelSurface flat role="status" className="p-4 text-sm text-muted-foreground">
            {model.emptyQueueText}
          </PanelSurface>
        )}
      </WorkspaceSection>

    </ScreenLayout>
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

function operationalTrustToneFromStrip(
  tone: AppShellTrustStripState["items"][number]["tone"]
): OperationalTrustTone {
  switch (tone) {
    case "ready":
      return "ready";
    case "review":
      return "review";
    case "blocked":
      return "blocked";
    case "pending":
      return "unknown";
  }
}
