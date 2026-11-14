import { Clock3 } from "lucide-react";
import { cn } from "@/lib/utils";
import { normalizeFinancialLabel } from "@/lib/format";
import {
  buildMetricTrust,
  describeGroupedMetricProvenance,
  describeMetricProvenance,
  sharedMetricProvenance,
  withMetricContext,
  worstMetricFreshness
} from "@/lib/metric-trust";
import { resolveFigureEvidence, useWorkspaceFigureContext, type FigureSourceKey } from "@/lib/workspace-figure-context";
import { useOperatingScopeLabel } from "@/lib/operating-scope/scope-label-context";
import { resolveStatStripScope } from "@/lib/operating-scope/figure-scope";
import type { MetricFreshness, MetricSnapshot } from "@/types";
import { usesTrackedCaps } from "@/design-system/label-case";
import { hasMetricBasisDetail, metricBasisLabel, metricBasisValue, presentMetricChange } from "@/lib/metric-context";
import { useId } from "react";
import { MetricPassport } from "@/components/meridian/metric-passport";

const statToneClass: Record<MetricSnapshot["tone"], string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

const degradedStripLabel: Record<Exclude<MetricFreshness, "fresh">, string> = {
  stale: "Stale figures",
  unverified: "Unverified figures"
};

/**
 * Compact single-row alternative to the four-card metric band. Workspace
 * routes lead with their working surface, so headline metrics compress into
 * one strip; each stat stays scannable without spending a viewport band.
 *
 * The strip applies the headline-figure trust rules from `@/lib/metric-trust`: a change is shown
 * only with what it is measured against, a figure fed by stale or unverified data is muted and
 * carries a clock, and the source, as-of time and scope the figures share are stated once under
 * the row. When the figures come from different sources, each source is stated with the figures
 * it covers (and each figure's own provenance is on hover).
 *
 * Pass `source` and `figureSource` (and `scope` when the page has one) for a strip fed by the
 * workstation load: the as-of time and freshness then come from the shell's record of that
 * payload, so a payload whose reload failed shows stale figures even while others reloaded. A
 * figure in the strip that another payload supplies carries its own provenance and freshness,
 * which win over the strip's. A screen with its own freshness evidence stamps its metrics with
 * `withMetricContext` instead and leaves `source` off.
 *
 * Every strip in the shell prints the scope its figures were counted over (see
 * `resolveStatStripScope`): figures that state their own scope keep it, and the rest inherit one
 * from the shell, so no screen has to wire it.
 */
