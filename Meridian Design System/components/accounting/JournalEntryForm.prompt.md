Balanced double-entry input — header (date/ref/memo) + an Account · Debit · Credit line grid with live totals and a balance gauge that stays red ("Out by …") until it ties out, then turns green.

```jsx
<JournalEntryForm
  accounts={["1000 Cash", "4000 Revenue", "5000 COGS"]}
  initialLines={[{ account:  "1000 Cash", debit: 1200 }, { account: "4000 Revenue", credit: 1200 }]}
  onChange={setDraft}
  onPost={handlePost}
/>
```

Gate the surrounding workflow on the same balance check `onPost` already enforces — don't add a second, separately-computed "is it balanced" gate elsewhere in your screen.
