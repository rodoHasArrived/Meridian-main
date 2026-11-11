export interface StatusObservation { timestamp: string; succeeded: boolean }

/** Dated discrete events; connecting line denotes elapsed time, never continuous availability. */
export function StatusTimeline({ observations, label }: { observations: StatusObservation[]; label: string }) {
  const first = Date.parse(observations[0]?.timestamp ?? "");
  const last = Date.parse(observations.at(-1)?.timestamp ?? "");
  const span = Math.max(1, last - first);
  return <svg role="img" aria-label={label} viewBox="0 0 720 105" width="100%" height="100%">
    <title>{label}</title>
    <line x1="24" x2="696" y1="35" y2="35" stroke="var(--chart-grid, #E4E3DE)" />
    {observations.map((observation, index) => <circle key={`${observation.timestamp}:${index}`}
      cx={observations.length === 1 ? 360 : 24 + (Date.parse(observation.timestamp) - first) / span * 672} cy="35" r="5"
      fill={observation.succeeded ? "var(--chart-equity, #3A7A56)" : "var(--chart-drawdown, #A8443C)"}>
      <title>{observation.timestamp} · {observation.succeeded ? "Verification passed" : "Verification failed"}</title>
    </circle>)}
    <text x="24" y="80" fontSize="12" fill="var(--chart-axis, #5E666F)">{observations[0]?.timestamp.slice(0, 10)}</text>
    <text x="696" y="80" textAnchor="end" fontSize="12" fill="var(--chart-axis, #5E666F)">{observations.at(-1)?.timestamp.slice(0, 10)}</text>
  </svg>;
}
