import type { ReactNode } from "react";
import { cn } from "@/lib/utils";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import "@/styles/table-workbench.css";

export interface DetailRailField { label: string; value: ReactNode; numeric?: boolean; }
export function DetailRail({ id, title, status, actions, fields, children, ariaLabel, className, role = "region" }: {
  id?: string; title: string; status?: ReactNode; actions?: ReactNode; fields: DetailRailField[];
  children?: ReactNode; ariaLabel?: string; className?: string; role?: "region" | "complementary";
}) {
  return <DenseRowDetailPanel id={id ?? "selected-record-detail"} ariaLabel={ariaLabel ?? `${title} detail`} role={role} className={cn("detail-rail", className)}>
    <div className="detail-rail-header"><h3>{title}</h3>{status}</div>
    {actions ? <div className="mt-2 flex flex-wrap gap-2">{actions}</div> : null}
    <dl className="detail-rail-fields">{fields.map(field => <div key={field.label} className={cn(field.numeric && "detail-rail-field--numeric",
      typeof field.value === "string" && (field.value.length > 24 || /date|time|submitted|observed|created|updated|expires/i.test(field.label)) && "detail-rail-field--wide")}>
      <dt>{field.label}</dt><dd>{field.value}</dd>
    </div>)}</dl>
    {children}
  </DenseRowDetailPanel>;
}
