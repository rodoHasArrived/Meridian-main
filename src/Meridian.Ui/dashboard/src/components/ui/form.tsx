import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

type GridColumns = 1 | 2 | 3;
type GridSpan = 1 | 2 | 3;

const gridColumns: Record<GridColumns, string> = {
  1: "grid-cols-1",
  2: "grid-cols-1 md:grid-cols-2",
  3: "grid-cols-1 md:grid-cols-3"
};

const gridSpans: Record<GridSpan, string> = {
  1: "",
  2: "md:col-span-2",
  3: "md:col-span-3"
};

export interface FormGridProps extends React.HTMLAttributes<HTMLDivElement> {
  columns?: GridColumns;
}

export function FormGrid({ className, columns = 2, ...props }: FormGridProps) {
  return <div className={cn("grid gap-x-5 gap-y-3.5", gridColumns[columns], className)} {...props} />;
}

export const Form = FormGrid;

export interface FormRowProps extends React.HTMLAttributes<HTMLDivElement> {
  error?: ReactNode;
  hint?: ReactNode;
  horizontal?: boolean;
  label?: ReactNode;
  labelFor?: string;
  span?: GridSpan;
}

export function FormRow({
  children,
  className,
  error,
  hint,
  horizontal = false,
  label,
  labelFor,
  span = 1,
  ...props
}: FormRowProps) {
  return (
    <div
      className={cn(
        horizontal ? "grid gap-2 md:grid-cols-[7.5rem_minmax(0,1fr)] md:items-start" : "grid gap-1.5",
        gridSpans[span],
        className
      )}
      {...props}
    >
      {label ? (
        <label htmlFor={labelFor} className="pt-1 font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          {label}
        </label>
      ) : null}
      <div className="min-w-0">
        {children}
        {error ? <p className="mt-1 text-xs leading-5 text-danger">{error}</p> : hint ? <p className="mt-1 text-xs leading-5 text-muted-foreground">{hint}</p> : null}
      </div>
    </div>
  );
}

export function FormDivider({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("h-px bg-border", className)} {...props} />;
}

export function FormSectionLabel({ className, children, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn("border-b border-border pb-2 font-mono text-[10px] font-semibold uppercase tracking-[0.16em] text-muted-foreground", className)}
      {...props}
    >
      {children}
    </div>
  );
}
