import { redactReportingCredentialText } from "@/lib/reporting-link-safety";
import type { GovernedReportingRun } from "@/types/reporting-governance";

export function projectParameterEntries(run: GovernedReportingRun | null): Array<[string, string]> {
  if (!run) return [];
  return flattenParameterObject(run.normalizedParameters as unknown as Record<string, unknown>);
}

function flattenParameterObject(
  record: Record<string, unknown>,
  prefix = ""
): Array<[string, string]> {
  const entries: Array<[string, string]> = [];
  for (const [key, value] of Object.entries(record)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value && typeof value === "object" && !Array.isArray(value)) {
      entries.push(...flattenParameterObject(value as Record<string, unknown>, path));
      continue;
    }
    const presented = presentValue(value);
    if (presented !== null) {
      entries.push([path, presented]);
    }
  }
  return entries;
}

function presentValue(value: unknown): string | null {
  if (typeof value === "string") {
    const retained = value.trim();
    return retained ? redactReportingCredentialText(retained) : null;
  }
  if (typeof value === "number" && Number.isFinite(value)) return String(value);
  if (typeof value === "boolean") return value ? "Yes" : "No";
  if (value === null) return "None";
  if (Array.isArray(value)) {
    const values = value.map(presentValue).filter((item): item is string => item !== null);
    return values.length > 0 ? values.join(", ") : "None";
  }
  return null;
}
