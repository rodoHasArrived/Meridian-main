// Meridian VirtualizedList — windowed rendering for large lists (1k–100k rows).
// Renders only the visible slice + overscan. Fixed row height for O(1) scroll math.
// Functional only: no radius, no shadows.
import React from "react";

export function VirtualizedList({
  items = [],
  rowHeight = 34,
  height = 360,
  overscan = 6,
  renderRow,
  className = "",
  style = {},
}) {
  const [scrollTop, setScrollTop] = React.useState(0);
  const total = items.length;
  const viewportRows = Math.ceil(height / rowHeight);

  const start = Math.max(0, Math.floor(scrollTop / rowHeight) - overscan);
  const end = Math.min(total, start + viewportRows + overscan * 2);
  const slice = items.slice(start, end);

  return (
    <div
      className={className}
      style={{
        height,
        overflowY: "auto",
        position: "relative",
        border: "1px solid var(--border, #D7DCE2)",
        background: "var(--bg-light, #FAFBFC)",
        ...style,
      }}
      onScroll={(e) => setScrollTop(e.currentTarget.scrollTop)}
    >
      {/* Spacer sets the true scroll height */}
      <div style={{ height: total * rowHeight, position: "relative" }}>
        {/* Offset the rendered window to its real position */}
        <div style={{ position: "absolute", top: start * rowHeight, left: 0, right: 0 }}>
          {slice.map((item, i) => {
            const index = start + i;
            return (
              <div
                key={index}
                style={{
                  height: rowHeight,
                  display: "flex",
                  alignItems: "center",
                  boxSizing: "border-box",
                  borderBottom: "1px solid var(--border-divider, #E5E9EE)",
                  padding: "0 12px",
                  fontFamily: "var(--font-data)",
                  fontSize: "13px",
                  color: "var(--text-primary, #22272E)",
                }}
              >
                {renderRow ? renderRow(item, index) : String(item)}
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

VirtualizedList.displayName = "VirtualizedList";
