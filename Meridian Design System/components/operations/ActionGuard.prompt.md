Declare, in one shared contract, whether a consequential action may proceed. `ActionGuardList` renders every guard with its state, policy reference, evidence count and remediation; `ActionAvailability` states an unavailable action's reason, resolution and required authority before the user clicks.

```jsx
<ActionGuardList onRemediate={requestPeriod} guards={[
  {id:"balanced",state:"satisfied",label:"Journal debits equal credits"},
  {id:"evidence",state:"warning",label:"Pricing source is stale by 23m",policyRef:"EV-3 §2",evidenceRefs:["src-441"]},
  {id:"period",state:"blocking",label:"Posting period is locked",
   reason:"July statutory close is hard closed.",policyRef:"AC-14 §6.3",
   remediation:{label:"Request adjustment period",actionId:"request-adjustment",requiredRole:"Controller"}}]} />
```

Use everywhere eligibility is decided — posting, attestation, resolution, period control, bulk action. Never hide a blocked action, and never ship an unexplained disabled button: state the reason, the remediation and the authority. Guards render at review density.
