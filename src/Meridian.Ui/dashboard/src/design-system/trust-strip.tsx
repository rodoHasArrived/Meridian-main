import { Link } from "react-router-dom";
import type { AppShellTrustStripState } from "@/app-shell.view-model";
import { cn } from "@/lib/utils";

/** Build / environment / data-source / provider trust posture strip rendered inside the masthead. */
export function DesignSystemTrustStrip({ viewModel }: { viewModel: AppShellTrustStripState }) {
  return (
    <section className="workstation-trust-strip" aria-label={viewModel.ariaLabel}>
      {viewModel.items.map((item) => {
        const content = (
          <>
            <span className="workstation-trust-label">{item.label}</span>
            <span className="workstation-trust-value">{item.value}</span>
            <span className="sr-only">
              {item.detail}
              {item.actionLabel ? ` ${item.actionLabel}.` : ""}
            </span>
          </>
        );

        return item.href ? (
          <Link
            key={item.id}
            to={item.href}
            className={cn("workstation-trust-item", `workstation-trust-item-${item.tone}`)}
            aria-label={`${item.ariaLabel} ${item.actionLabel}.`}
          >
            {content}
          </Link>
        ) : (
          <span
            key={item.id}
            className={cn("workstation-trust-item", `workstation-trust-item-${item.tone}`)}
            aria-label={item.ariaLabel}
          >
            {content}
          </span>
        );
      })}
    </section>
  );
}
