import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AccountBondPerformancePanel } from "./account-bond-performance-panel";
import type { BondPerformanceResult } from "@/types/bond-workflow.types";

const result: BondPerformanceResult = { scope: { tenantId: "tenant", companyId: "company", accountId: "account", bookId: "book", baseCurrency: "USD" }, periodStart: "2026-01-01", periodEnd: "2026-11-12", modelVersion: "bond-performance-v1", completeCoverage: false, baseObservedMarkAndCashChange: "125", coverageGaps: ["Complete transaction population unavailable"], securities: [], accountingIncome: [] };
describe("AccountBondPerformancePanel", () => {
  it("imports only reviewed evidence identifiers before recalculating performance", async () => {
    const importer = vi.fn(async () => ({ populationId: "verified-population" }));
    render(<AccountBondPerformancePanel bookId="book" accountId="account" start="2026-01-01" end="2026-11-12" importSource={importer} />);
    fireEvent.change(screen.getByLabelText("Reviewed transaction evidence URI"), { target: { value: "evidence://synthetic/source" } });
    fireEvent.change(screen.getByLabelText("Reviewed transaction document ID"), { target: { value: "reviewed-document" } });
    fireEvent.click(screen.getByRole("button", { name: "Use reviewed transaction source" }));
    await screen.findByRole("status");
    expect(importer).toHaveBeenCalledWith("book", "account", { evidenceUri: "evidence://synthetic/source", documentId: "reviewed-document" }, expect.objectContaining({ allowDevelopmentFallback: false }));
    expect(screen.queryByRole("region", { name: "Bond performance explanation" })).not.toBeInTheDocument();
  });
  it("keeps incomplete observed mark changes distinct from certified returns", async () => {
    const explain = vi.fn(async () => result); render(<AccountBondPerformancePanel bookId="book" accountId="account" start="2026-01-01" end="2026-11-12" explain={explain} />);
    fireEvent.click(screen.getByRole("button", { name: "Explain bond income and market return" })); await screen.findByRole("region", { name: "Bond performance explanation" });
    expect(screen.getByText("Complete transaction population unavailable")).toBeInTheDocument(); expect(screen.getByText(/not a certified investment return/)).toBeInTheDocument();
    expect(explain).toHaveBeenCalledWith("book", "account", expect.objectContaining({ periodStart: "2026-01-01", periodEnd: "2026-11-12", taxLotRecordIds: [] }), expect.objectContaining({ allowDevelopmentFallback: false }));
  });
  it("refuses a different account's explanation", async () => {
    const explain = vi.fn(async () => ({ ...result, scope: { ...result.scope, accountId: "other" } })); render(<AccountBondPerformancePanel bookId="book" accountId="account" start="2026-01-01" end="2026-11-12" explain={explain} />);
    fireEvent.click(screen.getByRole("button", { name: "Explain bond income and market return" })); await screen.findByRole("alert"); expect(screen.queryByRole("region", { name: "Bond performance explanation" })).not.toBeInTheDocument();
  });
});