export function StatStrip({
  metrics: suppliedMetrics,
  label,
  className,
  compact = false,
  source,
  figureSource,
  scope
}: {
  metrics: MetricSnapshot[];
  label: string;
  className?: string;
  compact?: boolean;
  /** The read model behind the strip, e.g. "Strategy workspace". */
  source?: string;
  /** The payload that supplies the strip's figures, so its own load decides as-of and freshness. */
  figureSource?: FigureSourceKey;
  /** The accounts, fund or run the strip covers; overrides the scope inherited from the shell. */
  scope?: string | null;
}) {
  const workspace = useWorkspaceFigureContext();
  const shellScopeLabel = useOperatingScopeLabel();
  const basisTermId = useId();
  const basisId = useId();
  if (suppliedMetrics.length === 0) {
    return null;
  }

  const loaded = resolveFigureEvidence(workspace, figureSource);
  const stripScope = resolveStatStripScope({ scope, figureSource, workspace, shellScopeLabel });
  const stamped = source
    ? withMetricContext(suppliedMetrics, {
        provenance: { source, asOf: loaded.asOf, scope: stripScope },
        freshness: loaded.freshness
      })
    : suppliedMetrics;
  // A session that is not on real data says so in every figure's source.
  const dataLabel = workspace?.dataLabel;
  const metrics = dataLabel
    ? stamped.map((metric) => metric.provenance
        ? { ...metric, provenance: { ...metric.provenance, source: `${metric.provenance.source} (${dataLabel})` } }
        : metric)
    : stamped;
  // The basis the figures share is stated once, beside a "Basis" term. A basis nobody declared is
  // not repeated on the strip (it said "Not declared by source" on every strip); each figure's
  // passport still states it. Mixed bases fall back to a line per figure that declares one.
  const anyBasisDetail = metrics.some(hasMetricBasisDetail);
  const firstBasis = metricBasisValue(metrics[0]!);
  const basesAgree = metrics.every(metric => metricBasisValue(metric) === firstBasis);
  const sharedBasis = anyBasisDetail && basesAgree ? firstBasis : null;
  // Each figure is described by the term and its value ("Basis Not declared by source"), since the
  // value alone does not say what it is.
  const sharedBasisDescription = sharedBasis ? `${basisTermId} ${basisId}` : undefined;
  // Figures from different sources: state each source once with the figures it supplies.
  const provenanceLine = describeMetricProvenance(sharedMetricProvenance(metrics)) ?? describeGroupedMetricProvenance(metrics);
  const stripFreshness = worstMetricFreshness(metrics);
  // The provenance line already names the scope when the figures state one (per source when they
  // differ); otherwise the strip's own scope is printed once, so no strip leaves the operator
  // guessing what it counts.
  const provenanceStatesScope = Boolean(provenanceLine) && metrics.some((metric) => Boolean(metric.provenance?.scope?.trim()));
  const footnote = [
    stripFreshness === "fresh" ? null : degradedStripLabel[stripFreshness],
    provenanceLine,
    !provenanceStatesScope && stripScope ? `Scope: ${stripScope}` : null
  ].filter(Boolean).join(" · ");

  return (
    <div className={cn("panel-surface px-4 py-2.5", className)}>
      <dl
        aria-label={label}
        className={compact ? "grid grid-cols-[repeat(auto-fit,minmax(min(100%,10rem),1fr))] gap-3" : "flex flex-wrap items-baseline gap-x-6 gap-y-1.5"}
      >
        {metrics.map((metric) => {
          const trust = buildMetricTrust(metric);
          const metricLabel = normalizeFinancialLabel(metric.label);
          const delta = metric.comparisonPeriod ? presentMetricChange(metric) : trust.delta?.text;

          return (
            <div
              key={metric.id}
              className={cn("flex min-w-0 gap-2", compact ? "flex-col gap-0.5" : "items-baseline")}
              title={trust.title ?? undefined}
              data-freshness={trust.freshness}
            >
              <dt className={cn("whitespace-nowrap text-[11px] font-medium text-muted-foreground", usesTrackedCaps(metricLabel) && "uppercase tracking-[0.08em]")}>
                {metricLabel}
              </dt>
              <dd className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5" aria-describedby={sharedBasisDescription}>
                <span
                  className={cn(
                    "inline-flex items-baseline gap-1 whitespace-nowrap font-mono text-sm font-semibold tabular-nums",
                    trust.degraded ? "text-muted-foreground" : statToneClass[metric.tone]
                  )}
                >
                  {trust.degraded ? <Clock3 className="size-3 self-center" aria-hidden="true" /> : null}
                  {metric.value}
                </span>
                <MetricPassport metric={metric} />
                {!basesAgree && hasMetricBasisDetail(metric) ? <span className="text-xs text-muted-foreground">{metricBasisLabel(metric)}</span> : null}
                {trust.freshnessDetail ? <span className="sr-only">{trust.freshnessDetail}</span> : null}
                {delta ? (
                  <span className="whitespace-nowrap text-xs text-muted-foreground">{delta}</span>
                ) : null}
                {!provenanceLine && trust.provenanceText ? <span className="sr-only">{trust.provenanceText}</span> : null}
              </dd>
            </div>
          );
        })}
        {sharedBasis ? <div className="flex min-w-0 items-baseline gap-2 text-xs text-muted-foreground"><dt id={basisTermId}>Basis</dt><dd id={basisId}>{sharedBasis}</dd></div> : null}
      </dl>
      {footnote ? (
        <p className="mt-1.5 text-xs leading-4 text-muted-foreground" data-testid="stat-strip-provenance">
          {footnote}
        </p>
      ) : null}
    </div>
  );
}
