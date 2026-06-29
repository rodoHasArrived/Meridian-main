import { useId, type ReactNode } from "react";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

export type NumberPassportBadgeVariant = "default" | "outline" | "success" | "warning" | "danger";

export interface NumberPassportPanelItem {
  id: string;
  label: string;
  value: string;
  detail: string;
  href?: string;
  ariaLabel?: string | null;
  valueClassName?: string;
}

export interface NumberPassportPanelProps {
  title: string;
  ariaLabel: string;
  summary: string;
  statusLabel: string;
  statusBadgeVariant: NumberPassportBadgeVariant;
  items: NumberPassportPanelItem[];
  className?: string;
  renderLink?: (item: NumberPassportPanelItem, className: string, children: ReactNode) => ReactNode;
}

export function NumberPassportPanel({
  title,
  ariaLabel,
  summary,
  statusLabel,
  statusBadgeVariant,
  items,
  className,
  renderLink
}: NumberPassportPanelProps) {
  const summaryId = useId();

  return (
    <section
      className={cn("rounded-md border border-primary/20 bg-primary/5 p-3", className)}
      aria-label={ariaLabel}
      aria-describedby={summaryId}
    >
      <p id={summaryId} className="sr-only">{summary}</p>
      <div className="flex items-center justify-between gap-2">
        <h4 className="text-xs font-semibold uppercase text-muted-foreground">{title}</h4>
        <Badge variant={statusBadgeVariant}>{statusLabel}</Badge>
      </div>
      <dl className="mt-3 grid gap-2 sm:grid-cols-2">
        {items.map((item) => {
          const valueClassName = cn("block truncate font-mono text-sm font-semibold", item.valueClassName);
          return (
            <div key={item.id} className="min-w-0 rounded-md border border-border/60 bg-background/70 px-3 py-2">
              <dt className="text-[11px] font-semibold uppercase text-muted-foreground">{item.label}</dt>
              <dd className="mt-1 min-w-0">
                {item.href ? (
                  renderLink ? renderLink(item, valueClassName, item.value) : (
                    <a href={item.href} className={valueClassName} aria-label={item.ariaLabel ?? undefined}>
                      {item.value}
                    </a>
                  )
                ) : (
                  <span className={valueClassName}>{item.value}</span>
                )}
                {item.detail ? <p className="mt-1 line-clamp-2 text-xs leading-5 text-muted-foreground">{item.detail}</p> : null}
              </dd>
            </div>
          );
        })}
      </dl>
    </section>
  );
}
