import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi, beforeEach } from "vitest";
import { ProviderSetupPanel } from "@/components/data/provider-setup-panel";
import type { ProviderModuleStatus, ProviderModuleCatalogueEntry } from "@/types/provider-setup";

const mockModule: ProviderModuleStatus = {
  moduleId: "alpaca",
  displayName: "Alpaca",
  enabled: true,
  priority: 100,
  isConfigured: true,
  isRunning: true,
  credentialStatus: [
    { key: "keyId", hasValue: true },
    { key: "secretKey", hasValue: false },
  ],
  capabilityNames: ["Streaming", "Brokerage"],
};

const mockCatalogueEntry: ProviderModuleCatalogueEntry = {
  moduleId: "new-provider",
  displayName: "New Provider",
  capabilityNames: ["Backfill"],
  credentialKeyHints: ["apiKey"],
  requiresExternalConfig: false,
  alreadyConfigured: false,
};

const mockUseProviderSetup = vi.hoisted(() => vi.fn());

vi.mock("@/lib/provider-setup/use-provider-setup", () => ({
  useProviderSetup: mockUseProviderSetup,
}));

const defaultHookReturn = {
  modules: [],
  catalogue: [],
  loading: false,
  error: null,
  pendingRestart: false,
  refresh: vi.fn().mockResolvedValue(undefined),
  addModule: vi.fn().mockResolvedValue(true),
  editModule: vi.fn().mockResolvedValue(true),
  editModuleWithOutcome: vi.fn().mockResolvedValue({ success: true, error: null, refused: false }),
  removeModule: vi.fn().mockResolvedValue(true),
  toggleEnabled: vi.fn().mockResolvedValue(true),
  testModule: vi.fn().mockResolvedValue({ success: true, credentialConfigValid: true, connectionVerified: true }),
  triggerRestart: vi.fn().mockResolvedValue(true),
};

