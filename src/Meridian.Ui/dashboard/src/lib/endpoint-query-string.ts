/** Shared query encoding for composed API endpoint builders. */
export function queryString(params: Record<string, string | number | boolean | readonly string[] | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (Array.isArray(value)) {
      const normalizedValues = value.map((entry) => entry.trim()).filter(Boolean);
      if (normalizedValues.length > 0) {
        search.set(key, normalizedValues.join(","));
      }
      continue;
    }

    if (typeof value === "string") {
      const trimmed = value.trim();
      if (trimmed) {
        search.set(key, trimmed);
      }
      continue;
    }

    if (typeof value === "number") {
      if (Number.isFinite(value)) {
        search.set(key, String(value));
      }
      continue;
    }

    if (typeof value === "boolean") {
      search.set(key, String(value));
    }
  }

  const value = search.toString();
  return value ? `?${value}` : "";
}
