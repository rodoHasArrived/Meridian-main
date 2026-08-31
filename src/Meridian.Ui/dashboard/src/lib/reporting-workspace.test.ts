import { describe, expect, it } from "vitest";
import { normalizeReportingWorkspace } from "@/lib/reporting-workspace";
import type {
  AccountingReportingSummary,
  AccountingWorkspaceResponse
} from "@/types";

const reporting: AccountingReportingSummary = {
  profileCount: 1,
  recommendedProfiles: ["excel"],
  profiles: [],
  summary: "One reporting profile is available."
};

describe("normalizeReportingWorkspace", () => {
  it("reads the canonical independent reporting payload", () => {
    expect(normalizeReportingWorkspace(reporting)).toBe(reporting);
  });

  it("temporarily tolerates the accounting envelope during rolling migration", () => {
    const envelope = { reporting } as AccountingWorkspaceResponse;

    expect(normalizeReportingWorkspace(envelope)).toBe(reporting);
  });

  it("keeps an unavailable response distinct from an empty reporting section", () => {
    expect(normalizeReportingWorkspace(null)).toBeNull();
  });
});
