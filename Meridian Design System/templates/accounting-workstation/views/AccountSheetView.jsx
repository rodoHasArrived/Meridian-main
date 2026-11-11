// Account Sheet — tier 3 of the drill ladder (row → drawer → screen). O-2 of
// docs/ACCOUNTING_INTERFACE_4_2.md: the account as a destination, not a filter. One surface where
// an account's closing balance sits above its proof (opening + debits − credits = closing), its
// postings with running balance, its open items, and its tie state — the answer to "why is
// 1100 · Cash what it is?", one click from any rendered account code. Deep link:
// ?view=account&account=1100. Props: code, coa (scoped tree), ledger, openings, recLeft/recRight,
// entity, locked, onInspect(ref), onPostTo(acct), onViewLedger(code), onOpenAccount(code).
const { useMemo } = React;

function findNode(nodes, code) {
  for (const n of nodes || []) {
    if (n.code === code) return n;
    const hit = n.children && findNode(n.children, code);
    if (hit) return hit;
  }
  return null;
}
function parentOf(nodes, code, parent) {
  for (const n of nodes || []) {
    if (n.code === code) return parent;
    const hit = n.children && parentOf(n.children, code, n);
    if (hit) return hit;
  }
  return null;
}
const CREDIT_NORMAL = { liability: 1, equity: 1, revenue: 1 };
const PERIOD_END = "2026-06-30";

