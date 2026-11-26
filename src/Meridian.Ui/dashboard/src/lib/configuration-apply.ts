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
  const failed = entries.filter(entry => entry.kind === "Failed");
  const reconfigured = entries.filter(entry => entry.kind === "ProviderReconfigured");
  const live = entries.filter(entry => entry.kind === "AppliedLive");
  const noEffect = entries.filter(entry => entry.kind === "NoRuntimeEffect");

  if (status?.runtimeState === "Failed" || failed.length > 0) {
    return {
      tone: "warning",
      title: "Collector application failed",
      detail: "The configuration is saved. The collector reported an error applying one or more settings.",
      notes: uniqueDetails(failed)
    };
  }

  if (status?.runtimeState === "Unknown") {
    return {
      tone: "warning",
      title: "Collector application unconfirmed",
      detail: "The host has no active collector acknowledgement for the current saved configuration.",
      notes: []
    };
  }

  if (status?.runtimeState === "Saved" && !status.collectorAcknowledgement) {
    return {
      tone: "warning",
      title: "Configuration saved",
      detail: "Setting activation status shows whether the collector has acknowledged this revision.",
      notes: []
    };
  }

  if (!status || status.requiresRestart || deferred.length > 0 || status.runtimeState === "Deferred") {
    return {
      tone: "warning",
      title: "Pending host restart",
      detail: (live.length > 0 || reconfigured.length > 0)
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
      detail: status.collectorAcknowledgement
        ? "The collector confirmed the saved provider settings for this revision."
        : "The running host now uses the saved settings; no restart is needed.",
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
