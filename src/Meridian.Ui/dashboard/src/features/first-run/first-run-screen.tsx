import { useEffect, useMemo, useState } from "react";
import { ArrowLeft, ArrowRight, Check, Database, ShieldCheck } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { apiGetJson, apiPostJson } from "@/lib/api";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import { Button } from "@/components/ui/button";
import type { FirstRunStatus } from "./types";

const goals = [
  ["monitor-investments", "Monitor investments"],
  ["operate-fund", "Operate a fund"],
  ["reconcile-accounts", "Reconcile accounts and statements"],
  ["research-strategies", "Research and test strategies"],
  ["collect-market-data", "Collect trusted market data"]
] as const;

const dataChoices = [
  ["upload", "Upload a statement or holdings file"],
  ["provider", "Connect a provider"],
  ["sample", "Use sample data"],
  ["skip", "Skip for now"]
] as const;

export function FirstRunScreen({ initialStatus, onStatusChange }: { initialStatus?: FirstRunStatus | null; onStatusChange?: (status: FirstRunStatus) => void }) {
  const navigate = useNavigate();
  const [status, setStatus] = useState<FirstRunStatus | null>(initialStatus ?? null);
  const [step, setStep] = useState(0);
  const [goal, setGoal] = useState("monitor-investments");
  const [starterKitId, setStarterKitId] = useState("personal-portfolio");
  const [dataChoice, setDataChoice] = useState("sample");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!status) apiGetJson<FirstRunStatus>(WORKSTATION_API_ENDPOINTS.firstRunStatus).then(setStatus).catch((reason) => setError(String(reason)));
  }, [status]);

  useEffect(() => {
    const recommended = status?.starterKits.find((kit) => kit.goal === goal);
    if (recommended) setStarterKitId(recommended.id);
  }, [goal, status]);

  const starter = useMemo(() => status?.starterKits.find((kit) => kit.id === starterKitId), [starterKitId, status]);
  const steps = ["Welcome", "Your goal", "Starting data", "Review", "Ready"];

  async function finish() {
    setBusy(true);
    setError(null);
    try {
      const next = await apiPostJson<FirstRunStatus>(WORKSTATION_API_ENDPOINTS.firstRunComplete, {
        goal,
        starterKitId,
        dataChoice,
        useSampleData: dataChoice === "sample"
      });
      setStatus(next);
      // Report completion upward so AppRoot's redirect guard releases the user
      // instead of bouncing post-setup navigation back to /setup.
      onStatusChange?.(next);
      setStep(4);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Meridian could not save this workspace setup.");
    } finally {
      setBusy(false);
    }
  }

  if (!status) return <div className="min-h-screen bg-slate-950 p-8 text-slate-100">Preparing Meridian…</div>;

  return (
    <main className="min-h-screen bg-slate-950 px-5 py-8 text-slate-100" aria-labelledby="first-run-title">
      <div className="mx-auto flex min-h-[calc(100vh-4rem)] max-w-5xl flex-col rounded-3xl border border-slate-800 bg-slate-900/80 shadow-2xl">
        <header className="border-b border-slate-800 px-7 py-6">
          <div className="flex items-center gap-3 text-sm font-semibold uppercase tracking-[0.22em] text-cyan-300"><ShieldCheck size={18} /> Meridian</div>
          <ol className="mt-5 grid grid-cols-5 gap-2" aria-label="Setup progress">
            {steps.map((label, index) => <li key={label} className={`rounded-full px-3 py-2 text-center text-xs ${index <= step ? "bg-cyan-400/15 text-cyan-200" : "bg-slate-800 text-slate-500"}`}>{index < step ? <Check className="mr-1 inline" size={13} /> : null}{label}</li>)}
          </ol>
        </header>

        <section className="flex flex-1 flex-col justify-center px-7 py-10 md:px-16">
          {step === 0 ? <>
            <p className="text-sm font-medium text-cyan-300">A calm start, with no infrastructure to configure</p>
            <h1 id="first-run-title" className="mt-3 text-4xl font-semibold tracking-tight">Welcome to Meridian</h1>
            <p className="mt-4 max-w-2xl text-lg text-slate-300">Set up a focused desk, or explore a believable paper workspace that works without credentials or network access.</p>
            <div className="mt-8 grid gap-4 md:grid-cols-2">
              <Choice selected onClick={() => { setDataChoice("sample"); setStep(1); }} title="Explore with sample data" description="Recommended. See a portfolio, statement, breaks, report, and paper strategy immediately." />
              <Choice selected={false} onClick={() => setStep(1)} title="Set up my workspace" description="Choose your goal and add your own data now or later." />
            </div>
          </> : null}

          {step === 1 ? <>
            <h1 id="first-run-title" className="text-3xl font-semibold">What do you want Meridian to help with?</h1>
            <div className="mt-7 grid gap-3 md:grid-cols-2">{goals.map(([id, label]) => <Choice key={id} selected={goal === id} onClick={() => setGoal(id)} title={label} description={status.starterKits.find((kit) => kit.goal === id)?.description ?? "Configure a focused Meridian desk."} />)}</div>
          </> : null}

          {step === 2 ? <>
            <h1 id="first-run-title" className="text-3xl font-semibold">Choose your starting data</h1>
            <p className="mt-3 text-slate-300">Sample mode is clearly labelled and can never place live trades or post production accounting.</p>
            <div className="mt-7 grid gap-3 md:grid-cols-2">{dataChoices.map(([id, label]) => <Choice key={id} selected={dataChoice === id} onClick={() => setDataChoice(id)} title={label} description={id === "sample" ? "Works offline and needs no provider credentials." : id === "skip" ? "You can add data later from your desk." : "Meridian will guide the next step after setup."} />)}</div>
          </> : null}

          {step === 3 ? <>
            <h1 id="first-run-title" className="text-3xl font-semibold">Review your setup</h1>
            <div className="mt-7 divide-y divide-slate-800 rounded-2xl border border-slate-700 bg-slate-950/40">
              <Review label="Starter workspace" value={starter?.name ?? starterKitId} />
              <Review label="Storage" value="Meridian application data (outside the installation directory)" />
              <Review label="Starting data" value={dataChoices.find(([id]) => id === dataChoice)?.[1] ?? dataChoice} />
              <Review label="Trading and accounting" value={dataChoice === "sample" ? "Paper and sample only" : "No live action until explicitly connected and approved"} />
              <Review label="Credentials" value="Encrypted credential store; never included in sample data" />
            </div>
          </> : null}

          {step === 4 ? <>
            <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-emerald-400/15 text-emerald-300"><Check size={32} /></div>
            <h1 id="first-run-title" className="mt-6 text-4xl font-semibold">Your workspace is ready</h1>
            {status.workspace.isSample ? <p className="mt-3 inline-flex w-fit items-center gap-2 rounded-full bg-amber-400/15 px-3 py-1.5 text-sm font-semibold text-amber-200"><Database size={15} /> {status.workspace.badge}</p> : null}
            <div className="mt-8 grid gap-4 md:grid-cols-3">{status.recommendedActions.map((action) => <button key={action.route} className="rounded-2xl border border-slate-700 bg-slate-950/40 p-5 text-left transition hover:border-cyan-400" onClick={() => navigate(action.route)}><strong className="block text-slate-100">{action.label}</strong><span className="mt-2 block text-sm text-slate-400">{action.description}</span></button>)}</div>
          </> : null}

          {error ? <p className="mt-6 rounded-xl border border-red-500/40 bg-red-500/10 p-4 text-red-200" role="alert">What failed: setup could not be saved. What it affects: this workspace is not ready yet. Meridian can try again. Details: {error}</p> : null}
        </section>

        {step > 0 && step < 4 ? <footer className="flex items-center justify-between border-t border-slate-800 px-7 py-5">
          <Button variant="ghost" onClick={() => setStep((value) => Math.max(0, value - 1))}><ArrowLeft size={16} /> Back</Button>
          <Button disabled={busy} onClick={() => step === 3 ? void finish() : setStep((value) => value + 1)}>{busy ? "Creating workspace…" : step === 3 ? "Create workspace" : "Continue"} <ArrowRight size={16} /></Button>
        </footer> : null}
      </div>
    </main>
  );
}

function Choice({ selected, onClick, title, description }: { selected: boolean; onClick: () => void; title: string; description: string }) {
  return <button type="button" aria-pressed={selected} onClick={onClick} className={`rounded-2xl border p-5 text-left transition ${selected ? "border-cyan-400 bg-cyan-400/10" : "border-slate-700 bg-slate-950/30 hover:border-slate-500"}`}><strong className="block text-base text-slate-100">{title}</strong><span className="mt-2 block text-sm leading-6 text-slate-400">{description}</span></button>;
}

function Review({ label, value }: { label: string; value: string }) {
  return <div className="grid gap-1 px-5 py-4 md:grid-cols-[13rem_1fr]"><span className="text-sm text-slate-400">{label}</span><strong className="font-medium text-slate-100">{value}</strong></div>;
}
