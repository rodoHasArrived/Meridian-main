import { createContext, lazy, Suspense, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import { FileSearch } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import { amountPassportKey, readAmountPassportLink } from "@/lib/amount-passport-api";
import { cn } from "@/lib/utils";
import { amountPassportStatusLabels } from "@/components/meridian/number-passport";
import type { AmountPassportSelection } from "@/components/meridian/amount-passport-drawer";
import type { AmountPassportIdentity } from "@/types/amount-passport";

const AmountPassportDrawer = lazy(() => import("./amount-passport-drawer").then(module => ({ default: module.AmountPassportDrawer })));
const PassportContext = createContext<((selection: AmountPassportSelection, trigger: HTMLButtonElement) => void) | null>(null);

interface Investigation {
  selections: AmountPassportSelection[];
  routeKey: string;
  session: unknown;
  trigger?: HTMLButtonElement;
}

/** Session changes discard selections and pinned addresses. The server authorizes every read. */
export function AmountPassportProvider({ children, enabled, scopeKey, session }: {
  children: ReactNode; enabled: boolean; scopeKey: string; session: unknown
}) {
  const location = useLocation();
  const navigate = useNavigate();
  const query = new URLSearchParams(location.search);
  const routeKey = JSON.stringify([location.pathname, scopeKey, ...["view", "runId", "ledgerBookId", "periodId"].map(key => query.get(key))]);
  const [investigation, setInvestigation] = useState<Investigation | null>(null);
  const [width, setWidth] = useState(32);
  const [pin, setPin] = useState<{ identity: AmountPassportIdentity; session: unknown } | null>(null);
  const [authority, setAuthority] = useState({ session, revision: 0 });
  if (authority.session !== session) setAuthority({ session, revision: authority.revision + 1 });
  const [linked, setLinked] = useState(() => ({
    pathname: location.pathname, hash: location.hash, identity: readAmountPassportLink(location.hash)
  }));

  // Some originating screens normalize their filters or scope on mount. Capture the linked
  // address before that normalization, and keep it available for this route.
  if (linked.pathname !== location.pathname || (location.hash && linked.hash !== location.hash)) {
    setLinked({ pathname: location.pathname, hash: location.hash, identity: readAmountPassportLink(location.hash) });
  }
  const link = linked.pathname === location.pathname ? linked.identity : null;
  const current = enabled && investigation?.routeKey === routeKey && investigation.session === session ? investigation : null;
  const selection = current?.selections.at(-1) ?? (enabled && link ? { label: "Linked amount", identity: link } : null);
  useEffect(() => {
    setInvestigation(value => value && (value.routeKey !== routeKey || value.session !== session) ? null : value);
    setPin(value => value && value.session !== session ? null : value);
  }, [routeKey, session]);
  const open = useCallback((amount: AmountPassportSelection, trigger: HTMLButtonElement) => {
    setInvestigation({ selections: [amount], routeKey, session, trigger });
  }, [routeKey, session]);

  function close() {
    const trigger = current?.trigger;
    setInvestigation(null);
    setLinked({ pathname: location.pathname, hash: location.hash, identity: null });
    if (location.hash && readAmountPassportLink(location.hash)) {
      void navigate({ pathname: location.pathname, search: location.search, hash: "" }, { replace: true, preventScrollReset: true });
    }
    if (trigger?.isConnected) requestAnimationFrame(() => trigger.focus({ preventScroll: true }));
    else requestAnimationFrame(() => document.getElementById("workbench-content")?.focus({ preventScroll: true }));
  }

  function follow(identity: AmountPassportIdentity) {
    if (!selection) return;
    const selectedIdentity = selection.reference?.identity ?? selection.identity;
    if (selectedIdentity && amountPassportKey(identity) === amountPassportKey(selectedIdentity)) return;
    setInvestigation({
      selections: [...(current?.selections ?? [selection]), { label: "Linked amount", identity }],
      routeKey, session, trigger: current?.trigger
    });
  }

  return <PassportContext.Provider value={enabled ? open : null}>
    {children}
    {selection ? <Suspense fallback={null}>
      <AmountPassportDrawer key={authority.revision} selection={selection} baseline={pin?.session === session ? pin?.identity : null}
        onPin={identity => setPin({ identity, session })} onClearPin={() => setPin(null)}
        onClose={close} onFollow={follow} width={width} onWidth={setWidth}
        onBack={current && current.selections.length > 1 ? () => setInvestigation({
          ...current, selections: current.selections.slice(0, -1)
        }) : undefined} />
    </Suspense> : null}
  </PassportContext.Provider>;
}

const shortOrigin = {
  sourced: "Src", calculated: "Calc", overridden: "Ovr", estimated: "Est", unavailable: "?"
} as const;

export function AmountPassportTrigger({ selection, children, ariaLabel, className, compact }: {
  selection: AmountPassportSelection; children?: ReactNode; ariaLabel?: string; className?: string; compact?: boolean
}) {
  const open = useContext(PassportContext);
  const [local, setLocal] = useState<AmountPassportSelection | null>(null);
  const [localWidth, setLocalWidth] = useState(32);
  const reference = selection.reference;
  const origin = reference?.origin ?? selection.origin ?? "unavailable";
  const freshness = reference?.freshness ?? selection.freshness;
  const zero = reference?.isZero ?? selection.zero;
  const marker = [
    [shortOrigin[origin], amountPassportStatusLabels[origin]],
    ...(zero ? [["0", "Zero"]] : []),
    ...(freshness === "stale" ? [["Stale", "Stale"]] : []),
    ...(freshness === "blocked" ? [["!", "Blocked"]] : []),
    ...(selection.restricted ? [["×", "Permission denied"]] : [])
  ];
  return <>
    <button type="button" aria-haspopup="dialog" className={cn(
      "inline-flex max-w-full items-center gap-1 rounded-sm text-left focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 hover:underline",
      className
    )} aria-label={ariaLabel ?? `Inspect ${selection.label}${selection.displayValue ? ` ${selection.displayValue}` : ""} · ${marker.map(([, label]) => label).join(", ")}`}
      title={`Amount passport · ${marker.map(([, label]) => label).join(", ")}`}
      onKeyDown={event => { if (event.key === "Enter" || event.key === " ") event.stopPropagation(); }}
      onClick={event => {
        event.stopPropagation();
        event.currentTarget.focus({ preventScroll: true });
        if (open) open(selection, event.currentTarget);
        else setLocal(selection);
      }}>
      <span aria-hidden="true" className="inline-flex items-center gap-1 whitespace-nowrap font-sans text-xs text-muted-foreground">
        <FileSearch className="size-3.5 shrink-0" />
        {compact ? null : marker.map(([text, label]) => <span key={label}>{text}</span>)}
      </span>
      {children}
    </button>
    {local ? <Suspense fallback={null}>
      <AmountPassportDrawer selection={local} onClose={() => setLocal(null)} onFollow={identity => setLocal({ label: "Linked amount", identity })}
        width={localWidth} onWidth={setLocalWidth} />
    </Suspense> : null}
  </>;
}
