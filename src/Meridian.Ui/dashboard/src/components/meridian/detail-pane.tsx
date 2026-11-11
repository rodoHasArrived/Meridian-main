import { useEffect, useRef, useState, type ReactNode } from "react";
import { Check, Copy } from "lucide-react";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import {
  inferDetailFieldKind,
  partitionDetailFields,
  usesDataFace,
  type DetailField,
  type DetailFieldKind,
  type DetailFieldTone,
  type ResolvedDetailField
} from "@/components/meridian/detail-pane.view-model";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { StatusBadge, toneToSeverity } from "@/design-system/status";
import { cn } from "@/lib/utils";

export type { DetailField, DetailFieldKind, DetailFieldTone } from "@/components/meridian/detail-pane.view-model";

const fieldToneClass: Record<DetailFieldTone, string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
};

// How each kind wraps. Prose wraps only at word boundaries (`hyphens-none` stops the browser
// splitting a word); figures and dates stay on one line; identifiers may wrap anywhere, since
// they have no word boundaries to wrap at.
const fieldKindClass: Record<DetailFieldKind, string> = {
  text: "break-words hyphens-none",
  number: "whitespace-nowrap tabular-nums",
  date: "whitespace-nowrap tabular-nums",
  code: "[overflow-wrap:anywhere]",
  id: "[overflow-wrap:anywhere]"
};

function fieldValueClass(kind: DetailFieldKind, value: string): string {
  return cn(fieldKindClass[kind], usesDataFace(kind, value) && "font-mono");
}

/** Rows flow into as many columns as fit, each at least 20rem wide. */
const AUTO_COLUMNS_CLASS = "grid grid-cols-[repeat(auto-fit,minmax(min(100%,20rem),1fr))] gap-x-8";

/**
 * One label/value row of a detail list; render it inside a `<dl>`.
 *
 * The label sits beside a left-aligned value while the row has room for both (about 290px);
 * below that the value wraps under its label instead of being squeezed into a column too narrow
 * for a word. The row sizes itself, so it behaves the same in a side pane and in a grid cell.
 * Numbers, dates and identifiers stay on one line in the data face; prose wraps at word
 * boundaries only.
 */
export function DetailFieldItem({
  label,
  value,
  tone = "default",
  kind,
  detail,
  className,
  valueClassName
}: {
  label: ReactNode;
  value: string;
  tone?: DetailFieldTone;
  kind?: DetailFieldKind;
  detail?: string;
  className?: string;
  /** Extra classes for the value, e.g. a tone class a view model already resolved. */
  valueClassName?: string;
}) {
  const resolvedKind = kind ?? inferDetailFieldKind(value);

  return (
    <div className={cn("flex min-w-0 flex-wrap items-start gap-x-4 gap-y-0.5 border-b border-border/60 py-1.5", className)}>
      <dt className="min-w-[7rem] max-w-[14rem] shrink-0 grow-0 basis-[42%] text-xs leading-5 text-muted-foreground">{label}</dt>
      <dd className={cn("min-w-0 grow basis-[9.5rem] text-left text-xs leading-5", fieldValueClass(resolvedKind, value), fieldToneClass[tone], valueClassName)}
        data-detail-kind={resolvedKind}
      >
        {value}
        {detail ? (
          <span className="mt-0.5 block whitespace-normal break-words font-sans text-xs leading-5 text-muted-foreground">
            {detail}
          </span>
        ) : null}
      </dd>
    </div>
  );
}

/**
 * The key-value list of a detail pane: {@link DetailFieldItem} rows between hairlines.
 *
 * `columns="auto"` lets a list in a wide surface (a summary band, not a side pane) flow into as
 * many columns of rows as fit; a side pane stays one column either way.
 */
