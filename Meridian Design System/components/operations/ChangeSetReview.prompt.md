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

Required per-field reasons gate both saving and submitting. Validating, committed, superseded and conflicted change sets cannot be saved/submitted through the bar. Partial outcomes retain a caller-owned recovery path. Validation updates must flow through the provider even when the number of issues stays constant.

`ChangeSetProvider.use()` is the same context hook as `ChangeSetHooks.useChangeSet()`. Uncontrolled consumers can set fields/drafts, set reasons and explicitly discard; controlled drafts remain authoritative until the parent accepts a callback.

Private field reasons, uncontrolled drafts and discard confirmation are bound to change-set id/version. Switching binding clears prior private review state; controlled draft values remain authoritative. Save/submit callbacks notify the parent and do not themselves own asynchronous server completion.
