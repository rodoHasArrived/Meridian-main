import { describe, expect, it } from "vitest";
import {
  buildChangeSinceReview,
  type ChangeSinceReviewInput,
  type ReportElementValueInput,
  type UpstreamChangeInput
} from "@/lib/report-change-since-review";

const REVIEWED_AT = "2026-09-30T14:00:00Z";

function change(overrides: Partial<UpstreamChangeInput> = {}): UpstreamChangeInput {
  return {
    changeId: "chg-1",
    kind: "Pricing update",
    occurredAtUtc: "2026-09-30T16:00:00Z",
    affectedElementIds: ["market-value"],
    ...overrides
  };
}

function elementValue(overrides: Partial<ReportElementValueInput> = {}): ReportElementValueInput {
  return {
    elementId: "market-value",
    label: "Portfolio Market Value",
    reviewedValueLabel: "$2.411B",
    currentValueLabel: "$2.414B",
    ...overrides
  };
}

function scenario(overrides: Partial<ChangeSinceReviewInput> = {}): ChangeSinceReviewInput {
  return {
    reviewedAtUtc: REVIEWED_AT,
    freezeState: "Open",
    changes: [change()],
    elements: [elementValue()],
    ...overrides
  };
}

describe("change since review", () => {
  it("reports only changes that landed after the review", () => {
    const model = buildChangeSinceReview(scenario({
      changes: [
        change({ changeId: "before", occurredAtUtc: "2026-09-30T12:00:00Z" }),
        change({ changeId: "at-review", occurredAtUtc: REVIEWED_AT }),
        change({ changeId: "after", occurredAtUtc: "2026-09-30T16:00:00Z" })
      ]
    }));

    expect(model.changes.map((entry) => entry.changeId)).toEqual(["after"]);
    expect(model.changeCount).toBe(1);
  });

  it("maps changes onto the report elements they moved, with before and after", () => {
    const model = buildChangeSinceReview(scenario({
      changes: [
        change({ changeId: "price", affectedElementIds: ["market-value"] }),
        change({ changeId: "journal", kind: "Late journal", affectedElementIds: ["income", "market-value"] })
      ],
      elements: [
        elementValue(),
        elementValue({
          elementId: "income",
          label: "Investment Income",
          reviewedValueLabel: "$18.21M",
          currentValueLabel: "$18.37M"
        }),
        elementValue({ elementId: "untouched", label: "Credit Attribution" })
      ]
    }));

    expect(model.affectedElementCount).toBe(2);
    expect(model.affectedElements.map((entry) => entry.elementId)).toEqual(["market-value", "income"]);
    expect(model.affectedElements[0]).toMatchObject({
      transitionLabel: "$2.411B → $2.414B",
      changeCount: 2,
      changeKinds: ["Pricing update", "Late journal"]
    });
    expect(model.affectedElements[1].transitionLabel).toBe("$18.21M → $18.37M");
    expect(model.headline).toBe("2 underlying changes affecting 2 report elements");
  });

  it("orders changes newest first", () => {
    const model = buildChangeSinceReview(scenario({
      changes: [
        change({ changeId: "older", occurredAtUtc: "2026-09-30T15:00:00Z" }),
        change({ changeId: "newer", occurredAtUtc: "2026-09-30T17:00:00Z" })
      ]
    }));

    expect(model.changes.map((entry) => entry.changeId)).toEqual(["newer", "older"]);
  });

  it("uses the singular form for a single change", () => {
    const model = buildChangeSinceReview(scenario());
    expect(model.headline).toBe("1 underlying change affecting 1 report element");
  });

  it("reports no changes for a report that has never been reviewed", () => {
    const model = buildChangeSinceReview(scenario({ reviewedAtUtc: null }));

    expect(model.hasBeenReviewed).toBe(false);
    expect(model.hasChanges).toBe(false);
    expect(model.changes).toEqual([]);
    expect(model.headline).toBe("Not yet reviewed");
  });

  it("is clean when nothing changed after review", () => {
    const model = buildChangeSinceReview(scenario({ changes: [] }));

    expect(model.hasChanges).toBe(false);
    expect(model.headline).toBe("No changes since review");
    expect(model.severity).toBe("ready");
    expect(model.options).toEqual([]);
  });
});

