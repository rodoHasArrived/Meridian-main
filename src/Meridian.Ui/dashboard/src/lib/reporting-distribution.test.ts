import { describe, expect, it } from "vitest";
import {
  buildDistributionHistory,
  evaluateRetention,
  normalizeDistributionEventKind
} from "@/lib/reporting-distribution";

describe("normalizeDistributionEventKind", () => {
  it("accepts common spellings", () => {
    expect(normalizeDistributionEventKind("sent")).toBe("Distributed");
    expect(normalizeDistributionEventKind("Restatement")).toBe("Restated");
    expect(normalizeDistributionEventKind("read")).toBe("Opened");
  });

  it("returns null rather than guessing", () => {
    expect(normalizeDistributionEventKind("forwarded-by-email")).toBeNull();
  });
});

describe("buildDistributionHistory", () => {
  const HISTORY = [
    { kind: "Published", timestampUtc: "2026-10-05T12:32:00Z", version: "09" },
    { kind: "Distributed", timestampUtc: "2026-10-05T12:35:00Z", recipient: "Investment Committee" },
    { kind: "Archived", timestampUtc: "2026-10-05T12:35:30Z" },
    { kind: "Restated", timestampUtc: "2026-10-06T15:20:00Z", version: "10" }
  ];

  it("orders the timeline oldest first", () => {
    const model = buildDistributionHistory([...HISTORY].reverse());

    expect(model.events.map((event) => event.kind)).toEqual([
      "Published",
      "Distributed",
      "Archived",
      "Restated"
    ]);
  });

  it("collects distinct recipients", () => {
    const model = buildDistributionHistory([
      ...HISTORY,
      { kind: "Distributed", timestampUtc: "2026-10-05T12:36:00Z", recipient: "Investment Committee" },
      { kind: "Delivered", timestampUtc: "2026-10-05T12:37:00Z", recipient: "Board" }
    ]);

    expect(model.recipients).toEqual(["Investment Committee", "Board"]);
    expect(model.distributedCount).toBe(3);
  });

  it("retains an event with an unreadable timestamp instead of dropping it", () => {
    const model = buildDistributionHistory([
      ...HISTORY,
      { kind: "Distributed", timestampUtc: "last Tuesday", recipient: "Auditor" }
    ]);

    expect(model.events).toHaveLength(4);
    expect(model.unplacedEvents.map((event) => event.recipient)).toEqual(["Auditor"]);
    expect(model.severity).toBe("review");
    expect(model.summary).toContain("could not be placed");
  });

  it("still counts an unplaced distribution towards recipients", () => {
    const model = buildDistributionHistory([
      { kind: "Distributed", timestampUtc: null, recipient: "Auditor" }
    ]);

    expect(model.recipients).toEqual(["Auditor"]);
    expect(model.distributedCount).toBe(1);
  });

  it("retains an event of an unrecognized kind", () => {
    const model = buildDistributionHistory([
      { kind: "Forwarded", timestampUtc: "2026-10-05T12:40:00Z" }
    ]);

    expect(model.unplacedEvents).toHaveLength(1);
    expect(model.unplacedEvents[0]?.label).toBe("Forwarded");
  });

  it("flags a report that was published but never distributed", () => {
    const model = buildDistributionHistory([
      { kind: "Published", timestampUtc: "2026-10-05T12:32:00Z" }
    ]);

    expect(model.isPublishedUndistributed).toBe(true);
    expect(model.severity).toBe("review");
    expect(model.summary).toContain("no distribution recorded");
  });

  it("reports an empty history plainly", () => {
    const model = buildDistributionHistory([]);

    expect(model.summary).toBe("No distribution history recorded.");
    expect(model.severity).toBe("ready");
  });
});

describe("evaluateRetention", () => {
  const evaluatedAtUtc = "2026-10-05T12:00:00Z";

  it("reports a record inside its retention period", () => {
    const model = evaluateRetention({
      policy: { years: 7, archiveFormats: ["PDF/A"], requiresLineageSnapshot: true },
      publishedAtUtc: "2026-10-05T12:32:00Z",
      evaluatedAtUtc
    });

    expect(model.status).toBe("Retained");
    expect(model.isDisposable).toBe(false);
    expect(model.retainUntilUtc).toBe("2033-10-05T12:32:00.000Z");
    expect(model.policy.archiveFormats).toEqual(["PDF/A"]);
    expect(model.policy.requiresLineageSnapshot).toBe(true);
  });

  it("reports a record whose retention has elapsed as disposable", () => {
    const model = evaluateRetention({
      policy: { years: 7 },
      publishedAtUtc: "2018-01-01T00:00:00Z",
      evaluatedAtUtc
    });

    expect(model.status).toBe("Eligible");
    expect(model.isDisposable).toBe(true);
    expect(model.summary).toContain("may be dispositioned");
  });

  it("blocks disposal for an unclassified record rather than treating it as free", () => {
    const model = evaluateRetention({
      policy: { years: null },
      publishedAtUtc: "2001-01-01T00:00:00Z",
      evaluatedAtUtc
    });

    expect(model.status).toBe("Unclassified");
    expect(model.isDisposable).toBe(false);
    expect(model.severity).toBe("action");
  });

  it("blocks disposal when there is no policy object at all", () => {
    const model = evaluateRetention({ publishedAtUtc: "2001-01-01T00:00:00Z", evaluatedAtUtc });

    expect(model.status).toBe("Unclassified");
    expect(model.isDisposable).toBe(false);
  });

  it("blocks disposal when the retention clock cannot be read", () => {
    const model = evaluateRetention({ policy: { years: 7 }, publishedAtUtc: null, evaluatedAtUtc });

    expect(model.status).toBe("Indeterminate");
    expect(model.isDisposable).toBe(false);
    expect(model.summary).toContain("publication date is unknown");
  });

  it("treats a zero-year policy as immediately eligible", () => {
    const model = evaluateRetention({
      policy: { years: 0 },
      publishedAtUtc: "2026-10-01T00:00:00Z",
      evaluatedAtUtc
    });

    expect(model.status).toBe("Eligible");
  });

  it("ignores a negative retention period rather than computing a past date", () => {
    const model = evaluateRetention({
      policy: { years: -3 },
      publishedAtUtc: "2026-10-01T00:00:00Z",
      evaluatedAtUtc
    });

    expect(model.status).toBe("Unclassified");
    expect(model.isDisposable).toBe(false);
  });

  it("reports the years remaining", () => {
    const model = evaluateRetention({
      policy: { years: 7 },
      publishedAtUtc: "2025-10-05T12:00:00Z",
      evaluatedAtUtc
    });

    expect(model.yearsRemaining).toBeGreaterThan(5.9);
    expect(model.yearsRemaining).toBeLessThan(6.1);
  });
});
