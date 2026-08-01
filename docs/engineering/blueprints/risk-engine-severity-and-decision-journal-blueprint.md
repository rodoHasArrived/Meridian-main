# Blueprint — Risk Engine: Severity-Aware Evaluation and Pre-Trade Decision Journal

**Status:** draft
**Owner:** Execution and Fund Accounts lane
**Reviewed:** 2026-08-01
**Depth mode:** full
**Related roadmap item:** `W9-SAFETY-007` (Kill-switch cancel-all and fat-finger, notional, and
collar rules) — this blueprint is the *engine* prerequisite, not the rule catalogue.

---

## ⚠️ Breaking Change

This blueprint changes four public surfaces across `Meridian.Execution.Sdk`, `Meridian.Risk`, and
`Meridian.Execution`.

| Surface | Change | Known consumers | Migration |
| --- | --- | --- | --- |
| `RiskRuleSeverity` | **Moves** from `Meridian.Risk` (`IRiskRule.cs:34`) to `Meridian.Execution.Sdk` | `IRiskRule`, `DrawdownGuardrailRule`, `CompositeRiskValidator` | Namespace-only; add a `using`. See "Assembly placement" below for why the move is mandatory |
| `IRiskRule.EvaluateAsync` / `TryEvaluate` | Return `RiskFinding?` instead of `RiskValidationResult` (`null` = pass) | 4 implementations: `DrawdownCircuitBreaker`, `OrderRateThrottle`, `PositionLimitRule` (`src/Meridian.Risk/Rules/`), `DrawdownGuardrailRule` (`src/Meridian.Ui.Shared/Services/`) | `RiskValidationResult.Approved()` → `null`; `RiskValidationResult.Rejected(r)` → `new RiskFinding(code, r, …)`. The rule no longer chooses whether it blocks — see Decision 1 |
| `CompositeRiskValidator.ValidateOrderAsync` | Evaluates all rules instead of returning on first failure | `OrderManagementSystem.cs:303` (sole call site, via `IRiskValidator`) | None required — the OMS reads `IsApproved`/`RejectReason`, which are preserved |
| `RiskValidationResult` | Gains `Decision` and `Violations`; `IsApproved` and `RejectReason` become computed | 107 read sites; **zero** direct-initializer construction sites | None — verified that all 24 construction sites go through `Approved()` / `Rejected(string)`, both of which are retained |
| `OrderResult` | Gains an optional `RiskDecisionSummary` | `Meridian.Execution.Sdk/Models.cs:169`; browser workstation submit path | Additive; existing readers unaffected. Required so the order ticket can render findings on the *admitted* path — see Decision 7 |

**One existing test asserts the behaviour being removed:**
`tests/Meridian.Tests/Risk/CompositeRiskValidatorTests.cs` →
`ValidateOrderAsync_WhenPriorityRuleRejects_ShortCircuitsBeforeLaterRules`. It must be rewritten to
assert the *replacement* guarantee (all rules evaluated, violations ordered by severity then
priority). Do not delete it — invert it.

No F# signature changes. No storage-format changes. No REST response removals (additive only).

### Assembly placement (this constrains everything below)

`Meridian.Risk` references `Meridian.Execution` (`Meridian.Risk.csproj:14`). `Meridian.Execution`
does **not** reference `Meridian.Risk`, and must not — that would be a project cycle and would not
compile.

`RiskValidationResult` lives in `Meridian.Execution`. So every type it exposes must resolve from an
assembly that both `Meridian.Execution` and `Meridian.Risk` can see. `Meridian.Execution.Sdk` is
that assembly: it references only `Meridian.Contracts`, both projects already reference it, and it
already owns `OrderRequest` and `OrderResult`.

**Every type this blueprint introduces or moves, and where it lives.** Three separate review
findings on this document were the same mistake — a type left in `Meridian.Risk` that
`Meridian.Execution` then had to reference. This table is the single source of truth; the code
blocks below follow it, and any snippet that disagrees with it is wrong.

| Type | Assembly | Why |
| --- | --- | --- |
| `RiskRuleSeverity` | `Meridian.Execution.Sdk` | **Moved** from `Meridian.Risk`. Referenced by `RiskViolation`, which `RiskValidationResult` exposes |
| `RiskFinding` | `Meridian.Execution.Sdk` | Returned by `IRiskRule`; read by the validator |
| `RiskViolation` | `Meridian.Execution.Sdk` | Exposed on `RiskValidationResult` |
| `RiskDecisionKind` | `Meridian.Execution.Sdk` | Referenced by `RiskDecisionSummary`, which the SDK's own `OrderResult` exposes |
| `RiskDecisionSummary` | `Meridian.Execution.Sdk` | Carried on `OrderResult` |
| `IRiskReservation` | `Meridian.Execution.Sdk` | **Handed to the OMS** for settlement, so `Meridian.Execution` must see it |
| `RiskValidationResult` | `Meridian.Execution` | **Unchanged location.** Extended in place |
| `IRiskValidator` | `Meridian.Execution` | Unchanged location |
| `IRiskDecisionJournal`, `RiskDecisionJournalEntry`, `RiskJournalOptions`, `RiskDecisionJournal` | `Meridian.Execution` | Injected into the OMS |
| `IRiskRule`, `IReservingRiskRule`, `CompositeRiskValidator`, rule implementations | `Meridian.Risk` | Rule-facing surface. Nothing in `Meridian.Execution` references these |

The rule of thumb behind the table: **if the OMS or `RiskValidationResult` touches it, it cannot
live in `Meridian.Risk`.**

---

## Scope

**In scope**

- Severity becomes decisional rather than decorative: `Info`/`Warning` annotate and admit,
  `Error`/`Critical` block.
- All applicable rules are evaluated for every order; the operator sees the complete violation set,
  not just the first one.
- `RiskValidationResult` carries structured `RiskViolation` records (rule, severity, code, message,
  observed value, limit).
- A durable pre-trade decision journal, written through the existing WAL-backed
  `ExecutionAuditTrailService`.
- Surfacing the F# `RiskDecision` model (`Approve | Reject | Escalate`) that already exists and is
  currently unreachable from C#.
- An additive `GET /api/risk/decisions` read surface plus the dashboard panel that consumes it.

**Out of scope**

- **New rule implementations.** Fat-finger, max-notional, price-collar, and kill-switch belong to
  `W9-SAFETY-007`. This blueprint makes them cheap to add; it does not add them.
- **Margin headroom as a rule.** Separate blueprint; depends on this one.
- **Mandate / restricted-list compliance.** Separate blueprint; depends on this one.
- Post-trade or passive-breach monitoring.
- Any change to `ExecutionOperatorControlService`'s upstream gate, which runs *before* the risk
  validator and keeps its own manual-override/bypass semantics.

**Assumptions**

- `ExecutionAuditTrailService` remains the durable execution-evidence path (WAL-backed, ADR-007).
  Verify it is registered in the host being targeted — `OrderManagementSystem` treats `_auditTrail`
  as nullable and silently skips journaling when it is absent.
- The rule set stays small (single-digit count) per order, so evaluating all rules is not a
  throughput concern on the paper/live order path. Revisit if the catalogue exceeds ~20 rules.
