import { ChevronRight } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface BreadcrumbItem {
  label: ReactNode;
  href?: string;
  onClick?: () => void;
  current?: boolean;
}

export interface BreadcrumbProps extends React.HTMLAttributes<HTMLElement> {
  items: BreadcrumbItem[];
  separator?: ReactNode;
}

export function Breadcrumb({ className, items, separator, ...props }: BreadcrumbProps) {
  return (
    <nav
      aria-label="Breadcrumb"
      className={cn("flex flex-wrap items-center gap-1.5 font-mono text-xs text-muted-foreground", className)}
      {...props}
    >
      {items.map((item, index) => {
        const current = item.current ?? index === items.length - 1;
        return (
          <span key={`${index}-${String(item.label)}`} className="inline-flex items-center gap-1.5">
            {index > 0 ? (
              <span aria-hidden="true" className="text-muted-foreground/60">
                {separator ?? <ChevronRight className="h-3 w-3" />}
              </span>
            ) : null}
            {current ? (
              <span aria-current="page" className="font-sans font-semibold text-foreground">
                {item.label}
              </span>
            ) : item.href ? (
              <a className="text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40" href={item.href}>
                {item.label}
              </a>
            ) : (
              <button
                type="button"
                className="text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                onClick={item.onClick}
                disabled={!item.onClick}
              >
                {item.label}
              </button>
            )}
          </span>
        );
      })}
    </nav>
  );
}
