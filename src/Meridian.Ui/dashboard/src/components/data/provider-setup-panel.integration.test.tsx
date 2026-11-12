// The panel with the real `useProviderSetup` hook over a stubbed API client: the inline priority
// cell, the hook's re-read after a write, and the edit drawer working against one stored value.
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ProviderSetupPanel } from "@/components/data/provider-setup-panel";
import { ApiError } from "@/lib/api-errors";
import type { ProviderModuleStatus, UpsertProviderModuleRequest } from "@/types/provider-setup";

const api = vi.hoisted(() => ({
  getProviderModules: vi.fn(),
  getProviderModuleCatalogue: vi.fn(),
  upsertProviderModule: vi.fn(),
  updateProviderModule: vi.fn(),
  deleteProviderModule: vi.fn(),
  setProviderModuleEnabled: vi.fn(),
  testProviderModule: vi.fn(),
  restartProviderHost: vi.fn()
}));

vi.mock("@/lib/api", () => api);

const seed: ProviderModuleStatus = {
  moduleId: "polygon",
  displayName: "Polygon",
  enabled: false,
  priority: 100,
  isConfigured: true,
  isRunning: false,
  credentialStatus: [],
  capabilityNames: ["Backfill"]
};

function priorityCell(name: RegExp): HTMLElement {
  const root = screen.getByRole("button", { name }).closest<HTMLElement>(".mds-opt");
  if (!root) {
    throw new Error("priority cell not found");
  }
  return root;
}

async function typePriority(user: ReturnType<typeof userEvent.setup>, draft: string) {
  await user.click(screen.getByRole("button", { name: /Edit Polygon priority/ }));
  const input = screen.getByRole("spinbutton");
  await user.clear(input);
  await user.type(input, `${draft}{Enter}`);
}

