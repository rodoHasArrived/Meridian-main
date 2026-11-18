/**
 * Every position surface names where its rows come from, so a symbol that reads differently on two
 * screens is visibly two sources rather than one contradictory figure. Trading shows the brokerage
 * session for the selected account; Portfolio shows live holdings across every account, or the
 * records retained by one run.
 */
export type PositionSourceKind = "paper-session" | "live-session" | "live-holdings" | "retained-run" | "brokerage-household" | "execution-gateway";

export interface PositionSource {
  kind: PositionSourceKind;
  /** Short display label, e.g. "Paper session (Alpaca …4567)". */
  label: string;
  /** Masked account reference, or null when the source spans every account. */
  accountRef: string | null;
}

/** Account identifiers are masked to their last four characters. */
export function maskAccountRef(account: string | null | undefined): string | null {
  const trimmed = account?.trim();
  if (!trimmed) return null;
  return trimmed.length > 4 ? `…${trimmed.slice(-4)}` : trimmed;
}

export function describeBrokerageSessionSource(brokerage: {
  provider?: string | null;
  account?: string | null;
  environment?: "paper" | "live" | null;
} | null | undefined): PositionSource {
  const kind: PositionSourceKind = brokerage?.environment === "live" ? "live-session" : "paper-session";
  const accountRef = maskAccountRef(brokerage?.account);
  const provider = brokerage?.provider?.trim();
  const qualifier = [provider, accountRef].filter(Boolean).join(" ");
  const base = kind === "live-session" ? "Live session" : "Paper session";
  return { kind, accountRef, label: qualifier ? `${base} (${qualifier})` : base };
}

export function describeLiveHoldingsSource(): PositionSource {
  return { kind: "live-holdings", accountRef: null, label: "Live holdings · all accounts" };
}

export function describeRetainedRunSource(runId: string | null | undefined, asOf: string | null | undefined): PositionSource {
  const parts = [runId?.trim() ? `run ${runId.trim()}` : null, asOf?.trim() ? `as of ${asOf.trim()}` : null].filter(Boolean);
  return { kind: "retained-run", accountRef: null, label: parts.length ? `Retained records · ${parts.join(" · ")}` : "Retained records" };
}

/** Positions a brokerage household reports for its accounts, e.g. "Alpaca paper household". */
export function describeBrokerageHouseholdSource(providerLabel: string | null | undefined): PositionSource {
  const provider = providerLabel?.trim();
  return { kind: "brokerage-household", accountRef: null, label: provider ? `${provider} household` : "Brokerage household" };
}

/** The position book the execution gateway acts on, named as its provenance names it. */
export function describeExecutionGatewaySource(provenanceLabel: string | null | undefined): PositionSource {
  const label = provenanceLabel?.trim();
  return { kind: "execution-gateway", accountRef: null, label: label ? `Execution gateway · ${label}` : "Execution gateway" };
}

/** Attributes the screenshot harness and tests read to confirm a positions table names its source. */
export function positionSourceAttributes(source: PositionSource) {
  return { "data-position-source": source.kind, "data-position-source-label": source.label };
}
