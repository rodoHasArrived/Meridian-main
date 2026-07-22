// Meridian entity summary (Concrete) — an identity fact band: a small-caps muted label
// over a value, with per-item mono/color control (e.g. tint a value green for healthy).
// Values ellipsis-truncate; laid out in N responsive columns.
import type { CSSProperties, ReactNode } from "react";

export interface EntitySummaryItem {
  label: ReactNode;
  value: ReactNode;
  /** Set false for prose values (names, descriptions). @default true */
  mono?: boolean;
  /** Override value color, e.g. "var(--severity-ready-fg)" for healthy. */
  color?: string;
}

export interface EntitySummaryProps {
  items: EntitySummaryItem[];
  /** @default 3 */
  columns?: number;
}

const labelStyle: CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: 10,
  fontWeight: 600,
  fontVariant: "all-small-caps",
  letterSpacing: "0.03em",
  color: "var(--text-muted, #59636F)",
  marginBottom: 3,
};

/**
 * EntitySummary — an identity fact band with per-item mono/color control.
 *
 * @example
 * <EntitySummary items={[
 *   { label: "Symbol", value: "AAPL" },
 *   { label: "Name", value: "Apple Inc.", mono: false },
 *   { label: "Status", value: "Active", color: "var(--severity-ready-fg)" },
 * ]} />
 */
export function EntitySummary({ items, columns = 3 }: EntitySummaryProps) {
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
        gap: "14px 20px",
      }}
    >
      {items.map((it, i) => (
        <div key={i} style={{ minWidth: 0 }}>
          <div style={labelStyle}>{it.label}</div>
          <div
            style={{
              fontSize: 13,
              fontFamily: it.mono === false ? "var(--font-body)" : "var(--font-data, monospace)",
              fontVariantNumeric: "tabular-nums",
              color: it.color || "var(--text-primary, #22272E)",
              overflow: "hidden",
              textOverflow: "ellipsis",
              whiteSpace: "nowrap",
            }}
          >
            {it.value}
          </div>
        </div>
      ))}
    </div>
  );
}
