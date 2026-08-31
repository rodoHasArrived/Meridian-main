import { ChevronDown } from "lucide-react";
import { Link } from "react-router-dom";
import type { AppShellTrustStripState } from "@/app-shell.view-model";
import { cn } from "@/lib/utils";

const trustToneRank = {
  ready: 0,
  pending: 1,
  review: 2,
  blocked: 3
} as const;

/**
 * Consolidated build / environment / data-source / provider posture control.
 * The masthead keeps one compact summary visible; supporting facts and route
 * actions remain available in a native disclosure.
 */
export function DesignSystemTrustStrip({ viewModel }: { viewModel: AppShellTrustStripState }) {
  const mode = viewModel.items.find((item) => item.id === "mode")?.value ?? "Unknown mode";
  const source = viewModel.items.find((item) => item.id === "source")?.value ?? "Unknown source";
  const provider = viewModel.items.find((item) => item.id === "providers");
  const summaryTone = viewModel.items.reduce<(typeof viewModel.items)[number]["tone"]>(
    (current, item) => trustToneRank[item.tone] > trustToneRank[current] ? item.tone : current,
    "ready"
  );
  const summaryValue = provider && provider.tone !== "ready"
    ? `${mode} · ${source} · ${provider.value}`
    : `${mode} · ${source}`;
  const summaryAriaLabel = `Environment, provenance, and provider posture. ${viewModel.items.map((item) => item.ariaLabel).join(" ")}`;

  return (
    <section
      className="workstation-trust-strip mds-trust-strip"
      aria-label={viewModel.ariaLabel}
      aria-live="polite"
      aria-atomic="true"
      data-design-system-component="Status"
    >
      <details className="workstation-trust-details">
        <summary
          className={cn(
            "workstation-trust-summary",
            `workstation-trust-item-${summaryTone}`,
            `mds-status--${summaryTone}`
          )}
          aria-label={summaryAriaLabel}
        >
          <span className="workstation-trust-label">Trust</span>
          <span className="workstation-trust-value">{summaryValue}</span>
          <ChevronDown className="workstation-trust-chevron h-3.5 w-3.5" aria-hidden="true" />
        </summary>
        <div
          className="workstation-trust-menu"
          role="group"
          aria-label="Environment, provenance, and provider details"
        >
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