describe("value impact", () => {
  it("signs a quantified impact", () => {
    const model = buildChangeSinceReview(scenario({
      changes: [
        change({ changeId: "up", valueImpact: 18341 }),
        change({ changeId: "down", valueImpact: -42901, occurredAtUtc: "2026-09-30T15:30:00Z" })
      ]
    }));

    const byId = Object.fromEntries(model.changes.map((entry) => [entry.changeId, entry]));
    expect(byId.up.valueImpactLabel).toBe("+18,341");
    expect(byId.down.valueImpactLabel).toBe("−42,901");
    expect(byId.up.hasValueImpact).toBe(true);
  });

  it("states plainly when a change moved no reported figure", () => {
    const model = buildChangeSinceReview(scenario({
      changes: [change({ kind: "Reference classification", valueImpact: 0 })]
    }));

    expect(model.changes[0].valueImpactLabel).toBe("No value impact");
    expect(model.changes[0].hasValueImpact).toBe(false);
  });

  it("prefers an explicit impact label over the derived one", () => {
    const model = buildChangeSinceReview(scenario({
      changes: [change({ valueImpact: 18341, valueImpactLabel: "+$18,341" })]
    }));

    expect(model.changes[0].valueImpactLabel).toBe("+$18,341");
  });
});

describe("freeze admission", () => {
  it("applies change automatically while the report is open", () => {
    const model = buildChangeSinceReview(scenario({ freezeState: "Open" }));

    expect(model.isAutoApplied).toBe(true);
    expect(model.requiresOperatorDecision).toBe(false);
    expect(model.admissionLabel).toBe("Applied automatically");
    expect(model.affectedElements[0].blockState).toBe("Changed");
    expect(model.options.map((option) => option.action)).toEqual(["ReviewChanges"]);
  });

  it("holds change for a preparer decision when soft frozen", () => {
    const model = buildChangeSinceReview(scenario({ freezeState: "SoftFrozen", frozenAtUtc: "2026-09-30T17:00:00Z" }));

    expect(model.isAutoApplied).toBe(false);
    expect(model.requiresOperatorDecision).toBe(true);
    expect(model.admissionLabel).toBe("Held for review");
    expect(model.severity).toBe("action");
    expect(model.options.map((option) => option.action)).toEqual(["ReviewChanges", "ApplyChanges", "RemainFrozen"]);
    expect(model.frozenAtUtc).toBe("2026-09-30T17:00:00Z");
  });

  it("marks held elements stale, because the rendered value no longer matches source", () => {
    const model = buildChangeSinceReview(scenario({ freezeState: "SoftFrozen" }));
    expect(model.affectedElements[0].blockState).toBe("Stale");
  });

  it("requires an explicit unfreeze on a hard-frozen report", () => {
    const model = buildChangeSinceReview(scenario({ freezeState: "HardFrozen" }));

    expect(model.admissionLabel).toBe("Not admitted");
    expect(model.requiresOperatorDecision).toBe(false);
    expect(model.options.map((option) => option.action)).toEqual(["ReviewChanges", "Unfreeze"]);
  });

  it("routes post-publication change toward a restatement assessment", () => {
    const model = buildChangeSinceReview(scenario({ freezeState: "Published" }));

    expect(model.admissionLabel).toBe("Not admitted");
    expect(model.options).toHaveLength(1);
    expect(model.options[0].description).toContain("restatement");
  });

  it("ignores changes with an unparseable timestamp rather than assuming they are new", () => {
    const model = buildChangeSinceReview(scenario({
      changes: [change({ changeId: "bad", occurredAtUtc: "not-a-date" })]
    }));

    expect(model.hasChanges).toBe(false);
  });
});
