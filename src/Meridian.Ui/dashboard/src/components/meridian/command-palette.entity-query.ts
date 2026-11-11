export type CommandPaletteRecordQuery = {
  kind: "general" | "report" | "statement" | "unsupported";
  value: string;
  guidance: string | null;
};

/** Exact retained identities only; an import ID is never reinterpreted as a run ID. */
export function parseCommandPaletteRecordQuery(query: string): CommandPaletteRecordQuery {
  const match = /^(report|report-run|statement|statement-run|journal|account|import):\s*(.*)$/i.exec(query.trim());
  if (!match) return { kind: "general", value: query.trim(), guidance: null };
  const prefix = match[1].toLowerCase();
  const value = match[2].trim();
  if (prefix === "journal" || prefix === "account" || prefix === "import") {
    return {
      kind: "unsupported", value,
      guidance: prefix === "import"
        ? "Import ID lookup is unavailable. Use statement: followed by the exact statement reconciliation run ID, or open Statement import."
        : `${prefix === "journal" ? "Journal" : "Financial account"} lookup requires a scoped search service. Open ${prefix === "journal" ? "Ledger Explorer" : "Accounts"} to select the book and record.`
    };
  }
  const kind = prefix.startsWith("report") ? "report" : "statement";
  return {
    kind, value,
    guidance: !value ? `Enter the exact retained ${kind === "report" ? "report" : "statement reconciliation"} run ID after ${kind}:` : null
  };
}
