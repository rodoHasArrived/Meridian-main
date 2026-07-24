import { ChevronDown } from "lucide-react";
import type { DetailsHTMLAttributes, ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface TechnicalDetailsProps extends Omit<DetailsHTMLAttributes<HTMLDetailsElement>, "title"> {
  label?: "System details" | "Audit details" | "Advanced" | string;
  description?: ReactNode;
  children: ReactNode;
  contentClassName?: string;
}

/**
 * Native, keyboard-accessible progressive disclosure for identifiers, hashes,
 * endpoint paths, rule codes, and other material that supports an operator task
 * without belonging in its default reading path.
 */
export function TechnicalDetails({
  label = "System details",
  description,
  children,
  className,
  contentClassName,
  ...props
}: TechnicalDetailsProps) {
  return (
    <details className={cn("group rounded-[2px] border border-border bg-secondary/15", className)} {...props}>
      <summary className="flex min-h-9 cursor-pointer list-none items-center justify-between gap-3 px-3 py-2 text-sm font-medium text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 [&::-webkit-details-marker]:hidden">
        <span>{label}</span>
        <ChevronDown
          className="h-4 w-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-180"
          aria-hidden="true"
        />
      </summary>
      <div className={cn("border-t border-border/70 px-3 py-3", contentClassName)}>
        {description ? <p className="mb-3 text-xs leading-5 text-muted-foreground">{description}</p> : null}
        {children}
      </div>
    </details>
  );
}
