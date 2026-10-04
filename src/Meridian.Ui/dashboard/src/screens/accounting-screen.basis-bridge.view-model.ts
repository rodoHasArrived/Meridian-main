import type { AccountingBasisKind } from "@/types";
import { formatCurrency } from "./accounting-screen.formatting";
import { readSourceEventIds } from "./accounting-screen.view-model";
import type {
  AccountingBasisBridgeRowViewModel,
  AccountingBasisBridgeViewState,
  BasisAwareLedgerTrialBalanceLine
} from "./accounting-screen.view-model";

/**
 * The basis bridge: the per-account difference between Primary and one other projection.
 *
 * Extracted from accounting-screen.view-model.ts, which sits on the repository's no-new-god-file
 * cap. The bridge is self-contained — it reads normalized rows and returns a view state — so it is
 * the cleanest seam to lift out, and the ratchet asks for decomposition rather than a raised cap.
 */
export function accountingBasisDisplayName(basis: AccountingBasisKind): string {
  return basis === "Gaap" ? "GAAP" : basis;
}

export function buildBasisBridgeViewState(
  rows: BasisAwareLedgerTrialBalanceLine[],
  selectedBasis: AccountingBasisKind,
  runLabel: string
): AccountingBasisBridgeViewState {
  const comparisonBasis = selectedBasis === "Primary"
    ? rows.find((row) => row.accountingBasis !== "Primary")?.accountingBasis ?? "Gaap"
    : selectedBasis;
  const primaryRows = rows.filter((row) => row.accountingBasis === "Primary");
  const comparisonRows = rows.filter((row) => row.accountingBasis === comparisonBasis);
  const tableLabel = `${accountingBasisDisplayName(comparisonBasis)} to Primary basis bridge for ${runLabel}`;

  if (comparisonBasis === "Primary" || primaryRows.length === 0 || comparisonRows.length === 0) {
    return {
      title: "Basis bridge",
      description: `${accountingBasisDisplayName(comparisonBasis)} to Primary comparison grouped by source/rule/account where lineage is available.`,
      tableLabel,
      fromBasis: "Primary",
      toBasis: comparisonBasis,
      rows: [],
      hasRows: false,
      emptyText: "No non-primary basis rows are available for this run yet. The bridge will populate after GAAP, Cash, Tax, or Statutory projection posts journal lines."
    };
  }

  const primaryByKey = new Map(primaryRows.map((row) => [basisBridgeKey(row), row]));
  const comparisonByKey = new Map(comparisonRows.map((row) => [basisBridgeKey(row), row]));
  const keys = [...new Set([...primaryByKey.keys(), ...comparisonByKey.keys()])].sort((left, right) => left.localeCompare(right));
  const bridgeRows = keys.map((key) => {
    const primary = primaryByKey.get(key) ?? null;
    const comparison = comparisonByKey.get(key) ?? null;
    const source = comparison ?? primary;
    const primaryBalance = primary?.balance ?? 0;
    const comparisonBalance = comparison?.balance ?? 0;
    const variance = comparisonBalance - primaryBalance;
    const sourceLabel = buildBasisBridgeSourceLabel(source);
    const accountLabel = source?.accountName.trim() || "Unnamed account";
    const accountTypeLabel = source?.accountType.trim() || "Unclassified";
    const varianceLabel = formatCurrency(variance);

    return {
      rowId: `${comparisonBasis}-${key}`,
      accountLabel,
      accountTypeLabel,
      primaryBalanceLabel: formatCurrency(primaryBalance),
      comparisonBalanceLabel: formatCurrency(comparisonBalance),
      varianceLabel,
      varianceTone: variance < 0 ? "danger" : variance > 0 ? "success" : "default",
      sourceLabel,
      ariaLabel: `${accountLabel} ${accountTypeLabel}. Primary ${formatCurrency(primaryBalance)}. ${accountingBasisDisplayName(comparisonBasis)} ${formatCurrency(comparisonBalance)}. Variance ${varianceLabel}.`
    } satisfies AccountingBasisBridgeRowViewModel;
  });

  return {
    title: "Basis bridge",
    description: `${accountingBasisDisplayName(comparisonBasis)} compared with Primary for ${runLabel}, grouped by source/rule/account where lineage is available.`,
    tableLabel,
    fromBasis: "Primary",
    toBasis: comparisonBasis,
    rows: bridgeRows,
    hasRows: bridgeRows.length > 0,
    emptyText: "No bridge rows matched the selected basis pair."
  };
}

function basisBridgeKey(line: BasisAwareLedgerTrialBalanceLine): string {
  const sourceEventId = readSourceEventIds(line).join(",");
  const ruleId = "ruleId" in line ? String(line.ruleId ?? "") : "";
  return [
    sourceEventId,
    ruleId,
    line.accountName,
    line.accountType,
    line.symbol ?? "",
    line.financialAccountId ?? ""
  ].join("|");
}

function buildBasisBridgeSourceLabel(line: BasisAwareLedgerTrialBalanceLine | null): string {
  if (!line) {
    return "Missing source group";
  }

  const sourceEventIds = readSourceEventIds(line);
  const ruleId = "ruleId" in line ? String(line.ruleId ?? "").trim() : "";
  if (sourceEventIds.length > 0 || ruleId) {
    return [
      sourceEventIds.length > 0 ? `Source ${sourceEventIds.join(", ")}` : null,
      ruleId ? `Rule ${ruleId}` : null
    ].filter(Boolean).join(" / ");
  }

  return line.symbol?.trim() || line.financialAccountId?.trim() || "Account group";
}