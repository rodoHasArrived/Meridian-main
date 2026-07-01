import { ArrowRight } from "lucide-react";
import { Link } from "react-router-dom";
import type { AppShellTrustStripState, AppShellWorkflowContinuityViewModel } from "@/app-shell.view-model";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { PanelSurface } from "@/components/ui/panel-surface";
import { buildDailyControlTowerModel } from "@/lib/daily-control-tower";

export interface DailyControlTowerScreenProps {
  viewModel: AppShellWorkflowContinuityViewModel;
  trustStrip: AppShellTrustStripState;
}

export function DailyControlTowerScreen({ viewModel, trustStrip }: DailyControlTowerScreenProps) {
  const model = buildDailyControlTowerModel(viewModel, trustStrip);

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

      <PanelSurface flat className="grid gap-3 p-4 sm:grid-cols-2 xl:grid-cols-5">
        <DailyControlTowerFact label="Status">
          <Badge variant={model.statusBadgeVariant} dot>{model.statusLabel}</Badge>
        </DailyControlTowerFact>
        <DailyControlTowerFact label="Owner">{model.ownerLabel}</DailyControlTowerFact>
        <DailyControlTowerFact label="Output">{model.outputLabel}</DailyControlTowerFact>
        <DailyControlTowerFact label="Next action">{model.nextActionLabel}</DailyControlTowerFact>
        <DailyControlTowerFact label="Evidence">{model.evidenceLabel}</DailyControlTowerFact>
      </PanelSurface>

      <section aria-label="Daily control tower decision drivers" className="grid gap-3 md:grid-cols-2 xl:grid-cols-5">
        {model.driverItems.map((item) => (
          <PanelSurface key={item.id} flat className="space-y-2 p-4">
            <div className="flex items-center justify-between gap-2">
              <span className="eyebrow-label">{item.label}</span>
              <Badge variant={item.badgeVariant}>{item.value}</Badge>
            </div>
            <p className="text-xs leading-5 text-muted-foreground">{item.detail}</p>
          </PanelSurface>
        ))}
      </section>

      <section aria-label="Daily control tower trust posture" className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        {model.trustItems.map((item) => (
          <PanelSurface key={item.id} flat className="space-y-2 p-4">
            <div className="flex items-center justify-between gap-2">
              <span className="eyebrow-label">{item.label}</span>
              <Badge variant={badgeVariantForTrustTone(item.tone)}>{item.value}</Badge>
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

      <PanelSurface flat className="overflow-hidden">
        <div className="border-b border-border px-4 py-3">
          <h3 className="text-sm font-semibold text-foreground">Finance queue</h3>
          <p className="mt-1 text-xs leading-5 text-muted-foreground">
            Each row carries the evidence needed to move from source issue to downstream output.
          </p>
        </div>
        {model.queueRows.length > 0 ? (
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
                {model.queueRows.map((row) => (
                  <tr key={row.item.id} className="align-top">
                    <td className="max-w-sm px-4 py-4">
                      <div className="space-y-2">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="font-medium text-foreground">{row.item.label}</span>
                          <Badge variant={row.badgeVariant}>{row.statusLabel}</Badge>
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
                    <td className="min-w-[22rem] px-4 py-4">
                      <section aria-label={`${row.item.label} Evidence summary`} className="space-y-3">
                        <div>
                          <h3 className="text-sm font-semibold text-foreground">Evidence summary</h3>
                          <p className="mt-1 text-xs leading-5 text-muted-foreground">{row.proofPassportSummary}</p>
                        </div>
                        <dl className="grid gap-2 sm:grid-cols-2">
                          {row.proofPassportItems.map((passportItem) => (
                            <div key={passportItem.id} className="rounded border border-border bg-background/60 p-2">
                              <dt className="eyebrow-label">{passportItem.label}</dt>
                              <dd className="mt-1 text-xs font-medium leading-5 text-foreground">{passportItem.value}</dd>
                              <dd className="mt-1 text-[11px] leading-4 text-muted-foreground">{passportItem.detail}</dd>
                            </div>
                          ))}
                        </dl>
                      </section>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p role="status" className="p-4 text-sm text-muted-foreground">{model.emptyQueueText}</p>
        )}
      </PanelSurface>

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

interface DailyControlTowerFactProps {
  label: string;
  children: React.ReactNode;
}

function DailyControlTowerFact({ label, children }: DailyControlTowerFactProps) {
  return (
    <div className="min-w-0 space-y-1">
      <div className="eyebrow-label">{label}</div>
      <div className="min-w-0 text-sm font-medium leading-5 text-foreground">{children}</div>
    </div>
  );
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
    <PanelSurface flat className="space-y-3 p-4">
      <div>
        <h3 className="text-sm font-semibold text-foreground">{label}</h3>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">{summary}</p>
      </div>
      {items.length > 0 ? (
        <ul className="space-y-2" aria-label={label}>
          {items.map((item) => (
            <li key={item.id} className="flex items-start justify-between gap-3 rounded border border-border bg-background/60 p-3">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-sm font-medium text-foreground">{item.label}</span>
                  <Badge variant={badgeVariantForTrustTone(item.tone)}>{item.status}</Badge>
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
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-sm text-muted-foreground">{emptyText}</p>
      )}
    </PanelSurface>
  );
}

function badgeVariantForTrustTone(tone: "ready" | "review" | "blocked" | "pending") {
  switch (tone) {
    case "blocked":
      return "danger";
    case "review":
      return "warning";
    case "ready":
      return "success";
    case "pending":
      return "outline";
  }
}
