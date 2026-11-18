import type { ConfigurationApplyEntry, ConfigurationChangeStatus } from "@/screens/settings-change-impact";

export interface ConfigurationApplyMessage {
  tone: "success" | "warning";
  title: string;
  detail: string;
  /** Per-setting outcomes reported by the host, deduplicated by explanation. */
  notes: string[];
}

const uniqueDetails = (entries: ConfigurationApplyEntry[]) =>
  [...new Set(entries.map(entry => entry.detail?.trim()).filter((detail): detail is string => Boolean(detail)))];

/**
 * Turns the host's change status into one honest message about a save. Without host evidence the
 * message stays conservative (restart required); it never claims a live apply the host did not report.
 */
export function describeConfigurationApply(status: ConfigurationChangeStatus | null | undefined): ConfigurationApplyMessage {
  const entries = status?.lastApply?.entries ?? [];
  const deferred = entries.filter(entry => entry.kind === "Deferred");
  const reconfigured = entries.filter(entry => entry.kind === "ProviderReconfigured");
  const live = entries.filter(entry => entry.kind === "AppliedLive");
  const noEffect = entries.filter(entry => entry.kind === "NoRuntimeEffect");

  if (!status || status.requiresRestart || deferred.length > 0) {
    return {
      tone: "warning",
      title: "Pending host restart",
      detail: entries.length > 0 && deferred.length < entries.length
        ? "Part of this save is already live; the rest applies after the host restarts."
        : "Saved configuration applies after the host restarts and rebuilds streaming providers. The active provider shown below is live runtime evidence; saving does not switch it.",
      notes: uniqueDetails([...deferred, ...reconfigured, ...live, ...noEffect])
    };
  }

  if (reconfigured.length > 0) {
    return {
      tone: "success",
      title: "Provider routing updated live",
      detail: "The running host re-evaluated streaming providers without a restart.",
      notes: uniqueDetails([...reconfigured, ...live, ...noEffect])
    };
  }

  if (live.length > 0) {
    return {
      tone: "success",
      title: "Applied live",
      detail: "The running host now uses the saved settings; no restart is needed.",
      notes: uniqueDetails([...live, ...noEffect])
    };
  }

  return {
    tone: "success",
    title: noEffect.length > 0 ? "Saved — no runtime effect" : "Saved",
    detail: noEffect.length > 0
      ? "The running host does not read these settings in its current mode."
      : "No pending activation is required.",
    notes: uniqueDetails(noEffect)
  };
}
