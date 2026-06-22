// Meridian key/value fact grid — small-caps label over mono value. Light theme.
import React from "react";

export function KeyValueGrid({ items, columns = 2 }) {
  return (
    <div style={{
      display: "grid", gridTemplateColumns: `repeat(${columns}, minmax(0,1fr))`,
      gap: "12px 24px"
    }}>
      {items.map((it, i) => (
        <div key={i} style={{ display: "flex", flexDirection: "column", gap: 3, minWidth: 0 }}>
          <div style={{
            fontFamily: "var(--font-body)", fontSize: 10, fontWeight: 600,
            fontVariant: "all-small-caps", letterSpacing: "0.03em",
            color: "var(--text-muted, #6E7781)"
          }}>{it.label}</div>
          <div style={{
            fontFamily: "var(--font-data)", fontSize: 13,
            fontVariantNumeric: "tabular-nums", color: "var(--text-primary, #22272E)"
          }}>{it.value}</div>
        </div>
      ))}
    </div>
  );
}
