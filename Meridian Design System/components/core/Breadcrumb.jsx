// Meridian Breadcrumb — section navigator for drill-down workstation flows.
import React from "react";

export function Breadcrumb({ items = [], separator = "·" }) {
  // items: [{ label, onClick? }] — last item is always the current (non-clickable) page
  return (
    <nav aria-label="breadcrumb" style={{
      display:"flex", alignItems:"center", gap:6,
      fontFamily:"var(--font-data)", fontSize:12,
      color:"var(--text-muted,#6E7781)",
      flexWrap:"wrap",
    }}>
      {items.map((item, i) => {
        const isLast = i === items.length - 1;
        return (
          <React.Fragment key={i}>
            {i > 0 && (
              <span style={{ color:"var(--text-disabled,#9AA4AF)", fontSize:10 }}>{separator}</span>
            )}
            {isLast ? (
              <span style={{
                color:"var(--text-primary,#22272E)", fontWeight:600,
                fontFamily:"var(--font-body)",
              }} aria-current="page">
                {item.label}
              </span>
            ) : (
              <button
                type="button"
                onClick={item.onClick}
                style={{
                  appearance:"none", border:"none", background:"transparent", padding:0,
                  color:"var(--accent,#2F6F8F)", fontFamily:"var(--font-data)", fontSize:12,
                  cursor:item.onClick ? "pointer" : "default",
                  textDecoration:"none",
                  transition:"opacity .1s ease",
                }}
                onMouseEnter={e => { if (item.onClick) e.currentTarget.style.opacity = "0.7"; }}
                onMouseLeave={e => { e.currentTarget.style.opacity = "1"; }}
              >
                {item.label}
              </button>
            )}
          </React.Fragment>
        );
      })}
    </nav>
  );
}
