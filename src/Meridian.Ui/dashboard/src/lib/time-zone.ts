export const UTC_TIME_ZONE = "UTC";

/** UTC first, then every IANA zone the runtime knows; the current value is always offered. */
export function listTimeZoneOptions(currentTimeZoneId: string): string[] {
  let supported: string[] = [];
  try {
    supported = typeof Intl.supportedValuesOf === "function" ? Intl.supportedValuesOf("timeZone") : [];
  } catch {
    supported = [];
  }

  const options = [UTC_TIME_ZONE, ...supported.filter((zone) => zone !== UTC_TIME_ZONE)];
  const current = currentTimeZoneId.trim();
  return current && !options.includes(current) ? [current, ...options] : options;
}

/** Converts a wall-clock time in an IANA zone to the UTC instant; unknown zones fall back to UTC. */
export function zonedWallTimeToUtc(
  year: number,
  monthIndex: number,
  day: number,
  hour: number,
  minute: number,
  timeZoneId: string
): Date {
  const wallAsUtc = Date.UTC(year, monthIndex, day, hour, minute);
  const zone = timeZoneId.trim();
  if (!zone || zone === UTC_TIME_ZONE) {
    return new Date(wallAsUtc);
  }

  let formatter: Intl.DateTimeFormat;
  try {
    formatter = new Intl.DateTimeFormat("en-US", {
      timeZone: zone,
      hourCycle: "h23",
      year: "numeric",
      month: "numeric",
      day: "numeric",
      hour: "numeric",
      minute: "numeric",
      second: "numeric"
    });
  } catch {
    return new Date(wallAsUtc);
  }

  const offsetAt = (instant: number): number => {
    const parts: Record<string, number> = {};
    for (const part of formatter.formatToParts(new Date(instant))) {
      if (part.type !== "literal") {
        parts[part.type] = Number.parseInt(part.value, 10);
      }
    }
    return Date.UTC(parts.year, parts.month - 1, parts.day, parts.hour, parts.minute, parts.second) - instant;
  };

  // Two passes settle the offset when the first guess lands on the other side of a
  // daylight-saving change.
  const firstGuess = wallAsUtc - offsetAt(wallAsUtc);
  return new Date(wallAsUtc - offsetAt(firstGuess));
}
