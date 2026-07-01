// Meridian EmptyState (Concrete) — a consistent zero-data placeholder for tables and
// panels. A muted line-icon, a title, an optional detail line, and an optional primary
// action (steel accent). Flat, no decoration.
import { injectStyle } from "../operations/inject-style";

const CSS = `
.mds-empty{display:flex;flex-direction:column;align-items:center;justify-content:center;
  text-align:center;font-family:var(--font-body);padding:48px 24px;gap:12px;}
.mds-empty--compact{padding:24px 16px;gap:8px;}
.mds-empty__icon{color:var(--text-disabled,#889099);display:inline-flex;}
.mds-empty__title{font-size:14px;font-weight:600;color:var(--text-secondary,#4D5967);}
.mds-empty--compact .mds-empty__title{font-size:13px;}
.mds-empty__detail{font-size:12px;color:var(--text-muted,#59636F);max-width:280px;line-height:1.5;}
.mds-empty__action{margin-top:4px;padding:7px 16px;border:1px solid var(--accent,#2F6F8F);
  background:var(--accent,#2F6F8F);color:#fff;border-radius:var(--radius-button,2px);
  font-family:var(--font-body);font-size:12px;font-weight:600;cursor:pointer;transition:background .12s ease;}
.mds-empty__action:hover{background:var(--accent-pressed,#255B75);border-color:var(--accent-pressed,#255B75);}
.mds-empty__action:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:2px;}
`;

export type EmptyStateIcon = "table" | "search" | "chart" | "docs" | "inbox";

const ICONS: Record<EmptyStateIcon, JSX.Element> = {
  table: (
    <>
      <rect x="3" y="3" width="18" height="18" rx="2" />
      <line x1="3" y1="9" x2="21" y2="9" />
      <line x1="3" y1="15" x2="21" y2="15" />
      <line x1="9" y1="9" x2="9" y2="21" />
    </>
  ),
  search: (
    <>
      <circle cx="11" cy="11" r="7" />
      <line x1="16.5" y1="16.5" x2="21" y2="21" />
    </>
  ),
  chart: <polyline points="22 12 18 12 15 21 9 3 6 12 2 12" />,
  docs: (
    <>
      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
      <polyline points="14 2 14 8 20 8" />
    </>
  ),
  inbox: (
    <>
      <polyline points="22 13 16 13 14 16 10 16 8 13 2 13" />
      <path d="M5.45 5.11L2 13v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-7.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z" />
    </>
  ),
};

export interface EmptyStateProps {
  /** @default "table" */
  icon?: EmptyStateIcon;
  /** @default "No data" */
  title?: string;
  detail?: string;
  /** Action label. Renders a button only when both `action` and `onAction` are set. */
  action?: string;
  onAction?: () => void;
  /** Tighter padding for inline use. @default false */
  compact?: boolean;
}

/**
 * EmptyState — a zero-data placeholder for tables and panels.
 *
 * @example
 * <EmptyState icon="search" title="No matching rows" detail="Adjust the filters and try again." />
 */
export function EmptyState({
  icon = "table",
  title = "No data",
  detail,
  action,
  onAction,
  compact = false,
}: EmptyStateProps) {
  injectStyle("empty-state", CSS);
  return (
    <div className={`mds-empty${compact ? " mds-empty--compact" : ""}`}>
      <span className="mds-empty__icon" aria-hidden="true">
        <svg
          width={32}
          height={32}
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={1.5}
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          {ICONS[icon] ?? ICONS.table}
        </svg>
      </span>
      <div className="mds-empty__title">{title}</div>
      {detail && <div className="mds-empty__detail">{detail}</div>}
      {action && onAction && (
        <button type="button" className="mds-empty__action" onClick={onAction}>
          {action}
        </button>
      )}
    </div>
  );
}
