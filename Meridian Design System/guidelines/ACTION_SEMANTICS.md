# Action semantics — the fate of an instruction

**Normative.** Rule ids `A-1…A-9`. Enforced (adoption only) by `tests/conformance.html` rule **C-13**; exercised against a transport by `tests/action-contract.html`. Companion to `ADAPTER_SEMANTICS.md` (which states what a failed adapter means) and `DECISION_SEMANTICS.md` (which states how a decision is reached).

## Why this exists

This system has an extraordinary vocabulary for the **provenance of a figure** — envelope, authority, condition, time role, evidence, coverage, trace. It had almost none for the **fate of an instruction**. `ActionGuard` answers *may I*. Nothing answered *did it*.

The evidence was plain. Twenty-seven screens, zero I/O. `DEPLOYMENT_CHECKLIST_4_1` asks, in prose, per action: endpoint · idempotency key · permission · optimistic-or-confirmed · failure surface — answered by a human, once, for three screens. `ADAPTER_SEMANTICS` states that a failed adapter leaves an instruction **unlanded**, and no control that submits one said the word. `trading-desk` orders rested or filled because a timer said so.

## The four fates, and the one that is missing

| Fate | What it means | What the operator may do |
| --- | --- | --- |
| **in flight** | Sent. No answer yet. | Wait. A second submit is the same instruction, not a new one. |
| **landed** | Acknowledged by the system of record, with a receipt. | Proceed; the receipt is the evidence. |
| **unlanded** | The adapter did not answer. The fate is **unknown**. | Retry **with the same idempotency key**, or reconcile before deciding. |
| **rejected** | Decided, and refused, with a reason. | Change something. Resubmitting unchanged will be refused again. |

**There is no "failed".** A timeout that reads as failure is how a desk sends the same order twice. "No" and "no answer" are different facts and only one of them is safe to act on.

## The rules

**A-1 · Declare before you attempt.** Every mutating control declares its endpoint, idempotency key, permission, posture and failure surface — in the markup, where a reviewer can read it. An undeclared key or failure surface renders **as a defect**, not as a blank.

**A-2 · The idempotency key is the instruction's identity.** It is declared up front (a ticket token, a period+account, a job signature), never generated at send time. Two submits carrying one key are one instruction.

**A-3 · A double click is not two instructions.** The submitting layer dedupes on the key while in flight or landed, and says it deduped. This is a test, not an incident.

**A-4 · Silence is unlanded.** A thrown error, a dropped connection, or no answer inside the declared window is `unlanded`. Only an explicit refusal **with a reason** is `rejected`; a refusal without one is treated as unlanded, because it did not tell the operator anything they can act on.

**A-5 · A retry after unlanded reuses the key and increments the attempt.** It asks "did the first one land?" — it does not send a second instruction. A fresh key on a retry is the defect this whole contract exists to prevent.

**A-6 · Posture is declared, not assumed.** `confirmed` shows no local effect until the instruction lands (money, orders, postings, deliveries). `optimistic` shows the effect immediately, marked provisional, and reconciles on landing (queue positions, view state, non-consequential toggles). A consequential action is never optimistic.

**A-7 · An unresolved instruction outlives its toast.** In flight and unlanded instructions stay in an `InstructionLedger` until somebody resolves them. A disappearing notification is not a record.

**A-8 · Permission is consumed visibly.** The envelope names the permission and whether it is held; an unheld permission disables the submit and says which permission is missing — never a dead button.

**A-9 · The design system ships the contract, not a transport.** Screens keep their fixtures. The mock that lies convincingly — latency, replay, a lost answer, silence — lives in `tests/mock-transport.js`, and the fates are exercised there rather than asserted in prose.

## Using it

```jsx
<ActionEnvelopeProvider
  actions={{ "order.submit": {
    label: "Submit order", endpoint: "POST /v1/orders",
    idempotencyKey: draft.token,                        // A-2
    permission: { required: "trade.submit", held: true, actor: "k.ito" },
    posture: "confirmed",                               // A-6
    failureSurface: "Blotter row · status bar · instruction ledger",
    payload: draft } }}
  transport={desk.transport} timeoutMs={6000}>          {/* A-4 */}
  <ActionEnvelope action="order.submit" submitLabel="Send order" />
  <InstructionLedger />                                 {/* A-7 */}
</ActionEnvelopeProvider>

<ActionStateChip state="unlanded" attempt={2} />         {/* in a blotter row */}
```

## The transport contract

```
resolve { ok: true, id }         → landed
resolve { ok: false, reason }    → rejected
throw / no answer in timeoutMs   → unlanded
```

A transport that has already recorded a key returns the first receipt with `duplicateOf` set. That is what makes A-5 safe, and it is the behaviour `tests/action-contract.html` asserts against the "wrote it, lost the answer" case — the one that turns a retry into a duplicate order in systems that do not have this contract.

## Open question, stated rather than hidden

Whether a Meridian screen ends up bound to a **real** API or to a convincing mock is still the deployment team's call. The contract is worth writing either way: it is what makes the deployment checklist mechanical instead of a promise. What a mock cannot tell you is which endpoints exist and what they reject.
