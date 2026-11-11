import { ChevronDown } from "lucide-react";
import { Link } from "react-router-dom";
import type { AppShellTrustStripState } from "@/app-shell.view-model";
import { DesignSystemBadge } from "@/design-system/badge";
import { cn } from "@/lib/utils";

/**
 * The masthead environment badge: PAPER / LIVE / DEMO, plus the data qualifier (SEEDED,
 * SIMULATED, SAMPLE, UNKNOWN) whenever the loaded data is not confirmed real. It says what the
 * operator is looking at and nothing else; what needs attention is the attention pill's job, so
 * this badge never changes colour for a provider or queue issue. Build, environment, provenance
 * and provider facts stay available in a native disclosure.
 */
export function DesignSystemTrustStrip({ viewModel }: { viewModel: AppShellTrustStripState }) {
  const { environment } = viewModel;
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
        <summary className="workstation-trust-summary" aria-label={summaryAriaLabel}>
          <DesignSystemBadge
            className="workstation-trust-environment"
            variant={environment.variant}
            dot
            data-environment={environment.variant}
          >
            {environment.label}
          </DesignSystemBadge>
          {environment.dataLabel ? (
            <>
              <span className="workstation-trust-sep" aria-hidden="true">·</span>
              <span className="workstation-trust-value workstation-trust-data" data-testid="masthead-data-label">
                {environment.dataLabel}
              </span>
            </>
          ) : null}
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
