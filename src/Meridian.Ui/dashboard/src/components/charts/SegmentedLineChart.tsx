export interface SegmentedObservation { timestamp: string; value: number; segment: number }

/** Connect only observations in the same retained coverage segment. No values fill source gaps. */
export function SegmentedLineChart({ points, maximum, label }: { points: SegmentedObservation[]; maximum: number; label: string }) {
  const timestamps = points.map((point) => Date.parse(point.timestamp));
  const first = Math.min(...timestamps);
  const span = Math.max(1, Math.max(...timestamps) - first);
  const x = (timestamp: number) => 45 + (timestamp - first) / span * 615;
  const y = (value: number) => 160 - value / Math.max(1, maximum) * 130;
  const segments = new Map<number, SegmentedObservation[]>();
  for (const point of points) {
    const segment = segments.get(point.segment) ?? [];
    segment.push(point); segments.set(point.segment, segment);
  }
  return <svg role="img" aria-label={label} width="100%" height="100%" viewBox="0 0 720 210">
    <title>{label}</title>
    <path d="M45,25V160H665" fill="none" stroke="var(--chart-grid, #E4E3DE)" />
    <text x="30" y="165" textAnchor="end" fontSize="12" fill="var(--chart-axis, #5E666F)">0</text>
    <text x="30" y="35" textAnchor="end" fontSize="12" fill="var(--chart-axis, #5E666F)">{maximum}</text>
    {Array.from(segments, ([segment, observations]) => <path key={segment} data-coverage-segment={segment}
      d={observations.map((point, index) => `${index === 0 ? "M" : "L"}${x(Date.parse(point.timestamp))},${y(point.value)}`).join(" ")}
      fill="none" stroke="var(--chart-primary, #A85436)" strokeWidth="2" />)}
    {points.map((point, index) => <circle key={`${point.timestamp}:${index}`} cx={x(Date.parse(point.timestamp))} cy={y(point.value)} r="4" fill="var(--chart-primary, #A85436)"><title>{point.timestamp}: {point.value} remaining</title></circle>)}
    <text x="45" y="195" fontSize="12" fill="var(--chart-axis, #5E666F)">{new Date(first).toISOString().slice(0, 10)}</text>
    <text x="665" y="195" textAnchor="end" fontSize="12" fill="var(--chart-axis, #5E666F)">{new Date(first + span).toISOString().slice(0, 10)}</text>
  </svg>;
}
