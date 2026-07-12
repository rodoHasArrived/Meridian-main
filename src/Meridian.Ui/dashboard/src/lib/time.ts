// Meridian shared time formatting. Relative-age text for freshness indicators and
// "last updated" labels; promoted from the watchlist view model so chips, screens,
// and view models share one age vocabulary.
export function formatRelativeAge(iso: string | null, now = Date.now()): string {
  if (!iso) {
    return "Never";
  }

  const timestamp = new Date(iso).getTime();
  if (Number.isNaN(timestamp)) {
    return "Never";
  }

  const diff = now - timestamp;
  if (diff < 0) {
    return new Date(iso).toLocaleString();
  }

  const seconds = Math.round(diff / 1000);
  if (seconds < 60) {
    return `${seconds}s ago`;
  }

  const minutes = Math.round(seconds / 60);
  if (minutes < 60) {
    return `${minutes}m ago`;
  }

  const hours = Math.round(minutes / 60);
  if (hours < 24) {
    return `${hours}h ago`;
  }

  const days = Math.round(hours / 24);
  return `${days}d ago`;
}
