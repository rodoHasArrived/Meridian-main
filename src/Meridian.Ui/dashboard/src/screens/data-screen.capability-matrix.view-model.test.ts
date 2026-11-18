import { describe, expect, it } from "vitest";
import { buildCapabilityFreshnessIndex, buildCapabilityMatrixModel, formatEvidenceAge } from "./data-screen.capability-matrix.view-model";
import type { ProviderCapabilityMatrixResponse } from "@/types";

function response(overrides: Partial<ProviderCapabilityMatrixResponse> = {}): ProviderCapabilityMatrixResponse {
  return {
    generatedAt: "2026-07-05T12:00:00Z",
    instrumentTypes: ["Equity", "Bond"],
    providers: [
      {
        providerId: "alpaca",
        declaredInstrumentTypes: ["Equity"],
        surfaces: {
          streaming: true,
          historical: true,
          symbolSearch: true,
          corporateActions: true,
          optionsChain: true,
          brokerage: true
        },
        cells: [
          {
            instrumentType: "Equity",
            supported: true,
            stream: true,
            backfill: true,
            corporateActions: true,
            symbolSearch: true,
            optionsChain: false
          },
          {
            instrumentType: "Bond",
            supported: false,
            stream: false,
            backfill: false,
            corporateActions: false,
            symbolSearch: false,
            optionsChain: false
          }
        ]
      }
    ],
    discoveryFailures: [],
    ...overrides
  };
}

describe("buildCapabilityMatrixModel", () => {
  it("marks supported cells with compact capability codes and unsupported cells with a dash", () => {
    const model = buildCapabilityMatrixModel(response());

    expect(model.columns).toEqual(["Equity", "Bond"]);
    const row = model.rows[0];
    expect(row.providerId).toBe("alpaca");
    expect(row.cells[0].marks).toBe("S·B·Q·C");
    expect(row.cells[0].supported).toBe(true);
    expect(row.cells[1].marks).toBe("—");
    expect(row.cells[1].supported).toBe(false);
    expect(row.cells[1].description).toContain("does not declare Bond");
  });

  it("counts declared coverage cells and surfaces discovery failures in the summary", () => {
    const model = buildCapabilityMatrixModel(
      response({
        discoveryFailures: [
          {
            stage: "register",
            subject: "Meridian.Infrastructure.Adapters.Foo.FooModule",
            moduleId: "foo",
            errorType: "InvalidOperationException",
            errorMessage: "credentials missing"
          }
        ]
      })
    );

    expect(model.summary).toContain("1 providers × 2 instrument types");
    expect(model.summary).toContain("1 declared coverage cells");
    expect(model.summary).toContain("1 discovery failure");
    expect(model.failures).toHaveLength(1);
  });

  it("retains task-specific evidence independently from broad instrument coverage", () => {
    const input = response();
    input.providers[0].fixedIncomeTasks = [{
      task: "ExecutableQuotes", adapterSupport: "Unsupported", detail: "No bond streaming profile", connections: []
    }];
    const row = buildCapabilityMatrixModel(input).rows[0];
    expect(row.fixedIncomeTasks).toEqual(input.providers[0].fixedIncomeTasks);
    expect(row.cells[0].supported).toBe(true);
  });
});

describe("buildCapabilityFreshnessIndex", () => {
  const now = Date.parse("2026-11-17T12:00:00Z");
  const freshness = buildCapabilityFreshnessIndex([
    { providerId: "Alpaca", latestEvidenceAt: "2026-11-17T09:00:00Z", evidenceStale: false },
    { providerId: "polygon", latestEvidenceAt: "2026-07-01T12:00:00Z", evidenceStale: true },
    { providerId: "ibkr", latestEvidenceAt: null, evidenceStale: false }
  ], now);

  it("joins by provider id regardless of case and reports the age", () => {
    expect(freshness("alpaca")).toEqual({ state: "current", label: "Current", ageText: "3h ago" });
    expect(freshness("polygon")).toEqual({ state: "stale", label: "Stale", ageText: "4mo ago" });
  });

  it("reads no evidence for an untimed or unknown provider", () => {
    expect(freshness("ibkr")).toEqual({ state: "no-evidence", label: "No evidence", ageText: null });
    expect(freshness("stooq").state).toBe("no-evidence");
  });

  it("formats ages in hours, days, then months", () => {
    expect(formatEvidenceAge(30 * 60_000)).toBe("under 1h ago");
    expect(formatEvidenceAge(50 * 3_600_000)).toBe("2d ago");
    expect(formatEvidenceAge(90 * 86_400_000)).toBe("3mo ago");
  });
});
