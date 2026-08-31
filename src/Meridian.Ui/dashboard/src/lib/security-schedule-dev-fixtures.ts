import type { SecurityCashFlowScheduleEvent } from "@/screens/accounting-screen.view-model";

// Development-only cash-flow schedule fixtures for the Security Master workbench.
// These rows previously lived inline in the production accounting view-model and were
// rendered as real posted/forecast accounting data whenever a live trust snapshot was
// absent — bypassing the DEV fixture gate and the operator "Demo data" notice. They are
// now resolved only behind `import.meta.env.DEV` (see resolveSecurityScheduleEvents),
// which also marks fixture usage so the demo banner fires.
const securityScheduleFixtures: Record<string, SecurityCashFlowScheduleEvent[]> = {
  "sec-dev-004": [
    {
      eventId: "sched-sec-dev-004-cpn-2026-06",
      securityId: "sec-dev-004",
      scheduleFamily: "bond",
      eventType: "Coupon",
      paymentDate: "2026-06-15",
      accrualStartDate: "2025-12-15",
      accrualEndDate: "2026-06-15",
      couponRatePct: 5.875,
      expectedAmount: 29375,
      actualAmount: null,
      principalAmount: null,
      interestAmount: 29375,
      factorStart: 1,
      factorEnd: 1,
      currency: "USD",
      postingStatus: "Forecast",
      auditReference: "fixture/security-master/cash-flow/sec-dev-004/cpn-2026-06",
      note: "Semi-annual fixed coupon projected from the reference coupon schedule."
    },
    {
      eventId: "sched-sec-dev-004-paydown-2026-09",
      securityId: "sec-dev-004",
      scheduleFamily: "structured",
      eventType: "Paydown",
      paymentDate: "2026-09-15",
      accrualStartDate: "2026-06-15",
      accrualEndDate: "2026-09-15",
      couponRatePct: 5.875,
      expectedAmount: 148750,
      actualAmount: 147920,
      principalAmount: 125000,
      interestAmount: 23750,
      factorStart: 1,
      factorEnd: 0.875,
      currency: "USD",
      postingStatus: "Variance",
      auditReference: "fixture/security-master/cash-flow/sec-dev-004/paydown-2026-09",
      note: "Principal paydown carries a small expected-versus-actual variance for operator review."
    },
    {
      eventId: "sched-sec-dev-004-maturity-2031-12",
      securityId: "sec-dev-004",
      scheduleFamily: "bond",
      eventType: "Maturity",
      paymentDate: "2031-12-15",
      accrualStartDate: "2031-06-15",
      accrualEndDate: "2031-12-15",
      couponRatePct: 5.875,
      expectedAmount: 529375,
      actualAmount: null,
      principalAmount: 500000,
      interestAmount: 29375,
      factorStart: 0.875,
      factorEnd: 0,
      currency: "USD",
      postingStatus: "Pending",
      auditReference: "fixture/security-master/cash-flow/sec-dev-004/maturity-2031-12",
      note: "Final coupon and principal repayment remain pending until trustee schedule confirmation."
    }
  ],
  "sec-1": [
    {
      eventId: "sched-sec-1-cpn-2026-05",
      securityId: "sec-1",
      scheduleFamily: "bond",
      eventType: "Coupon",
      paymentDate: "2026-05-15",
      accrualStartDate: "2025-11-15",
      accrualEndDate: "2026-05-15",
      couponRatePct: 5.25,
      expectedAmount: 26250,
      actualAmount: 26250,
      principalAmount: null,
      interestAmount: 26250,
      factorStart: 1,
      factorEnd: 1,
      currency: "USD",
      postingStatus: "Posted",
      auditReference: "fixture/security-master/cash-flow/sec-1/cpn-2026-05",
      note: "Validation coupon row used by browser workbench checks."
    },
    {
      eventId: "sched-sec-1-principal-2026-11",
      securityId: "sec-1",
      scheduleFamily: "bond",
      eventType: "Principal",
      paymentDate: "2026-11-15",
      accrualStartDate: "2026-05-15",
      accrualEndDate: "2026-11-15",
      couponRatePct: 5.25,
      expectedAmount: 126250,
      actualAmount: null,
      principalAmount: 100000,
      interestAmount: 26250,
      factorStart: 1,
      factorEnd: 0.9,
      currency: "USD",
      postingStatus: "Pending",
      auditReference: "fixture/security-master/cash-flow/sec-1/principal-2026-11",
      note: "Validation amortization row keeps schedule selection consistent."
    }
  ]
};

export function resolveDevSecurityScheduleEvents(securityId: string): SecurityCashFlowScheduleEvent[] {
  return (securityScheduleFixtures[securityId] ?? []).map((event) => ({ ...event }));
}
