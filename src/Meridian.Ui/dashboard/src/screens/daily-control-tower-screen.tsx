import { useState } from "react";
import { ArrowRight } from "lucide-react";
import { Link } from "react-router-dom";
import type { AppShellTrustStripState, AppShellWorkflowContinuityViewModel } from "@/app-shell.view-model";
import { Button } from "@/components/ui/button";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { PanelSurface } from "@/components/ui/panel-surface";
import { ScreenLayout } from "@/components/ui/screen-layout";
import { KeyValueGrid } from "@/components/data/concrete";
import { OperationalTrustSummary, type OperationalTrustTone } from "@/components/meridian/operational-trust-summary";
import { ReadinessPanel, SeverityBadge, WorkspaceSection } from "@/components/operations";
import { buildDailyControlTowerModel } from "@/lib/daily-control-tower";
import {
  badgeVariantToSeverityStatus,
  readinessToneToSeverityStatus
} from "@/lib/shared-tone-mappings";

export interface DailyControlTowerScreenProps {
  viewModel: AppShellWorkflowContinuityViewModel;
  trustStrip: AppShellTrustStripState;
}

export function DailyControlTowerScreen({ viewModel, trustStrip }: DailyControlTowerScreenProps) {
  const model = buildDailyControlTowerModel(viewModel, trustStrip);
  // Triage-in-place: the operator can inspect any queue row's evidence without
  // leaving the tower. Falls back to the top-ranked row until one is chosen
  // (or when the chosen row leaves the queue on refresh).
  const [selectedQueueItemId, setSelectedQueueItemId] = useState<string | null>(null);
  const selectedQueueRow =
    model.queueRows.find((row) => row.item.id === selectedQueueItemId) ?? model.queueRows[0] ?? null;
  const sourceEvidence = selectedQueueRow?.proofPassportItems.find((item) => item.id === "source");
  const freshnessEvidence = selectedQueueRow?.proofPassportItems.find((item) => item.id === "freshness");
  const freshness = buildTowerFreshness(
    selectedQueueRow?.proof?.timestampIso,
    freshnessEvidence?.detail
  );
  const trustTone: OperationalTrustTone = model.statusTone === "ready"
    ? "ready"
    : model.statusTone === "blocked"
      ? "blocked"
      : model.statusTone === "review"
        ? "review"
        : "unknown";

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
          value: sourceEvidence?.value ?? model.ownerLabel,
          detail: sourceEvidence?.detail ?? "Workspace supplying the leading decision",
          tone: trustTone
        }}
        scope={{
          value: model.ownerLabel,
          detail: model.outputLabel,
          tone: selectedQueueRow ? "ready" : "unknown"
        }}
        freshness={{
          value: freshness.value,
          detail: freshness.detail,
          tone: freshness.tone
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
        {model.queueRows.length > 0 ? (
          <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
            <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-border text-sm" aria-label="Daily control tower finance queue">
                <thead className="bg-muted/35 text-left text-xs uppercase tracking-[0.08em] text-muted-foreground">
                  <tr>
                    <th scope="col" className="px-4 py-3 font-semibold">Blocked item</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Owner</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Affected output</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Next action</th>
                    <th scope="col" className="px-4 py-3 font-semibold">Evidence</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {model.queueRows.map((row) => {
                    const isSelected = row.item.id === selectedQueueRow?.item.id;
                    return (
                    <tr
                      key={row.item.id}
                      className={isSelected ? "align-top bg-primary/5" : "align-top"}
                      aria-current={isSelected ? "true" : undefined}
                    >
                      <td className="max-w-sm px-4 py-4">
                        <div className="space-y-2">
                          <div className="flex flex-wrap items-center gap-2">
                            <button
                              type="button"
                              onClick={() => setSelectedQueueItemId(row.item.id)}
                              aria-pressed={isSelected}
                              aria-label={`Show evidence for ${row.item.label}`}
                              className="text-left font-medium text-foreground hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                            >
                              {row.item.label}
                            </button>
                            <SeverityBadge
                              status={badgeVariantToSeverityStatus(row.badgeVariant)}
                              label={row.statusLabel}
                            />
                          </div>
                          <p className="text-xs leading-5 text-muted-foreground">{row.item.detail}</p>
                        </div>
                      </td>
                      <td className="px-4 py-4 text-sm text-foreground">{row.item.workspaceLabel}</td>
                      <td className="px-4 py-4 text-sm text-foreground">{row.outputLabel}</td>
                      <td className="px-4 py-4">
                        <Link
                          to={row.item.route}
                          className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                          aria-label={row.item.ariaLabel}
                        >
                          <span>{row.item.actionLabel}</span>
                          <ArrowRight className="h-3 w-3" aria-hidden="true" />
                        </Link>
                      </td>
                      <td className="px-4 py-4 text-xs leading-5 text-muted-foreground">
                        {row.proof?.label ?? row.proofPassportItems.find((item) => item.id === "freshness")?.value ?? "No evidence linked"}
                      </td>
                    </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            {selectedQueueRow ? (
              <section
                aria-label={`${selectedQueueRow.item.label} Evidence summary`}
                className="space-y-3 border-l border-border bg-background/50 p-4"
              >
                <div>
                  <p className="eyebrow-label">Selected queue evidence</p>
                  <h3 className="text-sm font-semibold text-foreground">{selectedQueueRow.item.label}</h3>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{selectedQueueRow.proofPassportSummary}</p>
                </div>
                <dl className="grid gap-2">
                  {selectedQueueRow.proofPassportItems.slice(0, 4).map((passportItem) => (
                    <div key={passportItem.id} className="rounded border border-border bg-card p-2">
                      <dt className="eyebrow-label">{passportItem.label}</dt>
                      <dd className="mt-1 text-xs font-medium leading-5 text-foreground">{passportItem.value}</dd>
                      <dd className="mt-1 text-[11px] leading-4 text-muted-foreground">{passportItem.detail}</dd>
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
                          <dd className="mt-1 text-[11px] leading-4 text-muted-foreground">{passportItem.detail}</dd>
                        </div>
                      ))}
                    </dl>
                  </TechnicalDetails>
                ) : null}
              </section>
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