export function DetailFieldList({
  fields,
  label,
  columns = 1,
  className
}: {
  fields: readonly ResolvedDetailField[];
  label?: string;
  columns?: 1 | "auto";
  className?: string;
}) {
  if (fields.length === 0) {
    return null;
  }

  return (
    <dl aria-label={label} className={cn("border-t border-border/60", columns === "auto" && AUTO_COLUMNS_CLASS, className)}>
      {fields.map((field) => (
        <DetailFieldItem
          key={field.label}
          label={field.label}
          value={field.value}
          tone={field.tone}
          kind={field.kind}
          detail={field.detail}
        />
      ))}
    </dl>
  );
}

function CopyValueButton({ label, value }: { label: string; value: string }) {
  const [copied, setCopied] = useState(false);
  const resetTimer = useRef<number | null>(null);

  useEffect(() => () => {
    if (resetTimer.current !== null) {
      window.clearTimeout(resetTimer.current);
    }
  }, []);

  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      if (resetTimer.current !== null) {
        window.clearTimeout(resetTimer.current);
      }
      resetTimer.current = window.setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard access can be denied; the value stays selectable in the row.
      setCopied(false);
    }
  }

  return (
    <button
      type="button"
      onClick={() => void copy()}
      aria-label={copied ? `${label} copied` : `Copy ${label}`}
      className="inline-flex size-6 shrink-0 items-center justify-center rounded-[2px] text-muted-foreground hover:text-foreground focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40"
    >
      {copied ? <Check className="size-3.5" aria-hidden="true" /> : <Copy className="size-3.5" aria-hidden="true" />}
    </button>
  );
}

/** Identifier rows for a "Technical details" disclosure: each value is whole, selectable and copyable. */
export function DetailTechnicalFields({ fields }: { fields: readonly DetailField[] }) {
  return (
    <dl className="space-y-2">
      {fields.filter((field) => field.value.trim()).map((field) => (
        <div key={field.label} className="min-w-0">
          <dt className="text-xs leading-5 text-muted-foreground">{field.label}</dt>
          <dd className="flex min-w-0 items-start gap-2">
            <span className="min-w-0 flex-1 font-mono text-xs leading-5 text-foreground [overflow-wrap:anywhere]">
              {field.value}
            </span>
            <CopyValueButton label={field.label} value={field.value} />
          </dd>
        </div>
      ))}
    </dl>
  );
}

/**
 * A key-value list from raw view-model rows, for use outside a {@link DetailPane}: the reading
 * path as a {@link DetailFieldList} and any opaque identifiers behind "Technical details".
 */
export function DetailFields({
  fields,
  shownInRow,
  label,
  columns,
  className
}: {
  fields: readonly DetailField[];
  shownInRow?: readonly string[];
  label?: string;
  columns?: 1 | "auto";
  className?: string;
}) {
  const { visible, technical } = partitionDetailFields(fields, { shownInRow });
  if (visible.length === 0 && technical.length === 0) {
    return null;
  }

  return (
    <div className={className}>
      <DetailFieldList fields={visible} label={label} columns={columns} />
      {technical.length > 0 ? (
        <TechnicalDetails label="Technical details" className={visible.length > 0 ? "mt-3" : undefined}>
          <DetailTechnicalFields fields={technical} />
        </TechnicalDetails>
      ) : null}
    </div>
  );
}

export interface DetailPaneStatus {
  label: string;
  /** A view-model tone ("success", "warning", "danger", "default") or any readiness string. */
  tone: string;
}

