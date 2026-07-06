import { useState } from "react";
import { ArrowRight } from "lucide-react";
import { Link } from "react-router-dom";
import type { AppShellTrustStripState, AppShellWorkflowContinuityViewModel } from "@/app-shell.view-model";
import { Button } from "@/components/ui/button";
import { PanelSurface } from "@/components/ui/panel-surface";
import { KeyValueGrid, MetricCard, type MetricCardTone } from "@/components/data/concrete";
import { ReadinessPanel, SeverityBadge, WorkspaceSection } from "@/components/operations";
import { buildDailyControlTowerModel } from "@/lib/daily-control-tower";

export interface DailyControlTowerScreenProps {
  viewModel: AppShellWorkflowContinuityViewModel;
  trustStrip: AppShellTrustStripState;
}

// Concrete severity layer: the control tower's workflow-continuity tones
// (ready · review · blocked · pending) resolve to the operator-readiness status
// strings consumed by SeverityBadge / ReadinessPanel.
type ControlTowerTone = "ready" | "review" | "blocked" | "pending";

const severityStatusForTone: Record<ControlTowerTone, string> = {
  ready: "Ready",
  review: "ReviewRequired",
  blocked: "Blocked",
  pending: "Pending"
};

const badgeVariantToStatus: Record<"outline" | "success" | "warning" | "danger", string> = {
  success: "Ready",
  warning: "ReviewRequired",
  danger: "Blocked",
  outline: "Info"
};

export function DailyControlTowerScreen({ viewModel, trustStrip }: DailyControlTowerScreenProps) {
  const model = buildDailyControlTowerModel(viewModel, trustStrip);
  // Triage-in-place: the operator can inspect any queue row's evidence without
  // leaving the tower. Falls back to the top-ranked row until one is chosen
  // (or when the chosen row leaves the queue on refresh).
  const [selectedQueueItemId, setSelectedQueueItemId] = useState<string | null>(null);
  const selectedQueueRow =
    model.queueRows.find((row) => row.item.id === selectedQueueItemId) ?? model.queueRows[0] ?? null;

  return (
    <section
      className="space-y-5"
      aria-labelledby="daily-control-tower-heading"
      aria-describedby="daily-control-tower-summary"
    >
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div className="max-w-4xl space-y-2">
          <p className="eyebrow-label">Daily control tower</p>
          <h2 id="daily-control-tower-heading" className="font-display text-2xl font-semibold text-foreground">
            What needs an operator decision now
          </h2>
          <p id="daily-control-tower-summary" className="text-sm leading-6 text-muted-foreground">
            Ranked from shell workflow continuity, trust posture, linked context, and timestamped evidence.
          </p>
        </div>

        <Button asChild variant="default" size="sm" className="self-start">
          <Link to={model.nextActionHref} aria-label={model.nextActionAriaLabel}>
            <span>{model.nextActionLabel}</span>
            <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
          </Link>
        </Button>
      </div>

      <ReadinessPanel
        state={severityStatusForTone[model.statusTone]}
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

      <section
        aria-label="Daily control tower decision drivers"
        className="grid gap-3 md:grid-cols-2 xl:grid-cols-5"
      >
        {model.driverItems.map((item) => (
          <MetricCard
            key={item.id}
            label={item.label}
            value={item.value}
            delta={item.detail}
            tone={metricToneForVariant(item.badgeVariant)}
          />
        ))}
      </section>

      <section aria-label="Daily control tower trust posture" className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        {model.trustItems.map((item) => (
          <PanelSurface key={item.id} flat className="space-y-2 p-4">
            <div className="flex items-center justify-between gap-2">
              <span className="eyebrow-label">{item.label}</span>
              <SeverityBadge status={severityStatusForTone[item.tone]} label={item.value} />
            </div>
            <p className="text-xs leading-5 text-muted-foreground">{item.detail}</p>
            {item.href && item.actionLabel ? (
              <Link
                to={item.href}
                className="inline-flex items-center gap-1 text-xs font-medium text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                aria-label={item.ariaLabel}
              >
                <span>{item.actionLabel}</span>
                <ArrowRight className="h-3 w-3" aria-hidden="true" />
              </Link>
            ) : null}
          </PanelSurface>
        ))}
      </section>

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
                              status={badgeVariantToStatus[row.badgeVariant]}
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
                  {selectedQueueRow.proofPassportItems.map((passportItem) => (
                    <div key={passportItem.id} className="rounded border border-border bg-card p-2">
                      <dt className="eyebrow-label">{passportItem.label}</dt>
                      <dd className="mt-1 text-xs font-medium leading-5 text-foreground">{passportItem.value}</dd>
                      <dd className="mt-1 text-[11px] leading-4 text-muted-foreground">{passportItem.detail}</dd>
                    </div>
                  ))}
                </dl>
              </section>
            ) : null}
          </div>
        ) : (
          <PanelSurface flat role="status" className="p-4 text-sm text-muted-foreground">
            {model.emptyQueueText}
          </PanelSurface>
        )}
      </WorkspaceSection>

      <div className="grid gap-4 xl:grid-cols-2">
        <SupportingList
          label={viewModel.linkedContextLabel}
          summary={viewModel.linkedContextSummary}
          emptyText={viewModel.linkedContextEmptyText}
          items={model.linkedContextItems.map((item) => ({
            id: item.id,
            label: item.label,
            detail: `${item.workspaceLabel}: ${item.detail}`,
            status: item.statusLabel,
            href: item.route,
            ariaLabel: item.ariaLabel,
            tone: item.tone
          }))}
        />
        <SupportingList
          label={viewModel.evidenceTimelineLabel}
          summary={viewModel.evidenceTimelineSummary}
          emptyText={viewModel.evidenceTimelineEmptyText}
          items={model.evidenceTimelineItems.map((item) => ({
            id: item.id,
            label: item.label,
            detail: `${item.workspaceLabel} at ${item.timestampLabel}: ${item.detail}`,
            status: item.workspaceLabel,
            href: item.route,
            ariaLabel: item.ariaLabel,
            tone: item.tone
          }))}
        />
      </div>
    </section>
  );
}