- Escalation has no approval workflow behind it yet. A rule requests it via
  `RiskFinding.RequiresAcknowledgement`; this blueprint admits such orders with a recorded
  violation. Wiring escalation to a blocking operator approval gate is follow-on work — see Open
  Question 1.

---

## Architectural Overview

### What the code actually does today

Four findings from the current source drive every decision below.

1. **Severity is decorative.** `IRiskRule.Severity` (`src/Meridian.Risk/IRiskRule.cs:22`) declares
   `Info | Warning | Error | Critical`. The only consumer in the repository is
   `CompositeRiskValidator.cs:45`, where it is a structured-logging argument. A `Warning` rule
   blocks exactly as hard as `DrawdownGuardrailRule`'s `Critical`
   (`src/Meridian.Ui.Shared/Services/DrawdownGuardrailRule.cs:26`).

2. **First failure wins.** `CompositeRiskValidator.ValidateOrderAsync`
   (`src/Meridian.Risk/CompositeRiskValidator.cs:31-50`) returns on the first non-approved result.
   An order breaching three limits reports one.

3. **The F# domain already models this.** `src/Meridian.FSharp/Risk/RiskTypes.fs` defines
   `RiskDecision = Approve | Reject of reason | Escalate of reason`. `Interop.fs:245` carries a
   `DecisionKind` discriminator, and `Interop.fs:301` exposes `RiskInterop.Aggregate`. **All of it
   is unreachable from production C#:** `RiskInterop.Aggregate` has zero callers, and both
   interop-backed rules discard the discriminator —
   `PositionLimitRule.cs:64` and `DrawdownCircuitBreaker.cs:47` check only `decision.Approved` and
   take `decision.Reasons.FirstOrDefault()`, collapsing an array to one string.

4. **The journal plumbing already exists and is unused by this path.**
   `OrderManagementSystem.RejectOrderAsync` (`OrderManagementSystem.cs:1658`) already accepts
   `reasonCode` and `metadata` and writes a WAL-backed `ExecutionAuditEntry` with `Reason`, `Scope`,
   and `Metadata` fields (`ExecutionAuditTrailService.cs:25`). Two other gates populate them —
   `LIVE_ORDER_READINESS_REJECTED` (line 246) and `OPERATOR_CONTROL_REJECTED` (line 273). The risk
   call site at line 303 passes neither.

The through-line: **most of this feature is already built and disconnected.** The design below
connects it rather than replacing it.

### Context diagram

```
OrderManagementSystem.SubmitOrderAsync
  │
  ├─ live-order readiness gate ─────────────► RejectOrderAsync(reasonCode, metadata)  [exists]
  ├─ operator-control gate ─────────────────► RejectOrderAsync(reasonCode, metadata)  [exists]
  ├─ security-master gate ──────────────────► RejectOrderAsync(...)                   [exists]
  │
  └─ IRiskValidator.ValidateOrderAsync ──────────────────────────────────┐
                                                                        │
     ┌──────────────────────────────────────────────────────────────────┘
     │  CompositeRiskValidator                            [MODIFIED]
     │    ├─ evaluate EVERY rule → RiskFinding?           [NEW type]
     │    ├─ collect RiskViolation[]                      [NEW type]
     │    ├─ RiskDecisionPrecedence.Resolve(outcomes)     [NEW]
     │    └─ settle reservations: IRiskReservation        [NEW] ── keeps the throttle atomic
     │
     └─► RiskValidationResult { Decision, Violations, IsApproved, RejectReason }  [EXTENDED]
              │
              ├─ blocked  → RejectOrderAsync(reasonCode: "RISK_<CODE>", metadata: violations)
              └─ admitted → RiskDecisionJournal.RecordAdmittedAsync(...)          [NEW]
                                     │
                                     └─► ExecutionAuditTrailService.RecordAsync   [exists, WAL]
                                                │
                                                └─► GET /api/risk/decisions       [NEW, additive]
```

### Design decisions

**Decision 1 — A rule reports a `RiskFinding`; the validator decides what it means.**

*Alternatives considered:* (a) extend `RiskValidationResult` and use it for both levels;
(b) return `RiskDecisionDto` from F# directly; (c) have the rule return an outcome kind
(`Pass`/`Annotate`/`Escalate`/`Block`) alongside the finding.

*Rationale:* A rule reports one finding; the validator reports a set. Conflating them is what
produced today's "first failure is the whole answer" behaviour. Option (b) leaks the interop DTO
into every rule, including the three that never touch F#.

Option (c) was the original shape of this blueprint and is **wrong**, for a reason worth recording:
if the rule picks the outcome kind, severity is not actually decisional. A rule declaring
`Severity = Warning` could still return `Block`, and an `Error` rule could annotate — the two would
be free to contradict each other, which is the same class of bug as today's decorative severity,
just relocated. It also admits nonsense states (`Block` with no violation, `Pass` with one) that the
aggregate cannot faithfully summarise.

So the rule returns `RiskFinding?` — `null` means "no finding", and a finding carries *what was
measured*, never *what should happen about it*. The validator derives the outcome from
`rule.Severity`. Severity becomes the single lever, by construction rather than by convention.

*Consequences:* Four rule implementations change signature (mechanical). Escalation needs an
explicit channel, since it is not a severity level — `RiskFinding.RequiresAcknowledgement` carries
it. `RiskValidationResult`'s read surface is preserved, so the OMS and all 107 read sites are
untouched. Invalid kind/violation combinations become unrepresentable.

**Decision 2 — `RiskValidationResult` keeps `IsApproved` and `RejectReason` as computed members.**

*Alternatives considered:* a clean-break new type with a compatibility shim.

*Rationale:* Verified that all 24 construction sites use the `Approved()` / `Rejected(string)`
factories and **no** site uses an object initializer. Computing `IsApproved` from `Decision` is
therefore invisible to every existing caller, which collapses the migration to the four rules.

*Consequences:* `required bool IsApproved` becomes a computed property — a source-compatible change
here only because of the verified absence of initializer construction. Re-verify before
implementing if the branch has drifted.

**Decision 3 — Severity precedence resolves in C#, not in `RiskEvaluation.aggregate`.**

*Alternatives considered:* extend the F# `aggregate` to be severity-aware and call
`RiskInterop.Aggregate` from `CompositeRiskValidator`.

*Rationale:* `RiskEvaluation.aggregate` (`src/Meridian.FSharp/Risk/RiskEvaluation.fs`) is
`Seq.tryFind (not Approve)` — first-non-approve-wins, with no severity concept, because
`RiskDecision` carries no severity. Making it severity-aware means adding severity to the F# union,
which is a wider domain change than this blueprint should force. The C# side owns severity
(`IRiskRule.Severity`), so precedence belongs there.

*Consequences:* `RiskInterop.Aggregate` stays uncalled. Flag it in Open Questions rather than
silently leaving dead code — either wire it later or archive it.

**Decision 4 — Stateful rules reserve during evaluation and commit or roll back afterwards.**

*Rationale:* This is the trap in "evaluate all rules." `OrderRateThrottle.EvaluateAsync`
(`src/Meridian.Risk/Rules/OrderRateThrottle.cs`) **mutates state on the pass path** — it enqueues
`now` into `_recentOrders` when the order is under the ceiling. Under today's short-circuit,
a rule after a rejection never runs, so it never records. Evaluate-all would make the throttle count
orders that were subsequently blocked by another rule, corrupting the rate window and causing
spurious throttling after any rejection burst.

