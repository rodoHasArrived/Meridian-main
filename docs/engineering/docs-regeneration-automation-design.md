# Docs Regeneration Automation — Design Constraints

**Status:** active; hosted write activation requires human governance review
**Owner:** core-team
**Reviewed:** 2026-10-15

This note defines the boundaries of generated-output repair. Use the
[recovery guide](generated-merge-recovery.md) for commands and the
[CI/CD rollout guide](ci-cd-optimization.md) for workflow activation. It also retains the
design rationale behind `AR8-57` in the
[2026-08 adversarial review remediation plan](../product/plans/adversarial-review-2026-08-remediation-plan.md).
That identifier belongs to the historical plan, not the roadmap registry.

## Current detection and repair boundary

Combining two individually current branches can make tracked output stale. Documentation health
counts depend on Markdown across the repository; the structure report depends on every tracked
path. A browser source merge can require new hashed assets and deletion of old ones. Taking either
parent's output is only a seed for regeneration, not proof that the merged tree is current.

The canonical `Meridian CI / verify-docs` lane already runs the maintained generation sequence
and checks its outputs. `meridian-ci.yml` runs without path filters on pull requests, main pushes,
and `merge_group` events. Earlier versions of this note incorrectly described missing whole-tree
and merge-group detection. Those gaps are closed by the canonical lane. Documentation Automation
also has unrestricted PR/main triggers and a merge-group trigger, covering its generators' full
input corpus. The stable `quality-gate` still requires all four canonical lanes.

Repair prepares a candidate from pinned revisions in an isolated checkout, resolves only supported
generated conflicts, regenerates to a fixed point, and produces reviewable evidence. Ordinary
source conflicts, handwritten documentation conflicts, uncertain reviewed hashes, and changes to
the automation's own trust boundary retain a contributor path. Automation does not change required
checks, approve or merge a PR, or write directly to protected `main`.

## Discovery and permission are separate

The explicit maintained catalog in
[`run-maintained-generation.py`](../../build/scripts/run-maintained-generation.py) owns browser
contract/build, adapter readiness, roadmap/source, the core documentation profile, Mermaid, WPF
diagrams, optional UML, workflow overview, and workflow manifest generation. Canonical CI,
Documentation Automation, manual roadmap validation, and repair invoke that same catalog.
Workflows do not copy its generator sequence or recursively discover their own orchestration.

The output policy in
[`maintained-generation-outputs.json`](../../build/scripts/maintained-generation-outputs.json)
answers a different question: which writes are allowed. A new generated output does not
automatically gain write permission. Review its owner and add an exact path, appropriate generated
tree, or named generated block to the policy. Unknown writes fail rather than expanding the policy.

Registered source READMEs, `docs/HELP.md`, and documentation automation guidance contain generated
blocks alongside handwritten prose. Permission to update a block is not permission to replace the
whole file. Marker multiplicity, ordering, and boundaries must be valid; the content outside every
authorized block must remain unchanged against the candidate's pre-generation contents.

The writer independently applies trusted base-owned policy. A policy supplied by the producer
or changed in a pull request cannot authorize its own writes. For a merge repair, legitimate
source and prose changes from the pinned base are accounted for by reproducing the merge; the
remaining generated delta is checked against that merged candidate. A generator cannot disguise
an unrelated source edit as part of the merge.

Conflict recovery has separate explicit strategies in that policy. Only outputs with a declared
required generator can use whole-file regeneration. Protected suffixes use a real three-way
handwritten-content merge before seeding their generated prefix; a prose conflict refuses
recovery. This applies to the local resolver and the publisher's reconstructed baseline, so
lost prose cannot be hidden in the baseline used to validate a later generated delta.

## Reviewed hashes are not disposable output

[`source-hash-manifest.json`](../source/generated/source-hash-manifest.json) records a reviewed
source/documentation baseline. Recomputing every hash would assert review that never happened.
Use a three-way merge keyed by module ID:

- Combine entries updated independently for different modules.
- Accept identical parent updates.
- Carry a reviewed entry forward only when its source and README fingerprints match the final
  merged module under the manifest's existing hashing rules.
- Require module-specific review when final contents match neither reviewed parent, or metadata
  cannot be reconciled without making a new review assertion.

