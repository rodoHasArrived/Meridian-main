# Calculated recurring journal golden file

`calculated-period-drafts.json` is the expected approval-draft output of
`RecurringJournalCalculationPostgresTests` for two month-ends of one USD fund book.

Seeded ledger: 10,000,000 capital on 2025-12-31; 100,000 EUR bought at 1.08 on 2026-01-10;
1,000,000 realized gain on 2026-01-20. Terms: management fee 2% on a 365-day basis; performance
fee 20% above a 10,000,000 high-water mark with no hurdle; EUR/USD 1.10 on 2026-01-31 and 1.09 on
2026-02-28.

Hand check:

- January management fee: 10,000,000 x 2% x 31/365 = 16,986.30.
- January performance fee: 20% x (11,000,000 - 10,000,000) = 200,000.00.
- January FX: 100,000 x 1.10 - 108,000 carried = 2,000.00 gain.
- February opening net assets after posting January: 11,000,000 - 16,986.30 - 200,000 + 2,000 =
  10,785,013.70, so the management fee is 10,785,013.70 x 2% x 28/365 = 16,546.87.
- February performance fee: value before fee 10,785,013.70 + 200,000 payable = 10,985,013.70;
  cumulative fee 197,002.74 less 200,000 already accrued = 2,997.26 reversal.
- February FX: 100,000 x 1.09 - 110,000 carried = 1,000.00 loss.

Change the file only with a matching hand calculation.
