// Meridian entity summary — identity fact band with per-item mono/color control. Light theme.
import React from "react";

export function EntitySummary({ items, columns = 3 }) {
  // items: [{ label, value, mono?, color? }]
  return (
    <div style={{
      display: "grid", gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
      gap: "14px 20px"
    }}>
      {items.map((it, i) => (
        <div key={i} style={{ minWidth: 0 }}>
          <div style={{
            fontFamily: "var(--font-body)", fontSize: 10, fontWeight: 600,
            fontVariant: "all-small-caps", letterSpacing: "0.03em",
            color: "var(--text-muted, #59636F)", marginBottom: 3
          }}>{it.label}</div>
          <div style={{
            fontSize: 13,
            fontFamily: it.mono === false ? "var(--font-body)" : "var(--font-data)",
            fontVariantNumeric: "tabular-nums",
            color: it.color || "var(--text-primary, #22272E)",
            overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap"
          }}>{it.value}</div>
        </div>
      ))}
    </div>
  );
}
