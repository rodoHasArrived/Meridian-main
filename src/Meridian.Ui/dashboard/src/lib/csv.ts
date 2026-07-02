export type CsvCell = string | number | boolean | null | undefined;

export function serializeCsv(headers: string[], rows: CsvCell[][]): string {
  return [
    headers.map(escapeCsvCell).join(","),
    ...rows.map((row) => row.map(escapeCsvCell).join(",")),
  ].join("\n");
}

export function downloadCsv(filename: string, csv: string): boolean {
  if (typeof document === "undefined" || typeof URL === "undefined") {
    return false;
  }

  const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 0);
  return true;
}

export async function copyTextToClipboard(text: string): Promise<boolean> {
  if (typeof navigator === "undefined" || !navigator.clipboard?.writeText) {
    return false;
  }

  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}

function escapeCsvCell(value: CsvCell): string {
  if (value === null || value === undefined) {
    return "";
  }

  const cell = String(value);
  const escaped = cell.replace(/"/g, '""');
  return /[",\r\n]/.test(cell) ? `"${escaped}"` : escaped;
}
