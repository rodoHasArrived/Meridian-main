## Executive Summary
- Mode: `design_partner`
- Verdict: `steer`
- The welcome surface is directionally strong because it presents a short path into provider setup, symbols, and live workflow, but the current onboarding still hides too much around config path, disabled launch gating, and time-to-value.

## Panel
- Hobbyist Builder — tests whether the first session feels inviting instead of intimidating.
- Support / Onboarding Lead — tests whether the current copy will generate avoidable support tickets.
- Individual Trader — tests whether the screen feels action-oriented or bureaucratic.
- Owner-Operator — tests whether the onboarding flow creates momentum or support drag.

## Persona Findings
### Hobbyist Builder
- Liked: The welcome screen creates an understandable first path with Connect a Provider, Add Symbols, and Launch Live Session.
- Didn't like: The runtime config note says it lives outside the install directory without telling the user where the config path actually is.
- Missing or risky: The disabled Launch Live Session state feels mysterious because credentials gating is not explained clearly.
- Owner-minded improvement ideas: Show the actual config path, explain the credential dependency, and estimate time-to-value for first setup.
- Adoption verdict: Curious and willing to explore, but would hesitate if the first friction feels like hidden setup debt.
- Rubric (1-5 with evidence):
- Workflow Fit: 3/5 - The sequence is sensible, but the gating explanation is incomplete.
- Trust / Controls: 3/5 - The config note is honest, yet the hidden path weakens confidence.
- Time-to-Value: 3/5 - The lack of setup-time guidance makes the first win feel uncertain.
- Data Confidence: 3/5 - Nothing looks wrong, but the hidden prerequisites reduce confidence.
- Extensibility: 4/5 - The welcome flow can absorb better guidance without structural change.
- Learning Curve: 3/5 - It is approachable, but not yet self-explanatory.

### Support / Onboarding Lead
- Liked: The three-card structure gives support teams a simple language for the first-session path.
- Didn't like: Users will ask why Launch Live Session is disabled if credentials gating stays implicit.
- Missing or risky: There is no visible estimate of setup time or what counts as “done enough” to move forward.
- Owner-minded improvement ideas: Add inline copy for the disabled state, a config path disclosure, and a short setup-time expectation.
- Adoption verdict: Would support testing this with new users, but expects repeat tickets in the current copy.
- Rubric (1-5 with evidence):
- Workflow Fit: 3/5 - The path is good, but the state explanations are too thin.
- Trust / Controls: 3/5 - Clearer state messaging would improve trust quickly.
- Time-to-Value: 2/5 - Hidden setup effort undermines the first-session story.
- Data Confidence: 3/5 - The issue is not data quality, it is onboarding clarity.
- Extensibility: 4/5 - Better guidance can be added in place.
- Learning Curve: 3/5 - New users still need hand-holding.

### Individual Trader
- Liked: The screen points at obvious actions instead of opening with enterprise jargon.
- Didn't like: A disabled Launch Live Session button without a crisp reason feels like a dead end.
- Missing or risky: The onboarding does not tell the user how long it will take to get to a live session.
- Owner-minded improvement ideas: Show why the button is disabled and what the next fastest step is.
- Adoption verdict: Might bounce if the first blocked action feels arbitrary.
- Rubric (1-5 with evidence):
- Workflow Fit: 3/5 - Good flow, weak blocked-state explanation.
- Trust / Controls: 3/5 - Honest gating exists, but the explanation is not visible enough.
- Time-to-Value: 2/5 - The missing setup estimate hurts urgency.
- Data Confidence: 3/5 - No major data concern, just onboarding friction.
- Extensibility: 4/5 - Simple copy and state fixes would help a lot.
- Learning Curve: 4/5 - The surface is readable even if it still needs guidance.

### Owner-Operator
- Liked: The welcome screen already gestures toward Meridian as a workstation, not a loose collection of pages.
- Didn't like: Hidden config path details and unclear disabled gating create immediate support burden.
- Missing or risky: The onboarding does not yet tell users how long first setup takes or what unlocks the live path.
- Owner-minded improvement ideas: Steer the next pass toward explicit gating reasons, config path transparency, and a faster first-win story.
- Adoption verdict: Worth iterating, but not yet strong enough to become the polished first impression Meridian deserves.
- Rubric (1-5 with evidence):
- Workflow Fit: 3/5 - Coherent flow, incomplete explanation.
- Trust / Controls: 3/5 - Transparency is started, not finished.
- Time-to-Value: 2/5 - The missing estimate and hidden prerequisites make the first session feel riskier.
- Data Confidence: 3/5 - No direct data issue, but onboarding ambiguity still hurts perceived confidence.
- Extensibility: 4/5 - High leverage fixes are small.
- Learning Curve: 3/5 - The first session is understandable, not yet reassuring.

## Cross-Persona Tensions
- Shared complaint: the config path needs to be named explicitly, not hinted at vaguely.
- Shared complaint: the disabled Launch Live Session state needs a clear credentials explanation.
- Tension: the Individual Trader wants the shortest possible path, while the Support / Onboarding Lead wants a little more visible guidance before release.

## Owner Actions
- Now: Show the actual config path, explain the credentials gating inline, and add a visible setup-time estimate.
- Next: Add a small “what unlocks live session” checklist so the welcome screen feels self-guiding.
- Later: Layer in deeper onboarding help only after the blocked-state story is crisp.

## Release Recommendation
- Recommendation: `steer`
- Rationale: The structure is right, but the next pass should focus on transparency and first-session momentum before polishing visuals further.

## Confidence Notes
- Verified: The artifact summary explicitly mentions the outside-the-install-directory config note, the disabled Launch Live Session action, and the missing setup-time estimate.
- Inferred: Likely support burden and bounce risk are persona-level predictions.
- Missing evidence: The bundle does not show a visible config path, an explicit credentials explanation, or setup timing guidance.
