import { AlertTriangle, LoaderCircle } from "lucide-react";
import { Link } from "react-router-dom";
import type { ShellStatusPanel } from "@/app-shell.status-panel";
import { Button } from "@/components/ui/button";
import { StatusBanner } from "@/components/ui/status-banner";
import { cn } from "@/lib/utils";

export function WorkspaceStatus({ panel, onRetry, refreshing = false }: {
  panel: ShellStatusPanel;
  onRetry: () => void;
  refreshing?: boolean;
}) {
  const Icon = panel.tone === "loading" ? LoaderCircle : AlertTriangle;
  const itemSummary = panel.items.length > 0
    ? `${panel.items.length} ${panel.items.length === 1 ? "detail" : "details"}`
    : null;

  return (
    <section
      id={panel.id}
      role={panel.role}
      aria-live={panel.ariaLive}
      aria-labelledby={panel.titleId}
      aria-describedby={panel.detailId}
      aria-busy={panel.tone === "loading" || refreshing}
      className={cn(
        "shell-status-strip",
        `shell-status-strip-${panel.tone}`,
        panel.tone === "loading" && "startup-status-panel"
      )}
    >
      <div className="min-w-0">
        <StatusBanner
          tone={shellStatusToneToBannerTone(panel.tone)}
          title={(
            <span id={panel.titleId} className="inline-flex min-w-0 items-center gap-2">
              <Icon aria-hidden="true" className={`h-4 w-4 shrink-0 ${panel.tone === "loading" ? "animate-spin" : ""}`} />
              <span className="min-w-0 truncate">{panel.title}</span>
              {itemSummary ? (
                <span
                  aria-hidden="true"
                  className="rounded-sm border border-border/60 bg-background/45 px-1.5 py-0.5 font-mono text-[0.625rem] uppercase tracking-[0.12em] text-muted-foreground"
                >
                  {itemSummary}
                </span>
              ) : null}
            </span>
          )}
          detail={<span id={panel.detailId}>{panel.detail}</span>}
          className="shadow-none"
        />
        {panel.tone === "loading" ? (
          <div className="startup-status-meter mt-2" aria-hidden="true">
            <span />
          </div>
        ) : null}
        {panel.items.length > 0 ? (
          <details className="mt-2 text-xs" onKeyDown={(event) => {
            if (event.key === "Escape") {
              event.preventDefault();
              event.currentTarget.open = false;
              event.currentTarget.querySelector("summary")?.focus();
            }
          }}>
            <summary className="cursor-pointer font-semibold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary">
              {panel.itemListLabel} ({panel.items.length})
            </summary>
            <ul aria-label={panel.itemListLabel} className="mt-2 space-y-1">
              {panel.items.map((item) => (
                <li key={item.key} aria-label={item.ariaLabel}>
                  <span className="font-semibold">{item.label}:</span> {item.detail}
                </li>
              ))}
            </ul>
          </details>
        ) : null}
      </div>
      {panel.actionLabel || panel.secondaryActionHref ? (
        <div className="flex shrink-0 flex-wrap items-center gap-2">
          {panel.secondaryActionHref && panel.secondaryActionLabel ? (
            <Button asChild variant="outline" size="sm">
              <Link to={panel.secondaryActionHref} aria-label={panel.secondaryActionAriaLabel ?? panel.secondaryActionLabel}>
                {panel.secondaryActionLabel}
              </Link>
            </Button>
          ) : null}
          {panel.actionLabel ? (
            <Button
              variant="outline"
              size="sm"
              onClick={onRetry}
              aria-label={refreshing ? "Retrying workspace data" : panel.actionAriaLabel ?? panel.actionLabel}
              disabled={refreshing}
              disabledReason={refreshing ? "Workspace data recovery is in progress." : null}
            >
              {refreshing ? "Retrying…" : panel.actionLabel}
            </Button>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

function shellStatusToneToBannerTone(tone: ShellStatusPanel["tone"]): "danger" | "info" | "warning" {
  switch (tone) {
    case "danger":
      return "danger";
    case "warning":
      return "warning";
    case "loading":
    default:
      return "info";
  }
}
