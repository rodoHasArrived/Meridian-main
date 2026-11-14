The write side of the system. `ActionGuard` answers *may I*; `ActionEnvelope` answers *did it*. Reach for it on every control that mutates something outside the screen — submit an order, enqueue a backfill, post a journal, deliver a report pack.

Two halves. The **declaration** (endpoint · idempotency key · permission · posture · failure surface) is stated before the attempt, so the deployment checklist is answered by the component rather than by prose. The **fate** is one of exactly four: in flight · landed · **unlanded** · rejected. There is no "failed" — an adapter that does not answer leaves the instruction's fate unknown, and the retry therefore carries the *same* idempotency key.

```jsx
<ActionEnvelopeProvider
  actions={{ "order.submit": { label: "Submit order", endpoint: "POST /v1/orders",
    idempotencyKey: draft.token, permission: { required: "trade.submit", held: true, actor: "k.ito" },
    posture: "confirmed", failureSurface: "Blotter row · status bar", payload: draft } }}
  transport={desk.transport}>
  <ActionEnvelope action="order.submit" submitLabel="Send order" />
  <InstructionLedger />                         {/* every instruction this session, and its fate */}
</ActionEnvelopeProvider>

<ActionStateChip state="unlanded" attempt={2} />  {/* inline, in a blotter row */}
```

Rules. A double-clicked Submit is one instruction: the provider dedupes on the declared key. `posture="confirmed"` shows no local effect until the instruction lands; `"optimistic"` shows it immediately, marked provisional. A rejection must carry a reason — a refusal without one is treated as unlanded, because "no" and "no answer" are different facts. Never render an unlanded instruction as an error toast that disappears: it stays in the `InstructionLedger` until someone resolves it.

Retry an individual instruction with `retry(action, instruction.id)` so the selected row retains its exact key, payload, and next attempt. `ActionQueue` does this automatically. The action-only `retry(action)` form remains available and selects the latest unresolved instruction for that action. An unknown id, in-flight instruction, or landed instruction is a no-op; simultaneous retries of the same key share one attempt.
