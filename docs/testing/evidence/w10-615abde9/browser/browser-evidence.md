# Candidate-bound browser response simulation

**Status:** bounded rendering evidence captured; live acceptance pending
**Owner:** Workstation Shell and UX
**Reviewed:** 2026-10-02

Candidate: `615abde90001ab33bd6e58e545edc7fce635e254` (PR #3048).

This evidence exercises the actual browser workstation components using intercepted
responses. It is **not** a provisioned accounting population, live server evidence,
desktop acceptance, or a human operator decision. Every operator decision remains pending.
The representative accounting population is specified separately in the packet.

`evidence/provenance.json` records Chromium, Playwright, Node, operating system, actual
capture timestamps, source hashes, and source/candidate comparison. Screenshots carry a
harness-only visible simulation label and selected query scope. The source files were not
edited. Browser plugin was unavailable, so regular Playwright used `/usr/bin/chromium`.
No phone viewport or mobile product surface was introduced.

## Observed behavior and remaining decisions

- SEAM ran before MARK. Matching scope/workflow enabled close; missing/stale/unavailable
  contributor responses, each missing scope dimension, foreign entity, and delayed old
  scope responses prevented readiness transfer. Missing/stale contributor details were
  also inspected in Accounting → System details.
- MARK displayed fresh and review-required stale/future/unknown-date/coverage/confidence
  responses. Refused preview recovered on reassessment. Changed fund selection discarded
  the delayed old preview. The unsupported override case only verifies rendering of a
  simulated blocked reason; **real override expiry remains untested**.
- **B1: workflow repair does not refresh selected detail.** After the same workflow ID
  advances from version 4 to 5 in the simulated owning response, Refresh workflows reloads
  the list and shared readiness but retains selected version 4. Publish stays disabled.
  This is a candidate defect reproduced by `S7-refusal` → `S7-repair`, not a failed human
  acceptance verdict. `refresh()` retains the selected ID; its detail-fetch effect depends
  only on that ID and services (`operations-continuity-screen.view-model.ts:748, 847`).
- Representative population coverage, server policy evaluation, ownership/role refusal,
  actual override expiry, governed repair/approval, retained lineage/reload, installed WPF,
  PostgreSQL certification, and all designated human decisions remain pending.

`evidence/cases.json` contains exact browser actions, expected and observed state,
screenshots, bounded automation decisions, and pending operator decisions.
`evidence/requests-responses.json` retains UTC timestamps, methods, URLs, POST bodies,
status codes, response origins and fixtures. Command attempts were intercepted; no
application service persisted a close or valuation. HTTP 503 and 409 console entries are
deliberate simulated refusals. Preflight HTTP 502 entries record the absent real host
before the fixture helper imports; no framework overlay or page exception was observed.

The final capture ran from **2026-10-02 22:26:25.450 UTC** to **22:27:10.384 UTC**
at `http://127.0.0.1:4173/workstation/`, with Chromium `151.0.7922.173`, Playwright
`1.63.0`, Node `v24.19.0`, and desktop viewports `1440×900` and `1366×768`.
It retained **37 scenario observations, 43 screenshots and 173 target request/response
bodies**. There are **626 compact network observations**; unrelated response bodies are excluded. The bounded automation
decisions are 36 pass and 1 fail (B1). All 37 operator decisions are pending.

| Browser check | Observation |
| --- | --- |
| Page identity | Workstation Accounting/Operations Continuity routes and titles recorded per case |
| Meaningful screen | Production components and interactive controls rendered in every final case |
| Framework overlay | 0 observed |
| Console health | No page exception; expected simulated 503 contributor/preview and 409 close refusal; 3 preflight 502 responses from absent real host |
| Screenshot evidence | All screenshots visibly label response simulation, candidate and pending decision; key SEAM context/detail pairs retained |
| Interaction proof | Close gating, refresh/refusal/repair, changed selection and read-only mark previews exercised |

The previous incomplete harness attempt and its correction are recorded in `attempts.json`.
That failure is not silently represented as a completed acceptance run.

## Reproduce

Run the harness saved with this evidence against an isolated checkout of the candidate.
Pass `--root` or `W10_REPO_ROOT` so a later documentation commit does not change the source
under test. The harness refuses a different HEAD and records hashes against the candidate.
Use the maintained dependency installation in that candidate's dashboard if needed.

```bash
git worktree add --detach /tmp/meridian-w10-candidate 615abde90001ab33bd6e58e545edc7fce635e254
cd /tmp/meridian-w10-candidate/src/Meridian.Ui/dashboard
npm ci
MERIDIAN_SCREENSHOT_CAPTURE=1 npm run dev -- --port 4173
```

In another terminal, use the retained harness location and candidate source path:

```bash
node /path/to/retained/browser/capture.mjs --root /tmp/meridian-w10-candidate
```

`W10_BROWSER_URL` changes the Vite origin (default `http://127.0.0.1:4173`).
`W10_CHROMIUM` changes the Chromium executable. Serve Vite from the candidate passed to
`--root`. The output is written next to the retained harness; preserve previous capture
outputs before a rerun.

Focused automated support passed on the candidate: four Vitest files, 62 tests;
`npm run build` passed TypeScript and Vite. Retained logs are `vitest.log.gz` (byte-preserving gzip) and `build.log`.
These results are preliminary local support; same-commit hosted certification remains a
separate acceptance gate. Implementation order stays LOT → MARK → SEAM → RECON.
