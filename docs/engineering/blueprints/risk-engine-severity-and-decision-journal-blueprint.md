# Blueprint — Risk Engine: Severity-Aware Evaluation and Pre-Trade Decision Journal

**Status:** draft
**Owner:** Execution and Fund Accounts lane
**Reviewed:** 2026-08-01
**Depth mode:** full
**Related roadmap item:** `W9-SAFETY-007` (Kill-switch cancel-all and fat-finger, notional, and
collar rules) — this blueprint is the *engine* prerequisite, not the rule catalogue.

---

## ⚠️ Breaking Change

This blueprint changes three public surfaces in `Meridian.Risk` and `Meridian.Execution`.

| Surface | Change | Known consumers | Migration |
| --- | --- | --- | --- |
| `IRiskRule.EvaluateAsync` / `TryEvaluate` | Return `RiskRuleOutcome` instead of `RiskValidationResult` | 4 implementations: `DrawdownCircuitBreaker`, `OrderRateThrottle`, `PositionLimitRule` (`src/Meridian.Risk/Rules/`), `DrawdownGuardrailRule` (`src/Meridian.Ui.Shared/Services/`) | Mechanical: `RiskValidationResult.Approved()` → `RiskRuleOutcome.Pass()`; `RiskValidationResult.Rejected(r)` → `RiskRuleOutcome.Block(code, r, …)` |
| `CompositeRiskValidator.ValidateOrderAsync` | Evaluates all rules instead of returning on first failure | `OrderManagementSystem.cs:303` (sole call site, via `IRiskValidator`) | None required — the OMS reads `IsApproved`/`RejectReason`, which are preserved |
| `RiskValidationResult` | Gains `Decision` and `Violations`; `IsApproved` and `RejectReason` become computed | 107 read sites; **zero** direct-initializer construction sites | None — verified that all 24 construction sites go through `Approved()` / `Rejected(string)`, both of which are retained |

**One existing test asserts the behaviour being removed:**
`tests/Meridian.Tests/Risk/CompositeRiskValidatorTests.cs` →
`ValidateOrderAsync_WhenPriorityRuleRejects_ShortCircuitsBeforeLaterRules`. It must be rewritten to
assert the *replacement* guarantee (all rules evaluated, violations ordered by severity then
priority). Do not delete it — invert it.

No F# signature changes. No storage-format changes. No REST response removals (additive only).

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
- `Escalate` has no approval workflow behind it yet. This blueprint admits escalated orders with a
  recorded violation; wiring escalation to an operator approval gate is follow-on work.

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
     │    ├─ evaluate EVERY rule → RiskRuleOutcome        [NEW type]
     │    ├─ collect RiskViolation[]                      [NEW type]
     │    ├─ RiskDecisionPrecedence.Resolve(outcomes)     [NEW]
     │    └─ commit phase: IRiskRuleStateCommit.OnAdmitted [NEW] ── solves the throttle problem
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

**Decision 1 — Introduce `RiskRuleOutcome` for the per-rule result; extend `RiskValidationResult`
for the aggregate.**

*Alternatives considered:* (a) extend `RiskValidationResult` and use it for both levels;
(b) return `RiskDecisionDto` from F# directly.

*Rationale:* A rule reports one finding; the validator reports a set. Conflating them is what
produced today's "first failure is the whole answer" behaviour. Splitting the types makes the
aggregate's job explicit. Option (b) leaks the interop DTO into every rule, including the three
rules that never touch F#.

*Consequences:* Four rule implementations change signature (mechanical). `RiskValidationResult`'s
read surface is preserved, so the OMS and all 107 read sites are untouched.

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

**Decision 4 — Split evaluation from state commit.**

*Rationale:* This is the trap in "evaluate all rules." `OrderRateThrottle.EvaluateAsync`
(`src/Meridian.Risk/Rules/OrderRateThrottle.cs`) **mutates state on the pass path** — it enqueues
`now` into `_recentOrders` when the order is under the ceiling. Under today's short-circuit,
a rule after a rejection never runs, so it never records. Evaluate-all would make the throttle count
orders that were subsequently blocked by another rule, corrupting the rate window and causing
spurious throttling after any rejection burst.

*Consequences:* A new opt-in `IRiskRuleStateCommit` interface with `OnOrderAdmitted(OrderRequest)`.
`OrderRateThrottle` moves its enqueue there. Rules that are pure (the other three) ignore it. The
validator calls the commit phase only when the aggregate decision admits the order.

