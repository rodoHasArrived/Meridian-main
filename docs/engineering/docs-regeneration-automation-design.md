# Docs Regeneration Automation — Design Constraints

Design constraints for automating the post-merge regeneration of generated documentation
artifacts. This note is the detail behind roadmap item `AR8-57` in
[the 2026-08 adversarial review remediation plan](../product/adversarial-review-2026-08-remediation-plan.md);
that item states the problem and points here for the constraints an implementation must satisfy.

## Why this needs a design step

Six review rounds each proposed a concrete repair, and each was refuted by a different interaction.
The constraints below are what survived.

**They are not jointly satisfiable, and that is the note's main conclusion.** Direct pushes to
`main` are prohibited, a `merge_group` tree is synthetic and cannot be written to, and the
artifacts must stay tracked — so no design achieves "no manual toll, ever". The work is to pick
which outcome to relax, deliberately, and record the residual manual gate. Constraint 7 sets out
the two implementable shapes. Anyone who arrives here planning a quick fix should read that
constraint first.

The scope was also mis-stated repeatedly — first by enumerating affected artifacts, then by taking
the `core` profile as the boundary, and each version was still incomplete. Constraint 4 records the
general form instead: derive the scope from the `regenerate-docs` job's full generation sequence,
never from a hand-written list.

## The problem

`docs/status/doc-health-dashboard.{json,md}` reports totals derived from markdown line counts, over
a corpus that `build/scripts/docs/generate-health-dashboard.py:52-57` narrows by excluding
`.github`, `.claude`, `.codex`, `archive`, `artifacts`, vendor trees, and `docs/status/` itself.
Any merge combining two branches' edits to markdown inside that corpus leaves the dashboard stale,
because neither parent's copy describes the merged tree.

`regenerate-docs` in `.github/workflows/documentation.yml` re-runs the core docs-automation profile
and fails on the diff. **For a pull request whose changed paths match the workflow's filter this
detection is pre-merge, not post-merge:** the job checks out with no explicit `ref`, so on a
`pull_request` event it evaluates `refs/pull/<n>/merge` — the simulated merge of head into base
(`:100-152`). Aggregate drift for the merge tree is therefore normally caught while the pull request
is still open.

That makes the toll a **manual pre-merge repair**, not a post-merge surprise, and it is the case
the automation should target. Fifteen commits since June exist for no purpose other than re-running
the profile and committing the result — `2368b43b2`, `f4d900d67`, `0535db058`, `d8554cefe`,
`603d98cfe`, `fc8344145`, and `fb94b95cd` among them, across several unrelated branches, one of
which paid it three times. This is not a correctness bug; it is a standing toll on everyone whose
pull request touches documentation, and it trains contributors to read a red `regenerate-docs` as
routine noise rather than signal.

Two narrower classes *do* escape pre-merge detection entirely, and they are worse because nothing
reports them:

- **path-filter misses** — a pull request touching `README.md`, `.agents/**`, or a non-markdown
  tracked file that moves `repository-structure.md` never starts this workflow at all
  (constraint 3), and `verify-docs` does not run these generators, so stale output lands silently;
- **merge-queue combinations** — two individually-clean pull requests whose combined tree is stale,
  with no `merge_group` trigger to notice (constraint 7).

An implementation that only automates the first case leaves these two; one that assumes the toll is
post-merge will optimise for the wrong detection point.

## Constraints

Any implementation must satisfy all of these.

### 1. No write token in a job that runs pull-request code

`regenerate-docs` checks out the PR revision and executes its requirements file, Python generators,
and npm scripts (`.github/workflows/documentation.yml:104-152`). A write-capable token in that job
is capturable by any same-repository pull request that modifies those programs. A fork restriction
does not address this, because the exposure is same-repo.

The privileged repair must run **trusted base-revision tooling** in a job that does not execute the
pull request's code.

### 2. The repair must retrigger checks on its own result

A push made with the default `GITHUB_TOKEN` starts no workflow run, so the repaired head would
carry the verdict of the SHA it replaced. Use a GitHub App token or PAT for the push itself.

### 3. Trigger on the union of the generators' input corpora

