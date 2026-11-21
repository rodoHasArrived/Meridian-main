import type { MarkFreshnessAssessmentDto } from "@/types/mark-freshness";

/** Presentation only: the shared service owns dates, age, and the eligibility decision. */
export function presentMarkFreshness(assessment?: MarkFreshnessAssessmentDto | null) {
  const reviewRequired = assessment?.status !== "Current";
  const observedOn = assessment?.observedOn ?? null;
  const age = assessment?.ageDays == null ? null : `${assessment.ageDays} day(s)`;
  return {
    /** False when the shared service returned no assessment at all for the position. */
    assessed: assessment != null,
    /** One line for table cells: what was observed, or a single hint when nothing was. */
    observationSummary: observedOn && age
      ? `Observed ${observedOn} · age ${age}`
      : observedOn
        ? `Observed ${observedOn}`
        : age
          ? `Age ${age}`
          : "No mark observed",
    reviewRequired,
    label: reviewRequired ? "Review required" : "Current",
    tone: reviewRequired ? "warning" as const : "success" as const,
    observedOn: assessment?.observedOn ?? "Unknown",
    age: assessment?.ageDays == null ? "Unknown" : `${assessment.ageDays} day(s)`,
    valuationDate: assessment?.valuationDate ?? "Unknown",
    policyVersion: assessment?.policyVersion ?? "Unavailable",
    reason: assessment?.blockReason ?? (reviewRequired
      ? "Shared mark assessment unavailable. Refresh valuation evidence before approval."
      : "Mark observation is eligible under the shared valuation policy.")
  };
}

export type MarkFreshnessPresentation = ReturnType<typeof presentMarkFreshness>;

export function markFreshnessFields(mark: MarkFreshnessPresentation) {
  if (!mark.assessed) {
    // No assessment means no observation date, age, valuation date or policy. The mark price row
    // (`markPriceField`) already says the price has no observed mark, so it is not repeated here
    // as a separate "No mark observed" row beside a price; the assessment row carries the reason
    // and the next step.
    return [
      { label: "Mark readiness", value: mark.label, tone: mark.tone },
      { label: "Mark assessment", value: mark.reason, tone: mark.tone }
    ];
  }

  return [
    { label: "Mark readiness", value: mark.label, tone: mark.tone },
    { label: "Mark observed on", value: mark.observedOn, tone: "muted" as const },
    { label: "Mark age", value: mark.age, tone: mark.tone },
    { label: "Valuation date", value: mark.valuationDate, tone: "muted" as const },
    { label: "Mark policy", value: mark.policyVersion, tone: "muted" as const },
    { label: "Mark assessment", value: mark.reason, tone: mark.tone }
  ];
}

/**
 * The mark price row of a holding detail, labelled by where the price came from. A price carried on
 * the position record with no market observation behind it says so in the same cell, so a figure
 * never sits beside a separate row claiming no mark exists.
 */
export function markPriceField(price: string, mark: MarkFreshnessPresentation) {
  const observed = mark.assessed && mark.observedOn !== "Unknown";
  return observed
    ? { label: "Mark price", value: `${price} (observed ${mark.observedOn})`, tone: "muted" as const }
    : { label: "Mark price", value: `${price} (recorded on the position, no observed mark)`, tone: "warning" as const };
}
