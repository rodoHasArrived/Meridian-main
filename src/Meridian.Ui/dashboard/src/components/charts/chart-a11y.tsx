// Shared accessibility frame for Meridian's SVG charts: an overridable accessible name, an
// optional long description, and an optional visually hidden data table that gives
// assistive technology the plotted values a `role="img"` graphic cannot expose.
import { useId, type CSSProperties, type ReactNode } from "react";

/** Tabular alternative for a chart, rendered as a visually hidden `<table>`. */
export interface ChartAccessibleData {
  /** Table caption. @default `${ariaLabel} data` */
  caption?: string;
  /** Column headers. */
  columns: string[];
  /** One entry per row, cells parallel to `columns`; the first cell is the row header. */
  rows: Array<Array<string | number>>;
}

export interface ChartA11yProps {
  /** Accessible name of the chart graphic. Each chart defaults to its generic type label. */
  ariaLabel?: string;
  /** Longer description (trend, range, takeaway) announced with the chart. */
  ariaDescription?: string;
  /** Data alternative rendered as a visually hidden table next to the chart. */
  accessibleData?: ChartAccessibleData;
}

const VISUALLY_HIDDEN: CSSProperties = {
  position: "absolute",
  width: 1,
  height: 1,
  padding: 0,
  margin: -1,
  overflow: "hidden",
  clip: "rect(0, 0, 0, 0)",
  whiteSpace: "nowrap",
  border: 0
};

export interface ChartSvgProps extends ChartA11yProps {
  ariaLabel: string;
  viewBox: string;
  style: CSSProperties;
  /**
   * Interactive layer (see `ChartInteractionOverlay`), or null when the chart is static.
   * When present the svg becomes a plain `role="group"` container, the graphic moves to an
   * inner `<g role="img">`, and the overlay renders as its sibling: children of a `role="img"` element are presentational, so a focusable
   * control nested inside one is hidden from assistive technology.
   */
  overlay?: ReactNode;
  children: ReactNode;
}

export function ChartSvg({ viewBox, style, ariaLabel, ariaDescription, accessibleData, overlay = null, children }: ChartSvgProps) {
  const id = useId();
  const descriptionId = ariaDescription ? `${id}-description` : undefined;
  const image = { role: "img", "aria-label": ariaLabel, "aria-describedby": descriptionId } as const;

  return (
    <>
      {overlay ? (
        <svg viewBox={viewBox} preserveAspectRatio="none" style={style} role="group">
          <g {...image}>{children}</g>
          {overlay}
        </svg>
      ) : (
        <svg viewBox={viewBox} preserveAspectRatio="none" style={style} {...image}>
          {children}
        </svg>
      )}
      {ariaDescription ? (
        <p id={descriptionId} style={VISUALLY_HIDDEN}>
          {ariaDescription}
        </p>
      ) : null}
      {accessibleData ? (
        <table style={VISUALLY_HIDDEN}>
          <caption>{accessibleData.caption ?? `${ariaLabel} data`}</caption>
          <thead>
            <tr>
              {accessibleData.columns.map((column, c) => (
                <th key={c} scope="col">
                  {column}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {accessibleData.rows.map((row, r) => (
              <tr key={r}>
                {row.map((cell, c) =>
                  c === 0 ? (
                    <th key={c} scope="row">
                      {cell}
                    </th>
                  ) : (
                    <td key={c}>{cell}</td>
                  )
                )}
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </>
  );
}