The workflow's `pull_request` paths (`.github/workflows/documentation.yml:15-26`) are a curated list
covering `docs/**` and some script paths. That is narrower than the generators' real inputs:

- the health generator counts markdown outside its exclusion set, including `README.md`,
  `AGENTS.md`, `CLAUDE.md`, `.agents/**`, and `Meridian Design System/**`;
- the structure generator's corpus is **every tracked file**, so a change to a C# or configuration
  file also moves a generated artifact.

Nothing else catches the gap: `scripts/ci.sh`'s `verify-docs` lane runs `run-docs-automation.py`
with an explicit `--scripts` list that omits both generators, so stale output lands silently.

Compute the trigger set from the generators rather than maintaining a parallel path list — but note
that this cannot be done *inside* the workflow. GitHub evaluates `pull_request.paths` before it
creates a run, so no generator in that workflow can decide whether the workflow starts. Closing the
missed-trigger cases therefore requires either removing the restrictive `paths` filter outright, or
adding an always-triggered discovery job that decides what to run. Runtime filtering alone leaves
additions and renames outside the current paths landing stale artifacts exactly as they do today.

### 4. Derive the repair scope from the job's whole generation sequence; do not enumerate it

The repair must cover *every* artifact `regenerate-docs` regenerates for the change at hand,
computed from the job's full sequence rather than from a fixed allowlist — **and the `core` profile
is not that boundary either.** After the profile, the same job renders roadmap and source Mermaid
diagrams, WPF UI diagrams via `npm run generate-diagrams`, UML output, the workflow overview
(`generate-structure-docs.py --workflows-only`), and the workflow manifest, all before its final
repository-wide diff (`.github/workflows/documentation.yml:126-152`). A WPF navigation or
source-registry change can therefore move a diagram artifact while every core-profile artifact is
already current, leaving the check red for an implementation that scoped itself to
`PROFILE_CONFIG["core"]`.

Three illustrations of why any list keeps failing:

- `generate-structure-docs` (`run-docs-automation.py:293-299`) rewrites
  `docs/generated/repository-structure.md` from `git ls-files --cached`
  (`generate-structure-docs.py:_git_visible_files`), so any tracked file added, removed, or renamed
  moves it — not merely markdown;
- `scan-todos` reads `.md` among its `TEXT_EXTENSIONS` and rewrites tracked `docs/status/TODO.md`,
  so a documentation pull request adding a legitimate `TODO:` or `FIXME:` annotation dirties a
  second artifact;
- the post-profile diagram and workflow-overview steps above sit outside the profile entirely.

None of these is the last such case. The job's generation sequence is the source of truth, and it
must be read from the workflow rather than restated here — this note has already been wrong about
the boundary twice.

### 5. Generator-changing pull requests need their own path

If constraint 1 is met by running trusted base-revision tooling, a pull request that legitimately
changes `generate-health-dashboard.py` gets regenerated by the *old* generator while
`regenerate-docs` validates with the *new* one. The bot commits stale output and the check stays
red, or loops.

Either commit allowlisted output produced by an unprivileged PR-code job, or exclude this subset
and require manual regeneration for it.

### 6. Bind generated output to the exact tree it was computed from

If the privileged writer consumes an artifact produced by an unprivileged job, that artifact must
record the pull request head and base (or merge) SHA, and the writer must compare-and-swap against
both before committing.

The workflow's `concurrency` block (`:46-48`) groups by pull-request number with
`cancel-in-progress`. That is a cancellation policy, not a ref guard: the pull request can
synchronize, or `main` can advance, between generation and the privileged write, so output computed
from an obsolete tree would be committed onto a newer head — overwriting newer generated changes
and re-triggering the same red check.

### 7. Neither pre- nor post-merge repair is free

Per-branch repair does not fix the merge-queue tree. Two queued pull requests can each be
individually correct while their combined tree is stale, and `documentation.yml` has no
`merge_group` trigger (`:3-41`) while `meridian-ci.yml` has one (`:3-11`) but never runs the health
generator.

