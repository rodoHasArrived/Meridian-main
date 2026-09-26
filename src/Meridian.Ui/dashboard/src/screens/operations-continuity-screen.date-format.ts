/** Operations Continuity's retained UTC timestamp and due-date display formats. */
export function formatDate(value: string | null | undefined): string {
  if (!value) {
    return "Not recorded";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  const month = date.toLocaleString("en-US", { month: "short", timeZone: "UTC" });
  const day = date.getUTCDate().toString().padStart(2, "0");
  const hour = date.getUTCHours().toString().padStart(2, "0");
  const minute = date.getUTCMinutes().toString().padStart(2, "0");
  return `${month} ${day}, ${hour}:${minute} UTC`;
}

export function formatDateOnly(value: string | null | undefined): string {
  if (!value) {
    return "No due date";
  }

  const date = new Date(/^\d{4}-\d{2}-\d{2}$/.test(value) ? `${value}T00:00:00Z` : value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  const month = date.toLocaleString("en-US", { month: "short", timeZone: "UTC" });
  const day = date.getUTCDate().toString().padStart(2, "0");
  const year = date.getUTCFullYear();
  return `${month} ${day}, ${year}`;
}
