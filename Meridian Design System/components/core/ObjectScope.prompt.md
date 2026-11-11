Use this whenever a screen shows figures about more than one object — which is most of Meridian. Declare the surface's object once; mark only the figures that belong somewhere else.

```jsx
<ObjectScopeProvider scope="position" object="GA-FI-01">
  <ScopedValue label="Market value" value="$24.84M" size="lg" />
  <ScopedValue scope="security" label="Duration" value="8.22" explain />
  <ScopedValue scope="portfolio" label="Duration contribution" value="0.21" />
  <ScopeMismatch />
</ObjectScopeProvider>
```

In-scope figures print a quiet scope word; foreign figures get a left rail and a dashed/dotted mark, so "whose number is this" is answered structurally, not by color. `explain` prints the reason in words — worth it on the first foreign figure in a region, not on all of them.

`scope` takes object-model type ids (`security`, `lot`, `position`, `account`, `portfolio`, `legalEntity`), so the scope word on a figure is the same word `ObjectHeader` prints for that object type. Do not invent scope names for bases, periods, or scenarios: those are **context bar** parameters (`WorkspaceContextBar`), not the object a figure is true of.

`ScopeMismatch` audits its region and renders nothing when everything is in scope — put one at the foot of a dense analytical panel rather than annotating every figure.

Nest providers: a position drawer opened over a security workspace wraps its own `ObjectScopeProvider scope="position"`, so the drawer's figures read as in-scope inside it while the page behind stays security-scoped.
