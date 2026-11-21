Own the ordered, guarded, version-aware progression from observation to committed decision. Replaces template-local `step`/`selectedId`/`isValid` state. The provider restores its step from the URL and from storage, and invalidates completed review state when the object's version changes underneath the user.

```jsx
<DecisionFlowProvider object={{type:"reconciliation",id:"REC-418",version:18}}
  snapshotId="snap-2026-07-31-18" urlParam="decisionStep" initialStep="tieout"
  onValidate={id=>validateStep(id)}
  steps={[
    {id:"tieout",label:"Understand difference",stableAcrossVersions:true},
    {id:"evidence",label:"Review evidence",requires:["tieout"]},
    {id:"resolve",label:"Propose resolution",requires:["evidence"],invalidates:["post"]},
    {id:"post",label:"Review posting impact",requires:["resolve"]},
    {id:"attest",label:"Attest",requires:["post"]}]}>
  <DecisionFlowRail/>
  <DecisionFlowStage>{/* work plane, EvidenceRail, ConsequenceRail */}</DecisionFlowStage>
  <DecisionConsequenceBar primaryLabel="Post journal" reversibility="compensatable" onPrimary={post}/>
</DecisionFlowProvider>
```

`useDecisionFlow()` exposes `stepGuards`, `complete`, `goTo`, `unmet`, `versionChanged`. Do not let the flow become a wizard that hides evidence: the rail is navigation, not a funnel, and every step keeps its evidence and consequence surfaces reachable. Always declare `reversibility` on the consequence bar.

No declared/current decision step means no consequential primary action. Completion rejects unknown or unmet steps; empty data preserves a known restored selection until steps return. Version changes invalidate dependent completion as before.

Review state is bound to the canonical object type, id and version. Switching object identity clears all prior completion, guard results and transition history, including steps stable across versions. Pending validation or step-entry work from an older object binding cannot complete or transition the new binding.

A rejected asynchronous `onValidate` result becomes a blocking validation-unavailable guard and cannot complete a step. Explicit `validate(stepId)` or a later completion attempt can revalidate after recovery. Superseded object-bound results remain ignored. Unavailable local storage does not prevent URL navigation or in-memory flow state.

While asynchronous validation is in progress, the consequential primary waits. Only the newest result for a step may replace its guards or complete it; superseded results do not certify an empty guard set. A rejected step-entry callback produces blocking evidence and can recover through the existing step navigation. Pending validation/entry completions cannot settle a changed or unmounted object binding.