Version 2 fingerprints retain source review evidence and fingerprint handwritten README content
separately from generated block freshness. Exact registered markers remain part of the review
fingerprint; malformed markers fail. Migration verifies the accepted version 1 full README
fingerprint before changing its representation, preserves the accepted source hash, and refuses
unmatched evidence. Mixed-version parents require explicit migration before reconciliation.

Generated README blocks do not turn handwritten documentation review into a mechanical task.
Unresolved hash decisions remain explicit in the repair evidence. Follow the
[source documentation workflow](../source/README.md) to review a module and refresh its baseline.
Ordinary generated manifests can be rebuilt from final inputs under their output policy.
Package lockfiles must be regenerated by their owning package manager after dependency changes;
they are not covered by generic conflict acceptance.

## Generation must converge

A later generator can change an earlier report's inputs. For example, workflow-manifest generation
rewrites Markdown counted by the health dashboard. The runner repeats the full required sequence
until authorized file contents stop changing, with a bounded pass count and failure on
non-convergence. Added and deleted files count, including removed browser asset hashes.

A second invocation on unchanged inputs should converge immediately without a new delta. A failed
required command, unauthorized write, malformed generated markers, or non-convergence does not
produce a successful repair. Optional UML rendering retains its advisory status; failure there is
reported separately and does not become evidence of fresh required output.

Reports and per-step logs go under ignored `artifacts/`. Tracked output remains at the paths used
by the application and documentation tooling. Run the local generator with exclusive access to its
checkout: its byte comparison protects against accidental generator writes but is not a sandbox
for untrusted code or a way to distinguish concurrent user edits.

## Separate generation from the privileged writer

`generated-repair.yml` prepares evidence without a write credential or persisted checkout
credentials. Its `pull_request_target` event can run despite merge conflicts. An eligible
same-repository request with the repository App variable configured asks the publisher to dispatch
generation on the default branch; forks and requests without that variable generate artifact-only
candidates. The publisher never writes a target-event candidate. Version 2 run metadata records
the workflow execution SHA/ref separately from the PR head/base inputs. Publication requires a
server-verified default-branch dispatch whose execution SHA matches both that metadata and the
trusted workflow bytes. Checkout pins `github.workflow_sha`.

Candidate dependencies and generators execute only inside the container launched by
[`generated_repair_sandbox.py`](../../build/scripts/ci/generated_repair_sandbox.py). The trusted
Dockerfile uses a pinned Node image with Git and Python. The container runs as an unprivileged user
with a read-only root filesystem, dropped capabilities, resource limits and no privilege escalation.
Only a disposable clone without shared Git objects and a raw output directory are mounted; host
Actions runtime/cache credentials, actions directories and the Docker socket are not exposed.
Network access remains available for dependency installation.

After the container exits, trusted host tooling exports only allowlisted regular files within a
size bound, rejecting symbolic links, hard links and special files. Container logs are retained as
data rather than interpreted as Actions workflow commands. The local worktree-only repair command
does not provide these hosted isolation guarantees.

`generated-repair-publish.yml` runs on workflow completion, checks out trusted base tooling,
validates the artifact without executing PR code, and uses a narrowly scoped GitHub App credential
only for the authorized branch write and bounded retry dispatch.
Same-repository ownership alone is not a trust boundary: a same-repository PR can change a
generator too.

The App private key lives only in the `generated-repair` environment, restricted to deployment
from the default branch. A repository-wide secret would also be accessible to unreviewed branch
workflows and would defeat that boundary. Per-run environment approval may be configured by an
administrator but is not required by this mechanism.

The writer verifies workflow/run/attempt and artifact ownership, pinned base and head, output
policy, permitted file modes and paths, and all handwritten boundaries. Unexpected extra files or
prose changes reject the whole write. Payload contents, filenames and Git patches remain untrusted
data; they must not become shell commands or executable hooks. Changes to generators,
dependencies, workflows or policy use the manual fallback when they alter this trust boundary.

The App push triggers fresh checks on the repaired revision. A default `GITHUB_TOKEN` push does
not provide that behavior and must not be substituted. The repair workflow is supporting
automation; only the authoritative checks on the resulting revision establish freshness. With no
App configured, the workflow retains artifact-only behavior.

