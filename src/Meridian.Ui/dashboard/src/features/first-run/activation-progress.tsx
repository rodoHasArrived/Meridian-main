import { useEffect, useState } from "react";
import { FlaskConical } from "lucide-react";
import { apiGetJson, apiPostJson } from "@/lib/api";
import type { FirstRunStatus } from "./types";

export function ActivationHeaderProgress() {
  const [status, setStatus] = useState<FirstRunStatus | null>(null);
  useEffect(() => { void apiGetJson<FirstRunStatus>("/api/workstation/first-run/").then(setStatus).catch(() => undefined); }, []);
  if (!status?.isComplete) return null;
  const completed = status.outcomes.filter((outcome) => outcome.isComplete).length;
  return <div className="flex items-center gap-2">
    {status.workspace.isSample ? <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-400/15 px-2.5 py-1 text-xs font-semibold text-amber-200" title={status.workspace.safetyMessage}><FlaskConical size={13} />{status.workspace.badge}</span> : null}
    <span className="rounded-full bg-slate-800 px-2.5 py-1 text-xs text-slate-300" title="Activation is based on completed outcomes, not page visits">Getting started {completed}/{status.outcomes.length}</span>
    <button className="rounded-full border border-slate-700 px-2.5 py-1 text-xs text-slate-300 hover:border-cyan-400 hover:text-cyan-200" onClick={() => void apiPostJson("/api/workstation/desktop/launch", { page: "Portfolio" })}>Open desktop workstation</button>
  </div>;
}
