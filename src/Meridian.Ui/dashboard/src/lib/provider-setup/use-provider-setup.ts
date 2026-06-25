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

export interface UseProviderSetupReturn {
  modules: ProviderModuleStatus[];
  catalogue: ProviderModuleCatalogueEntry[];
  loading: boolean;
  error: string | null;
  pendingRestart: boolean;
  refresh: () => Promise<void>;
  addModule: (request: UpsertProviderModuleRequest) => Promise<boolean>;
  editModule: (moduleId: string, request: UpsertProviderModuleRequest) => Promise<boolean>;
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

  const refresh = useCallback(async () => {
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
      setModules(mods);
      setCatalogue(cat);
    } catch (err: unknown) {
      if (err instanceof DOMException && err.name === "AbortError") return;
      setError(err instanceof Error ? err.message : "Failed to load provider modules");
    } finally {
      setLoading(false);
    }
  }, []);

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
      setModules(prev => prev.map(m =>
        m.moduleId === moduleId ? { ...m, enabled } : m
      ));
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
    removeModule,
    toggleEnabled,
    testModule,
    triggerRestart,
  };
}
