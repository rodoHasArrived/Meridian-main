A sticky-header band that labels a region of an operator workspace — title, muted summary, optional jump link — with its heading pinned while the region scrolls beneath it.

```jsx
<WorkspaceSection
  id="reconciliation"
  title="Reconciliation"
  summary="3 open exceptions above tolerance · custody cash lane"
  jump="Open lane" jumpHref="#recon"
>
  <ValidationIssueList issues={issues} />
</WorkspaceSection>
```

Use to break a long single-scroll workstation page into named, linkable regions instead of introducing `Tabs` (which hide content rather than band it).
