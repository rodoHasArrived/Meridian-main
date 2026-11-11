Turns editing into a staged, inspectable transaction: **stage → validate → review impact →
commit**. `ChangeSetProvider` owns original and draft state; `ChangeSetDiff` renders field-level
before/after; `ChangeReviewBar` is the persistent status and action bar.

```jsx
<ChangeSetProvider original={record} version={18} fields={fieldMeta} validate={validateFn}>
  <Editor />
  <ChangeSetReview title="Security master change set" changeSetId="CS-1042"
    scope="Investment lot 00842" affectedRecords={412} initiatedBy="M. Okafor"
    impact={{records:412, recalculations:3, reports:2, lockedPeriod:true, invalidatesApprovals:true}}
    requireReason onSubmit={...} onSave={...} />
</ChangeSetProvider>
```

- Changes group by business domain (`fields[key].group`) and carry a per-field reason, validation
  issue, and downstream impact.
- Lifecycle: `clean` · `draft` · `validating` · `invalid` · `warning` · `ready` ·
  `submitted` · `approved` · `rejected` · `committed` · `partial` · `superseded` ·
  `conflicted`. Derived unless `status` is supplied.
- Commit is blocked while any error-severity issue is open; discard confirms and lists what is lost.
- Read the change set with `ChangeSetHooks.useChangeSet()` (also `ChangeSetProvider.use()`).
