import { FlaskConical } from "lucide-react";
import { apiPostJson } from "@/lib/api";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type { FirstRunStatus } from "./types";

/**
 * Masthead chip summarizing first-run activation progress. The status is fetched once by
 * AppRoot and passed down — this component must not issue its own fetch, so the shell
 * renders from a single consistent first-run snapshot.
 */
export function ActivationHeaderProgress({ status }: { status?: FirstRunStatus | null }) {
  if (!status?.isComplete) return null;
  const completed = status.outcomes.filter((outcome) => outcome.isComplete).length;
  return <div className="flex items-center gap-2">
    {status.workspace.isSample ? <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-400/15 px-2.5 py-1 text-xs font-semibold text-amber-200" title={status.workspace.safetyMessage}><FlaskConical size={13} />{status.workspace.badge}</span> : null}
    <span className="rounded-full bg-slate-800 px-2.5 py-1 text-xs text-slate-300" title="Activation is based on completed outcomes, not page visits">Getting started {completed}/{status.outcomes.length}</span>
    <button className="rounded-full border border-slate-700 px-2.5 py-1 text-xs text-slate-300 hover:border-cyan-400 hover:text-cyan-200" onClick={() => void apiPostJson(WORKSTATION_API_ENDPOINTS.desktopLaunch, { page: "Portfolio" })}>Open desktop workstation</button>
  </div>;
}
