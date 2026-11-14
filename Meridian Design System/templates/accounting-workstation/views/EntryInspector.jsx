// Entry inspector — tier 2 of the drill ladder (row → drawer → screen,
// guidelines/PROGRESSIVE_DISCLOSURE.md). A GL row is one side of a journal entry; clicking it
// opens the whole entry: every line, the Σ debit = Σ credit proof, the envelope that governs
// its figures, and the only correction the books allow — a reversal, never an edit.
function AcctEntryInspector(props) {
  const DS = window.MeridianDesignSystem_205d17;
  if (!props.entryRef || !DS) return null;
  const { Drawer, DrawerBody, Button, Badge, Eyebrow, Callout, EnvelopePanel, EnvelopeCoherence, Money } = DS;

  const rows = props.rows || [];
  const lines = rows.filter((r) => r.ref === props.entryRef);
  if (!lines.length) return null;

  const money = (v) => Money.formatMoney(v, { currency: "USD" });
  const dr = Money.sumAmounts(lines.map((l) => Money.toNumber(l.debit) || 0));
  const cr = Money.sumAmounts(lines.map((l) => Money.toNumber(l.credit) || 0));
  const out = Money.roundMoney(dr - cr, 2);
  const first = lines[0];
  const voided = lines.every((l) => l.status === "void");
  const envelopes = lines.map((l) => l.envelope).filter(Boolean);
  const controlling = envelopes[0];
  const reversedBy = props.reversedBy;
  const reverses = first.reversalOf;
  // The way back out (4.4 · E-2). An entry that came from a break can open it; the loop appears
  // only when this entry actually carries the link, so it is never a dead promise.
  const M = window.MeridianAppMap;
  const loops = M ? M.loops("entry", first) : [];
  const { ReturnPath } = DS;

  const cell = { padding: "7px 10px", fontFamily: "var(--font-data)", fontSize: 12, borderBottom: "1px solid var(--border-divider)" };
  const num = { ...cell, textAlign: "right", fontVariantNumeric: "tabular-nums" };
  const head = { padding: "6px 10px", fontFamily: "var(--font-body)", fontSize: 10, fontWeight: 600, fontVariantCaps: "all-small-caps", letterSpacing: ".04em", color: "var(--text-muted)", borderBottom: "1px solid var(--border)", textAlign: "left" };
  const fact = (k, v) => (
    <div style={{ display: "flex", gap: 10, alignItems: "baseline" }}>
      <span style={{ minWidth: 92, fontSize: 10, fontWeight: 600, fontVariantCaps: "all-small-caps", letterSpacing: ".04em", color: "var(--text-muted)" }}>{k}</span>
      <span style={{ fontFamily: "var(--font-data)", fontSize: 12, color: "var(--text-primary)" }}>{v}</span>
    </div>
  );

  return (
    <Drawer open onClose={props.onClose} title={"Journal entry " + props.entryRef} size="640px">
      <DrawerBody>
        <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
            <Badge variant={voided ? "danger" : "success"} dot>{voided ? "Voided" : "Posted"}</Badge>
            {reversedBy && <Badge variant="warning">Reversed</Badge>}
            {reverses && <Badge variant="neutral">Reversal</Badge>}
            <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
              {lines.length} lines · {props.entity || "MER-FUND-I"} · 2026-06
            </span>
          </div>

          <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
            {fact("Memo", first.memo || "—")}
            {fact("Posted", "2026-" + first.date)}
            {fact("Basis", "Accrual · USD")}
          </div>

          {ReturnPath && loops.length ? (
            <div style={{ display: "flex", flexDirection: "column", gap: 7 }}>
              <Eyebrow>Where this came from</Eyebrow>
              <ReturnPath loops={loops} label="" />
            </div>
          ) : null}

          {reversedBy && (
            <Callout tone="warning" title={"Reversed by " + reversedBy}>
              This entry stands in the ledger; its effect is undone by a later entry. Books are corrected
              by reversal, never by edit — both entries remain in the audit trail.
            </Callout>
          )}
          {reverses && (
            <Callout tone="info" title={"Reverses " + reverses}>
              Equal and opposite to the original entry, posted in the current open period.
            </Callout>
          )}

          <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
            <Eyebrow>Entry lines</Eyebrow>
            <table style={{ width: "100%", borderCollapse: "collapse", background: "var(--card-surface-raised)", border: "1px solid var(--border)" }}>
              <thead>
                <tr>
                  <th scope="col" style={head}>Account</th>
                  <th scope="col" style={{ ...head, textAlign: "right" }}>Debit</th>
                  <th scope="col" style={{ ...head, textAlign: "right" }}>Credit</th>
                </tr>
              </thead>
              <tbody>
                {lines.map((l, i) => (
                  <tr key={i} style={l.status === "void" ? { textDecoration: "line-through", color: "var(--text-muted)" } : null}>
                    <td style={cell}>{l.account}</td>
                    <td style={num}>{Money.toNumber(l.debit) ? money(l.debit) : "—"}</td>
                    <td style={num}>{Money.toNumber(l.credit) ? money(l.credit) : "—"}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td style={{ ...cell, borderBottom: "none", fontWeight: 600 }}>
                    {out === 0 ? "Balanced" : "Out by " + money(Math.abs(out))}
                  </td>
                  <td style={{ ...num, borderBottom: "none", fontWeight: 600, color: out === 0 ? "var(--text-primary)" : "var(--red)" }}>{money(dr)}</td>
                  <td style={{ ...num, borderBottom: "none", fontWeight: 600, color: out === 0 ? "var(--text-primary)" : "var(--red)" }}>{money(cr)}</td>
                </tr>
              </tfoot>
            </table>
            {voided && (
              <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)" }}>
                Void — excluded from the running balance, the trial balance, and every statement.
              </span>
            )}
          </div>

          {controlling && (
            <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
              <Eyebrow>Value envelope</Eyebrow>
              <EnvelopePanel title={props.entryRef + " · " + money(dr)}
                envelope={{ ...controlling, value: money(dr), label: "Entry total (debit side)" }} />
              {envelopes.length > 1 && <EnvelopeCoherence envelopes={envelopes} />}
            </div>
          )}

          <div style={{ borderTop: "1px solid var(--border)", paddingTop: 14, display: "flex", gap: 8, flexWrap: "wrap" }}>
            <Button variant="ghost" size="sm" onClick={() => props.onOpenAccount && props.onOpenAccount(first.account)}>
              Account sheet · {first.account.slice(0, 4)}
            </Button>
            <Button variant="ghost" size="sm" onClick={() => props.onViewAccount && props.onViewAccount(first.account)}>
              Open account ledger
            </Button>
            <Button variant="ghost" size="sm" onClick={() => props.onCopyRef && props.onCopyRef(props.entryRef)}>
              Copy reference
            </Button>
            <div style={{ flex: 1 }}></div>
            <Button variant="danger" size="sm" disabled={!!props.reverseDisabled}
              onClick={() => props.onReverse && props.onReverse(props.entryRef)}>
              Reverse entry
            </Button>
          </div>
          {props.reverseDisabled && (
            <span style={{ fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-muted)", marginTop: -8 }}>
              {props.reverseReason}
            </span>
          )}
        </div>
      </DrawerBody>
    </Drawer>
  );
}

window.AcctEntryInspector = AcctEntryInspector;
if (typeof module !== "undefined") module.exports = { AcctEntryInspector };
