import { describe, expect, it } from "vitest";
import {
  appendOperatingScopeToRoute,
  appendSearchValue,
  buildOperatingScopeFromSearch,
  normalizeOperatingContextSymbol,
  readOperatingScopeFromSearch,
  removeOperatingScopeFromSearch,
  summarizeOperatingScopeForRoute
} from "@/lib/app-shell-operating-scope";

describe("app shell operating scope", () => {
  it("normalizes route query scope into stable shell context", () => {
    const scope = buildOperatingScopeFromSearch(
      "?symbol= ms ft! &fundAccountId=fund:1&runId=run/9&provider=Alpaca%20Paper&from=2026-05-01&to=2026-05-15"
    );

    expect(scope).toMatchObject({
      label: "Operating scope",
      summary: "Subject: MSFT / Account: fund:1 / Run: run9 / Provider: Alpaca Paper / Window: 2026-05-01 to 2026-05-15",
      subjectSymbol: "MSFT",
      fundAccountId: "fund:1",
      runId: "run9",
      provider: "Alpaca Paper",
      hasScope: true,
      clearAriaLabel: "Clear operating scope: Subject MSFT, Account fund:1, Run run9, Provider Alpaca Paper, Window 2026-05-01 to 2026-05-15"
    });
    expect(scope.items.map((item) => [item.label, item.value])).toEqual([
      ["Subject", "MSFT"],
      ["Account", "fund:1"],
      ["Run", "run9"],
      ["Provider", "Alpaca Paper"],
      ["Window", "2026-05-01 to 2026-05-15"]
    ]);
    expect(readOperatingScopeFromSearch("?symbol=aapl").symbol).toBe("AAPL");
    expect(normalizeOperatingContextSymbol(" brk.b ")).toBe("BRK.B");
  });

  it("applies only route-relevant scope and preserves authoritative params", () => {
    const scope = buildOperatingScopeFromSearch("?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01");

    expect(appendOperatingScopeToRoute("/data/quotes", scope)).toBe("/data/quotes?symbol=MSFT&provider=Alpaca&from=2026-05-01");
    expect(appendOperatingScopeToRoute("/settings", scope)).toBe("/settings?fundAccountId=fund-1&provider=Alpaca");
    expect(appendOperatingScopeToRoute("/strategy?runId=existing", scope)).toBe(
      "/strategy?runId=existing&symbol=MSFT&provider=Alpaca&from=2026-05-01"
    );
    expect(summarizeOperatingScopeForRoute("/data/quotes", scope)).toBe("Subject: MSFT / Provider: Alpaca / Window: 2026-05-01 to Now");
    expect(summarizeOperatingScopeForRoute("/settings", scope)).toBe("Account: fund-1 / Provider: Alpaca");
  });

  it("clears scope params and appends ordinary query values without disturbing hash routes", () => {
    expect(removeOperatingScopeFromSearch("?symbol=MSFT&theme=ops&date=2026-05-01")).toBe("?theme=ops");
    expect(appendSearchValue("/reporting/evidence#packet", "symbol", "MSFT")).toBe("/reporting/evidence?symbol=MSFT#packet");
    expect(appendSearchValue("/strategy?runId=existing", "runId", "next", true)).toBe("/strategy?runId=existing");
  });
});
