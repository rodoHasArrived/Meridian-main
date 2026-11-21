// Text for charts whose plot is a stretched SVG (`preserveAspectRatio="none"`). SVG text inside
// such a plot is scaled with it and reads letter-spaced or squashed (UX Phase F), so the labels
// are drawn as an HTML layer over the plot instead: positioned by percentage of the viewBox, so
// they track the stretched geometry while the glyphs keep their real shape.

export interface StretchedChartLabel {
  value: string;
  x: number;
  y: number;
}

export function parseViewBoxSize(viewBox: string): { width: number; height: number } | null {
  const parts = viewBox.trim().split(/[\s,]+/).map(Number);
  if (parts.length !== 4 || parts.some((part) => !Number.isFinite(part))) return null;
  const [, , width, height] = parts as [number, number, number, number];
  return width > 0 && height > 0 ? { width, height } : null;
}

/**
 * Labels anchored at their right edge (the SVG `textAnchor="end"` they replace) with the given point
 * on the text baseline. Decorative: the values are already in the chart's accessible name or table.
 */
export function StretchedChartLabels({ viewBox, labels, fontSize = 12 }: { viewBox: string; labels: readonly StretchedChartLabel[]; fontSize?: number }) {
  const size = parseViewBoxSize(viewBox);
  if (!size) return null;
  return (
    <div aria-hidden="true" className="pointer-events-none absolute inset-0" data-chart-label-layer="">
      {labels.map((label, index) => (
        <span
          key={`${index}:${label.value}`}
          className="absolute whitespace-nowrap leading-none text-current opacity-55"
          style={{
            left: `${(label.x / size.width) * 100}%`,
            top: `${(label.y / size.height) * 100}%`,
            transform: "translate(-100%, -85%)",
            fontFamily: "var(--font-body, system-ui, sans-serif)",
            fontSize,
            fontVariantNumeric: "tabular-nums"
          }}
        >
          {label.value}
        </span>
      ))}
    </div>
  );
}
