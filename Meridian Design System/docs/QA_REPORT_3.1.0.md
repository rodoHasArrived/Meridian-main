# Meridian Design System 3.1.0 QA report

**Validated:** 2026-08-11  
**Release:** `3.1.0`  
**Result:** release gate passed; no unresolved release blockers

## Scope

This report covers the complete Meridian 3.1 source package: tokens, generated inventories, public browser bundle, component contracts, interactive specimens, responsive states, accessibility-sensitive behavior, workstation scaffold synchronization, and final archive integrity.

The release extends the existing Programmed Institutionalism system rather than restyling it. Visual review was anchored to the supplied Foundations & Primitives reference board and the established paper → plate → well material hierarchy.

## Release inventory under test

| Item | Validated count |
| --- | ---: |
| Source modules across seven component domains | 172 |
| Public component exports | 223 |
| Design tokens | 632 |
| Manifested card surfaces | 98 |
| Archetype workstation templates | 7 |
| Additive 3.1 source modules | 25 |
| Additive 3.1 public exports | 34 |
| New coordinated specimen cards | 10 |

Every additive source module includes its implementation, TypeScript declaration, and implementation prompt. The portable browser namespace is `window.MeridianDesignSystem_205d17`.

## Static release gate

Command:

```bash
python scripts/validate_release.py
```

The gate completed successfully and ran, in order:

1. Reproducible browser-bundle compilation for 26 explicit modules, including the revised primary `Button`.
2. JavaScript syntax validation of `_ds_bundle.js`.
3. Gallery and manifest catalog drift detection.
4. Generated README inventory synchronization.
5. CSS token parity validation.
6. Measured contrast checks for light, dark, semantic, chrome, selection, primary-action, and white-label states.
7. Design-system governance checks covering links, tokens, radii, prompt/type coverage, templates, and prohibited declarations.
8. Template/runtime scaffold drift detection.
9. Thirty-six Python unit and release-contract tests.

Result:

```text
36 tests passed
Catalog current: 97 specimens
Token parity: clean
Contrast: clean
Governance: clean
Scaffold: in sync
Meridian 3.1.0 release validation passed
```

The complete command log is retained at `qa/logs/static-validation.log`.

## Browser and interaction QA

The execution environment blocked direct `file://` and localhost navigation. Playwright therefore used `page.set_content()` with the release CSS, the compiled bundle, each specimen’s local CSS, and transpiled specimen JSX. The implementation itself was not replaced: React continued to own the application state and DOM structure while the Meridian SVG/DOM components owned analytical geometry and mark interaction.

The offline harness used Chromium, reduced motion, UTC, `en-US`, and a vendored React 18.2 production runtime. The shipped cards retain their normal React 18.3.1 CDN declarations. This runtime difference is limited to the test harness and is recorded rather than hidden.

### Browser result

| Surface | Viewport | Result | Interaction or contract exercised |
| --- | --- | --- | --- |
| Coordinated foundations | 1180×760 | Pass | Offline selection retains the last-known-good plot |
| Linked selection | 1180×700 | Pass | Table selection synchronizes the evidence rail |
| Workspace context | 1280×620 | Pass | Reporting-period change propagates through the workspace |
| Analytical figure | 1180×760 | Pass | Partial coverage retains the chart; keyboard mark selection updates the direct readout |
| Asset/liability ladders | 1180×850 | Pass | Shared-scale ladder and contribution surfaces render |
| Control dependency | 1100×720 | Pass | Node selection synchronizes the inspector |
| Accounting proofing | 1280×980 | Pass | Variance, tie-out, calculation, basis, and rollforward surfaces render |
| Close and posting | 1280×900 | Pass | Locked accounting period disables posting while impact evidence remains visible |
| Change control | 1320×1080 | Pass | Field edit creates a staged change set; attestation remains gated until rationale is entered |
| Evidence coverage | 1180×760 | Pass | Evidence pagination and explicit missing-source selection work |
| Workspace context — mobile | 390×844 | Pass | No page-level horizontal overflow |
| Analytical figure — mobile | 390×844 | Pass | Claim, evidence, exact-value fallback, and field reflow without overflow |
| Change control — mobile | 390×844 | Pass | Review actions, conflict comparison, resolution, and attestation reflow without overflow |

Summary:

```text
13 of 13 rendered states passed
14 of 14 interaction/responsive checks passed
0 console errors
0 page errors
0 bundle runtime errors
0 page-level overflow failures
```