**Decision 5 — Journal through `ExecutionAuditTrailService`; no new store.**

*Rationale:* It is already WAL-backed (ADR-007), already carries `Reason`/`Scope`/`Metadata`, and is
already the durable record for the two sibling gates. A parallel store would fragment execution
evidence across two formats.

*Consequences:* Violations serialize into the entry's
`IReadOnlyDictionary<string, string> Metadata`. Rejections cost nothing extra (the write already
happens). Admitted-order journaling is a *new* write on the hot path — see Decision 6.

**Decision 6 — Journal every decision that carries a violation; gate clean approvals behind config.**

*Rationale:* Evidence value concentrates in decisions that were not clean. Writing a WAL record for
every admitted order on a high-throughput path buys little and costs on the hot path.

*Consequences:* `RiskJournalOptions.JournalCleanApprovals` defaults to `false`. Any decision with
≥1 violation — including `Warning`s that were admitted — is always journaled. Operators who need
complete records flip one flag.

---

## Interface and API Contracts

### New types — `Meridian.Risk`

```csharp
namespace Meridian.Risk;

/// <summary>How a single rule judged one order.</summary>
public enum RiskOutcomeKind
{
    /// <summary>No finding. The rule is satisfied.</summary>
    Pass,

    /// <summary>A finding the operator should see, but which does not block the order.</summary>
    Annotate,

    /// <summary>A finding that requires operator acknowledgement. Admitted, recorded, surfaced.</summary>
    Escalate,

    /// <summary>A finding that blocks the order.</summary>
    Block
}

/// <summary>
/// A single rule finding, carrying enough structure for an operator to see what was
/// measured against what limit — rather than a pre-formatted sentence.
/// </summary>
/// <param name="RuleName">Matches <see cref="IRiskRule.RuleName"/>.</param>
/// <param name="Severity">The declaring rule's <see cref="IRiskRule.Severity"/>.</param>
/// <param name="Code">Stable SCREAMING_SNAKE identifier, e.g. <c>POSITION_LIMIT_EXCEEDED</c>.</param>
/// <param name="Message">Human-readable summary for operator surfaces.</param>
/// <param name="ObservedValue">What the rule measured, when expressible as a number.</param>
/// <param name="LimitValue">What the rule measured against, when expressible as a number.</param>
public sealed record RiskViolation(
    string RuleName,
    RiskRuleSeverity Severity,
    string Code,
    string Message,
    decimal? ObservedValue = null,
    decimal? LimitValue = null);

/// <summary>Result of evaluating one <see cref="IRiskRule"/> against one order.</summary>
public sealed record RiskRuleOutcome
{
    public required RiskOutcomeKind Kind { get; init; }

    /// <summary>Null when <see cref="Kind"/> is <see cref="RiskOutcomeKind.Pass"/>.</summary>
    public RiskViolation? Violation { get; init; }

    public static RiskRuleOutcome Pass() => new() { Kind = RiskOutcomeKind.Pass };

    public static RiskRuleOutcome Annotate(RiskViolation violation) =>
        new() { Kind = RiskOutcomeKind.Annotate, Violation = violation };

    public static RiskRuleOutcome Escalate(RiskViolation violation) =>
        new() { Kind = RiskOutcomeKind.Escalate, Violation = violation };

    public static RiskRuleOutcome Block(RiskViolation violation) =>
        new() { Kind = RiskOutcomeKind.Block, Violation = violation };
}
```

### Modified interface — `IRiskRule`

```csharp
public interface IRiskRule
{
    string RuleName { get; }
    int Priority => 0;
    RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    /// <summary>
    /// Optional synchronous fast path. Return <see langword="null"/> to fall back to
    /// <see cref="EvaluateAsync"/>. Must not mutate rule state — see
    /// <see cref="IRiskRuleStateCommit"/>.
    /// </summary>
    RiskRuleOutcome? TryEvaluate(OrderRequest request) => null;

    /// <summary>
    /// Evaluates one constraint. Implementations MUST be free of side effects: the validator
    /// evaluates every rule before the admit/block decision is known, so a rule that records
    /// state here would record orders that are subsequently blocked.
    /// </summary>
    Task<RiskRuleOutcome> EvaluateAsync(OrderRequest request, CancellationToken ct = default);
}

/// <summary>
/// Implemented by rules that maintain state across orders (rate windows, burst counters).
/// The validator invokes this only after the aggregate decision admits the order, so
/// state reflects orders that were actually routed.
/// </summary>
public interface IRiskRuleStateCommit
{
    ValueTask OnOrderAdmittedAsync(OrderRequest request, CancellationToken ct = default);
}
```

