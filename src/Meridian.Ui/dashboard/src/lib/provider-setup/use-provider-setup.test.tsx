import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import { useProviderSetup } from "@/lib/provider-setup/use-provider-setup";
import type { ProviderModuleStatus } from "@/types/provider-setup";

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

const polygon: ProviderModuleStatus = {
  moduleId: "polygon",
  displayName: "Polygon",
  enabled: false,
  priority: 100,
  isConfigured: true,
  isRunning: false,
  credentialStatus: [],
  capabilityNames: ["Backfill"]
};

async function mountHook() {
  const hook = renderHook(() => useProviderSetup());
  await waitFor(() => expect(hook.result.current.loading).toBe(false));
  return hook;
}

describe("useProviderSetup", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.getProviderModules.mockResolvedValue([polygon]);
    api.getProviderModuleCatalogue.mockResolvedValue([]);
  });

  describe("editModule (existing contract)", () => {
    it("resolves true, marks the restart pending and re-reads the modules when the edit lands", async () => {
      api.updateProviderModule.mockResolvedValue({ success: true });
      const { result } = await mountHook();
      api.getProviderModules.mockResolvedValue([{ ...polygon, priority: 50 }]);

      let outcome: boolean | undefined;
      await act(async () => {
        outcome = await result.current.editModule("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
      });

      expect(outcome).toBe(true);
      expect(api.updateProviderModule).toHaveBeenCalledWith("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
      expect(result.current.pendingRestart).toBe(true);
      expect(result.current.modules[0].priority).toBe(50);
    });

    it("resolves false without a restart flag when the result says the edit did not succeed", async () => {
      api.updateProviderModule.mockResolvedValue({ success: false, error: "ModuleId is required" });
      const { result } = await mountHook();

      let outcome: boolean | undefined;
      await act(async () => {
        outcome = await result.current.editModule("polygon", { moduleId: "polygon", priority: 50 });
      });

      expect(outcome).toBe(false);
      expect(result.current.pendingRestart).toBe(false);
      expect(api.getProviderModules).toHaveBeenCalledTimes(1);
    });

    it("still lets a request failure throw, as the edit drawer expects", async () => {
      api.updateProviderModule.mockRejectedValue(new Error("Network down"));
      const { result } = await mountHook();

      await expect(
        result.current.editModule("polygon", { moduleId: "polygon", priority: 50 })
      ).rejects.toThrow("Network down");
    });
  });

  describe("editModuleWithOutcome", () => {
    it("reports success, marks the restart pending and re-reads the modules", async () => {
      api.updateProviderModule.mockResolvedValue({ success: true });
      const { result } = await mountHook();
      api.getProviderModules.mockResolvedValue([{ ...polygon, priority: 50 }]);

      let outcome: unknown;
      await act(async () => {
        outcome = await result.current.editModuleWithOutcome("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
      });

      expect(outcome).toEqual({ success: true, error: null, refused: false, reread: null });
      expect(api.updateProviderModule).toHaveBeenCalledWith("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
      expect(result.current.pendingRestart).toBe(true);
      expect(result.current.modules[0].priority).toBe(50);
    });

    it("keeps the server's error string when the result says the edit was refused", async () => {
      api.updateProviderModule.mockResolvedValue({ success: false, error: "Configuration store is read-only" });
      const { result } = await mountHook();

      let outcome: unknown;
      await act(async () => {
        outcome = await result.current.editModuleWithOutcome("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
      });

      expect(outcome).toEqual({ success: false, error: "Configuration store is read-only", refused: true, reread: null });
      expect(result.current.pendingRestart).toBe(false);
      expect(api.getProviderModules).toHaveBeenCalledTimes(1);
    });

    it("treats a 4xx response as a refusal and surfaces the reason the server gave", async () => {
      // The endpoint answers a failed edit with 400 and `{ success: false, error }`, which the
      // API client turns into an ApiError whose message is that error string.
      api.updateProviderModule.mockRejectedValue(new ApiError({
        path: "/api/providers/modules/polygon",
        status: 400,
        detail: "Failed to read or save credentials; the configuration change was rolled back."
      }));
      const { result } = await mountHook();

      let outcome: unknown;
      await act(async () => {
        outcome = await result.current.editModuleWithOutcome("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
      });

      expect(outcome).toEqual({
        success: false,
        error: "Failed to read or save credentials; the configuration change was rolled back.",
        refused: true,
        reread: null
      });
      expect(result.current.pendingRestart).toBe(false);
      // A refusal is an answer: nothing is read again.
      expect(api.getProviderModules).toHaveBeenCalledTimes(1);
    });

    it.each([
      ["a dropped connection", new TypeError("Failed to fetch"), "Failed to fetch"],
      ["a server fault", new ApiError({ path: "/api/providers/modules/polygon", status: 503, detail: "Upstream unavailable" }), "Upstream unavailable"]
    ])("does not call %s a refusal: it reads the modules again and reports the edit was not stored", async (_name, failure, message) => {
      api.updateProviderModule.mockRejectedValue(failure);
      const { result } = await mountHook();

      let outcome: unknown;
      await act(async () => {
        outcome = await result.current.editModuleWithOutcome("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
      });

      expect(api.getProviderModules).toHaveBeenCalledTimes(2);
      expect(outcome).toEqual({ success: false, error: message, refused: false, reread: "not-saved" });
      expect(result.current.pendingRestart).toBe(false);
      expect(result.current.modules[0].priority).toBe(100);
    });

    it("reports success and marks the restart pending when the answer was lost but the edit is found stored", async () => {
      // The server applied the change and then answered 502.
      api.updateProviderModule.mockImplementation(async () => {
        api.getProviderModules.mockResolvedValue([{ ...polygon, priority: 50 }]);
        throw new ApiError({ path: "/api/providers/modules/polygon", status: 502, detail: "Bad gateway" });
      });
      const { result } = await mountHook();

      let outcome: unknown;
      await act(async () => {
        outcome = await result.current.editModuleWithOutcome("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
      });

      expect(outcome).toEqual({ success: true, error: null, refused: false, reread: "saved" });
      expect(result.current.pendingRestart).toBe(true);
      expect(result.current.modules[0].priority).toBe(50);
    });

    it("does not call the edit stored when the module holds the priority but not the enabled flag that was sent", async () => {
      api.updateProviderModule.mockImplementation(async () => {
        api.getProviderModules.mockResolvedValue([{ ...polygon, enabled: true, priority: 50 }]);
        throw new TypeError("Failed to fetch");
      });
      const { result } = await mountHook();

      let outcome: unknown;
      await act(async () => {
        outcome = await result.current.editModuleWithOutcome("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
      });

      expect(outcome).toEqual({ success: false, error: "Failed to fetch", refused: false, reread: "unknown" });
      expect(result.current.pendingRestart).toBe(false);
    });

    it("cannot read back an edit that carried credentials, which never return to the browser", async () => {
      api.updateProviderModule.mockImplementation(async () => {
        api.getProviderModules.mockResolvedValue([{ ...polygon, priority: 50 }]);
        throw new TypeError("Failed to fetch");
      });
      const { result } = await mountHook();

      let outcome: unknown;
      await act(async () => {
        outcome = await result.current.editModuleWithOutcome("polygon", {
          moduleId: "polygon",
          enabled: false,
          priority: 50,
          credentialValues: { apiKey: "synthetic-key" }
        });
      });

      expect(outcome).toEqual({ success: false, error: "Failed to fetch", refused: false, reread: "unknown" });
    });

    it("leaves the fate unknown when the read fails too, and marks the restart pending once a later read finds the edit stored", async () => {
      api.updateProviderModule.mockRejectedValue(new TypeError("Failed to fetch"));
      const { result } = await mountHook();
      api.getProviderModules.mockRejectedValueOnce(new TypeError("Failed to fetch"));

      let outcome: unknown;
      await act(async () => {
        outcome = await result.current.editModuleWithOutcome("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
      });

      expect(outcome).toEqual({ success: false, error: "Failed to fetch", refused: false, reread: "unknown" });
      expect(result.current.pendingRestart).toBe(false);
      expect(result.current.error).toBe("Failed to fetch");

      // The operator presses Refresh; the stored priority turns out to be the one that was sent.
      api.getProviderModules.mockResolvedValue([{ ...polygon, priority: 50 }]);
      await act(async () => {
        await result.current.refresh();
      });

      expect(result.current.modules[0].priority).toBe(50);
      expect(result.current.pendingRestart).toBe(true);
    });

    it("does not mark a restart pending when the later read shows the unanswered edit was never stored", async () => {
      api.updateProviderModule.mockRejectedValue(new TypeError("Failed to fetch"));
      const { result } = await mountHook();
      api.getProviderModules.mockRejectedValueOnce(new TypeError("Failed to fetch"));
      await act(async () => {
        await result.current.editModuleWithOutcome("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
      });

      await act(async () => {
        await result.current.refresh();
      });

      expect(result.current.modules[0].priority).toBe(100);
      expect(result.current.pendingRestart).toBe(false);
    });

    it("reports no reason when none was given", async () => {
      api.updateProviderModule.mockResolvedValue({ success: false, error: null });
      const { result } = await mountHook();

      let outcome: unknown;
      await act(async () => {
        outcome = await result.current.editModuleWithOutcome("polygon", { moduleId: "polygon", enabled: false, priority: 50 });
      });

      expect(outcome).toEqual({ success: false, error: null, refused: true, reread: null });
    });
  });

  describe("toggleEnabled and refresh (existing contract)", () => {
    it("flips the flag on the module it names and leaves the rest of the list as read", async () => {
      const alpaca: ProviderModuleStatus = { ...polygon, moduleId: "alpaca", displayName: "Alpaca", enabled: true };
      api.getProviderModules.mockResolvedValue([polygon, alpaca]);
      api.setProviderModuleEnabled.mockResolvedValue({ success: true });
      const { result } = await mountHook();

      let outcome: boolean | undefined;
      await act(async () => {
        outcome = await result.current.toggleEnabled("polygon", true);
      });

      expect(outcome).toBe(true);
      expect(api.setProviderModuleEnabled).toHaveBeenCalledWith("polygon", true);
      expect(result.current.modules).toEqual([{ ...polygon, enabled: true }, alpaca]);
      expect(api.getProviderModules).toHaveBeenCalledTimes(1);
    });

    it("leaves the modules alone when the toggle is refused", async () => {
      api.setProviderModuleEnabled.mockResolvedValue({ success: false, error: "nope" });
      const { result } = await mountHook();

      await act(async () => {
        await result.current.toggleEnabled("polygon", true);
      });

      expect(result.current.modules).toEqual([polygon]);
    });

    it("refresh resolves to nothing and reports a failed read through the error state", async () => {
      const { result } = await mountHook();
      api.getProviderModules.mockRejectedValueOnce(new Error("Network down"));

      let resolved: unknown = "unset";
      await act(async () => {
        resolved = await result.current.refresh();
      });

      expect(resolved).toBeUndefined();
      expect(result.current.error).toBe("Network down");
      expect(result.current.modules).toEqual([polygon]);
      expect(result.current.loading).toBe(false);
    });
  });
});
