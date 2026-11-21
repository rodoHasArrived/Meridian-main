import type { ReportWriterDatasetSource } from "@/types";

export function parseReportWriterCustomDatasetRows(text: string): { rows: Record<string, string>[]; error: null } | { rows: []; error: string } {
  const trimmed = text.trim();
  if (!trimmed) {
    return { rows: [], error: "Custom dataset requires JSON rows or CSV rows with a header." };
  }

  const parsed = trimmed.startsWith("[") ? parseReportWriterJsonDatasetRows(trimmed) : parseReportWriterCsvDatasetRows(trimmed);
  if (parsed.error) {
    return parsed;
  }

  if (parsed.rows.length === 0) {
    return { rows: [], error: "Custom dataset requires at least one row." };
  }

  return { rows: parsed.rows.slice(0, 100), error: null };
}

function parseReportWriterJsonDatasetRows(text: string): { rows: Record<string, string>[]; error: null } | { rows: []; error: string } {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return { rows: [], error: "Custom JSON dataset must be an array of row objects." };
  }

  if (!Array.isArray(parsed)) {
    return { rows: [], error: "Custom JSON dataset must be an array of row objects." };
  }

  const rows: Record<string, string>[] = [];
  for (const item of parsed) {
    if (!item || typeof item !== "object" || Array.isArray(item)) {
      return { rows: [], error: "Each custom JSON dataset row must be an object." };
    }

    const row: Record<string, string> = {};
    for (const [key, value] of Object.entries(item)) {
      if (!key.trim()) {
        continue;
      }

      row[key.trim()] = value == null ? "" : String(value);
    }

    if (Object.keys(row).length > 0) {
      rows.push(row);
    }
  }

  return { rows, error: null };
}

function parseReportWriterCsvDatasetRows(text: string): { rows: Record<string, string>[]; error: null } | { rows: []; error: string } {
  const lines = text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
  if (lines.length < 2) {
    return { rows: [], error: "Custom CSV dataset requires a header row and at least one data row." };
  }

  const headers = parseReportWriterCsvLine(lines[0]).map((header) => header.trim());
  if (headers.length === 0 || headers.every((header) => !header)) {
    return { rows: [], error: "Custom CSV dataset requires at least one header." };
  }

  const rows = lines.slice(1).map((line) => {
    const values = parseReportWriterCsvLine(line);
    const row: Record<string, string> = {};
    headers.forEach((header, index) => {
      if (header) {
        row[header] = values[index] ?? "";
      }
    });
    return row;
  }).filter((row) => Object.keys(row).length > 0);

  return { rows, error: null };
}

function parseReportWriterCsvLine(line: string): string[] {
  const values: string[] = [];
  let current = "";
  let inQuotes = false;

  for (let index = 0; index < line.length; index += 1) {
    const character = line[index];
    if (character === '"') {
      if (inQuotes && line[index + 1] === '"') {
        current += '"';
        index += 1;
      } else {
        inQuotes = !inQuotes;
      }
    } else if (character === "," && !inQuotes) {
      values.push(current);
      current = "";
    } else {
      current += character;
    }
  }

  values.push(current);
  return values;
}

export function buildReportWriterDatasetFieldCatalog(
  datasetSource: ReportWriterDatasetSource
): ReportWriterDatasetSource["fields"] {
  return [...datasetSource.fields]
    .sort((left, right) => {
      const roleDelta = reportWriterDatasetFieldRoleRank(left.role) - reportWriterDatasetFieldRoleRank(right.role);
      return roleDelta !== 0 ? roleDelta : (left.label || left.name).localeCompare(right.label || right.name);
    })
    .slice(0, 8);
}

function reportWriterDatasetFieldRoleRank(role: string | null | undefined): number {
  const normalized = role?.toLowerCase();
  if (normalized === "dimension") {
    return 0;
  }

  if (normalized === "metric") {
    return 1;
  }

  if (normalized === "generated" || normalized === "formula") {
    return 2;
  }

  return 3;
}

