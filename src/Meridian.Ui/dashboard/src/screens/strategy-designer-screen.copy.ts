/**
 * Operator-facing copy for the Strategy Designer that must stay consistent across the view-model
 * and the rendered screen.
 */

/**
 * Why a backtest proof cannot run from the designer today.
 *
 * The run endpoint requires governed evidence references — operator acceptance, retained evidence,
 * accounting records, approvals, paper validation, governed reports — that this screen has no way
 * to collect. The command is therefore blocked for every design regardless of validation state.
 * Holding the reason in one place keeps the status label, proof summary, accessible label, and
 * button from contradicting each other; an earlier revision disabled only the rendered button while
 * the view-model still advertised the proof as ready to run.
 */
export const BACKTEST_EVIDENCE_BLOCKER =
  "Backtest execution needs governed evidence references (operator acceptance, retained evidence, accounting, approvals, paper validation, governed reports) that this screen cannot collect yet.";