describe("ProviderSetupPanel with the real provider setup hook", () => {
  let stored: ProviderModuleStatus;

  beforeEach(() => {
    vi.clearAllMocks();
    stored = { ...seed };
    api.getProviderModules.mockImplementation(async () => [{ ...stored }]);
    api.getProviderModuleCatalogue.mockResolvedValue([]);
    // Mirrors the server record: an omitted `enabled` defaults to true.
    api.updateProviderModule.mockImplementation(async (_moduleId: string, request: UpsertProviderModuleRequest) => {
      stored = { ...stored, enabled: request.enabled ?? true, priority: request.priority ?? 100 };
      return { success: true };
    });
  });

  it("confirms an inline priority against the re-read value, keeps a disabled module disabled, and reverts it with a second write", async () => {
    const user = userEvent.setup();
    render(<ProviderSetupPanel />);
    await screen.findByRole("button", { name: "Edit Polygon priority: 100" });

    await typePriority(user, "50");

    await waitFor(() => expect(priorityCell(/Edit Polygon priority: 50, was 100/)).toHaveAttribute("data-status", "confirmed"));
    expect(api.updateProviderModule).toHaveBeenCalledWith("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
    expect(stored).toMatchObject({ enabled: false, priority: 50 });
    expect(screen.getByRole("switch", { name: "Enable provider" })).not.toBeChecked();
    expect(screen.getByText("Restart required")).toBeInTheDocument();
    expect(priorityCell(/Edit Polygon priority: 50/).querySelector(".mds-opt__mark")).toHaveTextContent(
      "ConfirmedSaved to configuration. Not live until the provider host restarts."
    );

    await user.click(screen.getByRole("button", { name: "Revert Polygon priority to 100" }));
    await user.type(screen.getByRole("textbox", { name: "Reason · required" }), "Meant for the other provider{Enter}");

    await waitFor(() => expect(priorityCell(/Edit Polygon priority: 100/)).toHaveAttribute("data-status", "reverted"));
    expect(api.updateProviderModule).toHaveBeenCalledTimes(2);
    expect(api.updateProviderModule).toHaveBeenLastCalledWith("polygon", { moduleId: "polygon", enabled: false, priority: 100 });
    expect(stored).toMatchObject({ enabled: false, priority: 100 });
    const note = within(priorityCell(/Edit Polygon priority: 100/)).getByRole("status");
    expect(note).toHaveTextContent("Meant for the other provider");
    expect(note).toHaveTextContent("Your reason is held in this browser session only");
  });

  it("lets the edit drawer supersede a confirmed inline priority instead of masking the drawer's value", async () => {
    const user = userEvent.setup();
    render(<ProviderSetupPanel />);
    await screen.findByRole("button", { name: "Edit Polygon priority: 100" });
    await typePriority(user, "50");
    await screen.findByRole("button", { name: "Revert Polygon priority to 100" });

    await user.click(screen.getByRole("button", { name: /edit provider/i }));
    const drawerPriority = await screen.findByLabelText("Priority");
    expect(drawerPriority).toHaveValue(50);
    // The drawer's field falls back to the stored value when it is emptied, so set it in one change.
    fireEvent.change(drawerPriority, { target: { value: "70" } });
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(priorityCell(/Edit Polygon priority: 70/)).toHaveAttribute("data-status", "idle"));
    expect(stored.priority).toBe(70);
    expect(screen.queryByRole("button", { name: /Revert Polygon priority/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("group", { name: /Edit ledger/ })).not.toBeInTheDocument();
  });

  it("shows the reason from the server's 400 response and leaves the stored priority alone", async () => {
    const user = userEvent.setup();
    api.updateProviderModule.mockRejectedValue(new ApiError({
      path: "/api/providers/modules/polygon",
      status: 400,
      detail: "Failed to read or save credentials; the configuration change was rolled back."
    }));
    render(<ProviderSetupPanel />);
    await screen.findByRole("button", { name: "Edit Polygon priority: 100" });

    await typePriority(user, "50");

    const note = await screen.findByRole("status");
    expect(note).toHaveTextContent("50 → back to 100");
    expect(note).toHaveTextContent("Failed to read or save credentials; the configuration change was rolled back.");
    expect(priorityCell(/Edit Polygon priority: 100/)).toHaveAttribute("data-status", "rejected");
    expect(stored.priority).toBe(100);
    expect(screen.queryByText("Restart required")).not.toBeInTheDocument();
  });
});

// Flows that cross the inline priority cell and the other ways the stored priority changes: the
// edit drawer, enable/disable, remove, and a server that goes quiet. Each one was a way for the
// screen to show, or write back, a priority the configuration no longer held.
describe("ProviderSetupPanel inline priority beside the other write paths", () => {
  const alpaca: ProviderModuleStatus = { ...seed, moduleId: "alpaca", displayName: "Alpaca", enabled: true };
  let store: ProviderModuleStatus[];
  const storedPriority = (moduleId: string) => store.find((module) => module.moduleId === moduleId)?.priority;
  const applyUpdate = (moduleId: string, request: UpsertProviderModuleRequest) => {
    store = store.map((module) =>
      module.moduleId === moduleId
        ? { ...module, enabled: request.enabled ?? true, priority: request.priority ?? 100 }
        : module);
  };

  beforeEach(() => {
    vi.resetAllMocks();
    store = [{ ...seed }];
    api.getProviderModules.mockImplementation(async () => store.map((module) => ({ ...module })));
    api.getProviderModuleCatalogue.mockResolvedValue([]);
    api.updateProviderModule.mockImplementation(async (moduleId: string, request: UpsertProviderModuleRequest) => {
      applyUpdate(moduleId, request);
      return { success: true };
    });
    api.deleteProviderModule.mockImplementation(async (moduleId: string) => {
      store = store.filter((module) => module.moduleId !== moduleId);
      return { success: true };
    });
  });

  async function typeModulePriority(user: ReturnType<typeof userEvent.setup>, module: string, draft: string) {
    await user.click(screen.getByRole("button", { name: new RegExp(`Edit ${module} priority`) }));
    const input = screen.getByRole("spinbutton");
    await user.clear(input);
    await user.type(input, `${draft}{Enter}`);
  }

  async function saveDrawerPriority(user: ReturnType<typeof userEvent.setup>, priority: string) {
    await user.click(screen.getByRole("button", { name: /edit provider/i }));
    fireEvent.change(await screen.findByLabelText("Priority"), { target: { value: priority } });
    await user.click(screen.getByRole("button", { name: "Save changes" }));
    await waitFor(() => expect(screen.queryByText("Edit Polygon")).not.toBeInTheDocument());
  }

  async function mount() {
    const user = userEvent.setup();
    render(<ProviderSetupPanel />);
    await screen.findByRole("button", { name: "Edit Polygon priority: 100" });
    return user;
  }

  it("never writes an older prior over a drawer save: edit, edit, revert the second, drawer save, batch", async () => {
    const user = await mount();
    await typeModulePriority(user, "Polygon", "50");
    await screen.findByRole("button", { name: "Revert Polygon priority to 100" });
    await typeModulePriority(user, "Polygon", "70");
    await user.click(await screen.findByRole("button", { name: "Revert Polygon priority to 50" }));
    await user.type(screen.getByRole("textbox", { name: "Reason · required" }), "Second one was wrong{Enter}");
    await waitFor(() => expect(priorityCell(/Edit Polygon priority: 50/)).toHaveAttribute("data-status", "reverted"));
    expect(storedPriority("polygon")).toBe(50);
    const bar = screen.getByRole("group", { name: "Edit ledger · priority edits" });
    expect(bar).toHaveTextContent("1 confirmed");

    // The Reverted note is still on screen when the drawer saves 80.
    await saveDrawerPriority(user, "80");

    await waitFor(() => expect(bar).not.toHaveTextContent("confirmed"));
    expect(bar).toHaveTextContent("1 priority edits");
    expect(bar).toHaveTextContent("1 reverted");
    expect(within(bar).getByRole("button", { name: "Revert row…" })).toBeDisabled();
    expect(priorityCell(/Edit Polygon priority: 80/)).toHaveAttribute("data-status", "reverted");
    expect(within(priorityCell(/Edit Polygon priority: 80/)).getByRole("status")).toHaveTextContent("Second one was wrong");
    // Three inline writes and the drawer's. Nothing put 100 back.
    expect(api.updateProviderModule).toHaveBeenCalledTimes(4);
    expect(storedPriority("polygon")).toBe(80);
  });

  it("shows the stored priority, not an older confirmed edit, after the drawer returns it to the original", async () => {
    const user = await mount();
    await typeModulePriority(user, "Polygon", "50");
    await screen.findByRole("button", { name: "Revert Polygon priority to 100" });
    await typeModulePriority(user, "Polygon", "70");
    await screen.findByRole("button", { name: "Revert Polygon priority to 50" });

    await saveDrawerPriority(user, "100");

    await waitFor(() => expect(priorityCell(/^Edit Polygon priority: 100$/)).toHaveAttribute("data-status", "idle"));
    expect(storedPriority("polygon")).toBe(100);
    expect(screen.queryByRole("button", { name: /Revert/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("group", { name: /Edit ledger/ })).not.toBeInTheDocument();
  });

  it("does not carry a confirmed edit over to a provider that was removed and added again", async () => {
    const user = await mount();
    await typeModulePriority(user, "Polygon", "50");
    await screen.findByRole("button", { name: "Revert Polygon priority to 100" });

    await user.click(screen.getByRole("button", { name: /remove provider/i }));
    await screen.findByText("No provider modules configured");
    // Configured again elsewhere under the same id, at the default priority.
    store = [{ ...seed }];
    await user.click(screen.getByRole("button", { name: "Refresh" }));

    const cell = await screen.findByRole("button", { name: "Edit Polygon priority: 100" });
    expect(cell.closest(".mds-opt")).toHaveAttribute("data-status", "idle");
    expect(screen.queryByText("Confirmed")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Revert/ })).not.toBeInTheDocument();
  });

  it("does not bring back a rejection that was retried successfully when the drawer later changes the priority", async () => {
    api.updateProviderModule.mockRejectedValueOnce(new ApiError({
      path: "/api/providers/modules/polygon",
      status: 429,
      detail: "Too many requests."
    }));
    const user = await mount();
    await typeModulePriority(user, "Polygon", "50");
    await waitFor(() => expect(priorityCell(/Edit Polygon priority: 100/)).toHaveAttribute("data-status", "rejected"));
    await typeModulePriority(user, "Polygon", "50");
    await screen.findByRole("button", { name: "Revert Polygon priority to 100" });

    await saveDrawerPriority(user, "80");

    await waitFor(() => expect(priorityCell(/^Edit Polygon priority: 80$/)).toHaveAttribute("data-status", "idle"));
    expect(screen.queryByText(/back to 100/)).not.toBeInTheDocument();
    expect(screen.queryByRole("group", { name: /Edit ledger/ })).not.toBeInTheDocument();
  });

  it("confirms a write whose answer was lost once the read-back finds it stored, and asks for the restart", async () => {
    api.updateProviderModule.mockImplementationOnce(async (moduleId: string, request: UpsertProviderModuleRequest) => {
      applyUpdate(moduleId, request);
      throw new ApiError({ path: "/api/providers/modules/polygon", status: 502, detail: "Bad gateway" });
    });
    const user = await mount();

    await typeModulePriority(user, "Polygon", "60");

    await waitFor(() => expect(priorityCell(/Edit Polygon priority: 60, was 100/)).toHaveAttribute("data-status", "confirmed"));
    expect(storedPriority("polygon")).toBe(60);
    expect(screen.getByText("Restart required")).toBeInTheDocument();
    expect(screen.queryByText("Rejected")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Revert Polygon priority to 100" })).toBeInTheDocument();
  });

  it("reads Unlanded with what the read-back showed when an unanswered write was not stored", async () => {
    api.updateProviderModule.mockRejectedValueOnce(
      new ApiError({ path: "/api/providers/modules/polygon", status: 502, detail: "Bad gateway" })
    );
    const user = await mount();

    await typeModulePriority(user, "Polygon", "60");

    const note = await screen.findByRole("status");
    expect(priorityCell(/Edit Polygon priority: 100/)).toHaveAttribute("data-status", "unlanded");
    expect(note).toHaveTextContent("60 sent · no answer");
    expect(note).toHaveTextContent(
      "No answer came back from the server. The stored priority was read again and is still 100, so this change has not been saved. (Bad gateway)"
    );
    expect(note).not.toHaveTextContent("back to");
    expect(screen.queryByText("Rejected")).not.toBeInTheDocument();
    expect(screen.queryByText("Restart required")).not.toBeInTheDocument();
    expect(storedPriority("polygon")).toBe(100);
  });

  it("does not contradict the stored value when neither the write nor the read-back answered, and settles on Refresh", async () => {
    api.updateProviderModule.mockImplementationOnce(async (moduleId: string, request: UpsertProviderModuleRequest) => {
      applyUpdate(moduleId, request);
      api.getProviderModules.mockRejectedValueOnce(new TypeError("Failed to fetch"));
      throw new TypeError("Failed to fetch");
    });
    const user = await mount();

    await typeModulePriority(user, "Polygon", "60");

    const note = await screen.findByRole("status");
    expect(priorityCell(/Edit Polygon priority: 100/)).toHaveAttribute("data-status", "unlanded");
    expect(note).toHaveTextContent("it is not known whether this priority was saved");
    expect(note).not.toHaveTextContent("back to");
    expect(screen.queryByText("Restart required")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Refresh" }));

    // The read shows the write did land: the cell says so, and the restart banner appears.
    await waitFor(() => expect(priorityCell(/Edit Polygon priority: 60, was 100/)).toHaveAttribute("data-status", "confirmed"));
    expect(screen.queryByText("Unlanded")).not.toBeInTheDocument();
    expect(screen.getByText("Restart required")).toBeInTheDocument();
  });

  it("does not let the drawer open on the old priority while a typed priority is being written", async () => {
    let answer: () => void = () => {};
    api.updateProviderModule.mockImplementationOnce((moduleId: string, request: UpsertProviderModuleRequest) =>
      new Promise((resolve) => {
        answer = () => {
          applyUpdate(moduleId, request);
          resolve({ success: true });
        };
      }));
    const user = await mount();

    // Typed, then straight to Edit: the blur sends the write and the same click reaches Edit.
    await user.click(screen.getByRole("button", { name: /Edit Polygon priority/ }));
    const input = screen.getByRole("spinbutton");
    await user.clear(input);
    await user.type(input, "50");
    await user.click(screen.getByRole("button", { name: /edit provider/i }));

    expect(api.updateProviderModule).toHaveBeenCalledWith("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
    expect(screen.getByRole("button", { name: /edit provider/i })).toBeDisabled();
    expect(screen.queryByLabelText("Priority")).not.toBeInTheDocument();

    await act(async () => {
      answer();
    });
    await screen.findByRole("button", { name: "Revert Polygon priority to 100" });
    await user.click(screen.getByRole("button", { name: /edit provider/i }));
    expect(await screen.findByLabelText("Priority")).toHaveValue(50);
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(api.updateProviderModule).toHaveBeenCalledTimes(2));
    expect(api.updateProviderModule).toHaveBeenLastCalledWith("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
    expect(storedPriority("polygon")).toBe(50);
  });

  it("holds the toggle, Edit and Remove while an older edit is reverting beneath a newer Reverted note", async () => {
    const user = await mount();
    await typeModulePriority(user, "Polygon", "50");
    await screen.findByRole("button", { name: "Revert Polygon priority to 100" });
    await typeModulePriority(user, "Polygon", "70");
    await user.click(await screen.findByRole("button", { name: "Revert Polygon priority to 50" }));
    await user.type(screen.getByRole("textbox", { name: "Reason · required" }), "Second one was wrong{Enter}");
    await waitFor(() => expect(priorityCell(/Edit Polygon priority: 50/)).toHaveAttribute("data-status", "reverted"));
    let answer: () => void = () => {};
    api.updateProviderModule.mockImplementationOnce((moduleId: string, request: UpsertProviderModuleRequest) =>
      new Promise((resolve) => {
        answer = () => {
          applyUpdate(moduleId, request);
          resolve({ success: true });
        };
      }));

    // The newest entry is the Reverted note; the write in flight belongs to the entry beneath it.
    const bar = screen.getByRole("group", { name: "Edit ledger · priority edits" });
    await user.click(within(bar).getByRole("button", { name: "Revert 1 confirmed…" }));
    await user.type(within(bar).getByRole("textbox", { name: "Reason · required" }), "And the first{Enter}");

    await waitFor(() => expect(api.updateProviderModule).toHaveBeenCalledTimes(4));
    expect(screen.getByRole("switch", { name: "Enable provider" })).toBeDisabled();
    expect(screen.getByRole("button", { name: /edit provider/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /remove provider/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Edit Polygon priority/ })).toHaveAttribute("aria-disabled", "true");

    await act(async () => {
      answer();
    });
    await waitFor(() => expect(screen.getByRole("switch", { name: "Enable provider" })).toBeEnabled());
    expect(screen.getByRole("button", { name: /edit provider/i })).toBeEnabled();
    expect(storedPriority("polygon")).toBe(100);
    expect(bar).toHaveTextContent("2 reverted");
  });

  it("keeps the revert that landed, with its reason, when the next one in the batch is refused", async () => {
    store = [{ ...alpaca }, { ...seed }];
    const user = userEvent.setup();
    render(<ProviderSetupPanel />);
    await screen.findByRole("button", { name: "Edit Alpaca priority: 100" });
    await typeModulePriority(user, "Alpaca", "50");
    await screen.findByRole("button", { name: "Revert Alpaca priority to 100" });
    await typeModulePriority(user, "Polygon", "60");
    await screen.findByRole("button", { name: "Revert Polygon priority to 100" });
    // Polygon's compensating write (newest, so first) is stored; Alpaca's is answered 429.
    api.updateProviderModule
      .mockImplementationOnce(async (moduleId: string, request: UpsertProviderModuleRequest) => {
        applyUpdate(moduleId, request);
        return { success: true };
      })
      .mockRejectedValueOnce(new ApiError({ path: "/api/providers/modules/alpaca", status: 429, detail: "Too many requests." }));

    const bar = screen.getByRole("group", { name: "Edit ledger · priority edits" });
    await user.click(within(bar).getByRole("button", { name: "Revert 2 confirmed…" }));
    await user.type(within(bar).getByRole("textbox", { name: "Reason · required" }), "wrong desk{Enter}");

    const polygonCell = await waitFor(() => {
      const found = priorityCell(/Edit Polygon priority: 100/);
      expect(found).toHaveAttribute("data-status", "reverted");
      return found;
    });
    expect([storedPriority("alpaca"), storedPriority("polygon")]).toEqual([50, 100]);
    const note = within(polygonCell).getByRole("status");
    expect(note).toHaveTextContent("60 → 100");
    expect(note).toHaveTextContent("wrong desk");
    const alpacaCell = priorityCell(/Edit Alpaca priority: 50/);
    expect(alpacaCell).toHaveAttribute("data-status", "confirmed");
    expect(within(alpacaCell).getByRole("alert")).toHaveTextContent(
      "Revert not applied; the value is still 50. Too many requests."
    );
    expect(bar).toHaveTextContent("2 priority edits");
    expect(bar).toHaveTextContent("1 confirmed");
    expect(bar).toHaveTextContent("1 reverted");
  });
});