### Modified type — `RiskValidationResult` (`Meridian.Execution`)

```csharp
namespace Meridian.Execution;

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

public sealed record RiskValidationResult
{
    public required RiskDecisionKind Decision { get; init; }

    /// <summary>Every finding, ordered by severity descending, then rule priority ascending.</summary>
    public IReadOnlyList<RiskViolation> Violations { get; init; } = [];

    /// <summary>Preserved for existing callers. True for every decision except <see cref="RiskDecisionKind.Rejected"/>.</summary>
    public bool IsApproved => Decision != RiskDecisionKind.Rejected;

    /// <summary>Preserved for existing callers. The highest-severity blocking violation's message.</summary>
    public string? RejectReason => Decision == RiskDecisionKind.Rejected
        ? Violations.FirstOrDefault()?.Message
        : null;

    /// <summary>Stable code of the highest-severity blocking violation, for audit attribution.</summary>
    public string? RejectCode => Decision == RiskDecisionKind.Rejected
        ? Violations.FirstOrDefault()?.Code
        : null;

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
namespace Meridian.Risk.Journal;

public sealed class RiskJournalOptions
{
    public const string SectionName = "Risk:Journal";

    /// <summary>Write a journal entry for orders admitted with no findings. Default false.</summary>
    public bool JournalCleanApprovals { get; init; }

    /// <summary>Cap on violations serialised into one entry's metadata. Default 20.</summary>
    public int MaxJournaledViolations { get; init; } = 20;
}

/// <summary>
/// Records pre-trade risk decisions to the durable execution audit trail so that
/// "why was this order blocked on Tuesday?" is a query rather than a log grep.
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

```
GET /api/risk/decisions?take=100&symbol=AAPL&decision=Rejected

