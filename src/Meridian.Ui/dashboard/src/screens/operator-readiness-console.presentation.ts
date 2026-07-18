export function levelFromReadiness(status: string): "ready" | "review" | "blocked" {
  if (status === "Ready") {
    return "ready";
  }

  if (status === "Blocked") {
    return "blocked";
  }

  return "review";
}

export function levelFromTone(tone: string): "ready" | "review" | "blocked" | "neutral" {
  if (tone === "Success") {
    return "ready";
  }

  if (tone === "Critical") {
    return "blocked";
  }

  if (tone === "Warning") {
    return "review";
  }

  return "neutral";
}

export function formatReadinessUtcMinute(value: string | null | undefined, fallback: string): string {
  if (!value) {
    return fallback;
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return fallback;
  }

  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

export function toErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message.trim()) {
    return err.message;
  }

  return fallback;
}

function padUtc(value: number): string {
  return String(value).padStart(2, "0");
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
