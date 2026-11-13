import {
  proveRollforward,
  proveTieOut,
  rollforwardVerdict,
  roundMoney,
  TIE_OUT_STATUS_TEXT,
  usableAmount
} from "./proof-arithmetic";

describe("proof-arithmetic.roundMoney", () => {
  it("rounds exact halves to the even neighbour by default", () => {
    expect(roundMoney(0.125)).toBe(0.12);
    expect(roundMoney(0.135)).toBe(0.14);
    expect(roundMoney(-0.125)).toBe(-0.12);
    expect(roundMoney(2.5, 0)).toBe(2);
    expect(roundMoney(3.5, 0)).toBe(4);
  });

  it("strips floating-point dust before detecting a half", () => {
    // 1.005 * 100 is 100.49999999999999 in binary floating point; it is still an exact half.
    expect(roundMoney(1.005)).toBe(1);
    expect(roundMoney(0.1 + 0.2)).toBe(0.3);
  });

  it("supports half-up and truncation as explicit policies", () => {
    expect(roundMoney(0.125, 2, "half-up")).toBe(0.13);
    expect(roundMoney(0.129, 2, "trunc")).toBe(0.12);
    expect(roundMoney(-0.129, 2, "trunc")).toBe(-0.12);
  });

  it("returns NaN for blank or unparseable input rather than zero", () => {
    expect(Number.isNaN(roundMoney(null))).toBe(true);
    expect(Number.isNaN(roundMoney(""))).toBe(true);
    expect(Number.isNaN(roundMoney("n/a"))).toBe(true);
  });
});

describe("proof-arithmetic.usableAmount", () => {
  it("keeps finite numbers, including zero", () => {
    expect(usableAmount(0)).toBe(0);
    expect(usableAmount(-12.5)).toBe(-12.5);
  });

  it("treats null, undefined and non-finite values as missing, never zero", () => {
    expect(usableAmount(null)).toBeNull();
    expect(usableAmount(undefined)).toBeNull();
    expect(usableAmount(Number.NaN)).toBeNull();
    expect(usableAmount(Number.POSITIVE_INFINITY)).toBeNull();
  });
});

describe("proof-arithmetic.proveRollforward", () => {
  it("ties when opening plus movements equals the stated closing", () => {
    const proof = proveRollforward({
      opening: 9_875_000,
      movements: [{ label: "Discount accretion", value: 3_812.4 }, { label: "Principal movement", value: 0 }],
      closing: 9_878_812.4
    });

    expect(proof).toMatchObject({ computable: true, ties: true, difference: 0, derivedClosing: 9_878_812.4 });
    expect(rollforwardVerdict(proof)).toBe("tied");
  });

  it("reports the out-by amount when the stated closing disagrees", () => {
    const proof = proveRollforward({
      opening: 100,
      movements: [{ label: "Call", value: 50 }, { label: "Distribution", value: -20 }],
      closing: 135.5
    });

    expect(proof.ties).toBe(false);
    expect(proof.derivedClosing).toBe(130);
    expect(proof.difference).toBe(5.5);
    expect(rollforwardVerdict(proof)).toBe("out");
  });

  it("uses opening plus movements as the closing when none is stated", () => {
    const proof = proveRollforward({ opening: 10, movements: [{ label: "Call", value: 2.5 }] });

    expect(proof.statedClosing).toBe(12.5);
    expect(proof.ties).toBe(true);
  });

  it("does not let binary floating-point dust break a tie", () => {
    const proof = proveRollforward({
      opening: 0.1,
      movements: [{ label: "Accrual", value: 0.2 }],
      closing: 0.3
    });

    expect(proof.ties).toBe(true);
  });

  it("is not computable when the opening or a movement has no amount, and never ties", () => {
    const missingOpening = proveRollforward({ openingLabel: "Opening net activity", opening: null, movements: [], closing: 0 });
    expect(missingOpening.computable).toBe(false);
    expect(missingOpening.ties).toBe(false);
    expect(missingOpening.gaps).toEqual(["Opening net activity has no value"]);
    expect(rollforwardVerdict(missingOpening)).toBe("indeterminate");

    const missingMovement = proveRollforward({
      opening: 100,
      movements: [{ label: "Capital call", value: Number.NaN }],
      closing: 100
    });
    expect(missingMovement.computable).toBe(false);
    expect(missingMovement.ties).toBe(false);
    expect(missingMovement.gaps).toEqual(['Movement "Capital call" has no amount']);
    expect(missingMovement.statedClosing).toBe(100);
  });

  it("does not tie against a closing that was expected but is not available", () => {
    const proof = proveRollforward({
      opening: 100,
      movements: [{ label: "Capital call", value: 25 }],
      closingLabel: "Ending net activity",
      closing: null
    });

    expect(proof.computable).toBe(false);
    expect(proof.ties).toBe(false);
    expect(proof.gaps).toEqual(["Ending net activity has no value"]);
    expect(proof.statedClosing).toBeNull();
  });

  it("does not call figures tied when they agree by construction", () => {
    // One net movement that is the closing restated: it agrees for every value, so it proves nothing.
    for (const net of [0, 125, -9_999_999.99, 0.1 + 0.2, 1e12 + 0.01]) {
      const proof = proveRollforward({ opening: 0, movements: [{ label: "Net capital activity", value: net }], closing: net });

      expect(proof.ties).toBe(true);
      expect(rollforwardVerdict(proof, { independent: false })).toBe("unsupported");
    }
  });

  it("still reports an out-by or a missing amount for figures that are not independent", () => {
    const out = proveRollforward({ opening: 0, movements: [{ label: "Net capital activity", value: 125 }], closing: 200 });
    expect(rollforwardVerdict(out, { independent: false })).toBe("out");

    const missing = proveRollforward({ opening: 0, movements: [{ label: "Net capital activity", value: 125 }], closing: null });
    expect(rollforwardVerdict(missing, { independent: false })).toBe("indeterminate");
  });
});

