# ObjectTrail

Per-operator traversal. Track A of `guidelines/SESSION_CONTINUITY.md`.

```jsx
<ObjectTrailProvider screenId="ops-wall" onNavigate={(entry,{versionMoved})=>openObject(entry,{versionMoved})}>
  <ObjectTrail />
  <WorkspaceLayoutGrid>…</WorkspaceLayoutGrid>
</ObjectTrailProvider>
```

The screen records its own arrivals — the trail never infers navigation:

```jsx
const trail = useObjectTrail();
React.useEffect(() => {
  trail.visit({ type:"reconciliation", id:"REC-2026-07-CASH", label:"July cash — General Account",
                version:12, currentVersion:12, meta:"3 breaks open", at:"09:41" });
}, [caseId]);
```

## Rules

- **Reference, not snapshot.** `{type, id, version, label}` plus one short `meta` qualifier. Never
  the object's data — a trail that caches values will show a figure that no longer exists.
- **`currentVersion` is the whole point.** Pass it whenever the screen knows it. When it differs from
  `version`, the entry renders *Version moved* and `onNavigate` reports `versionMoved: true`; the
  receiving surface must state the change (`VersionConflictNotice`, or `ContinuityFrame` for a
  restored view).
- **Type before label**, always from `ObjectModel` — "July" is a period, a journal batch and a report
  pack in the same session.
- **Pinning is intent; recency is behaviour.** Separate sections; `clearRecents()` keeps pins.
- Not an audit log. `EventTimeline` records what happened to an object; the trail records where a
  person went. Never render one as the other.
- `ObjectTrail` for a shell strip (under `WorkspaceContextBar`), `ObjectTrailList` for a rail or an
  arrival screen. Both are per operator — never per team.
- Hidden in print: where someone browsed is not part of a deliverable.