function metricToneForVariant(variant: "outline" | "success" | "warning" | "danger"): MetricCardTone {
  switch (variant) {
    case "success":
      return "success";
    case "warning":
      return "warning";
    case "danger":
      return "danger";
    case "outline":
      return "neutral";
  }
}

interface SupportingListItem {
  id: string;
  label: string;
  detail: string;
  status: string;
  href: string;
  ariaLabel: string;
  tone: "ready" | "review" | "blocked" | "pending";
}

function SupportingList({
  label,
  summary,
  emptyText,
  items
}: {
  label: string;
  summary: string;
  emptyText: string;
  items: SupportingListItem[];
}) {
  return (
    <WorkspaceSection title={label} summary={summary}>
      {items.length > 0 ? (
        <ul className="space-y-2" aria-label={label}>
          {items.map((item) => (
            <li key={item.id}>
              <PanelSurface flat className="flex items-start justify-between gap-3 p-3">
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-sm font-medium text-foreground">{item.label}</span>
                    <SeverityBadge status={severityStatusForTone[item.tone]} label={item.status} />
                  </div>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{item.detail}</p>
                </div>
                <Link
                  to={item.href}
                  className="shrink-0 text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  aria-label={item.ariaLabel}
                >
                  <ArrowRight className="h-4 w-4" aria-hidden="true" />
                </Link>
              </PanelSurface>
            </li>
          ))}
        </ul>
      ) : (
        <PanelSurface flat className="p-4 text-sm text-muted-foreground">
          {emptyText}
        </PanelSurface>
      )}
    </WorkspaceSection>
  );
}
