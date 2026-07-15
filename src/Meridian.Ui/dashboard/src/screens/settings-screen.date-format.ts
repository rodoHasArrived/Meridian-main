export function formatSettingsUtcMinute(
  value: string | Date | null | undefined,
  unavailableLabel = "Unavailable"
): string {
  if (!value) {
    return unavailableLabel;
  }

  const date = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) {
    return unavailableLabel;
  }

  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

export function formatSettingsDateOnly(value: string | null | undefined, unavailableLabel = "No date"): string {
  if (!value) {
    return unavailableLabel;
  }

  const [year, month, day] = value.split("-").map((part) => Number(part));
  if (!year || !month || !day) {
    return unavailableLabel;
  }

  return `${UTC_MONTH_LABELS[month - 1] ?? "Month"} ${day}, ${year}`;
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function padUtc(value: number): string {
  return String(value).padStart(2, "0");
}
