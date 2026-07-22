// Meridian key/value fact grid (Concrete) — a small-caps muted label over a mono tabular
// value, laid out in N responsive columns. Used in entity summaries and detail panes.
import type { CSSProperties, ReactNode } from "react";

export interface KeyValueGridItem {
  label: ReactNode;
  value: ReactNode;
}

export interface KeyValueGridProps {
  items: KeyValueGridItem[];
  /** @default 2 */
  columns?: number;
}

const labelStyle: CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: 10,
  fontWeight: 600,
  fontVariant: "all-small-caps",
  letterSpacing: "0.03em",
  color: "var(--text-muted, #59636F)",
};

const valueStyle: CSSProperties = {
  fontFamily: "var(--font-data, monospace)",
  fontSize: 13,
  fontVariantNumeric: "tabular-nums",
  color: "var(--text-primary, #22272E)",
};

/**
 * KeyValueGrid — a label-over-value fact grid.
 *
 * @example
 * <KeyValueGrid columns={3} items={[{ label: "CUSIP", value: "037833100" }]} />
 */
export function KeyValueGrid({ items, columns = 2 }: KeyValueGridProps) {
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
        gap: "12px 24px",
      }}
    >
      {items.map((it, i) => (
        <div key={i} style={{ display: "flex", flexDirection: "column", gap: 3, minWidth: 0 }}>
          <div style={labelStyle}>{it.label}</div>
          <div style={valueStyle}>{it.value}</div>
        </div>
      ))}
    </div>
  );
}
