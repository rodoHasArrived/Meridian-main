Optimistic-concurrency conflict stated in three-way form — **base · yours · latest**. It never
defaults to overwrite and never auto-merges a conflicting financial value.

```jsx
<VersionConflictNotice record="Investment lot 00842"
  baseVersion={18} latestVersion={19} latestAuthor="J. Patel" latestAt="11:37 UTC"
  fields={[{key:"stat", label:"STAT classification", base:"2.B", proposed:"2.C", latest:"2.D"}]}
  onReload={...} onMergeNonConflicting={...} onReapply={({reason, choices}) => ...} />
```

- Fields that changed on one side only are flagged **Merges** and can be applied in bulk; genuinely
  divergent fields are flagged **Conflict** and require a per-field choice.
- Replacing a newer value warns that validation re-runs and that any impact preview taken against
  the base version is invalidated; a rationale is required by default.
- Primary action is *Reload latest record*. The notice is `role="alert"` and takes focus.
