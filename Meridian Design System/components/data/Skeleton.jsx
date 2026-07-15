// Meridian Skeleton — loading placeholder for tables/content. Subtle shimmer (reduced-motion
// safe). Compose primitives (text lines, blocks) or use SkeletonTable for dense grids.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-skel{display:block;background:var(--bg-active,#E1EAF2);border-radius:var(--radius-chip,3px);
  position:relative;overflow:hidden;}
.mds-skel::after{content:"";position:absolute;inset:0;transform:translateX(-100%);
  background:linear-gradient(90deg,transparent,var(--bg-hover,#F1F4F7),transparent);
  animation:mds-skel-sweep 1.3s ease-in-out infinite;}
.mds-skel--text{height:12px;margin:3px 0;}
.mds-skel--circle{border-radius:50%;}
.mds-skel-lines{display:flex;flex-direction:column;gap:8px;}
.mds-skel-table{width:100%;border-collapse:collapse;font-family:var(--font-body);}
.mds-skel-table td{padding:9px 12px;border-bottom:1px solid var(--border-divider,#E5E9EE);}
@keyframes mds-skel-sweep{100%{transform:translateX(100%);}}
@media (prefers-reduced-motion:reduce){.mds-skel::after{animation:none;}}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "skeleton");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Skeleton({
  variant = "block", width, height, lines = 3, className = "", style = {}, ...rest
}) {
  inject();
  if (variant === "text" && lines > 1) {
    return (
      <div className={`mds-skel-lines${className ? " " + className : ""}`} {...rest}>
        {Array.from({ length: lines }).map((_, i) => (
          <span key={i} className="mds-skel mds-skel--text"
            style={{ width: i === lines - 1 ? "60%" : (width || "100%") }} />
        ))}
      </div>
    );
  }
  const v = variant === "text" ? " mds-skel--text" : variant === "circle" ? " mds-skel--circle" : "";
  const dims = { ...style };
  if (width != null) dims.width = typeof width === "number" ? width + "px" : width;
  if (height != null) dims.height = typeof height === "number" ? height + "px" : height;
  if (variant === "circle" && width != null && height == null) dims.height = dims.width;
  return <span className={`mds-skel${v}${className ? " " + className : ""}`} style={dims} {...rest} />;
}

export function SkeletonTable({ rows = 5, columns = 4, className = "", ...rest }) {
  inject();
  const widths = ["70%", "45%", "55%", "40%", "60%", "50%"];
  return (
    <table className={`mds-skel-table${className ? " " + className : ""}`} {...rest}>
      <tbody>
        {Array.from({ length: rows }).map((_, r) => (
          <tr key={r}>
            {Array.from({ length: columns }).map((_, c) => (
              <td key={c}><span className="mds-skel mds-skel--text"
                style={{ width: widths[(r + c) % widths.length] }} /></td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