function AcctAccountSheetView(props) {
  const DS = window.MeridianDesignSystem_205d17;
  const { Button, Badge, Eyebrow, Callout, EmptyState, RollforwardTrace, LedgerTable, TieOutPanel, EnvelopeCoherence, ValueEnvelope, Money, Assurance, AnnotationProvider, AnnotationAnchor, AnnotationPanel } = DS;
  const money = (v) => Money.formatMoney(v, { currency: "USD" });
  const code = String(props.code || "").slice(0, 4);
  const node = findNode(props.coa, code);
  const parent = parentOf(props.coa, code, null);
  const rows = props.ledger || [];

  const lines = useMemo(() => rows.filter((r) => r.account && r.account.slice(0, 4) === code), [rows, code]);
  const live = lines.filter((l) => l.status !== "void");
  const voids = lines.length - live.length;
  const dr = Money.sumAmounts(live.map((l) => Money.toNumber(l.debit) || 0));
  const cr = Money.sumAmounts(live.map((l) => Money.toNumber(l.credit) || 0));
  const creditNormal = !!CREDIT_NORMAL[node && node.type];
  const normalSide = creditNormal ? "credit" : "debit";
  // Balances in the CoA are signed debit-positive; a credit-normal account is stated positive
  // on its own side, so the sheet flips once and says so.
  const closingRaw = (props.balances || {})[code] != null ? (props.balances || {})[code] : node ? Number(node.balance) || 0 : 0;
  const closing = creditNormal ? -closingRaw : closingRaw;
  const movement = creditNormal ? cr - dr : dr - cr;
  const statedOpening = (props.openings || {})[code];
  const openingKnown = statedOpening != null;
  const opening = openingKnown ? statedOpening : Money.roundMoney(closing - movement, 2);

  const envelopes = live.map((l) => l.envelope).filter(Boolean);
  const conditions = Array.from(new Set(envelopes.map((e) => e.condition).filter(Boolean)));
  const worst = conditions.includes("overridden") ? "overridden" : conditions.includes("estimated") ? "estimated" : conditions.includes("provisional") ? "provisional" : conditions.includes("confirmed") ? "confirmed" : conditions[0];
  const material = envelopes.some((e) => e.materiality && e.materiality.material);

  // Open items — postings whose settlement clock is still ahead of period end.
  const open = live.filter((l) => l.envelope && l.envelope.clocks && l.envelope.clocks.settlement && l.envelope.clocks.settlement > PERIOD_END);
  const openTotal = Money.sumAmounts(open.map((l) => (Money.toNumber(l.debit) || 0) - (Money.toNumber(l.credit) || 0)));

  // Tie state — the cash account ties to the bank statement; everything else ties to its
  // own proof (opening + activity = closing) and says which.
  const isCash = code === "1100";
  const recL = props.recLeft || [], recR = props.recRight || [];
  const stmtTotal = Money.sumAmounts(recL.map((r) => r.amount));
  const bookTotal = Money.sumAmounts(recR.map((r) => r.amount));
  const unmatchedL = recL.filter((r) => !r.matched), unmatchedR = recR.filter((r) => !r.matched);
  const tieStatus = isCash ? (unmatchedL.length + unmatchedR.length === 0 ? "tied" : "exception") : "tied";

  // Assurance (O-4) — the signatures bound to THIS balance, and the one verb that advances it.
  const A = Assurance, asrRec = (props.assurance || {})[code], asrBal = (props.balances || {})[code];
  const asr = A ? A.state(asrRec, asrBal) : null;
  const asrVerb = A ? A.verb(asr.stage) : null;

  const num = { fontFamily: "var(--font-data)", fontVariantNumeric: "tabular-nums" };
  const meta = { fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" };
  const panel = { background: "var(--card-surface-raised)", border: "1px solid var(--border)", padding: 14, display: "flex", flexDirection: "column", gap: 10 };
  const sectionHead = { display: "flex", alignItems: "baseline", gap: 10 };

  if (!node) {
    return <EmptyState icon="search" title={"No account " + (props.code || "") + " in this scope"}
      detail="The code is not in the chart of accounts for the selected entity." action="Chart of accounts" onAction={() => props.onGo && props.onGo("coa")} />;
  }

  const ledgerRows = live.length ? lines.map((l) => ({ ...l })) : [];
  const running = opening;

  // 4.2 · E-1: the account is an annotation object. Its version is the live posting count, so a
  // follow-note written before JE-1080 landed reads stale; a pinned one keeps the version it named.
  const Notes = AnnotationProvider || React.Fragment;
  const notesProps = AnnotationProvider ? { objectId: "account:" + code, objectVersion: live.length, author: "you" } : {};
  const Anchor = AnnotationAnchor || ((p) => p.children);
  return (
    <Notes {...notesProps}>
    <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      {/* Identity + closing balance */}
      <div style={{ ...panel, gap: 12 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
          <Button variant="ghost" size="sm" style={{ whiteSpace: "nowrap" }} onClick={() => props.onGo && props.onGo("coa")}>‹ Chart of accounts</Button>
          <span style={meta}>{parent ? parent.code + " · " + parent.name + " ▸ " : ""}{code}</span>
          <div style={{ flex: 1 }} />
          <Badge variant="neutral">{node.type}</Badge>
          <Badge variant="neutral">{creditNormal ? "credit-normal" : "debit-normal"}</Badge>
          {worst && <Badge variant={worst === "confirmed" ? "success" : worst === "overridden" ? "danger" : "warning"} dot>{worst}</Badge>}
          {material && <Badge variant="warning">material</Badge>}
          {asr && <A.Chip record={asrRec} balance={asrBal} showActor />}
          {props.locked && <Badge variant="neutral">period locked</Badge>}
        </div>
        <div style={{ display: "flex", alignItems: "flex-end", gap: 24, flexWrap: "wrap" }}>
          <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>
            <Anchor anchor={{ kind: "header", ref: "account:" + code, label: code + " · " + node.name }}><span style={{ font: "600 22px var(--font-display)", color: "var(--text-primary)" }}>{code} · {node.name}</span></Anchor>
            <span style={meta}>{props.entity || "MER-FUND-I"} · 2026-06 · USD · accrual · {live.length} postings{voids ? " · " + voids + " void" : ""}</span>
          </div>
          <div style={{ flex: 1 }} />
          <div style={{ display: "flex", flexDirection: "column", alignItems: "flex-end", gap: 2 }}>
            <span style={{ fontSize: 10, fontWeight: 600, fontVariantCaps: "all-small-caps", letterSpacing: ".04em", color: "var(--text-muted)" }}>Closing balance · {normalSide}</span>
            <span style={{ ...num, fontSize: 26, fontWeight: 600, color: "var(--text-primary)" }}>{money(closing)}</span>
            <span style={meta}>as at {PERIOD_END}</span>
          </div>
        </div>
        <div style={{ display: "flex", gap: 8, flexWrap: "wrap", borderTop: "1px solid var(--border-divider)", paddingTop: 10 }}>
          <Button variant="primary" size="sm" style={{ whiteSpace: "nowrap" }} disabled={!!props.locked} onClick={() => props.onPostTo && props.onPostTo(code + " · " + node.name)}>Post to this account</Button>
          <Button variant="ghost" size="sm" style={{ whiteSpace: "nowrap" }} disabled={!live.length} onClick={() => props.onViewLedger && props.onViewLedger(code)}>Open in ledger</Button>
          <Button variant="ghost" size="sm" style={{ whiteSpace: "nowrap" }} onClick={() => props.onCopy && props.onCopy(code)}>Copy code</Button>
          {isCash && <Button variant="ghost" size="sm" onClick={() => props.onGo && props.onGo("recon")}>Reconciliation</Button>}
          <div style={{ flex: 1 }} />
          {asr && asrVerb && <Button variant="ghost" size="sm" style={{ whiteSpace: "nowrap" }} disabled={!!props.locked} onClick={() => props.onSign && props.onSign(code)}>{asrVerb} · {money(Math.abs(asrBal ?? closing))}</Button>}
        </div>
      </div>

      {asr && (asr.signatures.length > 0 || asr.voided) && (
        <div style={{ ...panel, gap: 8 }}>
          <div style={sectionHead}><Eyebrow>Assurance</Eyebrow><span style={meta}>each signature is bound to the balance it vouched for</span></div>
          {asr.voided && (
            <Callout tone="danger" title={"Signatures void — balance moved from " + money(Math.abs(asr.movedFrom)) + " to " + money(Math.abs(asrBal))}>
              The chain below was signed against a different number. It is void, not stale: nothing here vouches for the current balance until it is prepared again.
            </Callout>
          )}
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
            <tbody>
              {asr.signatures.map((sg, i) => (
                <tr key={i} style={asr.voided ? { textDecoration: "line-through", color: "var(--text-muted)" } : null}>
                  <td style={{ padding: "6px 0", borderBottom: "1px solid var(--border-divider)", width: 110 }}><A.Chip stage={sg.stage} /></td>
                  <td style={{ padding: "6px 8px", borderBottom: "1px solid var(--border-divider)", color: "var(--text-primary)" }}>{sg.actor} <span style={meta}>· {sg.authority}</span></td>
                  <td style={{ ...num, padding: "6px 8px", borderBottom: "1px solid var(--border-divider)", textAlign: "right" }}>{money(Math.abs(sg.balance))}</td>
                  <td style={{ ...meta, padding: "6px 0 6px 8px", borderBottom: "1px solid var(--border-divider)", textAlign: "right", whiteSpace: "nowrap" }}>{String(sg.at).replace("T", " ").slice(0, 16)}Z</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div style={{ display: "grid", gridTemplateColumns: "minmax(0,1.15fr) minmax(0,.85fr)", gap: 14, alignItems: "start" }}>
        {/* Proof */}
        <div style={panel}>
          <div style={sectionHead}><Eyebrow>Proof</Eyebrow><span style={meta}>opening {openingKnown ? "stated" : "derived from closing − movement"} · voids excluded</span></div>
          <RollforwardTrace opening={opening} openingLabel={"Opening " + (openingKnown ? "" : "(derived)")} closing={closing} closingLabel="Closing per chart of accounts" currency="USD"
            movements={[
              { id: "dr", label: creditNormal ? "Debits (decrease)" : "Debits (increase)", value: creditNormal ? -dr : dr, note: live.filter((l) => Money.toNumber(l.debit)).length + " postings", linkLabel: live.length ? "Postings" : undefined, onOpen: live.length ? () => props.onViewLedger && props.onViewLedger(code) : undefined },
              { id: "cr", label: creditNormal ? "Credits (increase)" : "Credits (decrease)", value: creditNormal ? cr : -cr, note: live.filter((l) => Money.toNumber(l.credit)).length + " postings", linkLabel: live.length ? "Postings" : undefined, onOpen: live.length ? () => props.onViewLedger && props.onViewLedger(code) : undefined },
            ]} />
          {!openingKnown && (
            <span style={meta}>No stated opening for {code} in this fixture — the trace derives it so the closing balance is still proved, and says so rather than printing a bare number.</span>
          )}
        </div>

        {/* Tie state + open items */}
        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div style={panel}>
            <div style={sectionHead}><Eyebrow>Tie state</Eyebrow><span style={meta}>{isCash ? "bank statement ↔ ledger" : "proof ↔ chart of accounts"}</span></div>
            {isCash ? (
              <TieOutPanel title="June cash" context="Custodian statement vs ledger" type="one-to-one" currency="USD" status={tieStatus}
                sourceA={{ label: "Statement", value: stmtTotal, source: "Custodian", count: recL.length }}
                sourceB={{ label: "Ledger", value: bookTotal, source: "GL 1100", count: recR.length }}
                items={[...unmatchedL.map((r) => ({ id: "L" + r.id, label: r.ref + " · " + r.memo, value: r.amount, condition: "unexplained" })),
                        ...unmatchedR.map((r) => ({ id: "R" + r.id, label: r.ref + " · " + r.memo, value: -r.amount, condition: "unexplained" }))]}
                onInvestigate={() => props.onGo && props.onGo("recon")} />
            ) : (
              <TieOutPanel title={code + " · " + node.name} context="Opening + activity vs stated closing" type="rollforward" currency="USD" status="tied"
                sourceA={{ label: "Proof", value: Money.roundMoney(opening + movement, 2), source: "Ledger" }}
                sourceB={{ label: "Chart of accounts", value: closing, source: "Books" }} />
            )}
          </div>
          <div style={panel}>
            <div style={sectionHead}><Eyebrow>Open items</Eyebrow><span style={meta}>settle after {PERIOD_END}</span></div>
            {open.length ? (
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                <tbody>
                  {open.map((l, i) => (
                    <tr key={i} style={{ cursor: "pointer" }} onClick={() => props.onInspect && props.onInspect(l.ref)}>
                      <td style={{ padding: "6px 0", borderBottom: "1px solid var(--border-divider)", fontFamily: "var(--font-data)", color: "var(--accent)" }}>{l.ref}</td>
                      <td style={{ padding: "6px 8px", borderBottom: "1px solid var(--border-divider)", color: "var(--text-secondary)" }}>{l.memo}</td>
                      <td style={{ ...num, padding: "6px 0", borderBottom: "1px solid var(--border-divider)", textAlign: "right", color: "var(--text-primary)" }}>{money((Money.toNumber(l.debit) || 0) - (Money.toNumber(l.credit) || 0))}</td>
                      <td style={{ ...meta, padding: "6px 0 6px 8px", borderBottom: "1px solid var(--border-divider)", textAlign: "right", whiteSpace: "nowrap" }}>due {l.envelope.clocks.settlement}</td>
                    </tr>
                  ))}
                  <tr><td colSpan={2} style={{ padding: "6px 0", fontWeight: 600, color: "var(--text-primary)" }}>Open</td><td style={{ ...num, padding: "6px 0", textAlign: "right", fontWeight: 600, color: "var(--text-primary)" }}>{money(openTotal)}</td><td /></tr>
                </tbody>
              </table>
            ) : <span style={meta}>Nothing outstanding — every posting to {code} settled within the period.</span>}
          </div>
        </div>
      </div>

      {/* Postings */}
      <div style={panel}>
        <div style={sectionHead}><Eyebrow>Postings</Eyebrow><span style={meta}>2026-06 · running balance from {money(running)} · click a row for the whole entry</span></div>
        {ledgerRows.length ? (
          <LedgerTable rows={ledgerRows} currency="USD" opening={opening} normalSide={normalSide} caption={code + " · " + node.name + " · 2026-06"}
            onRowClick={(row) => props.onInspect && props.onInspect(row.ref)} />
        ) : (
          <EmptyState icon="inbox" title="No postings this period" detail={"The closing balance carries from the opening — nothing has moved " + code + " in 2026-06."} />
        )}
        {AnnotationPanel && <AnnotationPanel title="Annotations on this account" emptyText="Press ¶ beside the account name to anchor a note. Follow-notes go stale when a posting lands; pinned notes keep their version." />}
      </div>

      {/* Envelope */}
      {envelopes.length > 0 && (
        <div style={panel}>
          <div style={sectionHead}><Eyebrow>Value envelope</Eyebrow><span style={meta}>{conditions.join(" · ")} · {envelopes.length} of {live.length} postings carry an envelope</span></div>
          <EnvelopeCoherence envelopes={envelopes} />
          {conditions.includes("estimated") && (
            <Callout tone="warning" title="Estimated figures in this balance">
              At least one posting is an estimate (declared rate × record-date shares). The closing balance is provisional to that extent until the settlement clock lands.
            </Callout>
          )}
        </div>
      )}
    </div>
    </Notes>
  );
}

window.AcctAccountSheetView = AcctAccountSheetView;
if (typeof module !== "undefined") module.exports = { AcctAccountSheetView };
