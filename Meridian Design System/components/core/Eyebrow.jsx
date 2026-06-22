// Meridian eyebrow — mirrors MetricLabelStyle / DataLabelStyle (10px small-caps muted).
import React from "react";

export function Eyebrow({ children, className = "", style = {}, ...rest }) {
  return (
    <div className={className} style={{
      fontFamily: "var(--font-body)", fontSize: 10, fontWeight: 600,
      fontVariant: "all-small-caps", letterSpacing: "0.03em",
      color: "var(--text-muted, #6E7781)", ...style
    }} {...rest}>{children}</div>
  );
}
