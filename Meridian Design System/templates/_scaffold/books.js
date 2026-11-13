// Meridian books — ONE scoped read model for the accounting loop (increment B).
//
// Before this file the reconciliation exception, journal review, general ledger, trial balance and
// account sheet each derived the books from their own private seed. The same figure was assembled
// five times, so a residual on one screen and a residual on the next were only *hopefully* the same
// number, and an object selected on one screen had no identity the next one could recognise.
//
// One selector, over the existing contracts:
//   ObjectScope       entity · book · basis          → what set of records am I reading
//   TimeContext       posting · reporting · as-of    → in whose clock is the figure stated
//   WorkspaceContext  the persistent bar             → the scope the operator can see and change
//
// Two rules the read model enforces for every surface built on it:
//   1. STABLE IDENTITY. Every record carries `identity` — "journal:JE-1042", "glline:JE-1042#1",
//      "account:1100", "break:CASE-4181". Selection travels between surfaces as that string, so a
//      ledger row, a trial-balance line and an account sheet line are the SAME object, not three
//      lookalikes with the same label.
//   2. ACTUAL AND PROJECTED NEVER MERGE. `residual.actual` is what the books say now;
//      `residual.projected` is what they would say if the staged journals posted. They are two
//      fields, never one — a projected tie-out presented as an actual one is the defect increment A
//      exists to prevent, one level up.
//
// ds-base.js loads this for you; templates need no extra <script>.
//   const m = MeridianBooks.select({ entity: "MER-FUND-I", basis: "STAT", period: "2026-06" });
//   m.residual.actual        // −142.18 — the open break
//   m.accountSheet("1100")   // opening, movement, closing, rows, identity
(function (global) {
  "use strict";

  var R2 = function (n) { return Math.round((Number(n) || 0) * 100) / 100; };

  // ── Scope facets ────────────────────────────────────────────────────────────
  // Declared, not inferred. A facet this seed does not vary carries `note` saying so, rather than
  // faking a difference: a basis switch that changes nothing is worse than a basis switch that
  // says what it changes.
  var SCOPES = {
    entity: [
      { id: "MER-FUND-I", label: "Meridian Fund I LP", path: "Group ▸ Meridian Fund I LP" },
      { id: "MER-SPV-ALPHA", label: "Meridian SPV Alpha LLC", path: "Group ▸ Fund I ▸ Meridian SPV Alpha LLC" },
    ],
    book: [{ id: "GL", label: "General ledger" }],
    basis: [
      { id: "STAT", label: "STAT", note: "Statutory — accrued income recognised when declared." },
      { id: "GAAP", label: "GAAP", note: "Dividend accruals excluded until received (JE-1060 is STAT-only in this seed)." },
    ],
    period: [
      { id: "2026-06", label: "June 2026", start: "2026-06-01", end: "2026-06-30", state: "open" },
      { id: "2026-05", label: "May 2026", start: "2026-05-01", end: "2026-05-31", state: "hard-closed" },
    ],
  };

  var ACCOUNTS = [
    { code: "1100", name: "Cash & equivalents", type: "asset" },
    { code: "1200", name: "Brokerage receivable", type: "asset" },
    { code: "1310", name: "Equities", type: "asset" },
    { code: "1500", name: "Custody account", type: "asset" },
    { code: "2200", name: "Accrued fees", type: "liability" },
    { code: "2400", name: "Payable to affiliate", type: "liability" },
    { code: "3100", name: "Contributed capital", type: "equity" },
    { code: "3200", name: "Retained earnings — opening", type: "equity" },
    { code: "4200", name: "Dividend & interest income", type: "revenue" },
    { code: "5100", name: "Commissions & fees", type: "expense" },
    { code: "5300", name: "Financing cost", type: "expense" },
  ];
  var CREDIT_NORMAL = { liability: 1, equity: 1, revenue: 1 };

  // Opening balances, debit-positive, by entity × account. The period's movement is derived from
  // the journals below — never stated twice.
  //
  // These PROVE: every entity's openings sum to zero, and every journal balances, so the trial
  // balance derived from them proves to zero without anyone adjusting it. `3200` is the opening
  // retained-earnings figure that makes that true — a fixture whose trial balance does not prove
  // teaches an operator to ignore the one number on the screen whose job is to be checked.
  var OPENINGS = {
    "MER-FUND-I": { 1100: 120000, 1200: 28890, 1310: 819882, 1500: 0, 2200: -3850, 3100: -700000, 3200: -308393.5, 4200: -24730, 5100: 28121.5, 5300: 40080 },
    "MER-SPV-ALPHA": { 1100: 250000, 1310: 366300, 2400: 0, 3100: -528280, 3200: -90090, 4200: -4180, 5300: 6250 },
  };

  var PERIOD = { start: "2026-06-01", end: "2026-06-30" };
  function ev(label, ref) { return { label: label, ref: ref }; }

  // ── The journals ────────────────────────────────────────────────────────────
  // One list. Every surface in the loop reads it: the GL lists its lines, the trial balance sums
  // them, the account sheet filters them, the reconciliation matches them against statement lines.
  // `attachments` and `rationale` live ON the journal, so what was reviewed is what is submitted.
  var JOURNALS = [
    { entity: "MER-FUND-I", ref: "JE-1042", date: "2026-06-02", memo: "Client settlement — AAPL block", status: "posted",
      sourceBreak: "CASE-4181", version: 1, condition: "confirmed", authority: "source",
      clocks: { posting: "2026-06-02", effective: "2026-06-02" },
      attachments: [ev("Custodian confirm", "CONF-88431")],
      lines: [{ code: "1100", debit: 82440 }, { code: "1200", credit: 82440 }] },
    { entity: "MER-FUND-I", ref: "JE-1043", date: "2026-06-03", memo: "Commission & exchange fees", status: "posted",
      version: 1, condition: "confirmed", authority: "source", clocks: { posting: "2026-06-03" },
      attachments: [ev("Broker invoice", "INV-2261")],
      lines: [{ code: "5100", debit: 318.5 }, { code: "1100", credit: 318.5 }] },
    { entity: "MER-FUND-I", ref: "JE-1051", date: "2026-06-05", memo: "Wire to custodian", status: "posted",
      version: 1, condition: "confirmed", authority: "source", clocks: { posting: "2026-06-05" },
      attachments: [ev("Wire advice", "WIRE-40118")],
      lines: [{ code: "1500", debit: 40000 }, { code: "1100", credit: 40000 }] },
    { entity: "MER-FUND-I", ref: "JE-1060", date: "2026-06-09", memo: "Dividend receivable — MSFT", status: "posted",
      sourceBreak: "CASE-4184", version: 2, condition: "estimated", authority: "estimate", basisOnly: ["STAT"],
      detail: "declared rate × record-date shares", clocks: { posting: "2026-06-09", settlement: "2026-07-10" },
      attachments: [ev("Dividend notice", "DIV-MSFT-0609")],
      lines: [{ code: "1200", debit: 1290 }, { code: "4200", credit: 1290 }] },
    { entity: "MER-FUND-I", ref: "JE-1071", date: "2026-06-12", memo: "Margin interest — June", status: "posted",
      version: 1, condition: "confirmed", authority: "derived", detail: "avg debit balance × 6.15%",
      clocks: { posting: "2026-06-12", calculatedAt: "2026-06-12 22:10:00Z" },
      attachments: [ev("Interest accrual run", "ACCR-0612")],
      lines: [{ code: "5300", debit: 1120 }, { code: "1100", credit: 1120 }] },
    { entity: "MER-FUND-I", ref: "JE-1072", date: "2026-06-13", memo: "Duplicate of JE-1043 — voided", status: "void",
      version: 2, condition: "overridden", authority: "desk", detail: "voided — duplicate of JE-1043",
      clocks: { posting: "2026-06-13" }, attachments: [ev("Void memo", "VM-1072")],
      lines: [{ code: "5100", debit: 318.5 }, { code: "1100", credit: 318.5 }] },
    { entity: "MER-FUND-I", ref: "JE-1080", date: "2026-06-16", memo: "Buy 40 NVDA @ 1,603.00", status: "posted",
      version: 1, condition: "confirmed", authority: "source", material: true,
      clocks: { trade: "2026-06-16", settlement: "2026-06-18", posting: "2026-06-16" },
      attachments: [ev("Execution report", "EX-77140")],
      lines: [{ code: "1310", debit: 64120 }, { code: "1100", credit: 64120 }] },
    { entity: "MER-SPV-ALPHA", ref: "SA-0131", date: "2026-06-11", memo: "Buy 500 XLE @ 61.00", status: "posted",
      version: 1, condition: "confirmed", authority: "source", clocks: { posting: "2026-06-11" },
      attachments: [ev("Execution report", "EX-77210")],
      lines: [{ code: "1310", debit: 30500 }, { code: "1100", credit: 30500 }] },
    { entity: "MER-SPV-ALPHA", ref: "SA-0142", date: "2026-06-30", memo: "IC loan interest — due to MER-FUND-I (June)", status: "posted",
      version: 1, condition: "confirmed", authority: "derived", detail: "mirrors JE-1078 (MER-FUND-I)",
      clocks: { posting: "2026-06-30" }, attachments: [ev("IC interest schedule", "ICL-0630")],
      lines: [{ code: "5300", debit: 12400 }, { code: "2400", credit: 12400 }] },
  ];

  // ── The custodian statement ─────────────────────────────────────────────────
  // The other side of the reconciliation. `ledgerRef` is the journal it matches; a line with none
  // is an open break — and its residual is ACTUAL until a journal posts.
  var STATEMENT = [
    { entity: "MER-FUND-I", id: "DTC-88421", date: "2026-06-02", memo: "Settlement credit", amount: 82440, ledgerRef: "JE-1042", account: "1100" },
    { entity: "MER-FUND-I", id: "FEE-0093", date: "2026-06-03", memo: "Commission", amount: -318.5, ledgerRef: "JE-1043", account: "1100" },
    { entity: "MER-FUND-I", id: "WIRE-5510", date: "2026-06-05", memo: "Custodian wire", amount: -40000, ledgerRef: "JE-1051", account: "1100" },
    { entity: "MER-FUND-I", id: "INT-0210", date: "2026-06-06", memo: "Interest accrual — sweep account", amount: 142.18, ledgerRef: null, account: "1100",
      breakId: "CASE-4187", classification: "Timing — income received, not accrued",
      evidence: [ev("Custodian statement page", "CS-0626 p.4"), ev("Sweep rate sheet", "SWP-0606")] },
    { entity: "MER-FUND-I", id: "MRG-0612", date: "2026-06-12", memo: "Margin interest", amount: -1120, ledgerRef: "JE-1071", account: "1100" },
    { entity: "MER-FUND-I", id: "EQT-7810", date: "2026-06-16", memo: "Equity purchase — NVDA", amount: -64120, ledgerRef: "JE-1080", account: "1100" },
    { entity: "MER-SPV-ALPHA", id: "EQT-3110", date: "2026-06-11", memo: "Equity purchase — XLE", amount: -30500, ledgerRef: "SA-0131", account: "1100" },
  ];

  // ── Identity ────────────────────────────────────────────────────────────────
  var id = function (type, key) { return String(type) + ":" + String(key); };
  var parse = function (identity) {
    var s = String(identity || ""), i = s.indexOf(":");
    return i < 0 ? { type: "", key: s } : { type: s.slice(0, i), key: s.slice(i + 1) };
  };

  var accountOf = function (code) {
    for (var i = 0; i < ACCOUNTS.length; i += 1) if (ACCOUNTS[i].code === String(code)) return ACCOUNTS[i];
    return { code: String(code), name: "Unmapped account", type: "asset" };
  };
  var label = function (code) { var a = accountOf(code); return a.code + " · " + a.name; };

  /** Does this journal belong in the selected scope? Basis exclusion is stated, never silent. */
  function inScope(j, scope) {
    if (j.entity !== scope.entity) return false;
    if (j.basisOnly && j.basisOnly.indexOf(scope.basis) < 0) return false;
    var p = periodOf(scope.period);
    return !p || (j.date >= p.start && j.date <= p.end);
  }
  function periodOf(pid) {
    for (var i = 0; i < SCOPES.period.length; i += 1) if (SCOPES.period[i].id === pid) return SCOPES.period[i];
    return null;
  }

  /**
   * Validate a draft journal. Returns `{ ok, issues, journal }` — issues are operator sentences,
   * so a caller can hand them straight to `JournalEntryForm`'s `blockers`.
   * A draft is NOT postable merely because it balances (increment A): rationale and at least one
   * attachment are part of the reviewed payload, and a closed period refuses the posting date.
   */
  function stage(draft, scope) {
    var lines = (draft.lines || []).map(function (l) {
      return { code: String(l.code || l.account || ""), debit: R2(l.debit), credit: R2(l.credit) };
    }).filter(function (l) { return l.code && (l.debit || l.credit); });
    var d = lines.reduce(function (a, l) { return a + l.debit; }, 0);
    var c = lines.reduce(function (a, l) { return a + l.credit; }, 0);
    var p = periodOf(scope && scope.period);
    var issues = [];
    if (lines.length < 2) issues.push({ label: "A journal needs at least two lines", detail: lines.length + " usable line(s)" });
    if (R2(d - c) !== 0) issues.push({ label: "Out of balance", detail: "Dr " + R2(d) + " · Cr " + R2(c) });
    if (!String(draft.rationale || "").trim()) issues.push({ label: "Rationale not recorded", detail: "the reviewed payload carries it — it is not a comment" });
    if (!(draft.attachments || []).length) issues.push({ label: "No evidence attached", detail: "the break's statement page is the minimum" });
    if (p && p.state !== "open") issues.push({ label: "Period is " + p.state, detail: p.label + " refuses a posting" });
    var dt = String(draft.date || "");
    var dm = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dt);
    var dv = dm && new Date(Date.UTC(+dm[1], +dm[2] - 1, +dm[3]));
    if (!dv || dv.getUTCFullYear() !== +dm[1] || dv.getUTCMonth() !== +dm[2] - 1 || dv.getUTCDate() !== +dm[3]) {
      issues.push({ label: "Posting date is not a valid date", detail: dt || "no date entered" });
    } else if (p && (dt < p.start || dt > p.end)) {
      issues.push({ label: "Posting date is outside " + p.label, detail: dt + " belongs to another period — the selected period's lock does not cover it" });
    }
    return {
      ok: issues.length === 0, issues: issues,
      journal: {
        entity: scope && scope.entity, ref: draft.ref, date: draft.date, memo: draft.memo,
        status: "staged", version: 1, condition: "provisional", authority: "desk",
        clocks: { posting: draft.date }, attachments: (draft.attachments || []).slice(),
        rationale: draft.rationale, sourceBreak: draft.sourceBreak, lines: lines,
      },
    };
  }

  /**
   * The reviewed journal payload — what a submit actually sends. Attachments and rationale are
   * INSIDE it, beside the snapshot the approval was taken against, so "approved" and "submitted"
   * cannot describe two different objects.
   */
  function reviewPayload(model, journal, review) {
    review = review || {};
    return {
      scope: { entity: model.scope.entity, book: model.scope.book, basis: model.scope.basis, period: model.scope.period },
      time: { asOf: model.time.asOf, postingDate: journal.date, reportingPeriod: model.scope.period },
      journal: {
        ref: journal.ref, memo: journal.memo, date: journal.date, version: journal.version || 1,
        lines: journal.lines.map(function (l) { return { account: label(l.code), code: l.code, debit: l.debit || 0, credit: l.credit || 0 }; }),
      },
      rationale: journal.rationale || review.rationale || null,
      attachments: (journal.attachments || []).slice(),
      resolves: journal.sourceBreak ? id("break", journal.sourceBreak) : null,
      snapshotId: review.snapshotId || null,
      approvedBy: review.actor || null,
      identity: id("journal", journal.ref),
    };
  }

  /**
   * The selector. `staged` is an array of draft journals (from `stage().journal`) which appear in
   * every derivation FLAGGED as staged and are summed into `projected` totals only.
   */
  function select(input) {
    var scope = {
      entity: (input && input.entity) || SCOPES.entity[0].id,
      book: (input && input.book) || "GL",
      basis: (input && input.basis) || "STAT",
      period: (input && input.period) || "2026-06",
    };
    var ent = SCOPES.entity.filter(function (e) { return e.id === scope.entity; })[0] || SCOPES.entity[0];
    var per = periodOf(scope.period) || SCOPES.period[0];
    scope.entityLabel = ent.label;
    scope.entityPath = ent.path;
    scope.periodLabel = per.label;
    scope.basisNote = (SCOPES.basis.filter(function (b) { return b.id === scope.basis; })[0] || {}).note;
    scope.label = [ent.label, scope.basis, per.label].join(" · ");

    var staged = ((input && input.staged) || []).filter(function (j) { return j && j.entity === scope.entity; });
    var posted = JOURNALS.filter(function (j) { return inScope(j, scope); });
    var excluded = JOURNALS.filter(function (j) {
      return j.entity === scope.entity && j.basisOnly && j.basisOnly.indexOf(scope.basis) < 0;
    });
    var all = posted.concat(staged);

    var asOf = (input && input.asOf) || (per.end + " 23:59:59Z");
    var time = {
      asOf: asOf, reportingPeriod: per.label, postingWindow: per.start + " → " + per.end,
      evidenceAt: "2026-06-26 06:00:00Z", calculatedAt: (global.MeridianWorld && global.MeridianWorld.stamp) ? global.MeridianWorld.stamp() : asOf,
      periodState: per.state,
    };

    // GL lines — one row per journal line, each with its own identity and its journal's.
    var glRows = [];
    all.forEach(function (j) {
      j.lines.forEach(function (l, i) {
        glRows.push({
          identity: id("glline", j.ref + "#" + (i + 1)), journalIdentity: id("journal", j.ref),
          date: j.date, ref: j.ref, code: l.code, account: label(l.code), memo: j.memo,
          debit: l.debit || "", credit: l.credit || "", status: j.status,
          staged: j.status === "staged", condition: j.condition, authority: j.authority, detail: j.detail,
          attachments: j.attachments || [], clocks: j.clocks,
        });
      });
    });

    var counts = function (rows) {
      return rows.reduce(function (a, r) {
        a.debits = R2(a.debits + (Number(r.debit) || 0));
        a.credits = R2(a.credits + (Number(r.credit) || 0));
        return a;
      }, { debits: 0, credits: 0 });
    };
    var live = glRows.filter(function (r) { return r.status !== "void"; });
    var actualRows = live.filter(function (r) { return !r.staged; });
    var proofActual = counts(actualRows), proofProjected = counts(live);
    proofActual.balanced = R2(proofActual.debits - proofActual.credits) === 0;
    proofProjected.balanced = R2(proofProjected.debits - proofProjected.credits) === 0;

    // Account balances — opening + movement, actual and projected kept apart.
    var openings = OPENINGS[scope.entity] || {};
    var accounts = ACCOUNTS.filter(function (a) { return openings[a.code] !== undefined; }).map(function (a) {
      var mine = function (rows) { return rows.filter(function (r) { return r.code === a.code; }); };
      var mv = function (rows) {
        return R2(mine(rows).reduce(function (s, r) { return s + (Number(r.debit) || 0) - (Number(r.credit) || 0); }, 0));
      };
      var opening = R2(openings[a.code]);
      var movement = mv(actualRows), projectedMovement = mv(live);
      return {
        identity: id("account", a.code), code: a.code, name: a.name, type: a.type, label: label(a.code),
        opening: opening, movement: movement, closing: R2(opening + movement),
        projectedMovement: projectedMovement, projectedClosing: R2(opening + projectedMovement),
        rows: mine(live),
      };
    });

    var trialBalance = accounts.map(function (a) {
      var flip = CREDIT_NORMAL[a.type] ? -1 : 1;
      return {
        identity: a.identity, code: a.code, account: a.name, type: a.type,
        balance: R2(a.closing * flip), projectedBalance: R2(a.projectedClosing * flip),
        moved: R2(a.projectedClosing - a.closing) !== 0,
      };
    });

    // Breaks — a statement line with no ledger counterpart. A staged journal closes the PROJECTED
    // residual; only a posted one closes the ACTUAL residual. The two never share a field.
    var stagedResolves = staged.map(function (j) { return j.sourceBreak; }).filter(Boolean);
    var postedResolves = staged.filter(function (j) { return j.status !== "staged"; })
      .map(function (j) { return j.sourceBreak; }).filter(Boolean);
    var breaks = STATEMENT.filter(function (s) { return s.entity === scope.entity && !s.ledgerRef; }).map(function (s) {
      var key = s.breakId || s.id;
      var byStaged = stagedResolves.indexOf(key) >= 0;
      var byPosted = postedResolves.indexOf(key) >= 0;
      return {
        identity: id("break", key), breakId: key, statementLine: s.id,
        date: s.date, memo: s.memo, amount: R2(s.amount), account: s.account, accountIdentity: id("account", s.account),
        classification: s.classification, evidence: s.evidence || [],
        condition: byPosted ? "reconciled" : "exception",
        actualResidual: byPosted ? 0 : R2(-s.amount),
        projectedResidual: byStaged ? 0 : R2(-s.amount),
        resolvedBy: byPosted ? "posted" : byStaged ? "staged" : null,
      };
    });
    var matched = STATEMENT.filter(function (s) { return s.entity === scope.entity && s.ledgerRef; }).map(function (s) {
      return { identity: id("stmtline", s.id), statementLine: s.id, date: s.date, memo: s.memo, amount: R2(s.amount),
        ledgerRef: s.ledgerRef, ledgerIdentity: id("journal", s.ledgerRef), account: s.account, condition: "reconciled" };
    });

    var residual = {
      actual: R2(breaks.reduce(function (a, b) { return a + b.actualResidual; }, 0)),
      projected: R2(breaks.reduce(function (a, b) { return a + b.projectedResidual; }, 0)),
      staged: staged.filter(function (j) { return j.status === "staged"; }).length,
      open: breaks.filter(function (b) { return b.actualResidual !== 0; }).length,
    };

    var model = {
      scope: scope, time: time, journals: all, excluded: excluded,
      glRows: glRows, accounts: accounts, trialBalance: trialBalance,
      proof: { actual: proofActual, projected: proofProjected },
      breaks: breaks, matched: matched, residual: residual,
      accountSheet: function (code) {
        return accounts.filter(function (a) { return a.code === String(code); })[0] || null;
      },
      journal: function (ref) {
        return all.filter(function (j) { return j.ref === String(ref); })[0] || null;
      },
      find: function (identity) {
        var p = parse(identity);
        if (p.type === "journal") return model.journal(p.key);
        if (p.type === "account") return model.accountSheet(p.key);
        if (p.type === "break") return breaks.filter(function (b) { return b.breakId === p.key; })[0] || null;
        if (p.type === "glline") return glRows.filter(function (r) { return r.identity === identity; })[0] || null;
        if (p.type === "stmtline") return matched.filter(function (m) { return m.statementLine === p.key; })[0] || null;
        return null;
      },
    };
    return model;
  }

  global.MeridianBooks = {
    SCOPES: SCOPES, ACCOUNTS: ACCOUNTS, JOURNALS: JOURNALS, STATEMENT: STATEMENT,
    select: select, stage: stage, reviewPayload: reviewPayload,
    id: id, parse: parse, label: label, accountOf: accountOf, round: R2,
  };
})(typeof window !== "undefined" ? window : this);
