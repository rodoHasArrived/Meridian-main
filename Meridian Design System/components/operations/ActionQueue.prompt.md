The in-flight tray. `ActionEnvelope` says what became of *one* instruction where it was issued; `ActionQueue` keeps *every* unresolved instruction on the screen until it resolves — so nothing an operator submitted can quietly vanish behind a toast.

Rows climb a ladder derived from the envelope's four fates, no new words: **submitted → acknowledged → confirmed | rejected | timed out**. Each row shows its idempotency key, elapsed clock, receipt when one lands, the rejection reason verbatim, and — for a timed-out instruction — one primary verb: *Retry · same key*. Confirmed rows leave after a few seconds; rejected rows leave once acknowledged; timed-out rows never leave on their own.

```jsx
<ActionEnvelopeProvider actions={actions} transport={transport}>
  <ActionEnvelope action="basket.commit" submitLabel="Commit basket" />
  <ActionQueue onOpen={(i) => go(i.action)} />        {/* corner tray, hides when empty */}
  <StatusBar items={[…, { label: "Actions", value: <ActionQueueChip /> }]} />
</ActionEnvelopeProvider>
```

Rules. Mount one queue per screen, outside the work plane, above the status bar (`docked="corner"`) or in an operations rail (`docked="inline"`). Never replace it with a toast: a toast is fire-and-forget and an unlanded instruction is neither. A double click is still one instruction — the provider dedupes on the key, so the queue never shows two rows for one intent.
