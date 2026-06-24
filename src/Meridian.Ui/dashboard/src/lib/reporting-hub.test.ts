import { describe, expect, it } from "vitest";
import {
  buildReportingHubModel,
  type ReportingHubRunInput,
  type ReportingHubTemplateInput
} from "@/lib/reporting-hub";

function run(overrides: Partial<ReportingHubRunInput>): ReportingHubRunInput {
  return {
    templateId: "tmpl",
    family: "Performance",
    status: "Released",
    asOfDateLabel: "2026-06-30",
    runIdLabel: "run-1",
    drilldownLinks: [],
    ...overrides
  };
}

function template(overrides: Partial<ReportingHubTemplateInput>): ReportingHubTemplateInput {
  return {
    templateName: "perf",
    name: "Performance Report",
    family: "Performance",
    ...overrides
  };
}

describe("reporting hub model", () => {
  it("groups runs by family, picks the most recent run, and surfaces approved as-of", () => {
    const model = buildReportingHubModel(
      [
        run({ family: "Performance", status: "Draft", asOfDateLabel: "2026-06-30", runIdLabel: "perf-jun" }),
        run({ family: "Performance", status: "Released", asOfDateLabel: "2026-05-31", runIdLabel: "perf-may" }),
        run({ family: "Holdings", status: "Approved", asOfDateLabel: "2026-06-30", runIdLabel: "hold-jun" })
      ],
      [
        template({ family: "Performance", templateName: "perf" }),
        template({ family: "Holdings", templateName: "holdings", name: "Holdings Report" })
      ]
    );

    expect(model.cards.map((card) => card.family)).toEqual(["Holdings", "Performance"]);

    const performance = model.cards.find((card) => card.family === "Performance")!;
    // Latest run is the June draft; readiness reflects that, but the approved line points at May.
    expect(performance.latestRunId).toBe("perf-jun");
    expect(performance.readiness).toBe("Draft");
    expect(performance.needsAttention).toBe(true);
    expect(performance.approvedAsOfLabel).toBe("Approved as of May 31, 2026");
    expect(performance.runCount).toBe(2);

    const holdings = model.cards.find((card) => card.family === "Holdings")!;
    expect(holdings.readiness).toBe("Approved");
    expect(holdings.isCurrent).toBe(true);
    expect(holdings.approvedAsOfLabel).toBe("Approved as of Jun 30, 2026");
  });

  it("resolves the open link from the latest run's first browser-navigable drilldown", () => {
    const model = buildReportingHubModel(
      [
        run({
          family: "Performance",
          status: "Released",
          asOfDateLabel: "2026-06-30",
          drilldownLinks: [
            { href: "/api/raw", label: "Manifest", isBrowserNavigable: false },
            { href: "/reporting/runs/perf-jun", label: "Run", isBrowserNavigable: true }
          ]
        })
      ],
      [template({ family: "Performance" })]
    );

    const performance = model.cards[0];
    expect(performance.openHref).toBe("/reporting/runs/perf-jun");
    expect(performance.openLabel).toBe("Open latest output");
  });

  it("represents families that have templates but no runs", () => {
    const model = buildReportingHubModel([], [template({ family: "Audit", templateName: "audit-pack" })]);

    const audit = model.cards[0];
    expect(audit.readiness).toBe("NoRuns");
    expect(audit.runCount).toBe(0);
    expect(audit.openHref).toBeNull();
    expect(audit.latestAsOfLabel).toBe("—");
    expect(audit.approvedAsOfLabel).toBe("No approved output yet");
    expect(audit.needsAttention).toBe(true);
  });

  it("summarizes current vs. attention counts", () => {
    const model = buildReportingHubModel(
      [
        run({ family: "Performance", status: "Released" }),
        run({ family: "Holdings", status: "Failed" })
      ],
      [
        template({ family: "Performance" }),
        template({ family: "Holdings", templateName: "holdings" })
      ]
    );

    expect(model.totalFamilies).toBe(2);
    expect(model.currentCount).toBe(1);
    expect(model.attentionCount).toBe(1);
    expect(model.summaryLabel).toBe("1 of 2 families current · 1 need attention");
  });

  it("is empty when nothing is configured", () => {
    const model = buildReportingHubModel([], []);
    expect(model.isEmpty).toBe(true);
    expect(model.summaryLabel).toBe("No report families are configured yet.");
  });
});
