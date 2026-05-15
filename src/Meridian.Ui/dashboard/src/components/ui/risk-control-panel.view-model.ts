import { useCallback, useEffect, useMemo, useState } from "react";
import * as api from "@/lib/api";
import type { RiskRuleConfig, RiskRuleStatus, UpdateRiskRuleConfigRequest } from "@/types";

export interface RiskControlPanelServices {
  getRiskRules: typeof api.getRiskRules;
  getRiskRuleConfig: typeof api.getRiskRuleConfig;
  updateRiskRuleConfig: typeof api.updateRiskRuleConfig;
}

export interface RiskRulePanelRow {
  status: RiskRuleStatus;
  config: RiskRuleConfig | null;
  draftConfig: Record<string, string>;
  saving: boolean;
}

export interface RiskControlPanelViewModel {
  loading: boolean;
  errorText: string | null;
  rows: RiskRulePanelRow[];
  refresh: () => Promise<void>;
  updateDraftValue: (ruleName: string, key: string, value: string) => void;
  saveRuleConfig: (ruleName: string) => Promise<void>;
}

const defaultServices: RiskControlPanelServices = {
  getRiskRules: api.getRiskRules,
  getRiskRuleConfig: api.getRiskRuleConfig,
  updateRiskRuleConfig: api.updateRiskRuleConfig
};

export function useRiskControlPanelViewModel(services: RiskControlPanelServices = defaultServices): RiskControlPanelViewModel {
  const [loading, setLoading] = useState(true);
  const [errorText, setErrorText] = useState<string | null>(null);
  const [statuses, setStatuses] = useState<RiskRuleStatus[]>([]);
  const [configs, setConfigs] = useState<Record<string, RiskRuleConfig>>({});
  const [drafts, setDrafts] = useState<Record<string, Record<string, string>>>({});
  const [saving, setSaving] = useState<Record<string, boolean>>({});

  const refresh = useCallback(async () => {
    setLoading(true);
    setErrorText(null);

    try {
      const latestStatuses = await services.getRiskRules();
      const latestConfigs = await Promise.all(
        latestStatuses.map(async (status) => ({
          ruleName: status.ruleName,
          config: await services.getRiskRuleConfig(status.ruleName)
        }))
      );

      const nextConfigMap = Object.fromEntries(
        latestConfigs.map(({ ruleName, config }) => [ruleName, config])
      );

      const nextDraftMap = Object.fromEntries(
        latestConfigs.map(({ ruleName, config }) => [ruleName, { ...config.config }])
      );

      setStatuses(latestStatuses);
      setConfigs(nextConfigMap);
      setDrafts(nextDraftMap);
    } catch (error) {
      setErrorText(error instanceof Error ? error.message : "Unable to load risk rule controls.");
    } finally {
      setLoading(false);
    }
  }, [services]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const updateDraftValue = useCallback((ruleName: string, key: string, value: string) => {
    setDrafts((current) => ({
      ...current,
      [ruleName]: {
        ...(current[ruleName] ?? {}),
        [key]: value
      }
    }));
  }, []);

  const saveRuleConfig = useCallback(async (ruleName: string) => {
    const nextConfig: UpdateRiskRuleConfigRequest = {
      config: drafts[ruleName] ?? {}
    };

    setSaving((current) => ({ ...current, [ruleName]: true }));
    setErrorText(null);

    try {
      const updatedConfig = await services.updateRiskRuleConfig(ruleName, nextConfig);
      const updatedStatus = await services.getRiskRuleConfig(ruleName);

      setConfigs((current) => ({
        ...current,
        [ruleName]: updatedConfig
      }));
      setDrafts((current) => ({
        ...current,
        [ruleName]: { ...updatedStatus.config }
      }));

      const latestStatuses = await services.getRiskRules();
      setStatuses(latestStatuses);
    } catch (error) {
      setErrorText(error instanceof Error ? error.message : `Unable to save ${ruleName} config.`);
    } finally {
      setSaving((current) => ({ ...current, [ruleName]: false }));
    }
  }, [drafts, services]);

  const rows = useMemo<RiskRulePanelRow[]>(() => {
    return statuses.map((status) => ({
      status,
      config: configs[status.ruleName] ?? null,
      draftConfig: drafts[status.ruleName] ?? {},
      saving: saving[status.ruleName] ?? false
    }));
  }, [configs, drafts, saving, statuses]);

  return {
    loading,
    errorText,
    rows,
    refresh,
    updateDraftValue,
    saveRuleConfig
  };
}
