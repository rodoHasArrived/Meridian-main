import { formatCount, formatCurrencyAmount } from "@/lib/format";
import type { AccountingBasisKind } from "@/types";
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
  runLabel: string,
  /** The book's currency. Without one, amounts render bare — never with an assumed "$". */
  currency: string | null = null
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
      summary: null,
      emptyText: "No non-primary basis rows are available for this run yet. The bridge will populate after GAAP, Cash, Tax, or Statutory journal lines are posted."
    };
  }

  const primaryByKey = new Map(primaryRows.map((row) => [basisBridgeKey(row), row]));
  const comparisonByKey = new Map(comparisonRows.map((row) => [basisBridgeKey(row), row]));
  const keys = [...new Set([...primaryByKey.keys(), ...comparisonByKey.keys()])].sort((left, right) => left.localeCompare(right));
  const fromName = accountingBasisDisplayName("Primary");
  const toName = accountingBasisDisplayName(comparisonBasis);
  const money = (value: number) => formatCurrencyAmount(value, currency, { context: "accounting", parens: false });
  const bridgeRows = keys.map((key) => {
    const primary = primaryByKey.get(key) ?? null;
    const comparison = comparisonByKey.get(key) ?? null;
    const source = comparison ?? primary;
    const sourceLabel = buildBasisBridgeSourceLabel(source);
    const accountLabel = source?.accountName.trim() || "Unnamed account";
    const accountTypeLabel = source?.accountType.trim() || "Unclassified";

    // An account posted on one basis only has nothing to compare against. Reading the absent side
    // as 0 turned its whole balance into a "variance" that looked like a real basis difference.
    if (!primary || !comparison) {
      const presentBasis = primary ? fromName : toName;
      const absentBasis = primary ? toName : fromName;
      const oneSidedLabel = `Only on ${presentBasis} basis — not compared`;
      const notOnLabel = `Not on ${absentBasis} basis`;
      const primaryBalanceLabel = primary ? money(primary.balance) : notOnLabel;
      const comparisonBalanceLabel = comparison ? money(comparison.balance) : notOnLabel;
      return {
        rowId: `${comparisonBasis}-${key}`,
        accountLabel,
        accountTypeLabel,
        comparison: primary ? "primary-only" : "comparison-only",
        variance: null,
        primaryBalanceLabel,
        comparisonBalanceLabel,
        varianceLabel: oneSidedLabel,
        varianceTone: "default",
        sourceLabel,
        ariaLabel: `${accountLabel} ${accountTypeLabel}. ${oneSidedLabel}. ${fromName} ${primaryBalanceLabel}. ${toName} ${comparisonBalanceLabel}.`
      } satisfies AccountingBasisBridgeRowViewModel;
    }

    const variance = comparison.balance - primary.balance;
    const varianceLabel = money(variance);
    return {
      rowId: `${comparisonBasis}-${key}`,
      accountLabel,
      accountTypeLabel,
      comparison: "compared",
      variance,
      primaryBalanceLabel: money(primary.balance),
      comparisonBalanceLabel: money(comparison.balance),
      varianceLabel,
      varianceTone: variance < 0 ? "danger" : variance > 0 ? "success" : "default",
      sourceLabel,
      ariaLabel: `${accountLabel} ${accountTypeLabel}. ${fromName} ${money(primary.balance)}. ${toName} ${money(comparison.balance)}. Variance ${varianceLabel}.`
    } satisfies AccountingBasisBridgeRowViewModel;
  });

  const comparedRows = bridgeRows.filter((row) => row.comparison === "compared");
  const comparedCount = comparedRows.length;
  const oneSidedCount = bridgeRows.length - comparedCount;
  const varianceTotal = comparedRows.reduce((total, row) => (row.variance === null ? total : total + row.variance), 0);

  return {
    title: "Basis bridge",
    description: `${toName} compared with Primary for ${runLabel}, grouped by source/rule/account where lineage is available.`,
    tableLabel,
    fromBasis: "Primary",
    toBasis: comparisonBasis,
    rows: bridgeRows,
    hasRows: bridgeRows.length > 0,
    summary: {
      comparedCount,
      oneSidedCount,
      // Only compared accounts contribute; one-sided accounts are counted, never summed as variance.
      varianceTotalLabel: comparedCount > 0 ? money(varianceTotal) : formatCurrencyAmount(null, currency),
      countsLabel: `Compared ${formatCount(comparedCount)} · one-sided ${formatCount(oneSidedCount)}`,
      oneSidedLabel: oneSidedCount > 0
        ? `${formatCount(oneSidedCount)} ${oneSidedCount === 1 ? "account" : "accounts"} on one basis only`
        : null
    },
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