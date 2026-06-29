import { Link } from "react-router-dom";
import type { AppShellTrustStripState } from "@/app-shell.view-model";
import { cn } from "@/lib/utils";

type WorkstationTrustStripVariant = "compact" | "card-list";

export function WorkstationTrustStrip({
  viewModel,
  variant = "compact",
  ariaLabel,
  className
}: {
  viewModel: AppShellTrustStripState;
  variant?: WorkstationTrustStripVariant;
  ariaLabel?: string;
  className?: string;
}) {
  if (variant === "card-list") {
    return (
      <dl className={cn("grid gap-2", className)} aria-label={ariaLabel ?? viewModel.ariaLabel}>
        {viewModel.items.map((item) => (
          <div key={item.id} className={cn("rounded-md border bg-background/45 px-3 py-2", trustStripToneBorderClass(item.tone))}>
            <dt className="text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">{item.label}</dt>
            <dd className="mt-1 text-sm font-semibold text-foreground">{item.value}</dd>
            <dd className="mt-1 text-xs leading-5 text-muted-foreground">{item.detail}</dd>
            {item.href && item.actionLabel ? (
              <dd className="mt-2">
                <Link to={item.href} className="text-xs font-medium text-primary hover:underline" aria-label={`${item.ariaLabel} ${item.actionLabel}.`}>
                  {item.actionLabel}
                </Link>
              </dd>
            ) : null}
          </div>
        ))}
      </dl>
    );
  }

  return (
    <section className={cn("workstation-trust-strip", className)} aria-label={ariaLabel ?? viewModel.ariaLabel}>
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

function trustStripToneBorderClass(tone: AppShellTrustStripState["items"][number]["tone"]): string {
  switch (tone) {
    case "blocked":
      return "border-danger/30";
    case "review":
      return "border-warning/30";
    case "ready":
      return "border-success/30";
    case "pending":
    default:
      return "border-border/70";
  }
}
