import { act, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiGetJson } from "@/lib/api";
import { renderWithRouter } from "@/test/render";
import { FirstRunScreen } from "./first-run-screen";
import type { FirstRunStatus } from "./types";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));

afterEach(() => vi.clearAllMocks());

describe("FirstRunScreen request lifetime", () => {
  it("aborts the initial status read when navigation unmounts the screen", async () => {
    let rejectRead!: (reason: unknown) => void;
    vi.mocked(apiGetJson).mockReturnValue(new Promise((_, reject) => { rejectRead = reject; }));
    const { unmount } = renderWithRouter(<FirstRunScreen />);
    expect(screen.getByText("Preparing Meridian…")).toBeVisible();
    const options = vi.mocked(apiGetJson).mock.calls[0][1];
    expect(options?.signal).toBeInstanceOf(AbortSignal);
    expect(options?.signal?.aborted).toBe(false);

    unmount();

    expect(options?.signal?.aborted).toBe(true);
    await act(async () => rejectRead(new DOMException("Navigation cancelled", "AbortError")));
  });

  it("does not read status again when the parent supplied it", () => {
    const status: FirstRunStatus = {
      isComplete: false, goal: null, starterKitId: null, dataChoice: null,
      workspace: { id: "test", name: "Test", isSample: false, badge: "LOCAL", safetyMessage: "Local", samplePackVersion: "" },
      starterKits: [], outcomes: [], recommendedActions: [], sampleWorkspace: null
    };
    renderWithRouter(<FirstRunScreen initialStatus={status} />);
    expect(screen.getByRole("heading", { name: "Welcome to Meridian" })).toBeVisible();
    expect(apiGetJson).not.toHaveBeenCalled();
  });
});
