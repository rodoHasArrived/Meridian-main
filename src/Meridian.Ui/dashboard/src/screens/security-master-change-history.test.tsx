import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { SecurityMasterChangeHistoryItem } from "@/lib/api/security-master-workbench.api";
import { formatDateTimeLabel } from "./accounting-screen.formatting";
import { SecurityChangeHistoryPanel } from "./accounting-screen.security-master-detail-panels";
import {
  buildSecurityChangeHistoryRows,
  buildSecurityChangeHistoryViewState,
  describeSecurityChangeValidTo
} from "./accounting-screen.security-master-presenters";

function item(overrides: Partial<SecurityMasterChangeHistoryItem>): SecurityMasterChangeHistoryItem {
  return {
    changeId: "1:TermsAmended",
    streamVersion: 1,
    eventType: "TermsAmended",
    changedAtUtc: "2026-04-01T10:00:00Z",
    effectiveAtUtc: null,
    actor: "ops.analyst",
    origin: "Manual",
    sourceSystem: "workstation",
    sourceRecordId: null,
    reason: null,
    summary: "Terms amended.",
    changedFields: ["Coupon rate"],
    changedFieldsSummary: "Coupon rate",
    ...overrides
  };
}

// v2 governs [Mar 1, Jun 1); v3 was corrected by v4 (same valid-from); v4 governs from Jun 1 onwards;
// v5 is a staged edit with no valid time.
const history: SecurityMasterChangeHistoryItem[] = [
  item({ changeId: "2:TermsAmended", streamVersion: 2, changedAtUtc: "2026-04-02T10:00:00Z", validFromUtc: "2026-03-01T00:00:00Z", validToUtc: "2026-06-01T00:00:00Z" }),
  item({ changeId: "3:TermsAmended", streamVersion: 3, changedAtUtc: "2026-04-03T10:00:00Z", validFromUtc: "2026-06-01T00:00:00Z", supersededByStreamVersion: 4 }),
  item({ changeId: "4:TermsAmended", streamVersion: 4, changedAtUtc: "2026-04-04T10:00:00Z", validFromUtc: "2026-06-01T00:00:00Z" }),
  item({ changeId: "5:EditStaged", streamVersion: 5, eventType: "EditStaged", changedAtUtc: "2026-04-05T10:00:00Z", changedFieldsSummary: "", changedFields: [], actor: "" })
];

describe("Security Master change history valid time", () => {
  it("describes valid-to as bounded, open, superseded, or not applicable", () => {
    expect(describeSecurityChangeValidTo({ validFromUtc: "2026-03-01T00:00:00Z", validToUtc: "2026-06-01T00:00:00Z" }))
      .toEqual({ label: formatDateTimeLabel("2026-06-01T00:00:00Z"), validity: "bounded" });
    expect(describeSecurityChangeValidTo({ validFromUtc: "2026-06-01T00:00:00Z", validToUtc: null }))
      .toEqual({ label: "open", validity: "open" });
    expect(describeSecurityChangeValidTo({ validFromUtc: "2026-06-01T00:00:00Z", supersededByStreamVersion: 4 }))
      .toEqual({ label: "superseded by v4", validity: "superseded" });
    expect(describeSecurityChangeValidTo({})).toEqual({ label: "—", validity: "none" });
  });

  it("maps rows newest recording first with valid-time, actor, and changed-field labels", () => {
    const rows = buildSecurityChangeHistoryRows(history);

    expect(rows.map((row) => row.versionLabel)).toEqual(["v5", "v4", "v3", "v2"]);
    const [staged, governing, corrected, bounded] = rows;
    expect(staged).toMatchObject({ validFromLabel: "—", validToLabel: "—", validity: "none", actorLabel: "—", changedFieldsLabel: "—" });
    expect(governing).toMatchObject({ validFromLabel: formatDateTimeLabel("2026-06-01T00:00:00Z"), validToLabel: "open", validity: "open" });
    expect(corrected).toMatchObject({ validToLabel: "superseded by v4", validity: "superseded" });
    expect(bounded).toMatchObject({
      changedAtLabel: formatDateTimeLabel("2026-04-02T10:00:00Z"),
      validToLabel: formatDateTimeLabel("2026-06-01T00:00:00Z"),
      actorLabel: "ops.analyst",
      changedFieldsLabel: "Coupon rate"
    });
    expect(bounded.ariaLabel).toContain("valid from");
    expect(new Set(rows.map((row) => row.rowId)).size).toBe(rows.length);
  });

  it("falls back to joined changed fields and breaks timestamp ties by stream version", () => {
    const rows = buildSecurityChangeHistoryRows([
      item({ streamVersion: 7, changeId: "7", changedFieldsSummary: "", changedFields: ["Coupon rate", "Maturity"] }),
      item({ streamVersion: 8, changeId: "8" })
    ]);

    expect(rows.map((row) => row.streamVersion)).toEqual([8, 7]);
    expect(rows[1].changedFieldsLabel).toBe("Coupon rate, Maturity");
  });

  it("reports loading, error, and empty states", () => {
    expect(buildSecurityChangeHistoryViewState({ securityId: "sec-1", changeHistory: null, loading: true }).loadingText)
      .toBe("Loading change history...");
    expect(buildSecurityChangeHistoryViewState({ securityId: "sec-1", changeHistory: null, error: "Snapshot unavailable." }))
      .toMatchObject({ errorText: "Snapshot unavailable.", statusAnnouncement: "Change history error: Snapshot unavailable." });
    expect(buildSecurityChangeHistoryViewState({ securityId: "sec-1", changeHistory: [] }))
      .toMatchObject({ hasRows: false, emptyText: "No change history is recorded for sec-1." });
  });

  it("renders the change history panel with valid-from and valid-to columns", () => {
    const view = buildSecurityChangeHistoryViewState({ securityId: "sec-1", changeHistory: history });
    render(<SecurityChangeHistoryPanel view={view} />);

    const table = screen.getByRole("table", { name: "Change history for sec-1" });
    expect(within(table).getByRole("columnheader", { name: "Valid from" })).toBeTruthy();
    expect(within(table).getByRole("columnheader", { name: "Valid to" })).toBeTruthy();
    expect(within(table).getByText("superseded by v4")).toBeTruthy();
    expect(within(table).getByText("open")).toBeTruthy();
    expect(within(table).getAllByText(formatDateTimeLabel("2026-06-01T00:00:00Z")).length).toBeGreaterThanOrEqual(2);
    expect(within(table).getByText("v2")).toBeTruthy();
    expect(screen.getByText("4 change entries loaded for sec-1.")).toBeTruthy();
  });

  it("renders the error state instead of the table", () => {
    const view = buildSecurityChangeHistoryViewState({ securityId: "sec-1", changeHistory: history, error: "Snapshot unavailable." });
    render(<SecurityChangeHistoryPanel view={view} />);

    expect(screen.getByRole("alert").textContent).toContain("Snapshot unavailable.");
    expect(screen.queryByRole("table")).toBeNull();
  });
});
