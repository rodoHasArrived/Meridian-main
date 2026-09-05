import type { MarkFreshnessAssessmentDto } from "@/types/mark-freshness";

/** Presentation only: the shared service owns dates, age, and the eligibility decision. */
export function presentMarkFreshness(assessment?: MarkFreshnessAssessmentDto | null) {
  const reviewRequired = assessment?.status !== "Current";
  return {
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
  return [
    { label: "Mark readiness", value: mark.label, tone: mark.tone },
    { label: "Mark observed on", value: mark.observedOn, tone: "muted" as const },
    { label: "Mark age", value: mark.age, tone: mark.tone },
    { label: "Valuation date", value: mark.valuationDate, tone: "muted" as const },
    { label: "Mark policy", value: mark.policyVersion, tone: "muted" as const },
    { label: "Mark assessment", value: mark.reason, tone: mark.tone }
  ];
}
