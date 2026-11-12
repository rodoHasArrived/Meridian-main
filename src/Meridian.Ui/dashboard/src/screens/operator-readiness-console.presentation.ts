import { formatUtcTimestamp } from "@/lib/format";

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

  return formatUtcTimestamp(date);
}

/** Ends a message with one period, so a message that already has one does not get a second. */
export function sentence(message: string): string {
  const trimmed = message.trim();
  return /[.!?]$/.test(trimmed) ? trimmed : `${trimmed}.`;
}

export function toErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof Error && err.message.trim()) {
    return err.message;
  }

  return fallback;
}

