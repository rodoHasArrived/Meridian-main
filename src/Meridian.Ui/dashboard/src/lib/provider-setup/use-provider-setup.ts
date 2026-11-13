import { useState, useEffect, useCallback, useRef } from "react";
import type {
  ProviderModuleStatus,
  ProviderModuleCatalogueEntry,
  UpsertProviderModuleRequest,
  ProviderModuleTestResult,
} from "@/types/provider-setup";
import {
  getProviderModules,
  getProviderModuleCatalogue,
  upsertProviderModule,
  updateProviderModule,
  deleteProviderModule,
  setProviderModuleEnabled,
  testProviderModule,
  restartProviderHost,
} from "@/lib/api";
import { isApiError } from "@/lib/api-errors";

/**
 * What an edit came back as, with the reason kept. `editModule` reports only a boolean and lets
 * request failures throw; inline editors need the reason to show beside the value they restore.
 */
export interface ProviderModuleEditOutcome {
  success: boolean;
  /** The server's own error string, or the failure message when no usable answer arrived. */
  error: string | null;
  /**
   * True when the server answered and refused the edit, so nothing was written. False on success,
   * and false when the request failed without a usable answer (dropped connection, server fault,
   * unreadable response): in that case the modules are read again, see `reread`.
   */
  refused: boolean;
  /**
   * Set only when the request got no usable answer, so its fate had to be read back:
   * - `"saved"`: the module now holds the requested enabled flag and priority. `success` is true.
   * - `"not-saved"`: the module still holds what it held before the request.
   * - `"unknown"`: the read failed, the module is gone, it holds something else, or the request
   *   carried credentials or settings, which never come back to the browser.
   * `null` when the server answered.
   */
  reread: ProviderModuleEditReread | null;
}

export type ProviderModuleEditReread = "saved" | "not-saved" | "unknown";

interface UnansweredEdit {
  moduleId: string;
  request: UpsertProviderModuleRequest;
  before: ProviderModuleStatus | undefined;
}

/** What a fresh read of the module says about an edit that got no answer. */
function readBackEdit(
  { request, before }: UnansweredEdit,
  after: ProviderModuleStatus | undefined
): ProviderModuleEditReread {
  if (!before || !after || request.credentialValues || request.settings) {
    return "unknown";
  }
  // The server record defaults an omitted `enabled` to true and an omitted `priority` to 100.
  const enabled = request.enabled ?? true;
  const priority = request.priority ?? 100;
  const holds = (module: ProviderModuleStatus) => module.enabled === enabled && module.priority === priority;
  if (holds(before)) {
    // The request changed nothing that can be read back, so the read cannot tell either way.
    return "unknown";
  }
  if (holds(after)) {
    return "saved";
  }
  return after.enabled === before.enabled && after.priority === before.priority ? "not-saved" : "unknown";
}

export interface UseProviderSetupReturn {
  modules: ProviderModuleStatus[];
  catalogue: ProviderModuleCatalogueEntry[];
  loading: boolean;
  error: string | null;
  pendingRestart: boolean;
  refresh: () => Promise<void>;
  addModule: (request: UpsertProviderModuleRequest) => Promise<boolean>;
  editModule: (moduleId: string, request: UpsertProviderModuleRequest) => Promise<boolean>;
  /**
   * Same write as `editModule`, but never throws and keeps the reason an edit did not land. When
   * no usable answer comes back it reads the modules again to find out whether the edit was stored.
   */
  editModuleWithOutcome: (moduleId: string, request: UpsertProviderModuleRequest) => Promise<ProviderModuleEditOutcome>;
  removeModule: (moduleId: string) => Promise<boolean>;
  toggleEnabled: (moduleId: string, enabled: boolean) => Promise<boolean>;
  testModule: (moduleId: string) => Promise<ProviderModuleTestResult | null>;
  triggerRestart: () => Promise<boolean>;
}

