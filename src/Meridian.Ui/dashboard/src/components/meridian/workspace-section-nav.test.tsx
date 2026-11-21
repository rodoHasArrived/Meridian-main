import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { WorkspaceSectionNav } from "@/components/meridian/workspace-section-nav";
import { ACCOUNTING_NAVIGATION_GROUPS } from "@/lib/accounting-navigation";
import { renderWithRouter } from "@/test/render";
import { cleanup } from "@testing-library/react";

describe("WorkspaceSectionNav", () => {
  it("marks every Accounting page current in its own group's menu", () => {
    for (const group of ACCOUNTING_NAVIGATION_GROUPS) {
      if (group.items.length < 2) continue;
      for (const page of group.items) {
        renderWithRouter(<WorkspaceSectionNav />, { initialEntries: [page.route] });
        const nav = screen.getByRole("navigation", { name: `${group.label} pages` });
        expect(within(nav).getByRole("link", { name: `${page.label}, current page` })).toHaveAttribute("aria-current", "page");
        expect(within(nav).getAllByRole("link")).toHaveLength(group.items.length);
        cleanup();
      }
    }
  });

  it("resolves a deep link to the page that owns it and carries the accounting context", () => {
    renderWithRouter(<WorkspaceSectionNav />, { initialEntries: ["/accounting/journal-entries/detail?journalEntryId=je-1&periodId=2026-06&ledgerBookId=book-a"] });
    const nav = screen.getByRole("navigation", { name: "Records pages" });
    expect(within(nav).getByRole("link", { name: "Adjustments, current page" })).toBeInTheDocument();
    const ledger = within(nav).getByRole("link", { name: "Open Ledger explorer" });
    expect(ledger.getAttribute("href")).toContain("ledgerBookId=book-a");
    expect(ledger.getAttribute("href")).not.toContain("journalEntryId");
  });

  it("renders nothing outside Accounting", () => {
    renderWithRouter(<WorkspaceSectionNav />, { initialEntries: ["/trading"] });
    expect(screen.queryByRole("navigation")).not.toBeInTheDocument();
  });
});