export interface DetailPaneProps {
  id: string;
  /** Accessible name of the pane; say what is selected, e.g. "Order PO-1 detail". */
  ariaLabel: string;
  /** What kind of thing is selected, e.g. "Order", "Holding", "Break". */
  objectType: string;
  /** The selected object's name. Omit (with `empty`) when nothing is selected. */
  title?: ReactNode;
  status?: DetailPaneStatus | null;
  /** One line under the heading, e.g. "Buy 20 MSFT". */
  subtitle?: ReactNode;
  /** A sentence or two of context under the heading. */
  summary?: ReactNode;
  /** The next action(s) for the selected object. */
  actions?: ReactNode;
  fields?: readonly DetailField[];
  /** Column labels the selected table row already shows; those fields are not repeated. */
  shownInRow?: readonly string[];
  /** Accessible name of the field list. @default `${objectType} details` */
  fieldsLabel?: string;
  /** Shown instead of the heading and fields when nothing is selected. */
  empty?: { title?: string; text: string } | null;
  /** Placed above the heading, e.g. "Selected holding is outside the current results." */
  notice?: ReactNode;
  children?: ReactNode;
  className?: string;
  role?: "region" | "complementary" | "status";
  ariaLive?: "polite" | "assertive" | "off";
  headingLevel?: 3 | 4;
}

/**
 * The one detail pane for a selected table row.
 *
 * It has a single heading line (object type, name, one status badge) in place of the separate
 * "Selected from …" pill, "Detail" chip and status eyebrow older panes stacked. Its key-value list
 * (see {@link DetailFieldList}) keeps values left-aligned and whole, and moves opaque identifiers
 * into a copyable "Technical details" disclosure. Pass `shownInRow` so the pane shows what the
 * selected row does not, and `actions` for the next step.
 *
 * Keyboard focus hand-off and Escape-to-row come from `DenseRowDetailPanel`, so the pane works
 * with `DenseDataTable` row selection unchanged.
 */
export function DetailPane({
  id,
  ariaLabel,
  objectType,
  title,
  status,
  subtitle,
  summary,
  actions,
  fields = [],
  shownInRow,
  fieldsLabel,
  empty,
  notice,
  children,
  className,
  role = "region",
  ariaLive = "polite",
  headingLevel = 3
}: DetailPaneProps) {
  const { visible, technical } = partitionDetailFields(fields, { shownInRow });
  const Heading = headingLevel === 3 ? "h3" : "h4";
  const showEmpty = Boolean(empty) && (title === undefined || title === null || title === "");

  return (
    <DenseRowDetailPanel
      id={id}
      ariaLabel={ariaLabel}
      role={role}
      ariaLive={ariaLive}
      pane
      className={cn("panel-surface h-fit min-w-0 rounded-md border border-border/70 p-4", className)}
    >
      {notice ? <div className="mb-3 border-b border-border pb-3">{notice}</div> : null}
      {showEmpty ? (
        <div role="status" className="text-sm leading-6 text-muted-foreground">
          {empty?.title ? <p className="font-medium text-foreground">{empty.title}</p> : null}
          <p>{empty?.text}</p>
        </div>
      ) : (
        <>
          <header className="flex flex-wrap items-start justify-between gap-x-3 gap-y-1">
            <Heading className="min-w-0 break-words text-sm font-semibold leading-5 text-foreground">
              <span className="font-normal text-muted-foreground">{objectType}</span>{" "}
              <span data-detail-pane-title="true">{title}</span>
            </Heading>
            {status ? <StatusBadge status={toneToSeverity(status.tone)} label={status.label} /> : null}
          </header>
          {subtitle ? <p className="mt-0.5 break-words text-xs leading-5 text-muted-foreground">{subtitle}</p> : null}
          {summary ? <p className="mt-2 break-words text-xs leading-5 text-foreground/80">{summary}</p> : null}
          {actions ? <div className="mt-3 flex flex-wrap gap-2">{actions}</div> : null}
          <DetailFieldList fields={visible} label={fieldsLabel ?? `${objectType} details`} className="mt-3" />
          {children ? <div className="mt-3">{children}</div> : null}
          {technical.length > 0 ? (
            <TechnicalDetails label="Technical details" className="mt-3">
              <DetailTechnicalFields fields={technical} />
            </TechnicalDetails>
          ) : null}
        </>
      )}
    </DenseRowDetailPanel>
  );
}
