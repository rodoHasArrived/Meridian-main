The period-end proof — one row per account, net on its normal side, grouped into type sections with subtotals; the footer proves Σdebit = Σcredit and flags any difference in red.

```jsx
const { TrialBalance, Money } = window.MeridianDesignSystem_4f61be;

// From raw journal postings — collapse first:
<TrialBalance rows={Money.buildTrialBalance(postings)} currency="USD" />

// Or pass rows directly; `balance` is signed in normal-side terms:
<TrialBalance rows={[
  { code: "1010", account: "Cash — operating", type: "asset", balance: 412800.55 },
  { code: "2100", account: "Accrued fees",     type: "liability", balance: 18240.00 },
  { code: "1550", account: "Accum. depreciation", type: "contra-asset", balance: 42100 }, // → credit column
]} />
```

Non-obvious: rows with a signed `balance` (no debit/credit) are placed by `type` normal side — negatives flip sides, and contra types classify into their parent section. `grouped={false}` gives a flat listing. Section subtotals only render when a section has more than one account.
