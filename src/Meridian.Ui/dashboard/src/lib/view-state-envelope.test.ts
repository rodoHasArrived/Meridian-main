import { describe, expect, it } from "vitest";
import {
  appendViewStateToRoute,
  buildTableViewState,
  decodeViewStateEnvelope,
  encodeViewStateEnvelope,
  MAX_VIEW_STATE_TOKEN_LENGTH,
  normalizeTableViewState,
  readViewStateFromSearch,
  stripViewStateFromSearch,
  VIEW_STATE_QUERY_KEY,
  type ViewStateEnvelope
} from "@/lib/view-state-envelope";

const envelope: ViewStateEnvelope = {
  v: 1,
  screen: "reporting-exports",
  state: {
    selectedExportsTemplateId: "investor-monthly-statement",
    asOfDate: "2026-06-30",
    note: "Fónd Δ — ünïcode"
  }
};

describe("view state envelope", () => {
  it("round-trips unicode payloads and nested filters", () => {
    const nested: ViewStateEnvelope = {
      v: 1,
      screen: "trial-balance",
      state: buildTableViewState({
        query: "cash",
        sortBy: { column: "balance", direction: "desc" },
        filters: { entity: ["Fund Δ", "Fund B"], status: ["open"] }
      })
    };

    const token = encodeViewStateEnvelope(nested);
    expect(token).not.toBeNull();
    expect(token).toMatch(/^[A-Za-z0-9_-]+$/);
    expect(decodeViewStateEnvelope(token)).toEqual(nested);

    const unicodeToken = encodeViewStateEnvelope(envelope);
    expect(decodeViewStateEnvelope(unicodeToken)).toEqual(envelope);
  });

  it("rejects tampered tokens, wrong versions, and malformed shapes", () => {
    const token = encodeViewStateEnvelope(envelope)!;
    expect(decodeViewStateEnvelope(`${token}garbage!!`)).toBeNull();
    expect(decodeViewStateEnvelope("not-base64-json")).toBeNull();

    const wrongVersion = { ...envelope, v: 2 } as unknown as ViewStateEnvelope;
    expect(decodeViewStateEnvelope(encodeViewStateEnvelope(wrongVersion))).toBeNull();

    const arrayState = { v: 1, screen: "x", state: [] } as unknown as ViewStateEnvelope;
    expect(decodeViewStateEnvelope(encodeViewStateEnvelope(arrayState))).toBeNull();
  });

  it("returns null when the payload exceeds the token budget", () => {
    const oversized: ViewStateEnvelope = {
      v: 1,
      screen: "reporting-exports",
      state: { blob: "x".repeat(MAX_VIEW_STATE_TOKEN_LENGTH * 2) }
    };
    expect(encodeViewStateEnvelope(oversized)).toBeNull();
    expect(appendViewStateToRoute("/reporting/exports", oversized)).toBe("/reporting/exports");
  });

  it("reads only the matching screen from a search string", () => {
    const route = appendViewStateToRoute("/reporting/exports?fundAccountId=fund-a", envelope);
    const search = route.slice(route.indexOf("?"));

    expect(readViewStateFromSearch(search, "reporting-exports")).toEqual(envelope);
    expect(readViewStateFromSearch(search, "trial-balance")).toBeNull();
  });

  it("strips only the view param, preserving scope keys", () => {
    const route = appendViewStateToRoute(
      "/reporting/exports?symbol=AAPL&fundAccountId=f-1&runId=r-1&provider=p&from=a&to=b&date=d&asOf=e",
      envelope
    );
    const search = route.slice(route.indexOf("?"));
    const stripped = stripViewStateFromSearch(search);

    const params = new URLSearchParams(stripped);
    expect(params.get(VIEW_STATE_QUERY_KEY)).toBeNull();
    for (const key of ["symbol", "fundAccountId", "runId", "provider", "from", "to", "date", "asOf"]) {
      expect(params.get(key)).not.toBeNull();
    }
  });

  it("normalizes table view state defensively", () => {
    expect(normalizeTableViewState(null)).toEqual({ query: "", sortBy: null, filters: {} });
    expect(
      normalizeTableViewState({ query: 42, sortBy: { column: "x", direction: "sideways" }, filters: { a: [1] } })
    ).toEqual({ query: "", sortBy: null, filters: {} });
    expect(
      normalizeTableViewState({ query: "q", sortBy: { column: "x", direction: "asc" }, filters: { a: ["1"] } })
    ).toEqual({ query: "q", sortBy: { column: "x", direction: "asc" }, filters: { a: ["1"] } });
  });
});
