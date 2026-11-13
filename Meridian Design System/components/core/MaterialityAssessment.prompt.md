State how consequential a difference, override or exception is — independently of whether it passed a tolerance test. `MaterialityBadge` inline in a row or header; `MaterialityAssessment` for the full panel with quantitative benchmark, qualitative checklist and policy reference.

```jsx
<MaterialityAssessment assessment={{
  classification:"operational", amount:331.68, percentage:0.07,
  quantitative:{benchmark:"Segment pre-tax income",threshold:500,ratio:0.0007},
  qualitativeFactors:[
    {id:"regulatory",label:"Regulatory or covenant sensitivity",present:false},
    {id:"trend",label:"Reverses a reported trend",present:false},
    {id:"control",label:"Indicates a control weakness",present:true,rationale:"Third recurrence of the same timing break this quarter."}],
  policyId:"MAT-2026",policyVersion:"2026.2",evaluatedAt:"2026-08-01T06:20Z",evaluatedBy:"a.rivera"}} />
```

Never compute materiality as `abs(variance) > tolerance`. Never encode the classification in color alone. Render tolerance, materiality, evidence completeness and control state as separate conclusions — they can legitimately disagree.