describe("proof-arithmetic.proveTieOut", () => {
  it("is indeterminate when a source has no value, whatever the tolerance", () => {
    const proof = proveTieOut({
      sourceA: { label: "Ledger balance", value: 100 },
      sourceB: { label: "Custodian statement", condition: "missing" },
      tolerance: 500
    });

    expect(proof.status).toBe("indeterminate");
    expect(proof.gaps).toEqual(["Custodian statement has no value"]);
    expect(proof.grossDifference).toBeNull();
    expect(proof.residual).toBeNull();
    expect(TIE_OUT_STATUS_TEXT[proof.status]).toBe("Not tied — input missing");
  });

  it("does not let a declared status outrank an unusable condition", () => {
    const proof = proveTieOut({
      sourceA: { label: "Ledger", value: 100, condition: "confirmed" },
      sourceB: { label: "Custodian", value: 100, condition: "stale" },
      status: "tied"
    });

    expect(proof.status).toBe("indeterminate");
    expect(proof.gaps).toEqual(["Custodian is stale"]);
  });

  it("names a not-applicable source in operator words", () => {
    const proof = proveTieOut({
      sourceA: { label: "Ledger", value: 100, condition: "na" },
      sourceB: { label: "Custodian", value: 100 }
    });

    expect(proof.gaps).toEqual(["Ledger is not applicable"]);
  });

  it("ties when explained items account for the whole difference", () => {
    const proof = proveTieOut({
      sourceA: { label: "Ledger", value: 100 },
      sourceB: { label: "Custodian", value: 90 },
      items: [{ label: "In-transit wire", value: -10 }]
    });

    expect(proof).toMatchObject({ status: "tied", grossDifference: 10, explainedTotal: -10, residual: 0 });
  });

  it("lists but does not sum items that are identified and not yet explained", () => {
    const proof = proveTieOut({
      sourceA: { label: "Ledger", value: 100 },
      sourceB: { label: "Custodian", value: 90 },
      items: [{ label: "Suspected timing", value: -10, explained: false }]
    });

    expect(proof).toMatchObject({
      status: "exception",
      explainedTotal: 0,
      explainedCount: 0,
      unexplainedItemCount: 1,
      residual: 10
    });
  });

  it("separates within-tolerance from tied", () => {
    const proof = proveTieOut({
      sourceA: { label: "Ledger", value: 100 },
      sourceB: { label: "Custodian", value: 97 },
      tolerance: 5
    });

    expect(proof.status).toBe("tolerance");
  });

  it("is indeterminate when an explained item has no amount", () => {
    const proof = proveTieOut({
      sourceA: { label: "Ledger", value: 100 },
      sourceB: { label: "Custodian", value: 100 },
      items: [{ label: "Fee accrual", value: null }]
    });

    expect(proof.status).toBe("indeterminate");
    expect(proof.explainedTotal).toBeNull();
    expect(proof.gaps).toEqual(['Reconciling item "Fee accrual" has no amount']);
  });

  it("honours a declared status that is stricter than the arithmetic", () => {
    const proof = proveTieOut({
      sourceA: { label: "Custodian statement", value: 0 },
      sourceB: { label: "Internal ledger", value: 0 },
      items: [
        { label: "Cash", value: 20, explained: false },
        { label: "Payables", value: -20, explained: false }
      ],
      status: "exception"
    });

    expect(proof.derivedStatus).toBe("tied");
    expect(proof.status).toBe("exception");
    expect(proof.residual).toBe(0);
  });

  it("ignores a declared status that is more favourable than the arithmetic", () => {
    const proof = proveTieOut({
      sourceA: { label: "Custodian statement", value: 800 },
      sourceB: { label: "Internal ledger", value: 600 },
      status: "tied"
    });

    expect(proof.status).toBe("exception");
    expect(proof.residual).toBe(200);
  });

  it("believes a caller that declares the tie-out indeterminate", () => {
    const proof = proveTieOut({
      sourceA: { label: "Custodian statement", value: 10 },
      sourceB: { label: "Internal ledger", value: 10 },
      status: "indeterminate"
    });

    expect(proof.status).toBe("indeterminate");
    expect(proof.residual).toBeNull();
    expect(proof.gaps).toHaveLength(1);
  });
});
