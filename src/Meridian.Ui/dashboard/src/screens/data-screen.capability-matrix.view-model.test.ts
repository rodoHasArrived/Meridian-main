import { describe, expect, it } from "vitest";
import { buildCapabilityMatrixModel } from "./data-screen.capability-matrix.view-model";
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
});
