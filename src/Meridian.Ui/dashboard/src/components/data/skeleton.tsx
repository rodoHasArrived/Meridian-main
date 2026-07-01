// Meridian Skeleton (Concrete) — a loading placeholder with a subtle shimmer that is
// reduced-motion safe. Compose `text` lines / `block` cards / `circle` avatars, or use
// SkeletonTable for a dense placeholder grid while a table is still fetching.
import type { CSSProperties, HTMLAttributes } from "react";
import { injectStyle } from "../operations/inject-style";

const CSS = `
.mds-skel{display:block;background:var(--bg-active,#E1EAF2);border-radius:var(--radius-chip,2px);
  position:relative;overflow:hidden;}
.mds-skel::after{content:"";position:absolute;inset:0;transform:translateX(-100%);
  background:linear-gradient(90deg,transparent,var(--bg-hover,#F1F4F7),transparent);
  animation:mds-skel-sweep 1.3s ease-in-out infinite;}
.mds-skel--text{height:12px;margin:3px 0;}
.mds-skel--circle{border-radius:50%;}
.mds-skel-lines{display:flex;flex-direction:column;gap:8px;}
.mds-skel-table{width:100%;border-collapse:collapse;font-family:var(--font-body);}
.mds-skel-table td{padding:9px 12px;border-bottom:1px solid var(--border-divider,#DDE3EA);}
@keyframes mds-skel-sweep{100%{transform:translateX(100%);}}
@media (prefers-reduced-motion:reduce){.mds-skel::after{animation:none;}}
`;

export interface SkeletonProps extends HTMLAttributes<HTMLSpanElement> {
  /** Shape. @default "block" */
  variant?: "block" | "text" | "circle";
  /** CSS width (number → px). */
  width?: number | string;
  /** CSS height (number → px). For `circle`, defaults to `width`. */
  height?: number | string;
  /** Line count when `variant="text"` (last line is shortened). @default 3 */
  lines?: number;
}

function toDim(value?: number | string): string | undefined {
  if (value == null) return undefined;
  return typeof value === "number" ? `${value}px` : value;
}

/**
 * Skeleton — a shimmer loading placeholder.
 *
 * @example
 * <Skeleton variant="text" lines={4} />
 * <Skeleton variant="circle" width={40} />
 */
export function Skeleton({
  variant = "block",
  width,
  height,
  lines = 3,
  className,
  style,
  ...rest
}: SkeletonProps) {
  injectStyle("skeleton", CSS);

  if (variant === "text" && lines > 1) {
    return (
      <div className={`mds-skel-lines${className ? " " + className : ""}`} {...rest}>
        {Array.from({ length: lines }).map((_, i) => (
          <span
            key={i}
            className="mds-skel mds-skel--text"
            style={{ width: i === lines - 1 ? "60%" : toDim(width) ?? "100%" }}
          />
        ))}
      </div>
    );
  }

  const modifier =
    variant === "text" ? " mds-skel--text" : variant === "circle" ? " mds-skel--circle" : "";
  const dims: CSSProperties = { ...style };
  if (width != null) dims.width = toDim(width);
  if (height != null) dims.height = toDim(height);
  if (variant === "circle" && width != null && height == null) dims.height = toDim(width);

  return (
    <span className={`mds-skel${modifier}${className ? " " + className : ""}`} style={dims} {...rest} />
  );
}

export interface SkeletonTableProps extends HTMLAttributes<HTMLTableElement> {
  /** Placeholder row count. @default 5 */
  rows?: number;
  /** Placeholder column count. @default 4 */
  columns?: number;
}

/**
 * SkeletonTable — a dense placeholder grid for tables that are still fetching.
 *
 * @example
 * <SkeletonTable rows={8} columns={5} />
 */
export function SkeletonTable({ rows = 5, columns = 4, className, ...rest }: SkeletonTableProps) {
  injectStyle("skeleton", CSS);
  const widths = ["70%", "45%", "55%", "40%", "60%", "50%"];
  return (
    <table className={`mds-skel-table${className ? " " + className : ""}`} {...rest}>
      <tbody>
        {Array.from({ length: rows }).map((_, r) => (
          <tr key={r}>
            {Array.from({ length: columns }).map((_, c) => (
              <td key={c}>
                <span className="mds-skel mds-skel--text" style={{ width: widths[(r + c) % widths.length] }} />
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
