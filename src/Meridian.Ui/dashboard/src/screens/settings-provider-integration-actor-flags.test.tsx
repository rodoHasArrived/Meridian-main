import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import {
  listProviderIntegrationActorFlags,
  scanProviderIntegrationActorFlags,
  type ProviderIntegrationActorFlag
} from "@/lib/api/provider-integration-actor-flags.api";
import { ProviderIntegrationActorFlagsPanel } from "./settings-provider-integration-actor-flags";

const approver: ProviderIntegrationActorFlag = {
  flagId: "f1",
  recordPath: "manifest-versions/abc/3.json",
  recordKind: "manifest-versions",
  subjectId: "vendor-csv",
  fieldPath: "manifest.approvedBy",
  recordedValue: "Meridian Operator",
  reason: "session desk label",
  recordedAt: "2026-11-20T10:00:00Z",
  flaggedBy: "ops.lead",
  flaggedAt: "2026-11-25T18:00:00Z"
};

describe("ProviderIntegrationActorFlagsPanel", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("lists flagged records with what was recorded and why", async () => {
    render(<ProviderIntegrationActorFlagsPanel load={() => Promise.resolve([approver, { ...approver, flagId: "f2", fieldPath: "reviewedBy", recordedValue: " ", reason: "blank actor", subjectId: null, recordKind: "quarantine-decisions" }])} />);

    expect(await screen.findByText("vendor-csv")).toBeInTheDocument();
    expect(screen.getByText("manifest.approvedBy")).toBeInTheDocument();
    expect(screen.getByText("Meridian Operator")).toBeInTheDocument();
    expect(screen.getByText("session desk label")).toBeInTheDocument();
    expect(screen.getByText("(blank)")).toBeInTheDocument();
    expect(screen.getAllByText("quarantine-decisions")).toHaveLength(2);
  });

  it("scans, reports the counts and reloads", async () => {
    const load = vi.fn().mockResolvedValueOnce([]).mockResolvedValueOnce([approver]);
    const scan = vi.fn().mockResolvedValue({ actorFieldsScanned: 7, flagsAppended: 1, alreadyFlagged: 2, flags: [approver] });
    render(<ProviderIntegrationActorFlagsPanel load={load} scan={scan} />);
    expect(await screen.findByText("No unverified actors are flagged.")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Scan for unverified actors" }));

    expect(await screen.findByText("Checked 7 actor field(s): 1 newly flagged, 2 already flagged.")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText("vendor-csv")).toBeInTheDocument());
    expect(load).toHaveBeenCalledTimes(2);
  });

  it("explains a refused scan", async () => {
    const scan = vi.fn().mockRejectedValue(new ApiError({ path: "/x", status: 403, detail: "You need provider configuration permission." }));
    render(<ProviderIntegrationActorFlagsPanel load={() => Promise.resolve([])} scan={scan} />);
    await screen.findByText("No unverified actors are flagged.");

    await userEvent.click(screen.getByRole("button", { name: "Scan for unverified actors" }));

    expect(await screen.findByRole("alert")).toBeInTheDocument();
  });

  it("uses the shared actor-flag routes", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response("[]", { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ actorFieldsScanned: 0, flagsAppended: 0, alreadyFlagged: 0, flags: [] }), { status: 200 }));
    vi.stubGlobal("fetch", fetch);

    await listProviderIntegrationActorFlags();
    await scanProviderIntegrationActorFlags(["Ops Desk"]);

    expect(fetch.mock.calls[0][0]).toBe("/api/workstation/provider-integrations/actor-flags");
    expect(fetch.mock.calls[1][0]).toBe("/api/workstation/provider-integrations/actor-flags/scan");
    expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ extraLabels: ["Ops Desk"] });
  });
});
