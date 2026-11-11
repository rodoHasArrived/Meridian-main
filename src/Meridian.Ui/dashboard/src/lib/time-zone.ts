export const UTC_TIME_ZONE = "UTC";

/** UTC first, then every IANA zone the runtime knows; the current value is always offered. */
export function listTimeZoneOptions(currentTimeZoneId: string): string[] {
  let supported: string[];
  try {
    supported = typeof Intl.supportedValuesOf === "function" ? Intl.supportedValuesOf("timeZone") : [];
  } catch {
    supported = [];
  }

  const options = [UTC_TIME_ZONE, ...supported.filter((zone) => zone !== UTC_TIME_ZONE)];
  const current = currentTimeZoneId.trim();
  return current && !options.includes(current) ? [current, ...options] : options;
}

/**
 * Converts a wall-clock time in an IANA zone to the UTC instant; unknown zones fall back to UTC.
 * A wall time skipped by a spring-forward change resolves to the first valid time after the gap;
 * a wall time repeated by a fall-back change resolves to its second occurrence (standard time).
 */
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

  // A zone has at most two offsets around any one day (before and after a daylight-saving
  // change), so the requested wall time maps to at most two instants. Keep the ones that read
  // back as that wall time.
  const DAY = 24 * 60 * 60 * 1000;
  const resolve = (wall: number): number[] => {
    const candidates = new Set([wall - offsetAt(wall - DAY), wall - offsetAt(wall + DAY)]);
    return [...candidates].filter((instant) => instant + offsetAt(instant) === wall).sort((x, y) => x - y);
  };

  // These rules mirror the server (ReportingSchedulePeriodPlanner.DueAtUtc), so a due time saved
  // from the form and one advanced by the worker agree:
  // - a wall time repeated by a fall-back change resolves to its second, standard-time occurrence;
  // - a wall time skipped by a spring-forward change moves forward in 30-minute steps to the
  //   first time that exists (02:30 becomes 03:00).
  for (let wall = wallAsUtc, step = 0; step <= 8; wall += 30 * 60 * 1000, step += 1) {
    const instants = resolve(wall);
    if (instants.length > 0) {
      return new Date(instants[instants.length - 1]);
    }
  }

  return new Date(wallAsUtc);
}