describe("ProviderSetupPanel", () => {
  beforeEach(() => {
    mockUseProviderSetup.mockReturnValue({ ...defaultHookReturn });
  });

  it("shows empty state when no modules are configured", () => {
    render(<ProviderSetupPanel />);
    expect(screen.getByText("No provider modules configured")).toBeInTheDocument();
  });

  it("shows a restart banner when pendingRestart is true", () => {
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      pendingRestart: true,
    });

    render(<ProviderSetupPanel />);
    expect(screen.getByText("Restart required")).toBeInTheDocument();
  });

  it("shows an error banner when error is present", () => {
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      error: "Network error",
    });

    render(<ProviderSetupPanel />);
    expect(screen.getByText("Failed to load provider modules")).toBeInTheDocument();
  });

  it("renders a configured module card", () => {
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      modules: [mockModule],
    });

    render(<ProviderSetupPanel />);
    expect(screen.getByText("Alpaca")).toBeInTheDocument();
    expect(screen.getByText("alpaca")).toBeInTheDocument();
    expect(screen.getByText("Running")).toBeInTheDocument();
  });

  it("renders credential status dots for each credential", () => {
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      modules: [mockModule],
    });

    render(<ProviderSetupPanel />);
    expect(screen.getByText("keyId")).toBeInTheDocument();
    expect(screen.getByText("secretKey")).toBeInTheDocument();
  });

  it("opens the add provider drawer when Add provider button is clicked", async () => {
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      catalogue: [mockCatalogueEntry],
    });

    render(<ProviderSetupPanel />);
    const addButton = screen.getAllByRole("button", { name: /add provider/i })[0];
    await userEvent.click(addButton);

    await waitFor(() => {
      expect(screen.getByRole("dialog")).toBeInTheDocument();
      expect(screen.getByText("Add provider module")).toBeInTheDocument();
    });
  });

  it("calls testModule when the test button is clicked", async () => {
    const testModule = vi.fn().mockResolvedValue({ success: true, credentialConfigValid: true, connectionVerified: true });
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      modules: [mockModule],
      testModule,
    });

    render(<ProviderSetupPanel />);
    const testBtn = screen.getByRole("button", { name: /test connection/i });
    await userEvent.click(testBtn);

    await waitFor(() => {
      expect(testModule).toHaveBeenCalledWith("alpaca");
    });
  });

  it("calls removeModule when the remove button is clicked", async () => {
    const removeModule = vi.fn().mockResolvedValue(true);
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      modules: [mockModule],
      removeModule,
    });

    render(<ProviderSetupPanel />);
    const removeBtn = screen.getByRole("button", { name: /remove provider/i });
    await userEvent.click(removeBtn);

    await waitFor(() => {
      expect(removeModule).toHaveBeenCalledWith("alpaca");
    });
  });

  it("shows connected badge after successful test", async () => {
    mockUseProviderSetup.mockReturnValue({
      ...defaultHookReturn,
      modules: [mockModule],
      testModule: vi.fn().mockResolvedValue({ success: true, credentialConfigValid: true, connectionVerified: true }),
    });

    render(<ProviderSetupPanel />);
    await userEvent.click(screen.getByRole("button", { name: /test connection/i }));

    await waitFor(() => {
      expect(screen.getByText("Connected")).toBeInTheDocument();
    });
  });

  describe("inline priority (OptimisticCell)", () => {
    const disabledModule: ProviderModuleStatus = {
      moduleId: "polygon",
      displayName: "Polygon",
      enabled: false,
      priority: 20,
      isConfigured: true,
      isRunning: false,
      credentialStatus: [],
      capabilityNames: ["Backfill"],
    };
    const sessionOnly =
      "The provider API has no reason or audit field for this write. Your reason is held in this browser session only: " +
      "it is not sent to the server and is gone once you leave or reload this view.";

    function priorityCell(name: RegExp): HTMLElement {
      const root = screen.getByRole("button", { name }).closest<HTMLElement>(".mds-opt");
      if (!root) {
        throw new Error("priority cell not found");
      }
      return root;
    }

    async function typePriority(user: ReturnType<typeof userEvent.setup>, name: RegExp, draft: string) {
      await user.click(screen.getByRole("button", { name }));
      const input = screen.getByRole("spinbutton");
      await user.clear(input);
      await user.type(input, `${draft}{Enter}`);
    }

    it("always shows the priority, including the default of 100", () => {
      mockUseProviderSetup.mockReturnValue({ ...defaultHookReturn, modules: [mockModule, disabledModule] });

      render(<ProviderSetupPanel />);

      expect(screen.getAllByText("Priority")).toHaveLength(2);
      expect(screen.getByRole("button", { name: "Edit Alpaca priority: 100" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Edit Polygon priority: 20" })).toBeInTheDocument();
      // No edit has been made, so there is no ledger bar and no revert control.
      expect(screen.queryByRole("group", { name: /Edit ledger/ })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /Revert/ })).not.toBeInTheDocument();
    });

    it("writes through the existing edit mutation with the module's current enabled flag, and reads Provisional until it lands", async () => {
      const user = userEvent.setup();
      let land: (outcome: { success: boolean; error: string | null; refused: boolean }) => void = () => {};
      const editModuleWithOutcome = vi.fn(() => new Promise((resolve) => { land = resolve; }));
      const editModule = vi.fn().mockResolvedValue(true);
      const hook = { ...defaultHookReturn, modules: [disabledModule], editModule, editModuleWithOutcome };
      mockUseProviderSetup.mockReturnValue(hook);
      const { rerender } = render(<ProviderSetupPanel />);

      await typePriority(user, /Edit Polygon priority/, "5");

      // A disabled module stays disabled: the request carries enabled: false beside the priority.
      expect(editModuleWithOutcome).toHaveBeenCalledTimes(1);
      expect(editModuleWithOutcome).toHaveBeenCalledWith("polygon", { moduleId: "polygon", enabled: false, priority: 5 });
      expect(editModule).not.toHaveBeenCalled();
      const cell = priorityCell(/Edit Polygon priority: 5, was 20/);
      expect(cell).toHaveAttribute("data-status", "pending");
      expect(cell.querySelector(".mds-opt__mark")).toHaveTextContent("Provisional");
      // The enable toggle and Remove wait for the write, which resends `enabled`.
      expect(screen.getByRole("switch", { name: "Enable provider" })).toBeDisabled();
      expect(screen.getByRole("button", { name: /remove provider/i })).toBeDisabled();

      // The hook re-reads the modules and flags the restart before the write resolves.
      mockUseProviderSetup.mockReturnValue({ ...hook, modules: [{ ...disabledModule, priority: 5 }], pendingRestart: true });
      rerender(<ProviderSetupPanel />);
      await act(async () => {
        land({ success: true, error: null, refused: false });
      });

      expect(cell).toHaveAttribute("data-status", "confirmed");
      // Confirmed is the configuration write, not the running provider host.
      expect(cell.querySelector(".mds-opt__mark")).toHaveTextContent(
        "ConfirmedSaved to configuration. Not live until the provider host restarts."
      );
      expect(screen.getByText("Restart required")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Revert Polygon priority to 20" })).toBeInTheDocument();
      expect(screen.getByRole("switch", { name: "Enable provider" })).toBeEnabled();
      expect(screen.getByRole("group", { name: "Edit ledger · priority edits" })).toHaveTextContent("1 confirmed");
    });

    it("restores the prior priority and shows the server's error string when the edit is refused", async () => {
      const user = userEvent.setup();
      const editModuleWithOutcome = vi.fn().mockResolvedValue({
        success: false,
        error: "Failed to read or save credentials; the configuration change was rolled back.",
        refused: true,
      });
      mockUseProviderSetup.mockReturnValue({ ...defaultHookReturn, modules: [mockModule], editModuleWithOutcome });
      render(<ProviderSetupPanel />);

      await typePriority(user, /Edit Alpaca priority/, "50");

      const cell = await waitFor(() => {
        const found = priorityCell(/Edit Alpaca priority: 100/);
        expect(found).toHaveAttribute("data-status", "rejected");
        return found;
      });
      const note = within(cell).getByRole("status");
      expect(note).toHaveTextContent("Rejected");
      expect(note).toHaveTextContent("50 → back to 100");
      expect(note).toHaveTextContent("Failed to read or save credentials; the configuration change was rolled back.");
      expect(screen.queryByRole("button", { name: /Revert Alpaca priority/ })).not.toBeInTheDocument();

      await user.click(within(note).getByRole("button", { name: "Dismiss" }));
      expect(priorityCell(/Edit Alpaca priority: 100/)).toHaveAttribute("data-status", "idle");
    });

    it("does not present an unanswered request as a server refusal", async () => {
      const user = userEvent.setup();
      const editModuleWithOutcome = vi.fn().mockResolvedValue({ success: false, error: "Failed to fetch", refused: false });
      mockUseProviderSetup.mockReturnValue({ ...defaultHookReturn, modules: [mockModule], editModuleWithOutcome });
      render(<ProviderSetupPanel />);

      await typePriority(user, /Edit Alpaca priority/, "50");

      const note = await screen.findByRole("status");
      expect(note).toHaveTextContent(
        "No confirmation came back from the server, so it is not known whether this priority was saved. " +
        "Use Refresh to read the stored value before trying again. (Failed to fetch)"
      );
      // No answer is "Unlanded": not a rejection, and no claim that the value went back.
      const cell = priorityCell(/Edit Alpaca priority: 100/);
      expect(cell).toHaveAttribute("data-status", "unlanded");
      expect(cell.querySelector(".mds-opt__mark")).toHaveTextContent("Unlanded");
      expect(note).toHaveTextContent("50 sent · no answer");
      expect(cell).not.toHaveTextContent("Rejected");
      expect(cell).not.toHaveTextContent("back to");
      expect(screen.getByRole("group", { name: "Edit ledger · priority edits" })).toHaveTextContent("1 unlanded");
    });

    it("says what the read-back showed when an unanswered write was not stored", async () => {
      const user = userEvent.setup();
      const editModuleWithOutcome = vi.fn().mockResolvedValue({
        success: false,
        error: "Bad gateway",
        refused: false,
        reread: "not-saved",
      });
      mockUseProviderSetup.mockReturnValue({ ...defaultHookReturn, modules: [mockModule], editModuleWithOutcome });
      render(<ProviderSetupPanel />);

      await typePriority(user, /Edit Alpaca priority/, "50");

      const note = await screen.findByRole("status");
      expect(note).toHaveTextContent(
        "No answer came back from the server. The stored priority was read again and is still 100, " +
        "so this change has not been saved. (Bad gateway)"
      );
      expect(priorityCell(/Edit Alpaca priority: 100/)).toHaveAttribute("data-status", "unlanded");
    });

    it.each(["0", "1000", "2.5"])("validates the priority like the edit drawer and does not write %s", async (draft) => {
      const user = userEvent.setup();
      const editModuleWithOutcome = vi.fn();
      mockUseProviderSetup.mockReturnValue({ ...defaultHookReturn, modules: [mockModule], editModuleWithOutcome });
      render(<ProviderSetupPanel />);

      await typePriority(user, /Edit Alpaca priority/, draft);

      expect(screen.getByRole("alert")).toHaveTextContent("Enter a whole number from 1 to 999.");
      expect(editModuleWithOutcome).not.toHaveBeenCalled();
      await user.keyboard("{Escape}");
      expect(priorityCell(/Edit Alpaca priority: 100/)).toHaveAttribute("data-status", "idle");
    });

    it("reverts with a second write of the prior value, and says the reason stays in this browser session", async () => {
      const user = userEvent.setup();
      const editModuleWithOutcome = vi.fn().mockResolvedValue({ success: true, error: null, refused: false });
      const hook = { ...defaultHookReturn, modules: [disabledModule], editModuleWithOutcome };
      mockUseProviderSetup.mockReturnValue(hook);
      const { rerender } = render(<ProviderSetupPanel />);
      await typePriority(user, /Edit Polygon priority/, "5");
      const opener = await screen.findByRole("button", { name: "Revert Polygon priority to 20" });
      mockUseProviderSetup.mockReturnValue({ ...hook, modules: [{ ...disabledModule, priority: 5 }], pendingRestart: true });
      rerender(<ProviderSetupPanel />);

      await user.click(opener);
      const dialog = screen.getByRole("dialog", { name: "Revert this cell" });
      expect(dialog).toHaveTextContent("Puts 5 back to 20. The revert is sent as its own write, not as an erasure of the first.");
      const reason = within(dialog).getByRole("textbox", { name: "Reason · required" });
      expect(reason).toHaveFocus();
      expect(reason).toHaveAccessibleDescription(sessionOnly);
      expect(within(dialog).getByRole("button", { name: "Revert cell" })).toBeDisabled();

      await user.keyboard("{Escape}");
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Revert Polygon priority to 20" })).toHaveFocus();

      await user.click(screen.getByRole("button", { name: "Revert Polygon priority to 20" }));
      await user.type(screen.getByRole("textbox", { name: "Reason · required" }), "Meant for the other provider{Enter}");

      await waitFor(() => expect(editModuleWithOutcome).toHaveBeenCalledTimes(2));
      // The compensating write is the same mutation with the prior value. The reason is not in it.
      expect(editModuleWithOutcome).toHaveBeenLastCalledWith("polygon", { moduleId: "polygon", enabled: false, priority: 20 });
      mockUseProviderSetup.mockReturnValue({ ...hook, modules: [disabledModule], pendingRestart: true });
      rerender(<ProviderSetupPanel />);

      const cell = priorityCell(/Edit Polygon priority: 20/);
      await waitFor(() => expect(cell).toHaveAttribute("data-status", "reverted"));
      const note = within(cell).getByRole("status");
      expect(note).toHaveTextContent("Reverted");
      expect(note).toHaveTextContent("5 → 20");
      expect(note).toHaveTextContent("Meant for the other provider");
      expect(note).toHaveTextContent(sessionOnly);
      expect(screen.getByRole("group", { name: "Edit ledger · priority edits" })).toHaveTextContent("1 reverted");
    });

    it("keeps the edit Confirmed and shows why when the revert write is refused", async () => {
      const user = userEvent.setup();
      const editModuleWithOutcome = vi.fn()
        .mockResolvedValueOnce({ success: true, error: null, refused: false })
        .mockResolvedValueOnce({ success: false, error: "Configuration store is read-only", refused: true });
      const hook = { ...defaultHookReturn, modules: [mockModule], editModuleWithOutcome };
      mockUseProviderSetup.mockReturnValue(hook);
      const { rerender } = render(<ProviderSetupPanel />);
      await typePriority(user, /Edit Alpaca priority/, "50");
      await screen.findByRole("button", { name: "Revert Alpaca priority to 100" });
      mockUseProviderSetup.mockReturnValue({ ...hook, modules: [{ ...mockModule, priority: 50 }] });
      rerender(<ProviderSetupPanel />);

      await user.click(screen.getByRole("button", { name: "Revert Alpaca priority to 100" }));
      await user.type(screen.getByRole("textbox", { name: "Reason · required" }), "Wrong provider{Enter}");

      expect(await screen.findByRole("alert")).toHaveTextContent(
        "Revert not applied; the value is still 50. Configuration store is read-only"
      );
      expect(priorityCell(/Edit Alpaca priority: 50/)).toHaveAttribute("data-status", "confirmed");
    });

    it("reverts every confirmed priority from the row bar, newest first, under one reason", async () => {
      const user = userEvent.setup();
      const editModuleWithOutcome = vi.fn().mockResolvedValue({ success: true, error: null, refused: false });
      const hook = { ...defaultHookReturn, modules: [mockModule, disabledModule], editModuleWithOutcome };
      mockUseProviderSetup.mockReturnValue(hook);
      const { rerender } = render(<ProviderSetupPanel />);
      await typePriority(user, /Edit Alpaca priority/, "50");
      await screen.findByRole("button", { name: "Revert Alpaca priority to 100" });
      await typePriority(user, /Edit Polygon priority/, "5");
      await screen.findByRole("button", { name: "Revert Polygon priority to 20" });
      mockUseProviderSetup.mockReturnValue({
        ...hook,
        modules: [{ ...mockModule, priority: 50 }, { ...disabledModule, priority: 5 }],
      });
      rerender(<ProviderSetupPanel />);

      const bar = screen.getByRole("group", { name: "Edit ledger · priority edits" });
      expect(bar).toHaveTextContent("2 priority edits");
      await user.click(within(bar).getByRole("button", { name: "Revert 2 confirmed…" }));
      const dialog = within(bar).getByRole("dialog", { name: "Revert 2 confirmed edits" });
      expect(within(dialog).getByRole("textbox", { name: "Reason · required" })).toHaveAccessibleDescription(sessionOnly);
      await user.type(within(dialog).getByRole("textbox", { name: "Reason · required" }), "Routing review{Enter}");

      await waitFor(() => expect(editModuleWithOutcome).toHaveBeenCalledTimes(4));
      expect(editModuleWithOutcome.mock.calls.slice(2)).toEqual([
        ["polygon", { moduleId: "polygon", enabled: false, priority: 20 }],
        ["alpaca", { moduleId: "alpaca", enabled: true, priority: 100 }],
      ]);
      await waitFor(() => expect(bar).toHaveTextContent("2 reverted"));
    });

    it("keeps the revert that was written when a batch fails part of the way through", async () => {
      const user = userEvent.setup();
      const editModuleWithOutcome = vi.fn()
        .mockResolvedValueOnce({ success: true, error: null, refused: false })
        .mockResolvedValueOnce({ success: true, error: null, refused: false })
        // Polygon's compensating write (newest, so first) lands; Alpaca's is refused.
        .mockResolvedValueOnce({ success: true, error: null, refused: false })
        .mockResolvedValueOnce({ success: false, error: "Too many requests", refused: true });
      const hook = { ...defaultHookReturn, modules: [mockModule, disabledModule], editModuleWithOutcome };
      mockUseProviderSetup.mockReturnValue(hook);
      const { rerender } = render(<ProviderSetupPanel />);
      await typePriority(user, /Edit Alpaca priority/, "50");
      await screen.findByRole("button", { name: "Revert Alpaca priority to 100" });
      await typePriority(user, /Edit Polygon priority/, "5");
      await screen.findByRole("button", { name: "Revert Polygon priority to 20" });
      mockUseProviderSetup.mockReturnValue({
        ...hook,
        modules: [{ ...mockModule, priority: 50 }, { ...disabledModule, priority: 5 }],
      });
      rerender(<ProviderSetupPanel />);

      const bar = screen.getByRole("group", { name: "Edit ledger · priority edits" });
      await user.click(within(bar).getByRole("button", { name: "Revert 2 confirmed…" }));
      await user.type(within(bar).getByRole("textbox", { name: "Reason · required" }), "Wrong desk{Enter}");
      await waitFor(() => expect(editModuleWithOutcome).toHaveBeenCalledTimes(4));
      mockUseProviderSetup.mockReturnValue({
        ...hook,
        modules: [{ ...mockModule, priority: 50 }, disabledModule],
      });
      rerender(<ProviderSetupPanel />);

      // The revert that landed is a recorded write with its reason, not an erased entry.
      const polygon = priorityCell(/Edit Polygon priority: 20/);
      await waitFor(() => expect(polygon).toHaveAttribute("data-status", "reverted"));
      expect(within(polygon).getByRole("status")).toHaveTextContent("5 → 20");
      expect(within(polygon).getByRole("status")).toHaveTextContent("Wrong desk");
      const alpaca = priorityCell(/Edit Alpaca priority: 50/);
      expect(alpaca).toHaveAttribute("data-status", "confirmed");
      expect(within(alpaca).getByRole("alert")).toHaveTextContent(
        "Revert not applied; the value is still 50. Too many requests"
      );
      expect(bar).toHaveTextContent("1 confirmed");
      expect(bar).toHaveTextContent("1 reverted");
    });

    it("does not attempt the rest of a batch after a revert fails, and says so on each one", async () => {
      const user = userEvent.setup();
      const editModuleWithOutcome = vi.fn()
        .mockResolvedValueOnce({ success: true, error: null, refused: false })
        .mockResolvedValueOnce({ success: true, error: null, refused: false })
        .mockResolvedValueOnce({ success: false, error: "Too many requests", refused: true });
      const hook = { ...defaultHookReturn, modules: [mockModule, disabledModule], editModuleWithOutcome };
      mockUseProviderSetup.mockReturnValue(hook);
      const { rerender } = render(<ProviderSetupPanel />);
      await typePriority(user, /Edit Alpaca priority/, "50");
      await screen.findByRole("button", { name: "Revert Alpaca priority to 100" });
      await typePriority(user, /Edit Polygon priority/, "5");
      await screen.findByRole("button", { name: "Revert Polygon priority to 20" });
      mockUseProviderSetup.mockReturnValue({
        ...hook,
        modules: [{ ...mockModule, priority: 50 }, { ...disabledModule, priority: 5 }],
      });
      rerender(<ProviderSetupPanel />);

      const bar = screen.getByRole("group", { name: "Edit ledger · priority edits" });
      await user.click(within(bar).getByRole("button", { name: "Revert 2 confirmed…" }));
      await user.type(within(bar).getByRole("textbox", { name: "Reason · required" }), "Wrong desk{Enter}");

      await waitFor(() => expect(within(priorityCell(/Edit Polygon priority: 5/)).getByRole("alert")).toHaveTextContent(
        "Revert not applied; the value is still 5. Too many requests"
      ));
      expect(within(priorityCell(/Edit Alpaca priority: 50/)).getByRole("alert")).toHaveTextContent(
        "Revert not applied; the value is still 50. Not attempted: an earlier revert in this batch did not land. Too many requests"
      );
      expect(editModuleWithOutcome).toHaveBeenCalledTimes(3);
      expect(bar).toHaveTextContent("2 confirmed");
    });

    it("refuses to write a prior back when the last read no longer shows the edit's value", async () => {
      const user = userEvent.setup();
      const editModuleWithOutcome = vi.fn().mockResolvedValue({ success: true, error: null, refused: false });
      // The modules were never re-read after the write: the last read still says 100.
      mockUseProviderSetup.mockReturnValue({ ...defaultHookReturn, modules: [mockModule], editModuleWithOutcome });
      render(<ProviderSetupPanel />);
      await typePriority(user, /Edit Alpaca priority/, "50");

      await user.click(await screen.findByRole("button", { name: "Revert Alpaca priority to 100" }));
      await user.type(screen.getByRole("textbox", { name: "Reason · required" }), "Wrong provider{Enter}");

      expect(await screen.findByRole("alert")).toHaveTextContent(
        "Revert not applied; the value is still 50. The last read of this provider shows priority 100, not 50, " +
        "so writing the prior back would overwrite a newer value. Nothing was written. Use Refresh, then revert from what is shown."
      );
      expect(editModuleWithOutcome).toHaveBeenCalledTimes(1);
    });

    it("makes the edit drawer wait for an inline priority write, so it cannot open on the old priority", async () => {
      const user = userEvent.setup();
      let land: (outcome: { success: boolean; error: string | null; refused: boolean }) => void = () => {};
      const editModuleWithOutcome = vi.fn(() => new Promise((resolve) => { land = resolve; }));
      const hook = { ...defaultHookReturn, modules: [mockModule], editModuleWithOutcome };
      mockUseProviderSetup.mockReturnValue(hook);
      const { rerender } = render(<ProviderSetupPanel />);

      // Type a priority and go straight to Edit without pressing Enter: the blur starts the write.
      await user.click(screen.getByRole("button", { name: /Edit Alpaca priority/ }));
      const input = screen.getByRole("spinbutton");
      await user.clear(input);
      await user.type(input, "50");
      await user.click(screen.getByRole("button", { name: /edit provider/i }));

      expect(editModuleWithOutcome).toHaveBeenCalledWith("alpaca", { moduleId: "alpaca", enabled: true, priority: 50 });
      const edit = screen.getByRole("button", { name: /edit provider/i });
      expect(edit).toBeDisabled();
      expect(edit).toHaveAttribute("title", "Wait for the priority change to settle.");
      expect(screen.queryByText("Edit Alpaca")).not.toBeInTheDocument();

      mockUseProviderSetup.mockReturnValue({ ...hook, modules: [{ ...mockModule, priority: 50 }] });
      rerender(<ProviderSetupPanel />);
      await act(async () => {
        land({ success: true, error: null, refused: false });
      });

      // Once the write has settled the drawer opens on the priority that was just saved.
      await user.click(screen.getByRole("button", { name: /edit provider/i }));
      expect(await screen.findByLabelText("Priority")).toHaveValue(50);
    });

    it.each([
      ["enabled or disabled", "toggleEnabled", "This provider is being enabled or disabled. Wait for that to finish.", "A provider is being enabled or disabled. Wait for that to finish."],
      ["removed", "removeModule", "This provider is being removed. Wait for that to finish.", "A provider is being removed. Wait for that to finish."],
    ] as const)("does not send a priority revert while the provider is being %s", async (_name, mutation, cellReason, barReason) => {
      const user = userEvent.setup();
      const editModuleWithOutcome = vi.fn().mockResolvedValue({ success: true, error: null, refused: false });
      let finish: (ok: boolean) => void = () => {};
      const held = vi.fn(() => new Promise<boolean>((resolve) => { finish = resolve; }));
      const hook = { ...defaultHookReturn, modules: [mockModule], editModuleWithOutcome, [mutation]: held };
      mockUseProviderSetup.mockReturnValue(hook);
      const { rerender } = render(<ProviderSetupPanel />);
      await typePriority(user, /Edit Alpaca priority/, "50");
      await screen.findByRole("button", { name: "Revert Alpaca priority to 100" });
      mockUseProviderSetup.mockReturnValue({ ...hook, modules: [{ ...mockModule, priority: 50 }] });
      rerender(<ProviderSetupPanel />);

      // The reason form is already open, with a reason typed, when the other write starts.
      await user.click(screen.getByRole("button", { name: "Revert Alpaca priority to 100" }));
      const reason = screen.getByRole("textbox", { name: "Reason · required" });
      await user.type(reason, "Wrong provider");
      await user.click(
        mutation === "toggleEnabled"
          ? screen.getByRole("switch", { name: "Disable provider" })
          : screen.getByRole("button", { name: /remove provider/i })
      );
      expect(held).toHaveBeenCalledTimes(1);

      // A revert now would resend the old `enabled` flag, or re-create a module being removed.
      const confirm = screen.getByRole("button", { name: "Revert cell" });
      expect(confirm).toBeDisabled();
      expect(confirm).toHaveAttribute("title", cellReason);
      await user.type(reason, "{Enter}");
      const batch = screen.getByRole("button", { name: "Revert 1 confirmed…" });
      expect(batch).toBeDisabled();
      expect(batch).toHaveAttribute("title", barReason);
      await user.click(screen.getByRole("button", { name: "Cancel" }));
      const opener = screen.getByRole("button", { name: "Revert Alpaca priority to 100" });
      expect(opener).toBeDisabled();
      expect(opener).toHaveAttribute("title", cellReason);
      expect(editModuleWithOutcome).toHaveBeenCalledTimes(1);

      await act(async () => {
        finish(false);
      });
      expect(screen.getByRole("button", { name: "Revert Alpaca priority to 100" })).toBeEnabled();
      expect(screen.getByRole("button", { name: "Revert 1 confirmed…" })).toBeEnabled();
    });

    it("does not send a priority that is committed after an enable or disable has started", async () => {
      const user = userEvent.setup();
      const editModuleWithOutcome = vi.fn().mockResolvedValue({ success: true, error: null, refused: false });
      const toggleEnabled = vi.fn(() => new Promise<boolean>(() => {}));
      mockUseProviderSetup.mockReturnValue({ ...defaultHookReturn, modules: [mockModule], editModuleWithOutcome, toggleEnabled });
      render(<ProviderSetupPanel />);

      // The editor is open when the toggle is activated without moving focus (assistive
      // technology, touch), so no blur committed the draft first.
      await user.click(screen.getByRole("button", { name: /Edit Alpaca priority/ }));
      const input = screen.getByRole("spinbutton");
      await user.clear(input);
      await user.type(input, "50");
      fireEvent.click(screen.getByRole("switch", { name: "Disable provider" }));
      expect(toggleEnabled).toHaveBeenCalledWith("alpaca", false);
      await user.type(input, "{Enter}");

      // The write would have carried `enabled: true` while the provider was being disabled.
      const note = await screen.findByRole("status");
      expect(note).toHaveTextContent(
        "This provider is being enabled or disabled. Wait for that to finish. The priority was not written."
      );
      expect(priorityCell(/Edit Alpaca priority: 100/)).toHaveAttribute("data-status", "rejected");
      expect(editModuleWithOutcome).not.toHaveBeenCalled();
    });

    it("drops a removed provider's priority edits, so the same id added again starts clean", async () => {
      const user = userEvent.setup();
      const hook = { ...defaultHookReturn, modules: [mockModule] };
      mockUseProviderSetup.mockReturnValue(hook);
      const { rerender } = render(<ProviderSetupPanel />);
      await typePriority(user, /Edit Alpaca priority/, "50");
      await screen.findByRole("button", { name: "Revert Alpaca priority to 100" });
      const edited = { ...mockModule, priority: 50 };
      mockUseProviderSetup.mockReturnValue({ ...hook, modules: [edited] });
      rerender(<ProviderSetupPanel />);
      expect(priorityCell(/Edit Alpaca priority: 50, was 100/)).toHaveAttribute("data-status", "confirmed");

      // The provider is removed, then configured again with the priority it happened to have.
      mockUseProviderSetup.mockReturnValue({ ...hook, modules: [] });
      rerender(<ProviderSetupPanel />);
      expect(screen.getByText("No provider modules configured")).toBeInTheDocument();
      mockUseProviderSetup.mockReturnValue({ ...hook, modules: [edited] });
      rerender(<ProviderSetupPanel />);

      expect(priorityCell(/^Edit Alpaca priority: 50$/)).toHaveAttribute("data-status", "idle");
      expect(screen.queryByRole("button", { name: /Revert/ })).not.toBeInTheDocument();
      expect(screen.queryByRole("group", { name: /Edit ledger/ })).not.toBeInTheDocument();
    });

    it("drops its confirmed entry when the edit drawer saves a different priority", async () => {
      const user = userEvent.setup();
      const hook = { ...defaultHookReturn, modules: [mockModule] };
      mockUseProviderSetup.mockReturnValue(hook);
      const { rerender } = render(<ProviderSetupPanel />);
      await typePriority(user, /Edit Alpaca priority/, "50");
      await screen.findByRole("button", { name: "Revert Alpaca priority to 100" });
      mockUseProviderSetup.mockReturnValue({ ...hook, modules: [{ ...mockModule, priority: 50 }] });
      rerender(<ProviderSetupPanel />);
      expect(priorityCell(/Edit Alpaca priority: 50/)).toHaveAttribute("data-status", "confirmed");

      // The full-form path writes 70; the hook re-reads the modules.
      mockUseProviderSetup.mockReturnValue({ ...hook, modules: [{ ...mockModule, priority: 70 }] });
      rerender(<ProviderSetupPanel />);

      await waitFor(() => expect(priorityCell(/Edit Alpaca priority: 70/)).toHaveAttribute("data-status", "idle"));
      expect(screen.queryByRole("button", { name: /Revert/ })).not.toBeInTheDocument();
      expect(screen.queryByRole("group", { name: /Edit ledger/ })).not.toBeInTheDocument();
    });

    it("keeps the edit drawer as the full-form path", async () => {
      const user = userEvent.setup();
      const editModule = vi.fn().mockResolvedValue(true);
      const editModuleWithOutcome = vi.fn();
      mockUseProviderSetup.mockReturnValue({ ...defaultHookReturn, modules: [mockModule], editModule, editModuleWithOutcome });
      render(<ProviderSetupPanel />);

      await user.click(screen.getByRole("button", { name: /edit provider/i }));
      expect(await screen.findByText("Edit Alpaca")).toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: "Save changes" }));

      await waitFor(() => expect(editModule).toHaveBeenCalledWith("alpaca", { moduleId: "alpaca", enabled: true, priority: 100 }));
      expect(editModuleWithOutcome).not.toHaveBeenCalled();
    });

    it("has no axe violations with a confirmed priority and the reason form open", async () => {
      const user = userEvent.setup();
      const hook = { ...defaultHookReturn, modules: [mockModule, disabledModule], pendingRestart: true };
      mockUseProviderSetup.mockReturnValue(hook);
      const { container } = render(<main><ProviderSetupPanel /></main>);
      expect((await axe(container)).violations).toEqual([]);

      await typePriority(user, /Edit Alpaca priority/, "50");
      await user.click(await screen.findByRole("button", { name: "Revert Alpaca priority to 100" }));

      expect(screen.getByRole("dialog", { name: "Revert this cell" })).toBeInTheDocument();
      expect((await axe(container)).violations).toEqual([]);
    });
  });
});