*Alternatives considered:* a post-decision `OnOrderAdmitted(OrderRequest)` callback that re-does the
enqueue. That was this blueprint's original shape and is **unsafe**: today the throttle holds one
lock across purge → count → enqueue, so the ceiling check and the record are atomic. Splitting them
into "read under lock" then "write under lock later" reintroduces a time-of-check/time-of-use race —
N concurrent submissions can each observe room below the cap, all pass evaluation, and then all
commit, overshooting the ceiling by up to N. The throttle's whole purpose is to be exact at the
boundary, so this is not an acceptable trade.

*Consequences:* Evaluation performs an atomic **reserve** under the rule's existing lock — the same
purge → count → *reserve* sequence it does today, so capacity is consumed at check time and
concurrent callers cannot double-spend it. The validator then commits the reservation when the
aggregate admits the order, or rolls it back when it does not. Reservations are per-evaluation and
must be released on every path, including exceptions and cancellation, or the throttle leaks
capacity and eventually blocks everything. That makes the release path itself a first-class test
target.

**Decision 5 — The OMS owns journaling, not the validator.**

*Alternatives considered:* `CompositeRiskValidator` writes the journal entry directly (this
blueprint's original shape).

*Rationale:* Two independent reasons, both fatal to the original shape.

First, the validator does not know the order id. `OrderManagementSystem.PlaceOrderAsync` computes
`orderId = request.ClientOrderId ?? GenerateOrderId()` at line 160 and calls
`ValidateOrderAsync(safeRequest, ct)` — the id is never passed in, and the browser submit path does
not supply a `ClientOrderId`, so it is generated locally and exists only inside the OMS. A journal
keyed on order id cannot be written from inside the validator without inventing a second context
parameter.

Second, it would double-write. `RejectOrderAsync` already writes an `Order`/`OrderRejected` audit
entry for every rejection. A validator-side journal write would add a `Risk`/`PreTradeDecision`
entry for the same event, giving audit consumers two records for one decision and contradicting this
blueprint's own claim that rejections cost no extra write.

*Consequences:* One canonical record per decision, written by the OMS, which already holds
`orderId`, `actor`, `runId`, `correlationId`, `sessionId`, and `brokerName` (extracted at
`OrderManagementSystem.cs:160-172`):

| Decision | Record written | New write? |
| --- | --- | --- |
| `Rejected` | The existing `Order`/`OrderRejected` entry, now carrying `reasonCode` and the violation metadata | No |
| `Escalated` / `ApprovedWithWarnings` | One `Risk`/`PreTradeDecision` entry | Yes |
| `Approved` (clean) | One `Risk`/`PreTradeDecision` entry only when `JournalCleanApprovals` is set | Opt-in |

`/api/risk/decisions` projects over both categories, so a reader sees every decision regardless of
which record carries it.

**Decision 6 — Journal every decision that carries a violation; gate clean approvals behind config.**

*Rationale:* Evidence value concentrates in decisions that were not clean. Writing a WAL record for
every admitted order on a high-throughput path buys little and costs on the hot path.

*Consequences:* `RiskJournalOptions.JournalCleanApprovals` defaults to `false`. Any decision with
≥1 violation — including `Warning`s that were admitted — is always journaled. Operators who need
complete records flip one flag.

**Decision 7 — `OrderResult` carries the decision back to the caller.**

*Rationale:* The order ticket must render findings on the *admitted* path, and there is currently no
route for them. The OMS discards `riskResult` once `IsApproved` is true, and `OrderResult`
(`Meridian.Execution.Sdk/Models.cs:169`) exposes only `Success`, `OrderId`, `ErrorMessage`, and
`OrderState`. The `/api/risk/decisions` history endpoint cannot substitute: it is written
asynchronously and offers no deterministic read-back for the submission that just returned.

*Consequences:* `OrderResult` gains an optional `RiskDecisionSummary`. Additive, so existing readers
are unaffected, but the browser workstation's TypeScript submit contract must be extended in the
same PR as the panel that consumes it.

**Decision 8 — Reservations settle at the routing boundary, not the validation boundary.**

*Rationale:* Decision 4 gives stateful rules reserve/commit semantics so a blocked order does not
consume throttle capacity. But committing as soon as the validator returns is still wrong: passing
the risk gate is not the same as being routed. Between the two, the OMS can still fail to register
the client-order id, the audit write can fail or be cancelled, or the gateway submission can be
rejected. In each case no order reaches the venue, yet its slot stays counted for the rest of the
window — the same over-counting Decision 4 exists to prevent, moved one step later.

*Consequences:* `ValidateOrderAsync` returns the reservations alongside the result, and the **OMS**
settles them: commit once the gateway has accepted the order, roll back on every earlier failure
path. The `finally` that guarantees settlement therefore lives in the OMS, not the validator. The
"routed orders exactly" claim this blueprint makes for the throttle is only true with the commit at
this boundary.

*Cost:* `IRiskValidator.ValidateOrderAsync`'s return type has to carry the reservations, which
widens the interface change beyond what Decision 2's compatibility argument covers. `IsApproved` and
`RejectReason` still behave identically for the 107 existing read sites; only the OMS call site
learns about reservations.

---

## Interface and API Contracts

### New types — `Meridian.Execution.Sdk`

These live in the SDK, not in `Meridian.Risk`, because `RiskValidationResult` (in
`Meridian.Execution`) exposes them and `Meridian.Execution` cannot reference `Meridian.Risk`. See
"Assembly placement" above.

```csharp
namespace Meridian.Execution.Sdk;

/// <summary>
/// How seriously a rule treats its own findings. This is the single lever that decides
/// admission: the validator maps it to an outcome, and no rule can override that mapping.
/// Moved here from Meridian.Risk so both Meridian.Execution and Meridian.Risk can see it.
/// </summary>
public enum RiskRuleSeverity
{
    /// <summary>Recorded and surfaced. Admits.</summary>
    Info,

    /// <summary>Recorded and surfaced more prominently. Admits.</summary>
    Warning,

    /// <summary>Blocks.</summary>
    Error,

    /// <summary>Blocks. Reserved for guardrails whose breach implies a halt.</summary>
    Critical
}

/// <summary>
/// What a rule measured, and against what. A finding never states what should happen about
/// it — that is <see cref="RiskRuleSeverity"/>'s job, resolved by the validator.
/// </summary>
/// <param name="Code">Stable SCREAMING_SNAKE identifier, e.g. <c>POSITION_LIMIT_EXCEEDED</c>.</param>
/// <param name="Message">Human-readable summary for operator surfaces.</param>
/// <param name="ObservedValue">What the rule measured, when expressible as a number.</param>
/// <param name="LimitValue">What the rule measured against, when expressible as a number.</param>
/// <param name="RequiresAcknowledgement">
/// Requests escalation rather than plain admission. Escalation is a separate axis from severity —
/// a finding may be low-severity yet still need a human to acknowledge it before routing.
/// Ignored when the resolved severity blocks, since a blocked order is never admitted.
/// </param>
public sealed record RiskFinding(
    string Code,
    string Message,
    decimal? ObservedValue = null,
    decimal? LimitValue = null,
    bool RequiresAcknowledgement = false);

/// <summary>
/// A finding attributed to the rule that raised it, as the validator records it.
/// Constructed only by <c>CompositeRiskValidator</c>, which is what guarantees that
/// <see cref="Severity"/> is the declaring rule's own and not a value a rule chose per-order.
/// </summary>
public sealed record RiskViolation(
    string RuleName,
    RiskRuleSeverity Severity,
    string Code,
    string Message,
    decimal? ObservedValue = null,
    decimal? LimitValue = null,
    bool RequiresAcknowledgement = false)
{
    /// <summary>
    /// True when this violation is one that blocks. Derived from <see cref="Severity"/>, so it
    /// cannot disagree with the validator's own admission logic.
    /// </summary>
    public bool IsBlocking => Severity is RiskRuleSeverity.Error or RiskRuleSeverity.Critical;
}

/// <summary>Compact decision summary returned to the submitter on <see cref="OrderResult"/>.</summary>
public sealed record RiskDecisionSummary(
    RiskDecisionKind Decision,
    IReadOnlyList<RiskViolation> Violations);
```

There is deliberately no rule-facing "outcome kind" type. A rule returns `RiskFinding?` and nothing
else, so the states that an outcome kind would have made representable — blocking with no violation,
passing with one, a `Warning` rule returning `Block` — cannot be constructed at all.

### Modified interface — `IRiskRule`

```csharp
namespace Meridian.Risk;

public interface IRiskRule
{
    string RuleName { get; }
    int Priority => 0;

    /// <summary>
    /// How this rule's findings are treated. Fixed per rule, not per order — the validator
    /// resolves admission from this value alone.
    /// </summary>
    RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    /// <summary>
    /// Evaluates one constraint. Returns <see langword="null"/> when the rule is satisfied.
    /// Implementations must not mutate observable state directly; a rule that needs to consume
    /// capacity implements <see cref="IReservingRiskRule"/> instead.
    /// </summary>
    Task<RiskFinding?> EvaluateAsync(OrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Optional synchronous fast path. Returning <see langword="null"/> is ambiguous with
    /// "no finding", so a rule signals "no fast path" by not overriding this at all;
    /// <see cref="HasSyncFastPath"/> tells the validator which is meant.
    /// </summary>
    RiskFinding? TryEvaluate(OrderRequest request) => null;

    /// <summary>True when <see cref="TryEvaluate"/> is authoritative and the async path may be skipped.</summary>
    bool HasSyncFastPath => false;
}

/// <summary>
/// Implemented by rules that consume finite capacity (rate windows, burst counters).
/// <para>
/// The reservation is taken <em>during</em> evaluation, under whatever lock the rule already
/// holds, so the check and the consumption stay atomic exactly as they are today. The validator
/// then settles it. Splitting check and consumption across two calls would let concurrent
/// submissions each see room and all commit, overshooting the ceiling.
/// </para>
/// </summary>
public interface IReservingRiskRule : IRiskRule
{
    /// <summary>
    /// Atomically evaluates and, when the rule is satisfied, reserves the capacity this order
    /// would consume. The reservation is non-null whenever capacity was taken and must be
    /// settled by the validator on every path.
    /// </summary>
    Task<(RiskFinding? Finding, IRiskReservation? Reservation)> EvaluateAndReserveAsync(
        OrderRequest request,
        CancellationToken ct = default);
}

```

`IRiskReservation` lives in `Meridian.Execution.Sdk`, not here: the OMS receives these handles and
settles them at the routing boundary (Decision 8), so `Meridian.Execution` has to see the type.
Declaring it in `Meridian.Risk` would recreate the project cycle.

```csharp
namespace Meridian.Execution.Sdk;

/// <summary>
/// Capacity held for one in-flight evaluation. Exactly one of <see cref="Commit"/> or
/// <see cref="Rollback"/> is called; both are idempotent so a cleanup path can settle
/// unconditionally without double-settling.
/// <para>
/// Ownership moves: the validator rolls these back if evaluation throws or is cancelled, and
/// transfers them to the OMS on a normal return. Only the OMS commits, and only once the gateway
/// has accepted the order.
/// </para>
/// </summary>
public interface IRiskReservation
{
    /// <summary>Keeps the reserved capacity — the order was routed.</summary>
    void Commit();

    /// <summary>Returns the reserved capacity — the order was blocked, threw, cancelled, or failed downstream.</summary>
    void Rollback();
}
```

### Modified type — `RiskValidationResult` (`Meridian.Execution`)

`RiskDecisionKind` is declared in `Meridian.Execution.Sdk` alongside the other shared risk
contracts, because `RiskDecisionSummary` (also in the SDK) references it and the SDK cannot
reference `Meridian.Execution`.

```csharp
namespace Meridian.Execution.Sdk;

public enum RiskDecisionKind
{
    /// <summary>Admitted with no findings.</summary>
    Approved,

    /// <summary>Admitted; findings recorded for operator visibility.</summary>
    ApprovedWithWarnings,

    /// <summary>Admitted; requires operator acknowledgement.</summary>
    Escalated,

    /// <summary>Blocked.</summary>
    Rejected
}
```

`RiskValidationResult` itself stays in `Meridian.Execution` — it is an existing public type, and
moving it would break fully-qualified consumers such as the current risk-composition tests and
widen the migration well past the factory-only change Decision 2 promises.

```csharp
namespace Meridian.Execution;

public sealed record RiskValidationResult
{
    public required RiskDecisionKind Decision { get; init; }

    /// <summary>Every finding, ordered by severity descending, then rule priority ascending.</summary>
    public IReadOnlyList<RiskViolation> Violations { get; init; } = [];

    /// <summary>Preserved for existing callers. True for every decision except <see cref="RiskDecisionKind.Rejected"/>.</summary>
    public bool IsApproved => Decision != RiskDecisionKind.Rejected;

    /// <summary>
    /// The violation that actually blocked the order: highest severity first, then lowest rule
    /// priority. Selected by <see cref="IsBlocking"/> rather than by position, so a non-blocking
    /// finding can never be reported as the rejection reason.
    /// </summary>
    public RiskViolation? BlockingViolation => Decision == RiskDecisionKind.Rejected
        ? Violations.FirstOrDefault(static v => v.IsBlocking)
        : null;

    /// <summary>Preserved for existing callers. The blocking violation's message.</summary>
    public string? RejectReason => BlockingViolation?.Message;

    /// <summary>Stable code of the blocking violation, for audit attribution.</summary>
    public string? RejectCode => BlockingViolation?.Code;

    // Retained factories — all 24 existing construction sites use these.
    public static RiskValidationResult Approved() =>
        new() { Decision = RiskDecisionKind.Approved };

    public static RiskValidationResult Rejected(string reason) => new()
    {
        Decision = RiskDecisionKind.Rejected,
        Violations = [new RiskViolation("Unattributed", RiskRuleSeverity.Error, "RISK_REJECTED", reason)]
    };
}
```

> The `Rejected(string)` factory synthesises an `Unattributed` violation so the legacy call sites in
> `RiskRuleRuntimeService` keep working unchanged until they are migrated in Phase 2.

### New service — `RiskDecisionJournal`

```csharp
namespace Meridian.Execution.Journal;

public sealed class RiskJournalOptions
{
    public const string SectionName = "Risk:Journal";

    /// <summary>Write a journal entry for orders admitted with no findings. Default false.</summary>
    public bool JournalCleanApprovals { get; init; }

    /// <summary>Cap on violations serialised into one entry's metadata. Default 20.</summary>
    public int MaxJournaledViolations { get; init; } = 20;
}

/// <summary>
/// Records pre-trade risk decisions to the execution audit trail.
/// <para>
/// <b>Read-back is bounded by retention, not by durability.</b> The WAL keeps every entry, but
/// <c>ExecutionAuditTrailService</c>'s query methods serve an in-memory collection trimmed to
/// <c>ExecutionAuditTrailOptions.InMemoryRetention</c> (default 1,000 entries), including after
/// replay. So <c>GET /api/risk/decisions</c> answers "what happened recently", not "why was this
/// order blocked on Tuesday" — see Open Question 6 for the scope decision this forces.
/// </para>
/// </summary>
public interface IRiskDecisionJournal
{
    ValueTask RecordAsync(
        RiskDecisionJournalEntry entry,
        CancellationToken ct = default);
}

public sealed record RiskDecisionJournalEntry(
    string OrderId,
    string Symbol,
    RiskDecisionKind Decision,
    IReadOnlyList<RiskViolation> Violations,
    string? Actor,
    string? RunId,
    string? CorrelationId,
    DateTimeOffset OccurredAt);
```

### REST surface (additive)

Extends `src/Meridian.Ui.Shared/Endpoints/RiskEndpoints.cs`, which today maps `/rules`,
`/rules/{ruleName}/status`, and `/rules/{ruleName}/config` under both `/api/risk` and
`/api/v1/risk`. The new route follows the same dual-mapping.

`orderId` is an exact-match filter, so a caller holding the id returned by a submission can read
back that decision deterministically even under concurrent submissions for the same symbol.

`journalCompleteness` reports the effective `JournalCleanApprovals` setting. Without it the panel
cannot honestly describe its own history: it would either claim completeness it does not have or
warn about a gap that is not there. The read surface must state which.

```
GET /api/risk/decisions?take=100&symbol=AAPL&decision=Rejected&orderId=ord-8823

200 Response:
{
  "journalCompleteness": {
    "cleanApprovalsRecorded": false,
    "note": "Decisions with no findings are not journaled under the current configuration."
  },
  "decisions": [
    {
      "orderId": "ord-8823",
      "symbol": "AAPL",
      "decision": "Rejected",
      "occurredAt": "2026-08-01T14:22:07Z",
      "actor": "operator:jd",
      "violationCount": 2,
      "violationsTruncated": false,
      "violations": [
        {
          "ruleName": "PositionLimit",
          "severity": "Error",
          "requiresAcknowledgement": false,
          "code": "POSITION_LIMIT_EXCEEDED",
          "message": "Position would reach 1,240 shares against a 1,000 share limit.",
          "observedValue": 1240,
          "limitValue": 1000
        },
        {
          "ruleName": "OrderRateThrottle",
          "severity": "Warning",
          "code": "ORDER_RATE_NEAR_LIMIT",
          "message": "52 orders in the last minute against a 60 order ceiling.",
          "observedValue": 52,
          "limitValue": 60
        }
      ]
    }
  ]
}
```

Serialization uses the source-generated context per ADR-014 — add the new DTOs to the workstation
JSON context rather than reflection-based serialization.

---

## Component Design

### `CompositeRiskValidator` (modified)

**Namespace:** `Meridian.Risk`
**Type:** `sealed class CompositeRiskValidator : IRiskValidator`
**Lifetime:** Singleton — composed at `WorkstationServiceCollectionExtensions.cs:360-384`

**Responsibilities**

- Evaluate every registered rule against the order, in priority order.
- Resolve the aggregate decision from the outcome set by severity precedence.
- Order violations for presentation: severity descending, then rule priority ascending.
- Return reservations to the caller for settlement at the routing boundary (see Decision 8).

The validator does **not** journal and does **not** settle reservations itself. Both belong to the
OMS, which owns the order id and the true admission boundary. Keeping them here would reintroduce
the missing-order-id and double-write defects that Decision 5 exists to prevent.

**Dependencies (constructor-injected)**

- `IEnumerable<IRiskRule> rules`
- `ILogger<CompositeRiskValidator> logger`

**Precedence resolution**

| Highest outcome present | Aggregate decision |
| --- | --- |
| Any `Block` | `Rejected` |
| Any `Escalate` (no `Block`) | `Escalated` |
| Any `Annotate` (no `Block`/`Escalate`) | `ApprovedWithWarnings` |
| All `Pass` | `Approved` |

A rule's declared `Severity` maps to its outcome kind when the rule reports a finding:
`Info` → `Annotate`, `Warning` → `Annotate`, `Error` → `Block`, `Critical` → `Block`. A rule may
override by returning `Escalate` explicitly. This is the single line that turns severity from a log
field into a decision.

**Error handling**

A rule that throws must not take down the pre-trade gate, but must also not silently admit an order.
Catch per-rule, log at `Error`, and synthesise a violation with code
`RISK_RULE_EVALUATION_FAILED` carrying the failing rule's name. Fail closed: a gate that cannot
evaluate is not a gate that passed.

**The synthesised violation carries `Critical`, not the rule's declared severity**, and this is the
one deliberate exception to "a violation carries its declaring rule's own severity". The reason is
that the two rules interact badly: `IsBlocking` derives from severity alone, so preserving an
`Info`/`Warning` rule's severity would either admit an order after its gate failed, or produce a
`Rejected` result whose `BlockingViolation`, `RejectReason`, and `RejectCode` are all null. Neither
is acceptable, and silently rewriting the rule's severity to `Error` would be a quieter version of
the same contradiction.

An evaluation failure is an *engine* fault, not a finding about the order, so it is attributed to
the engine: `RuleName` identifies which rule failed, `Severity` is `Critical` because the platform
could not evaluate a gate. Test both that an `Info`-severity rule throwing still blocks, and that
the resulting `RejectReason` and `RejectCode` are non-null.

**Cancellation**

`ct.ThrowIfCancellationRequested()` before each rule, as today — but cancellation must not leak
reserved capacity. Rules evaluated before the cancellation point may already hold reservations, and
because Decision 8 transfers those handles through the *return value*, an exception path returns
nothing and the OMS has no handles to roll back. Repeated cancelled submissions would then consume
the throttle window and block later orders.

**The validator owns reservation cleanup until a successful handoff.** Wrap evaluation in a
`try`/`catch`: on any exception, including `OperationCanceledException`, roll back every reservation
taken so far and then rethrow. Ownership transfers to the OMS only when `ValidateOrderAsync` returns
normally. This is the one piece of settlement that stays in the validator, and it is not in tension
with Decision 8 — the validator handles failures *before* the handoff, the OMS handles everything
after.

### `OrderRateThrottle` (modified)

**Change:** `EvaluateAsync` becomes pure — it reads the window and reports, but no longer enqueues.
The class implements `IReservingRiskRule`: purge → count → *reserve* stay inside the existing lock, and the reservation is committed or rolled back by the validator.

**Behavioural note worth calling out in the PR:** this is a *correctness improvement independent of
the rest of the blueprint*. Today the throttle counts orders that a later gate rejected only because
short-circuiting happened to hide them; it already miscounts whenever the throttle itself is not the
first rule to fail. After this change the window counts routed orders exactly.

**A graduated throttle needs two rules, not one.** An earlier draft of this blueprint claimed the
throttle could annotate at 80% of ceiling and block at 100%. It cannot: `Severity` is fixed per rule
and the validator derives admission from it alone, so `Warning` admits both thresholds and `Error`
blocks both. That is the deliberate cost of making severity the single lever — one rule cannot
express a graduated response.

Ship it as two registrations over shared window state: `OrderRateNearLimitRule` (`Warning`, fires at
80%) and `OrderRateThrottle` (`Error`, fires at 100%). Only the blocking rule reserves capacity; the
warning rule is a pure read. This is deferred to `W9-SAFETY-007` with the rest of the rule
catalogue — the engine work here only has to make it expressible.

### `RiskDecisionJournal`

**Namespace:** `Meridian.Execution.Journal`
**Type:** `sealed class RiskDecisionJournal : IRiskDecisionJournal`
**Lifetime:** Singleton

**Dependencies**

- `ExecutionAuditTrailService? auditTrail` — nullable, matching `OrderManagementSystem`'s existing
  tolerance for hosts without the audit trail registered
- `IOptionsMonitor<RiskJournalOptions> options` (ADR-011 — hot-reloadable)
- `ILogger<RiskDecisionJournal> logger`

**Behaviour**

- Returns immediately when `auditTrail is null`, or when the decision is a clean `Approved` and
  `JournalCleanApprovals` is false.
- Maps to `ExecutionAuditEntry` with `Category: "Risk"`, `Action: "PreTradeDecision"`,
  `Outcome: Decision.ToString()`, `Reason: <highest-severity violation code>`.
- Serialises violations into `Metadata` as flat keys — `violation.0.rule`, `violation.0.code`,
  `violation.0.severity`, `violation.0.message`, `violation.0.observed`, `violation.0.limit` —
  capped at `MaxJournaledViolations`. Flat keys keep the entry queryable without a nested-JSON blob
  inside a string dictionary.

  **`message` is not optional.** The audit entry is the only place a journaled violation is
  persisted — nothing else retains the `RiskViolation` objects — so omitting it would leave
  `GET /api/risk/decisions` unable to render violation text for any decision with more than one
  finding. The entry's own top-level `Message` field holds the aggregate summary, not the
  per-violation text. A write/read round-trip test is listed for exactly this reason.
- Records `violation.count` alongside the entries, so a reader can tell a truncated set from a
  complete one when the cap is hit. `/api/risk/decisions` surfaces this as `violationCount` plus
  `violationsTruncated`; without them a consumer renders a truncated decision as complete.
- Awaits the write. This is lifecycle-sensitive evidence; per the repository's execution guardrail,
  do not fire-and-forget it.

### `OrderManagementSystem` (modified — one call site)

At `OrderManagementSystem.cs:303`, pass the structured data the method already accepts:

```csharp
var riskResult = await _riskValidator.ValidateOrderAsync(safeRequest, ct).ConfigureAwait(false);
if (!riskResult.IsApproved)
{
    return await RejectOrderAsync(
        orderId, safeRequest, actor, brokerName, runId, correlationId,
        riskResult.RejectReason, sessionId, ct,
        rejectionSource: "risk validator",
        reasonCode: riskResult.RejectCode ?? "RISK_REJECTED",          // NEW
        metadata: BuildRiskRejectedAuditMetadata(riskResult))          // NEW
        .ConfigureAwait(false);
}
```

`BuildRiskRejectedAuditMetadata` mirrors the existing
`BuildOrderRejectedByControlAuditMetadata` (line 273) and
`BuildLiveOrderReadinessRejectedAuditMetadata` (line 246) helpers. The `reasonCode` follows the
established SCREAMING_SNAKE convention already used by `OPERATOR_CONTROL_REJECTED` and
`DUPLICATE_CLIENT_ORDER_ID`.

**It must also stamp a discriminator: `metadata["decisionSource"] = "risk"`.** `OrderRejected` is
the shared audit action for *every* gate — live-order readiness, operator controls, security master,
duplicate client-order-id, and risk all funnel through `RejectOrderAsync`. A read projection that
selects on `Action == "OrderRejected"` alone would report an unrelated gate failure as a risk
decision. `/api/risk/decisions` must filter on the discriminator, not the action.

---

## Data Flow

### Order admitted with a warning

1. Strategy or operator submits an order; `OrderManagementSystem.SubmitOrderAsync` runs the
   readiness, operator-control, and security-master gates.
2. `CompositeRiskValidator.ValidateOrderAsync` evaluates all four rules.
3. `DrawdownGuardrailRule` → `Pass`. `PositionLimitRule` → `Pass`.
   `OrderRateThrottle` → finding `ORDER_RATE_NEAR_LIMIT` (observed 52, limit 60); rule severity is
   `Warning`, so the validator resolves it to an annotation and capacity is reserved.
4. Precedence resolves `ApprovedWithWarnings`.
5. The throttle's reservation is committed; the window keeps the order.
6. `RiskDecisionJournal.RecordAsync` writes an `ExecutionAuditEntry` (the decision carries a
   violation, so it journals regardless of `JournalCleanApprovals`).
7. `IsApproved` is true → the OMS routes the order normally.
8. The dashboard's risk panel shows an amber annotation against the order.

**This path is impossible today** — step 3's `Annotate` would be a hard rejection.

### Order blocked by two rules

1–2. As above.
3. `PositionLimitRule` → `Block(POSITION_LIMIT_EXCEEDED, observed 1240, limit 1000)`.
   `OrderRateThrottle` → finding `ORDER_RATE_NEAR_LIMIT` (`Warning` → annotation). Both evaluated.
4. Precedence resolves `Rejected`; violations ordered `[POSITION_LIMIT_EXCEEDED (Error),
   ORDER_RATE_NEAR_LIMIT (Warning)]`.
5. Commit phase **skipped** — the throttle does not record a blocked order.
6. Journal writes both violations.
7. `RejectOrderAsync` records `reasonCode: "POSITION_LIMIT_EXCEEDED"` with both violations in
   metadata, and returns the failed `OrderResult`.
8. The operator sees both findings, not just the position limit.

### Rule throws

3. `PositionLimitRule.EvaluateAsync` throws `InvalidOperationException`.
4. The validator catches it, logs at `Error`, synthesises
   `Block(RISK_RULE_EVALUATION_FAILED, rule: PositionLimit)`.
5. Aggregate resolves `Rejected` — fail closed.
6. Journal and rejection path proceed as above, with the failure visible as a violation rather than
   as an exception that either escapes the gate or silently admits the order.

---

## UI Design

**WPF:** a tracked parity deliverable, not an omission. `AGENTS.md` (L123-126) makes the browser
and desktop workstations co-equal active lanes and names closing parity gaps as the WPF lane's
immediate focus (`W8-WPF-PARITY-001`), so shipping two new browser-only operator panels would open a
fresh gap rather than close one.

PR 4 adds the WPF consumers over the *same* shared read model — no forked state, no duplicated
decision logic:

- A pre-trade violation list on the order ticket, bound to the `RiskDecisionSummary` now carried on
  `OrderResult`.
- A decision-history panel bound to `GET /api/risk/decisions`.
- View-model tests in `tests/Meridian.Wpf.Tests` mirroring the browser view-model tests.

Sequencing it after PR 3 is deliberate — the shared contract should prove itself on one surface
before the second consumes it — but it is in scope for this blueprint, not deferred to an untracked
follow-up.

**Browser workstation** — extends the Trading screen's existing risk state
(`WorkstationTradingRiskState`, composed at `WorkstationEndpoints.cs:3613`).

**Pre-trade violation panel (order ticket)**

- Renders inline beneath the submit control, only when the pending order produced findings.
- Primary line: the aggregate decision, colour-keyed — green `Approved`, amber
  `ApprovedWithWarnings`, orange `Escalated`, red `Rejected`.
- Secondary: one row per violation, severity chip on the left, `observed / limit` right-aligned and
  numerically formatted so magnitudes compare at a glance.
- Progressive disclosure: rows collapse to a count ("2 findings") once acknowledged.

**Decision history panel (Trading screen)**

- Reverse-chronological list backed by `GET /api/risk/decisions`.
- Default filter: non-clean decisions only, matching the journal's default write policy — the panel
  must not imply completeness it does not have. When `JournalCleanApprovals` is false, show a
  one-line footnote saying clean approvals are not recorded. The panel reads this from the
  response's `journalCompleteness` block rather than assuming a default, so it never misstates its
  own coverage.
- Row: timestamp, symbol, decision chip, top violation code, violation count.
- Click expands the full violation set for that order.

---

## Test Plan

**Principle:** the validator is the unit under test; rules are stubbed at the `IRiskRule` boundary.
Journal assertions go through a fake `IRiskDecisionJournal` — do not assert against WAL files in
unit tests.

### Unit — `CompositeRiskValidator`

| Test | Verifies |
| --- | --- |
| `ValidateOrderAsync_WhenMultipleRulesReportFindings_EvaluatesAllRules` | Replaces the deleted short-circuit test. All stubs invoked exactly once. |
| `ValidateOrderAsync_WhenTwoRulesBlock_ReturnsBothViolations` | `Violations.Count == 2` |
| `ValidateOrderAsync_OrdersViolationsBySeverityThenPriority` | Critical before Error before Warning; ties broken by rule priority |
| `ValidateOrderAsync_WithWarningSeverityFinding_AdmitsOrder` | `IsApproved` true, `Decision == ApprovedWithWarnings` — the core behaviour change |
| `ValidateOrderAsync_WithInfoSeverityFinding_AdmitsOrder` | `Info` annotates, does not block |
| `ValidateOrderAsync_WithCriticalFinding_Rejects` | `Decision == Rejected` |
| `ValidateOrderAsync_WithEscalateOutcome_AdmitsAndRecords` | `Decision == Escalated`, `IsApproved` true |
| `ValidateOrderAsync_WhenRuleThrows_FailsClosedWithSynthesisedViolation` | `Rejected` + `RISK_RULE_EVALUATION_FAILED`; exception not rethrown |
| `ValidateOrderAsync_WhenCancelled_PropagatesOperationCanceled` | Cancellation is not swallowed by the per-rule catch |
| `ValidateOrderAsync_WhenAdmitted_InvokesStateCommitOnStatefulRules` | Commit phase fires |
| `ValidateOrderAsync_WhenBlocked_DoesNotInvokeStateCommit` | The throttle-corruption regression |
| `ValidateOrderAsync_WithSyncFastPath_DoesNotCallAsyncPath` | Preserved from the existing suite |
| `RejectReason_WithMultipleViolations_ReturnsHighestSeverityMessage` | Legacy read-surface compatibility |
| `IsApproved_ForEachDecisionKind_MatchesExpectedAdmission` | Compatibility across all four kinds |

### Unit — `OrderRateThrottle`

| Test | Verifies |
| --- | --- |
| `EvaluateAsync_DoesNotMutateWindow` | Repeated evaluation without commit does not consume the ceiling |
| `CommitAsync_KeepsReservedCapacity` | Committed reservation stays consumed |
| `ConcurrentEvaluations_NeverExceedCeiling` | N parallel evaluations at the boundary reserve at most the ceiling — the race the reservation model exists to prevent |
| `Rollback_ReturnsCapacity` | Blocked order frees its slot |
| `EvaluateAsync_AtEightyPercentOfCeiling_AnnotatesWithoutBlocking` | The near-limit warning |
| `EvaluateAsync_AtCeiling_Blocks` | Existing behaviour preserved |

### Unit — `RiskDecisionJournal`

| Test | Verifies |
| --- | --- |
| `RecordAsync_WithCleanApprovalAndFlagDisabled_WritesNothing` | Default policy |
| `RecordAsync_WithCleanApprovalAndFlagEnabled_WritesEntry` | Opt-in |
| `RecordAsync_WithWarningOnAdmittedOrder_AlwaysWritesEntry` | Findings always journal |
| `RecordAsync_SerialisesViolationsIntoFlatMetadataKeys` | Metadata shape |
| `RecordAsync_TruncatesAtMaxJournaledViolations` | Cap honoured |
| `RecordAsync_WithNullAuditTrail_DoesNotThrow` | Host without audit trail |

### Integration

| Test | Verifies |
| --- | --- |
| `SubmitOrderAsync_WhenRiskRejects_WritesAuditEntryWithReasonCodeAndMetadata` | End-to-end through `OrderManagementSystem` — the plumbing that is currently unused |
| `SubmitOrderAsync_WhenRiskWarns_RoutesOrderAndJournalsDecision` | Warning admits and records |
| `GetRiskDecisions_ReturnsJournaledDecisions` | Endpoint, alongside existing `RiskEndpointTests` |

### Regression to update, not delete

`tests/Meridian.Tests/Risk/CompositeRiskValidatorTests.cs` →
`ValidateOrderAsync_WhenPriorityRuleRejects_ShortCircuitsBeforeLaterRules` must be rewritten as
`..._EvaluatesAllRules`. Also review
`tests/Meridian.Tests/Risk/EnforcedRiskValidatorCompositionTests.cs` and
`RiskIntegrationTests.cs` for short-circuit assumptions.

### Test infrastructure

- `FakeRiskRule` — configurable outcome, priority, severity, invocation counter.
- `FakeReservingRiskRule` — implements `IReservingRiskRule`, records commit/rollback calls and
  asserts each reservation is settled exactly once.
- `RecordingRiskDecisionJournal` — captures entries in memory.

---

## Implementation Checklist

**Estimated effort:** Medium — 6–8 working days for one developer.
**Suggested branch:** `codex/risk-engine-severity-and-journal`
**Suggested PR sequence:** three PRs, each independently green. PR 1 and PR 2 are behaviour-visible;
splitting them keeps the diff reviewable and lets the throttle fix land early.

### PR 1 — Outcome types and evaluate-all (Foundation)

- [ ] **Move** `RiskRuleSeverity` from `Meridian.Risk/IRiskRule.cs` to `Meridian.Execution.Sdk`;
      add `RiskFinding`, `RiskViolation` (with `IsBlocking`), `RiskDecisionKind`, and
      `RiskDecisionSummary` there. Confirm `Meridian.Execution` still does not reference
      `Meridian.Risk` — a cycle here does not compile
- [ ] Add `IReservingRiskRule` and `IRiskReservation` to `Meridian.Risk`
- [ ] Change `IRiskRule.EvaluateAsync` / `TryEvaluate` to return `RiskFinding?`; add
      `HasSyncFastPath`
- [ ] Extend `RiskValidationResult` with `Decision`, `Violations`, `BlockingViolation`,
      `RejectCode`; make `IsApproved` / `RejectReason` computed; retain both factories
- [ ] Re-verify no object-initializer construction of `RiskValidationResult` has appeared
- [ ] Rewrite `CompositeRiskValidator`: evaluate all, map each rule's `Severity` to an outcome,
      resolve precedence, order violations, per-rule fail-closed catch
- [ ] Reservation ownership: the validator rolls back on any exception before it returns (including
      cancellation), then **transfers** the handles to the OMS on a normal return. It must not
      commit — per Decision 8 that happens at the gateway-routing boundary, and committing here
      would consume capacity for orders that later fail id registration, journaling, or submission
- [ ] Migrate the four rules to `RiskFinding?` with stable SCREAMING_SNAKE codes
- [ ] Convert `OrderRateThrottle` to `IReservingRiskRule`: keep purge → count → reserve inside the
      existing lock; add commit/rollback
- [ ] Surface `DecisionKind` from the F# interop in `PositionLimitRule` and
      `DrawdownCircuitBreaker` — map `"escalate"` to `RequiresAcknowledgement` and stop discarding
      `Reasons` beyond the first
- [ ] Update `CompositeRiskValidatorTests`, `EnforcedRiskValidatorCompositionTests`,
      `RiskIntegrationTests`
- [ ] Write the unit tests listed above

### PR 2 — Decision journal

- [ ] Add `RiskJournalOptions`, `IRiskDecisionJournal`, `RiskDecisionJournalEntry`,
      `RiskDecisionJournal`
- [ ] Register in `WorkstationServiceCollectionExtensions` and inject into
      **`OrderManagementSystem`**, not the validator — it is the only component holding the
      generated order id and audit attribution
- [ ] Add `BuildRiskRejectedAuditMetadata` to `OrderManagementSystem` (including the
      `decisionSource=risk` discriminator); pass `reasonCode` and `metadata` at line 303
- [ ] Call the journal for **every** risk-approved result, clean ones included, and let
      `RiskDecisionJournal` apply `JournalCleanApprovals`. Gating the call site instead would mean
      enabling the flag changes nothing, because clean decisions would never reach the service
- [ ] Bind `Risk:Journal` from host configuration, and add the section to **both** tracked config
      sources: `config/appsettings.sample.json` and the machine-validated
      `config/appsettings.schema.json`. There is no `config/appsettings.json` in the repository —
      it is a runtime artifact, so editing it would leave the new section out of every source a
      reader or validator actually sees
- [ ] Write the journal unit tests and OMS integration tests, including the metadata round trip

### PR 3 — Read surface

- [ ] Add `GET /api/risk/decisions` to `RiskEndpoints.cs` under both route groups
- [ ] Add DTOs to the source-generated JSON context (ADR-014)
- [ ] Build the order-ticket violation panel and the decision-history panel in
      `src/Meridian.Ui/dashboard/`
- [ ] Render the footnote from the response's `journalCompleteness` block
- [ ] Extend `OrderResult` with `RiskDecisionSummary` and update the TypeScript submit contract so
      the order ticket can render findings on the admitted path
- [ ] Endpoint test alongside `RiskEndpointTests`; dashboard view-model tests

### Wrap-up (final PR)

- [ ] Consider an ADR: this changes the pre-trade contract's decision model. If ADR-015
      (strategy execution contract) covers `IRiskValidator`, amend it; otherwise add a short ADR.
- [ ] XML doc comments on all new public types
- [ ] `bash scripts/ci.sh`
- [ ] Review: cancellation preserved, structured logging (no interpolation), no `.Result`/`.Wait()`,
      no direct file writes, no package versions in `.csproj`

---

## Open Questions

| # | Question | Owner | Impact if unresolved |
| --- | --- | --- | --- |
| 1 | Should `Escalated` block until an operator acknowledges, or admit-and-record as designed here? | Product | Admit-and-record is the assumption. If escalation must block, it needs an approval store and a resume path — materially larger scope. |
| 2 | `RiskInterop.Aggregate` and F# `RiskEvaluation.aggregate` stay uncalled after this work. Wire them later, or archive them? | Implementer + Architecture | Leaving unreferenced domain code invites a future contributor to assume it is live. Decide explicitly. |
| 3 | Should `RiskRuleRuntimeService`'s `EvaluateDrawdownGuardrail` return a structured violation rather than `RiskValidationResult.Rejected(string)`? | Implementer | Until migrated it produces `Unattributed` violations, so the dashboard cannot attribute drawdown breaches to the rule. Recommend folding into PR 1. |
| 4 | Retention for journal entries — does the execution WAL already prune, and is that policy right for risk evidence? | Ops | Unbounded growth on a high-throughput host if `JournalCleanApprovals` is enabled without retention. |
| 5 | Does the OMS have a `FundAccountId` in scope at the risk call site for per-fund journal filtering? | Implementer | Affects whether `/api/risk/decisions` can filter by fund in PR 3 or needs a follow-up. |
| 6 | **How far back must `/api/risk/decisions` see?** `ExecutionAuditTrailService` serves queries from a collection trimmed to `InMemoryRetention` (default 1,000 entries), so the endpoint answers "recently" and not "on Tuesday". Options: (a) scope the feature to recent retained decisions and say so in the UI; (b) raise the retention for this host; (c) build a WAL/archive-backed query path. | **Product + Ops** | (a) is free but weakens the evidence claim that motivates the journal. (c) is a materially larger build and undercuts Decision 5's "no new store" premise. This must be decided before PR 3 is scoped. |

## Risks

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| A rule currently relying on the short-circuit gains a side effect under evaluate-all | Medium | High | `IReservingRiskRule` plus the explicit no-side-effects contract on `EvaluateAsync`; the "blocked does not commit" and concurrency tests are the regression guards |
| `Warning`-severity findings silently admit orders that operators expected to be blocked | Medium | High | All four current rules declare `Error`/`Critical`, so nothing changes on day one. Any rule *downgraded* to `Warning` is a deliberate, reviewable decision |
| Journal write latency on the order path | Low | Medium | Clean approvals off by default; rejection-path writes already happen today |
| `RiskValidationResult` change breaks an unexamined consumer | Low | Medium | Verified zero initializer construction and preserved factories + read surface; re-verify at implementation time |
| Scope creep into `W9-SAFETY-007` rules | Medium | Medium | Out-of-scope list is explicit; new rules land after this engine work |

---

## Why This Sequences Before `W9-SAFETY-007`

`W9-SAFETY-007` adds fat-finger, max-notional, and price-collar rules. Landing them on today's
engine means:

- a price collar cannot warn — the archetypal collar case ("unusual price, proceed with
  acknowledgement") is exactly the `Warning` outcome that currently hard-blocks;
- an order breaching notional *and* collar reports one of them;
- no rule can report observed-vs-limit as data, only as a formatted sentence;
- none of the rejections are queryable after the fact.

Building the engine first costs 6–8 days and makes every subsequent rule cheaper and more useful.
