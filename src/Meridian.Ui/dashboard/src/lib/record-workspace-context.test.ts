import { describe, expect, it } from "vitest";
import {
  appendRecordWorkspaceContext,
  buildRecordWorkspaceContext,
  readRecordWorkspaceContext,
  relatedRecordWork,
  withoutRecordWorkspaceContext,
  type RecordIdentity
} from "@/lib/record-workspace-context";

const position: RecordIdentity = {
  kind: "position",
  id: "position-67",
  name: "MSFT · Short",
  identifier: "MSFT",
  assetClass: "Equity",
  entity: "North Fund",
  book: "GAAP",
  status: "Open",
  asOf: "2026-10-28",
  trust: "Mark review required",
  trustTone: "review",
  scope: { symbol: "MSFT", positionKey: "position-67", fundAccountId: "brokerage-a", ledgerBookId: "book-a", periodId: "period-a" }
};

describe("record workspace context", () => {
  it("round-trips the origin selection, filters, financial scope and snapshot without reusing foreign run IDs", () => {
    const context = buildRecordWorkspaceContext(position, {
      pathname: "/portfolio",
      search: "?view=positions-filters&runId=paper-run&provider=Alpaca",
      hash: ""
    }, { positionRef: "id:position-67" }, "#portfolio-position-detail")!;
    const report = relatedRecordWork(position, "/portfolio").find(({ route }) => route.startsWith("/reporting"))!;
    const target = new URL(appendRecordWorkspaceContext(`${report.route}?reportView=ready#templates`, context)!, "https://meridian.local");

    expect(target.pathname).toBe("/reporting/library");
    expect(target.hash).toBe("#templates");
    expect(target.searchParams.get("runId")).toBeNull();
    const carried = readRecordWorkspaceContext(target.search);
    expect(carried?.record).toMatchObject(position);
    expect(carried?.operatingScope).toMatchObject({ runId: "paper-run", provider: "Alpaca" });
    expect(carried?.origin).toBe("/portfolio?view=positions-filters&runId=paper-run&provider=Alpaca&positionRef=id%3Aposition-67#portfolio-position-detail");
    expect(withoutRecordWorkspaceContext(target.search)).toBe("?reportView=ready");
  });

  it("applies only supported destination selectors and preserves the originating record on subsequent handoffs", () => {
    const context = buildRecordWorkspaceContext(position, { pathname: "/portfolio", search: "", hash: "" }, { positionRef: "id:position-67" })!;
    const links = relatedRecordWork(position, "/portfolio").map(({ route }) => route);
    expect(links).toEqual([
      "/accounting/ledger?ledgerBookId=book-a&periodId=period-a",
      "/data/quotes?symbol=MSFT",
      "/trading/positions?positionRef=id%3Aposition-67",
      "/reporting/library"
    ]);
    const first = new URL(appendRecordWorkspaceContext(links[1], context)!, "https://meridian.local");
    const second = new URL(appendRecordWorkspaceContext(links[2], readRecordWorkspaceContext(first.search)!)!, "https://meridian.local");
    expect(readRecordWorkspaceContext(second.search)).toEqual(readRecordWorkspaceContext(first.search));
  });

  it("rejects malformed context and external or unrelated return destinations", () => {
    expect(readRecordWorkspaceContext("?recordContext=malformed&recordFrom=%2Fportfolio")).toBeNull();
    const context = buildRecordWorkspaceContext(position, { pathname: "/portfolio", search: "", hash: "" }, {})!;
    const route = new URL(appendRecordWorkspaceContext("/data/quotes", context)!, "https://meridian.local");
    for (const invalid of ["//example.test/portfolio", "/portfolio\\foo", "/portfolio/../settings", "/accounting/ledger"]) {
      route.searchParams.set("recordFrom", invalid);
      expect(readRecordWorkspaceContext(route.search)).toBeNull();
    }
  });
});
