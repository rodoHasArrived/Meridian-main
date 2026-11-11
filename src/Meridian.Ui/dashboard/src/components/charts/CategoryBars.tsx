export interface CategoryBar {
  id: string;
  label: string;
  value: number;
  valueLabel: string;
}

/** Non-negative categorical values; the caller owns units, provenance and the table alternative. */
export function CategoryBars({ rows, label }: { rows: CategoryBar[]; label: string }) {
  const valid = rows.filter((row) => Number.isFinite(row.value) && row.value >= 0);
  const max = Math.max(1, ...valid.map((row) => row.value));
  const height = Math.max(80, valid.length * 36 + 16);
  return (
    <svg role="img" aria-label={label} width="100%" height="100%" viewBox={`0 0 720 ${height}`} preserveAspectRatio="xMinYMid meet">
      <title>{label}</title>
      {valid.map((row, index) => {
        const y = index * 36 + 12;
        return <g key={row.id}>
          <text x="0" y={y + 14} fill="var(--chart-axis, #5E666F)" fontSize="12"><title>{row.label}</title>{row.label.length > 22 ? `${row.label.slice(0, 21)}…` : row.label}</text>
          <rect x="180" y={y} width="420" height="20" fill="var(--chart-grid, #E4E3DE)" />
          <rect x="180" y={y} width={420 * row.value / max} height="20" fill="var(--chart-primary, #A85436)" />
          <text x="610" y={y + 14} fill="var(--chart-axis, #5E666F)" fontSize="12">{row.valueLabel}</text>
        </g>;
      })}
    </svg>
  );
}