## Bind output to current revisions and bound retries

The evidence records both the exact PR head and base commit used for generation. Before writing,
the writer rereads the PR and base and rejects any mismatch. A normal, non-force push protects
against a concurrent branch update. Changes to the head or base invalidate the prepared output;
regenerate from the new revisions instead of applying it to a moving branch.

Concurrency cancellation cannot replace these checks. Stale-ref retries are bounded to two
requeues, with the original root revision retained for the chain. A repair-commit marker prevents
self-triggered synchronize loops only when the repaired head already contains the current base;
it must not prevent a later base advance from triggering repair. Source conflicts, policy refusal,
generation failure or exhaustion
retain an actionable report or contributor fallback. The writer rereads the base after its push
and requeues a detected advance within the same bound. The next authoritative merge-tree check
still controls acceptance; GitHub cannot atomically compare and update both refs.

Base-only changes need their own trigger. The publisher also scans open same-repository PRs on
main pushes, daily at 05:23 UTC and on manual dispatch. It requests at most ten newly stale
head/base pairs per scan, deduplicates previously requested pairs and reports remaining backlog.
Daily/manual scans continue that work. API listing is paginated with a 1,000-item bound; reaching
the bound fails explicitly instead of silently claiming complete coverage. Forks retain their
contributor path.

Writer concurrency is scoped to each source workflow run. A single global group would allow
GitHub's one-pending-run policy to discard another PR's pending repair; ref checks remain necessary
when independent writers overlap.

## Merge queue behavior

A `merge_group` commit is synthetic. There is no writable branch on which to install its repair.
Updating a constituent PR invalidates that group, and the replacement group must run fresh checks.
Two individually repaired branches can still combine into stale output.

The chosen behavior is to keep the canonical freshness check failing on such a group. Refresh
the affected PR branches against the current base and rebuild the group. If combined groups
repeatedly require mutually incompatible generated output, an administrator can evaluate
one-PR merge groups under the existing governance process. No workflow changes queue settings or
accepts a stale synthetic tree automatically.

Do not describe per-PR repair as solving combined merge-group generation. Do not bypass the gate
to land stale output and rely on a later push to repair it. A repair PR can recover pre-existing
main drift through the ordinary protected flow, but it is not an alternative to a required
merge-group freshness check.

## Forks and contributor fallback

A base-repository App cannot normally push to a contributor's fork. Fork runs retain read-only
generation and downloadable evidence; the contributor applies or regenerates the repair on their
own branch. A maintainer-owned branch is another ordinary PR path. Never give fork or PR-code jobs
the writer credential.

For handwritten/source conflicts, merge those files with their owners first, then regenerate.
For changed generator/tooling policy, use the local command and review the resulting output in
the same PR. The [recovery guide](generated-merge-recovery.md) distinguishes a repair patch from
the Git merge parents required to update a branch.

## Verification and operational acceptance

Tests must verify outcomes at the boundary, including:

1. Independent source/Markdown changes, non-Markdown tracked additions and renames, and generated
   TODO inventory changes produce current output on the first repaired revision.
2. Old browser hashes are removed, all required outputs converge, and a second run is unchanged.
3. Independent module baseline updates merge; a same-module combined change requires review.
4. Source conflicts, generator changes, malformed blocks, unexpected writes and unauthorized
   handwritten edits refuse the write without losing the contributor's changes.
5. Head advancement and base-only advancement discard stale evidence and follow bounded retry
   behavior; changed artifact provenance or unexpected file modes also reject publication.
6. Fork fallback leaves usable evidence without a write attempt.
7. A synthetic merge-group combination that is stale still fails the authoritative check; PR repair
   is never used to mark that existing group successful.

Local tests establish deterministic behavior, not GitHub App configuration or hosted activation.
After human governance review, exercise same-repository and fork PRs, a concurrent base update,
and a merge group in Actions. Measure the proportion of first repaired revisions that pass the
authoritative checks without manual regeneration, plus refusals, retries, non-convergence and
runner minutes. Preserve existing test coverage and required gate identities.

A local `.gitattributes` merge driver cannot provide this hosted behavior: it depends on each
clone's Git configuration, does not configure GitHub's merge service, and cannot establish that
either parent's generated files match the merged inputs.
