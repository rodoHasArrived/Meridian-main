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