Machine-readable results are retained at `qa/browser-qa-results.json`; the browser transcript is retained at `qa/logs/browser-qa.log`.

## Accessibility-sensitive checks

The release QA explicitly verified that:

- Consequential data remains visible during stale, partial, delayed, and offline states.
- The curve’s selected observation is reachable by keyboard and produces a persistent readout.
- Graph nodes are real buttons and update a nonvisual inspector.
- Complex analytical surfaces retain exact-value or text-alternative disclosure paths.
- Missing, stale, provisional, confirmed, and locked conditions are named rather than communicated by color alone.
- Primary actions can be programmatically disabled by accounting-period and readiness gates.
- Dense mobile states do not require hover and do not introduce page-level horizontal overflow.
- Reduced-motion mode preserves all evidence and interactions.
- Measured token contrast passes the release contrast matrix.

## Visual comparison ledger

The supplied reference board and the latest browser captures were inspected directly. The following comparison points were reviewed:

| Comparison point | Reference contract | Verified result |
| --- | --- | --- |
| Material hierarchy | Warm paper canvas, continuous plate, bright analytical well | Preserved across figures, capsules, proof panels, and drawers |
| Action and brand separation | Graphite primary action; terracotta reserved for brand/scenario | Primary buttons are graphite; terracotta is confined to scenario/warning-adjacent roles |
| Meridian primitives | Datum, Field, Trace, Plate, Capsule carry structural meaning | All five are implemented as data-bearing or compositional contracts |
| Typography | Sentence-case working UI, mono/tabular data, restrained micro labels | Preserved in desktop and dense mobile captures |
| Geometry | Fine rules, low radii, no default card shadows or gradients | Preserved; governance also rejects prohibited declarations |
| Analytical hierarchy | One dominant evidence plane with secondary state/evidence regions | Preserved in the figure, dependency, and accounting specimen layouts |
| Responsive continuation | Mobile is a sibling state, not a squeezed desktop grid | Figure headers and consequential action rows now stack and remain operable |
| Data trust | Source, as-of, coverage, condition, and exact values remain visible | Present in analytical, accounting, close, and evidence specimens |

### Material mismatches found and fixed during QA

1. The historical monolithic bundle embedded 21 page-level `ds-base.js` loaders. Executing those loaders inside the bundle attempted recursive bundle loads and injected duplicate environment chrome. The extension compiler now strips those page loaders deterministically and removes their stale bundle source hashes.
2. The first browser harness omitted specimen-local layout CSS. Those captures were rejected; the harness now injects both release CSS and local specimen CSS before visual review.
3. The analytical figure’s mobile metadata compressed the claim into a narrow column. The header now becomes a single-column claim-and-context sequence below 520px.
4. Dense mobile change-control action rows wrapped long labels unpredictably. Conflict, staged-review, and attestation controls now use explicit mobile grids or wrapping contracts.

No material mismatch remains in the retained captures.

## Retained visual evidence

Desktop captures:

- `qa/screenshots/coordinated-foundations.png`
- `qa/screenshots/linked-selection.png`
- `qa/screenshots/analytical-figure.png`
- `qa/screenshots/accounting-proofing.png`
- `qa/screenshots/change-control.png`

Mobile captures:

- `qa/screenshots/workspace-context-mobile.png`
- `qa/screenshots/analytical-figure-mobile.png`
- `qa/screenshots/change-control-mobile.png`

## Copy and content review

The specimen cards use synthetic financial data solely to demonstrate component behavior. No investment claim, forecast, recommendation, or live operational status was introduced. Above-the-fold labels, actions, sources, states, and caveats are code-native and match their specimen source; generated screenshots are not used as interface content.

## Known test-environment differences

- Browser QA uses the system fallback fonts because remote font loading is intentionally omitted for deterministic offline rendering. The package does not ship font binaries.
- Direct browser navigation was blocked by the execution environment, so the QA harness injects each specimen into a clean Playwright page.
- The harness uses a locally available React 18.2 production runtime; shipped standalone cards continue to declare React 18.3.1.

These differences do not alter the component source, bundle API, interaction contracts, or token values.

## Release conclusion

The complete source package, generated bundle, catalog, manifests, documentation, templates, browser evidence, and test automation are internally consistent. The release is suitable as the replacement Meridian design-system package for the implemented 3.1 scope.