The obvious escape — a bot commit to `main` after the merge — is **not** available. `main` is
protected and `AGENTS.md:12-15` forbids bypassing branch protections, so a privileged direct push is
prohibited rather than merely awkward. Routing the repair through a follow-up pull request is
policy-compliant but reintroduces a human merge, which does not meet the no-manual-toll outcome.

Nor is a merge-group-safe pre-merge repair actually available. A `merge_group` SHA names a
synthetic, non-writable tree: there is nothing to push to. Committing the repair to either
constituent pull request head invalidates that group and causes it to be rebuilt, which discards
the output computed from the combined tree rather than preserving it.

**Taken together these constraints have no fully-satisfying solution, and the design must therefore
relax one of its own outcomes.** Direct post-merge pushes are prohibited, in-place merge-group
repair does not exist, and the artifacts must stay tracked — so "no manual toll, ever" is not
achievable. The implementable shapes are:

- **repair the pull-request head** and accept that a merge-queue combination can still land stale
  artifacts, fixed by a follow-up repair pull request; or
- **route every repair through a follow-up pull request**, which is policy-compliant and complete
  but reintroduces a human merge for the affected cases.

Pick one deliberately and record the residual manual gate in the item as an accepted limitation.
What is *not* acceptable is presenting pre-merge repair as though it closes the merge-queue case.

### 8. Fork pull requests cannot be repaired automatically — say so

When the head belongs to a fork, neither a GitHub App installed on the base repository nor a
maintainer PAT normally has permission to push into the contributor-owned fork branch, and the
fork's workflow run cannot be handed the write credential instead (which is the same property that
makes constraint 1 necessary). No automated repair is possible for that case.

Scope the automation explicitly to writable same-repository heads, and document the fallback for
forks — manual regeneration by the contributor, or a maintainer-owned branch that carries the
repair. State this as a known limitation of the outcome rather than leaving it implied; the
"no manual toll" goal holds for same-repository pull requests only.

### 9. Keep the artifacts tracked

`docs/documentation-ownership.md:23` designates `docs/status/` automation-owned output that must
stay "at the paths consumed by tooling", so untracking these files in favour of CI-only artifacts is
out of policy.

## Rejected alternatives

**A `.gitattributes` merge driver.** A custom driver runs only where `merge.<driver>.driver` is
configured locally, and GitHub's server-side merge inherits no contributor configuration, so it
would never run for the normal pull-request merge this work exists to fix.

## Governance

The implementation necessarily edits `.github/workflows/documentation.yml`, including its
`permissions` block, and introduces a privileged token. That is a protected governance file under
`.github/pull_request_template.md`, so the implementing pull request must declare the governance
change and carry explicit human approval. Constraint 1 is a security requirement and should be
reviewed as one.

## Verification

Assert the outcome — every artifact produced by the **full `regenerate-docs` generation sequence**,
including the post-profile diagram, workflow-overview, and manifest steps, matching the merged tree
— rather than the mechanism. Cover:

1. a merge of a branch editing in-corpus markdown into a branch editing different in-corpus
   markdown, confirming `regenerate-docs` ends green **on the final head** with no human running
   the profile;
2. two pull requests entering the merge queue together — asserting whichever behaviour constraint
   7's chosen shape commits to, since the combined tree cannot be repaired in place: either the
   follow-up repair pull request is raised, or the documented staleness window is what occurs;
3. a pull request that adds or renames a **non-markdown** tracked file, exercising
   `repository-structure.md` through a path the current triggers miss;
4. a documentation pull request that adds a `TODO:` annotation, exercising `docs/status/TODO.md`;
5. a pull request that changes a generator itself;
6. a concurrent-update case where the **head** advances between generation and the privileged
   write, asserting the writer refuses the stale artifact rather than committing it;
7. a second concurrent-update case where the **head is unchanged and `main` advances**, which a
   writer checking only the head would wrongly accept — this is the case that actually exercises
   the base half of constraint 6's compare-and-swap;
8. a fork pull request, asserting the documented fallback path is what runs rather than a silent
   failure or a red check with no route forward.

## Also document

Whichever design is chosen, document the single regeneration command next to the check, so a
contributor who hits a red `regenerate-docs` need not reconstruct it from the workflow.
