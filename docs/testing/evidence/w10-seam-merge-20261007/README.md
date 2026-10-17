# W10-SEAM browser reproduction — integrated candidate 7cbdaff

**Status:** supporting browser checks passed; full CI, hosted checks and live acceptance are separate gates

**Owner:** Workstation Shell and UX / designated operator

**Reviewed:** 2026-10-07

Application candidate **`7cbdaff7293f91f9d486275e6386ced11f8e893b`** is the integrated
application under [PR #3108](https://github.com/rodoHasArrived/Meridian-main/pull/3108).
This packet records a fresh browser reproduction with captured dependency identities.
It preserves the earlier [2026-10-06 continuation](../w10-seam-refresh-20261006/README.md)
and [615abde9 packet](../w10-615abde9/README.md), including their failed attempts and pending decisions.

| Observed support | Result and retained evidence |
| --- | --- |
| Focused regressions | **58 passed, zero failed/pending**, across two files: 37 view-model and 21 component tests. [JSON](focused-tests.json.gz), [log](focused-tests.log.gz). |
| Rendered response simulation | **16/16 automated observations passed** at 1440×900 and 1366×768. [Compact cases](cases.json), [original full cases](cases-full.json.gz), [provenance](provenance.json). |
| Dependencies | Dashboard `npm ls --all --json` exited **0**, with empty stderr. [Dependency record](dependencies.json), [complete npm tree](npm-ls-all.json.gz), [stderr](npm-ls-all.stderr.txt.gz). Root and dashboard package/lock files are hashed; no separate root npm-ls run is claimed. |
| Capture verification | Recorded source and dependency hashes remained unchanged; seven fixture/component files matched the candidate. [Verification](verification.json). |
| Candidate identity | [Binding](candidate-binding.json) checks source/dependency hashes and records the candidate's `src`, `tests` and `wwwroot/workstation` Git tree IDs. |

Capture ran **2026-10-07 15:31:56.963–15:32:38.770 UTC**, using Node `v24.19.0`,
npm `11.9.0`, Chromium `151.0.7922.173` and Playwright `1.63.0`. The recorded installed
graph includes Tailwind `3.4.19`, PostCSS `8.5.23`, Vite `8.0.16` and Vitest `4.1.11`.
The browser served **Vite source components**; the bundle tree ID establishes identity,
not execution of the committed production bundle.

The exercised sequence includes refusal, refreshed v9 details, a simulated v10 publication
response, version/scope mismatches, same-version evidence refresh and delayed-response rejection.
The [71 request/response records](requests-responses.json.gz) retain intercepted command bodies.
The 409 refusal and 503 detail failure were deliberate scenarios. Plan/evidence revisions are
simulated contributor record identities; no separate retained version checks or real publication
are established here. Two screenshots are retained: [repaired v9](screenshots/S4-repaired-v9.png)
and [simulated publication v10](screenshots/S5-published-v10.png); other captures are marked
unretained in the compact case file.

The exact [capture harness](capture.mjs) and [dependency recorder](record-dependencies.mjs)
retain their capture-environment paths. Reproduce in an isolated candidate checkout with a fresh
output directory; preserve this packet. [Capture log](capture.log.gz), [dependency log](dependencies.log.gz)
and [Vite log](vite.log.gz) supplement the machine-readable records. The
[manifest](manifest.json) hashes all retained artifacts except itself.

Full CI and required hosted-check conclusions must be read from the current checks on
[PR #3108](https://github.com/rodoHasArrived/Meridian-main/pull/3108); this packet asserts no
full-gate pass. It supplies no installed Windows execution, retained accounting population,
live authorization or operator acceptance. All captured operator verdicts remain **pending**.
Implementation order remains **LOT → MARK → SEAM → RECON**, and the joint live session remains
**SEAM before MARK**. This capture exercises SEAM only and does not establish LOT/MARK completion.