export function useProviderSetup(): UseProviderSetupReturn {
  const [modules, setModules] = useState<ProviderModuleStatus[]>([]);
  const [catalogue, setCatalogue] = useState<ProviderModuleCatalogueEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [pendingRestart, setPendingRestart] = useState(false);
  const abortRef = useRef<AbortController | null>(null);
  // The modules as last set, readable from callbacks without waiting for a render.
  const modulesRef = useRef<ProviderModuleStatus[]>([]);
  // Edits that got no answer and could not be read back yet. The next successful read settles them.
  const unansweredEdits = useRef<UnansweredEdit[]>([]);

  /** Reads the modules and catalogue. Resolves to the modules, or `null` when the read failed or was superseded. */
  const reload = useCallback(async (): Promise<ProviderModuleStatus[] | null> => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setLoading(true);
    setError(null);
    try {
      const [mods, cat] = await Promise.all([
        getProviderModules({ signal: controller.signal }),
        getProviderModuleCatalogue({ signal: controller.signal }),
      ]);
      if (controller.signal.aborted) return null;
      modulesRef.current = mods;
      setModules(mods);
      setCatalogue(cat);
      const unanswered = unansweredEdits.current;
      if (unanswered.length > 0) {
        unansweredEdits.current = [];
        const landed = unanswered.some(edit =>
          readBackEdit(edit, mods.find(m => m.moduleId === edit.moduleId)) === "saved");
        // The configuration did change, so the provider host needs its restart after all.
        if (landed) setPendingRestart(true);
      }
      return mods;
    } catch (err: unknown) {
      if (controller.signal.aborted) return null;
      setError(err instanceof Error ? err.message : "Failed to load provider modules");
      return null;
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, []);

  const refresh = useCallback(async () => {
    await reload();
  }, [reload]);

  useEffect(() => {
    void refresh();
    return () => { abortRef.current?.abort(); };
  }, [refresh]);

  const addModule = useCallback(async (request: UpsertProviderModuleRequest): Promise<boolean> => {
    const result = await upsertProviderModule(request);
    if (result.success) {
      setPendingRestart(true);
      await refresh();
    }
    return result.success;
  }, [refresh]);

  const editModule = useCallback(async (moduleId: string, request: UpsertProviderModuleRequest): Promise<boolean> => {
    const result = await updateProviderModule(moduleId, request);
    if (result.success) {
      setPendingRestart(true);
      await refresh();
    }
    return result.success;
  }, [refresh]);

  const editModuleWithOutcome = useCallback(async (
    moduleId: string,
    request: UpsertProviderModuleRequest
  ): Promise<ProviderModuleEditOutcome> => {
    const before = modulesRef.current.find(m => m.moduleId === moduleId);
    try {
      const result = await updateProviderModule(moduleId, request);
      if (result.success) {
        setPendingRestart(true);
        await refresh();
        return { success: true, error: null, refused: false, reread: null };
      }
      return { success: false, error: result.error?.trim() || null, refused: true, reread: null };
    } catch (err: unknown) {
      const message = err instanceof Error && err.message.trim() ? err.message.trim() : null;
      // A 4xx is the server declining the edit, with its reason as the message.
      if (isApiError(err) && err.status >= 400 && err.status < 500) {
        return { success: false, error: message, refused: true, reread: null };
      }
      // Anything else (no connection, a server fault, an unreadable body) is no answer: the edit
      // may or may not be stored. Read the modules again rather than guess.
      const edit: UnansweredEdit = { moduleId, request, before };
      const mods = await reload();
      const reread = mods ? readBackEdit(edit, mods.find(m => m.moduleId === moduleId)) : "unknown";
      if (reread === "saved") {
        setPendingRestart(true);
        return { success: true, error: null, refused: false, reread };
      }
      if (!mods) {
        // The read failed too. Whichever read succeeds next settles the restart flag.
        unansweredEdits.current = [...unansweredEdits.current, edit];
      }
      return { success: false, error: message, refused: false, reread };
    }
  }, [refresh, reload]);

  const removeModule = useCallback(async (moduleId: string): Promise<boolean> => {
    const result = await deleteProviderModule(moduleId);
    if (result.success) {
      setPendingRestart(true);
      await refresh();
    }
    return result.success;
  }, [refresh]);

  const toggleEnabled = useCallback(async (moduleId: string, enabled: boolean): Promise<boolean> => {
    const result = await setProviderModuleEnabled(moduleId, enabled);
    if (result.success) {
      const next = modulesRef.current.map(m =>
        m.moduleId === moduleId ? { ...m, enabled } : m
      );
      modulesRef.current = next;
      setModules(next);
    }
    return result.success;
  }, []);

  const testModule = useCallback(async (moduleId: string): Promise<ProviderModuleTestResult | null> => {
    try {
      return await testProviderModule(moduleId);
    } catch {
      return null;
    }
  }, []);

  const triggerRestart = useCallback(async (): Promise<boolean> => {
    try {
      await restartProviderHost();
      setPendingRestart(false);
      return true;
    } catch {
      return false;
    }
  }, []);

  return {
    modules,
    catalogue,
    loading,
    error,
    pendingRestart,
    refresh,
    addModule,
    editModule,
    editModuleWithOutcome,
    removeModule,
    toggleEnabled,
    testModule,
    triggerRestart,
  };
}
