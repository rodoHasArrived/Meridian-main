import { ChevronDown } from "lucide-react";
import { Link } from "react-router-dom";
import type { AppShellAttentionState, AppShellTrustStripState, AppShellWorkflowContinuityViewModel } from "@/app-shell.view-model";
import { buildDataProvenanceBadgeViewModel, type DataProvenanceKind } from "@/app-shell.data-provenance-badge";
import { DataProvenanceBanner } from "@/components/meridian/data-provenance-banner";
import { cn } from "@/lib/utils";
import { DesignSystemBadge } from "@/design-system/badge";

const trustToneRank = { ready: 0, pending: 1, review: 2, blocked: 3 } as const;

/**
 * One masthead disclosure for environment, persistent data provenance and the worst workstation
 * issue. Its attention headline uses the same state as Today, page status rows and the footer;
 * source details and recovery links remain inspectable without repeating separate chrome.
 * A LIVE badge is shown only when real provenance agrees with the live session.
 */
export function DesignSystemTrustStrip({
  viewModel,
  provenance = "unknown",
  onRetryLiveData,
  retryBusy = false,
  decisionBrief,
  attention
}: {
  viewModel: AppShellTrustStripState;
  provenance?: DataProvenanceKind;
  onRetryLiveData?: () => void;
  retryBusy?: boolean;
  decisionBrief?: AppShellWorkflowContinuityViewModel["decisionBrief"];
  attention?: AppShellAttentionState;
}) {
  const mode = viewModel.items.find((item) => item.id === "mode")?.value ?? "Unknown mode";
  const source = viewModel.items.find((item) => item.id === "source")?.value ?? "Unknown source";
  const provider = viewModel.items.find((item) => item.id === "providers");
  const childTone = viewModel.items.reduce<(typeof viewModel.items)[number]["tone"]>(
    (current, item) => trustToneRank[item.tone] > trustToneRank[current] ? item.tone : current,
    "ready"
  );
  const briefTone = attention?.tone ?? decisionBrief?.statusTone ?? "ready";
  const provenanceTone = provenance === "real" ? "ready" : "review";
  const summaryTone = ([childTone, briefTone, provenanceTone] as const).reduce<typeof childTone>((current, tone) =>
    trustToneRank[tone] > trustToneRank[current] ? tone : current, "ready");
  const badge = buildDataProvenanceBadgeViewModel({ provenance });
  const summaryValue = provenance !== "real"
    ? provider && provider.tone !== "ready" ? provider.value : badge.headline
    : provider && provider.tone !== "ready"
    ? `${mode} · ${source} · ${provider.value}`
    : `${mode} · ${source}`;
  const headline = attention && !attention.clear ? attention.title : decisionBrief && decisionBrief.statusTone !== "ready" ? decisionBrief.title : summaryValue;
  const statusLabel = attention?.statusLabel ?? decisionBrief?.statusLabel;
  const summaryAriaLabel = `Environment, provenance, and provider posture. ${attention?.ariaLabel ?? ""} ${badge.ariaLabel} ${decisionBrief ? `${decisionBrief.statusLabel}: ${decisionBrief.title}. ` : ""}${viewModel.items.map((item) => item.ariaLabel).join(" ")}`;

  return (
    <section
      className="workstation-trust-strip mds-trust-strip"
      aria-label={viewModel.ariaLabel}
      aria-live="polite"
      aria-atomic="true"
      data-design-system-component="Status"
    >
      <details className="workstation-trust-details" onKeyDown={(event) => {
        if (event.key === "Escape") {
          event.currentTarget.open = false;
          event.currentTarget.querySelector("summary")?.focus();
        }
      }}>
        <summary
          className={cn(
            "workstation-trust-summary",
            `workstation-trust-item-${summaryTone}`,
            `mds-status--${summaryTone}`
          )}
          aria-label={summaryAriaLabel}
        >
          {viewModel.environment.variant !== "live" || provenance === "real" ? <DesignSystemBadge className="workstation-trust-environment" variant={viewModel.environment.variant} dot data-environment={viewModel.environment.variant}>{viewModel.environment.label}</DesignSystemBadge> : null}
          <DataProvenanceBanner provenance={provenance} compact />
          {statusLabel ? <span className="workstation-trust-blocker">{statusLabel}</span> : null}
          <span className="workstation-trust-value" title={headline}>
            {headline}
          </span>
          <ChevronDown className="workstation-trust-chevron h-3.5 w-3.5" aria-hidden="true" />
        </summary>
        <div
          className="workstation-trust-menu"
          role="group"
          aria-label="Environment, provenance, and provider details"
        >
          {badge.visible ? <p className="workstation-trust-detail">{badge.headline}. {badge.detail}</p> : null}
          {provenance === "unknown" && onRetryLiveData ? (
            <button type="button" className="workstation-trust-retry" disabled={retryBusy} onClick={onRetryLiveData}
              aria-label={retryBusy ? "Retrying live Meridian workspace data" : "Retry live Meridian workspace data"}>
              {retryBusy ? "Retrying live data" : "Retry live data"}
            </button>
          ) : null}
          {attention ? <Link to={attention.href} className="workstation-trust-item" aria-label={attention.ariaLabel} data-attention-tone={attention.tone}>
            <span className="workstation-trust-label">{attention.statusLabel}</span>
            <span className="workstation-trust-value">{attention.title}</span>
          </Link> : null}
          {decisionBrief ? (
            <Link to={decisionBrief.actionHref} className="workstation-trust-item" aria-label={decisionBrief.actionAriaLabel}>
              <span className="workstation-trust-label">{decisionBrief.statusLabel}</span>
              <span className="workstation-trust-value">{decisionBrief.title}</span>
              <span className="workstation-trust-detail">{decisionBrief.summary}</span>
              <span className="workstation-trust-detail">{decisionBrief.actionLabel}</span>
            </Link>
          ) : null}
          {viewModel.items.map((item) => {
            const content = (
              <>
                <span className="workstation-trust-label">{item.label}</span>
                <span className="workstation-trust-value">{item.value}</span>
                <span className="workstation-trust-detail">{item.detail}</span>
              </>
            );

            return item.href ? (
              <Link
                key={item.id}
                to={item.href}
                className={cn(
                  "workstation-trust-item",
                  `workstation-trust-item-${item.tone}`,
                  `mds-status--${item.tone}`
                )}
                aria-label={`${item.ariaLabel} ${item.actionLabel}.`}
              >
                {content}
              </Link>
            ) : (
              <span
                key={item.id}
                className={cn(
                  "workstation-trust-item",
                  `workstation-trust-item-${item.tone}`,
                  `mds-status--${item.tone}`
                )}
                aria-label={item.ariaLabel}
              >
                {content}
              </span>
            );
          })}
        </div>
      </details>
    </section>
  );
}