200 Response:
{
  "decisions": [
    {
      "orderId": "ord-8823",
      "symbol": "AAPL",
      "decision": "Rejected",
      "occurredAt": "2026-08-01T14:22:07Z",
      "actor": "operator:jd",
      "violations": [
        {
          "ruleName": "PositionLimit",
          "severity": "Error",
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
- Invoke the commit phase on stateful rules when — and only when — the order is admitted.
- Hand the result to the journal.

**Dependencies (constructor-injected)**

- `IEnumerable<IRiskRule> rules`
- `IRiskDecisionJournal journal`
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
Catch per-rule, log at `Error`, and synthesise a `Block` violation with code
`RISK_RULE_EVALUATION_FAILED` carrying the rule name. Fail closed: a gate that cannot evaluate is
not a gate that passed. `OperationCanceledException` propagates unchanged.

**Cancellation**

`ct.ThrowIfCancellationRequested()` before each rule, as today.

### `OrderRateThrottle` (modified)

**Change:** `EvaluateAsync` becomes pure — it reads the window and reports, but no longer enqueues.
The enqueue moves to `OnOrderAdmittedAsync`. The class gains `IRiskRuleStateCommit`.

**Behavioural note worth calling out in the PR:** this is a *correctness improvement independent of
the rest of the blueprint*. Today the throttle counts orders that a later gate rejected only because
short-circuiting happened to hide them; it already miscounts whenever the throttle itself is not the
first rule to fail. After this change the window counts routed orders exactly.

**Secondary opportunity:** with severity available, the throttle can report
`ORDER_RATE_NEAR_LIMIT` as an `Annotate` at 80% of ceiling and `ORDER_RATE_EXCEEDED` as a `Block` at
100% — two findings from one rule, which was unrepresentable before.

### `RiskDecisionJournal`

**Namespace:** `Meridian.Risk.Journal`
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
  `violation.0.severity`, `violation.0.observed`, `violation.0.limit` — capped at
  `MaxJournaledViolations`. Flat keys keep the entry queryable without a nested-JSON blob inside a
  string dictionary.
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

---

## Data Flow

### Order admitted with a warning

1. Strategy or operator submits an order; `OrderManagementSystem.SubmitOrderAsync` runs the
   readiness, operator-control, and security-master gates.
2. `CompositeRiskValidator.ValidateOrderAsync` evaluates all four rules.
3. `DrawdownGuardrailRule` → `Pass`. `PositionLimitRule` → `Pass`.
   `OrderRateThrottle` → `Annotate(ORDER_RATE_NEAR_LIMIT, observed 52, limit 60)`.
4. Precedence resolves `ApprovedWithWarnings`.
5. `OnOrderAdmittedAsync` fires on `OrderRateThrottle`; the window records the order.
6. `RiskDecisionJournal.RecordAsync` writes an `ExecutionAuditEntry` (the decision carries a
   violation, so it journals regardless of `JournalCleanApprovals`).
7. `IsApproved` is true → the OMS routes the order normally.
8. The dashboard's risk panel shows an amber annotation against the order.

**This path is impossible today** — step 3's `Annotate` would be a hard rejection.

### Order blocked by two rules

1–2. As above.
3. `PositionLimitRule` → `Block(POSITION_LIMIT_EXCEEDED, observed 1240, limit 1000)`.
   `OrderRateThrottle` → `Annotate(ORDER_RATE_NEAR_LIMIT)`. Both evaluated.
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

**WPF:** N/A for this blueprint. The WPF lane's current focus is web-UI parity
(`W8-WPF-PARITY-001`); the desktop risk surface should consume the same shared read model once the
browser surface lands, rather than forking a parallel one.

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
  one-line footnote saying clean approvals are not recorded.
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
| `OnOrderAdmittedAsync_RecordsOrderInWindow` | Commit records |
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
- `FakeStatefulRiskRule` — implements `IRiskRuleStateCommit`, records commit calls.
- `RecordingRiskDecisionJournal` — captures entries in memory.

---

## Implementation Checklist

**Estimated effort:** Medium — 6–8 working days for one developer.
**Suggested branch:** `codex/risk-engine-severity-and-journal`
**Suggested PR sequence:** three PRs, each independently green. PR 1 and PR 2 are behaviour-visible;
splitting them keeps the diff reviewable and lets the throttle fix land early.

### PR 1 — Outcome types and evaluate-all (Foundation)

- [ ] Add `RiskOutcomeKind`, `RiskViolation`, `RiskRuleOutcome` to `Meridian.Risk`
- [ ] Add `IRiskRuleStateCommit`
- [ ] Change `IRiskRule.EvaluateAsync` / `TryEvaluate` to return `RiskRuleOutcome`
- [ ] Add `RiskDecisionKind`; extend `RiskValidationResult` with `Decision`, `Violations`,
      `RejectCode`; make `IsApproved` / `RejectReason` computed; retain both factories
- [ ] Re-verify no object-initializer construction of `RiskValidationResult` has appeared
- [ ] Rewrite `CompositeRiskValidator`: evaluate all, resolve precedence, order violations,
      per-rule fail-closed catch, commit phase on admission
- [ ] Migrate the four rules to `RiskRuleOutcome` with stable SCREAMING_SNAKE codes
- [ ] Move `OrderRateThrottle`'s enqueue into `OnOrderAdmittedAsync`
- [ ] Surface `DecisionKind` from the F# interop in `PositionLimitRule` and
      `DrawdownCircuitBreaker` — map `"escalate"` to `RiskOutcomeKind.Escalate` and stop discarding
      `Reasons` beyond the first
- [ ] Update `CompositeRiskValidatorTests`, `EnforcedRiskValidatorCompositionTests`,
      `RiskIntegrationTests`
- [ ] Write the 18 unit tests listed above

### PR 2 — Decision journal

- [ ] Add `RiskJournalOptions`, `IRiskDecisionJournal`, `RiskDecisionJournalEntry`,
      `RiskDecisionJournal`
- [ ] Register in `WorkstationServiceCollectionExtensions` and inject into `CompositeRiskValidator`
- [ ] Add `BuildRiskRejectedAuditMetadata` to `OrderManagementSystem`; pass `reasonCode` and
      `metadata` at line 303
- [ ] Add the `Risk:Journal` section to `config/appsettings.json` with documented defaults
- [ ] Write the 6 journal unit tests and 2 OMS integration tests

### PR 3 — Read surface

- [ ] Add `GET /api/risk/decisions` to `RiskEndpoints.cs` under both route groups
- [ ] Add DTOs to the source-generated JSON context (ADR-014)
- [ ] Build the order-ticket violation panel and the decision-history panel in
      `src/Meridian.Ui/dashboard/`
- [ ] Show the "clean approvals not recorded" footnote when the flag is off
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

## Risks

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| A rule currently relying on the short-circuit gains a side effect under evaluate-all | Medium | High | `IRiskRuleStateCommit` plus the explicit no-side-effects contract on `EvaluateAsync`; the "blocked does not commit" test is the regression guard |
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
