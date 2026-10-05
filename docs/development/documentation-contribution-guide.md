# Documentation Contribution Guide

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05
**Audience:** documentation contributors

Use this supporting guide to write, review, and retire Meridian documentation. The
[Documentation Ownership Contract](../documentation-ownership.md) owns placement and authority;
[Engineering](../engineering/README.md) is the contributor entrypoint.

## Documentation Principles

- Start with the reader's task and the result they should obtain.
- Give each durable fact one owner. Link to configuration references, registries, and current
  plans instead of copying their changing status into other pages.
- Check procedures against current source and commands. Distinguish source inspection from an
  executed example, operator acceptance, and production certification.
- Preserve useful historical evidence with its original date, commit, and replacement link.

## Documentation Structure

The [folder inventory](../documentation-inventory.md) lists every supported lane. In particular:

- **Engineering** owns contributor entrypoints, the shortest current build/test/run path,
  validation routing, and links to deeper guides.
- **Development** holds detailed supporting implementation and tooling guides, linked from
  Engineering or its Development index. It is an active supporting lane.
- **Architecture** owns current system design and rationale; it is not a legacy bucket.
- **Operators** owns executable operating procedures. Reference owns their configuration and API
  lookup tables.
- **Product** explains direction. Roadmap registries own delivery state; the readiness tracker
  owns release follow-up. Dated reviews and plans must name the evidence snapshot they describe.

### Document Categories

| Kind | Reader's question | Content |
| --- | --- | --- |
| Tutorial | How do I get a first successful result? | One supported path with prerequisites and expected output. |
| Procedure | How do I complete this task? | Ordered actions, checks, failure handling, and recovery. |
| Reference | What does this option or contract mean? | Exact names, types, defaults, constraints, and examples. |
| Explanation | Why does the system work this way? | Concepts, boundaries, tradeoffs, and links to decisions. |

## Where Should This Doc Go?

Use the ownership contract before creating a file. Check whether an existing document already
owns the topic; extend it when that keeps the reader's task coherent.

### Quick Lookup

| If the document covers… | Home |
| --- | --- |
| First-run setup | `docs/start/` |
| Contributor orientation and build/test/run routing | `docs/engineering/` |
| Detailed implementation, extension, testing, or tooling techniques | `docs/development/`, linked from Engineering |
| System architecture and domain concepts | `docs/architecture/` or `docs/domain/` |
| Architectural decision | `docs/adr/` |
| Provider setup, deployment, operating procedures, recovery | `docs/operators/` |
| API, configuration, schema, capability lookup | `docs/reference/` |
| Product framing and dated product analysis | `docs/product/`, with analysis linked from its review or plans index |
| Implementation blueprint or delivery plan | The home named by the [Plans and Blueprints Register](../engineering/blueprints/README.md) |
| Roadmap status | `docs/roadmap/data/`; regenerate the views |
| Source-module facts | `docs/source/data/` and the registered source README |
| Assistant workflows | `docs/ai/` |
| Generated report | The generator's registered output path; edit its inputs or generator |
| Superseded or historical record | `archive/docs/<bucket>/`, with an index and replacement or retention reason |

Do not add durable guidance to the transitional `docs/operations/` or `docs/plans/` paths.
Do not move existing paths until their links, tests, and tooling consumers have been checked.

## Lifecycle Tags

### Required Fields

Put actual metadata immediately below the title (or in YAML front matter), before body sections:

```markdown
# Document title

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

State the task, audience, and expected result here.
```

Replace the example owner and date with the accountable team and the date the content was
verified. Template values inside code blocks do not count as document metadata. Keep the date in
`YYYY-MM-DD` format; put review scope or qualifications in a separate paragraph.

### Status Values

| Status | Meaning | Required context |
| --- | --- | --- |
| `active` | Current maintained guidance | Owner and review date. |
| `draft` | Proposed or incomplete guidance | Unresolved decisions and what has not been verified. |
| `deprecated` | Superseded guidance retained for compatibility | Prominent replacement link and retirement condition. |
| `archived` | Historical record | Original evidence date/commit and archive or replacement reason. |

Lifecycle status is distinct from placement (`canonical`, `supporting`, `source-material`) and
roadmap implementation status. Existing headers that use a placement label should clarify their
lifecycle when reviewed; a document being active does not mean its proposed feature is delivered.

### Review Cadence Guidelines

Review setup and operator commands when their behavior changes and before a release. Review API
references with the related source change. Review architecture when a decision or boundary changes.
Keep dated evidence historical; add a correction or newer record instead of silently refreshing
its original claims.

### Stale Detection

The structure validator flags old or invalid review dates and missing metadata. Review flagged
hand-authored documents against their owners; update generated reports through their generators.
A recently edited file is not necessarily a recently verified document.

## Writing Standards

- Lead with the purpose and result, then give prerequisites and steps.
- Define domain terms and acronyms on first use, or link to their reference.
- Use one title and descriptive section headings in logical order.
- State whether a claim is current behavior, a proposal, or dated evidence.
- Use tables for comparisons and numbered steps for procedures.
- Keep landing pages short: task routes, authoritative sources, and links to detailed history.
- Name the operating system, shell, working directory, required permissions, and configuration
  before commands. Use repository-relative paths for portable examples.

## File Naming Conventions

Use descriptive kebab-case names. Keep `README.md` for folder entrypoints and `NNN-title.md` for
ADRs. Dated reviews and prioritization records may include the evidence date in their names;
continuously maintained procedures should use stable names. Preserve generator-owned filenames.

## Cross-References and Links

Use descriptive Markdown links with paths relative to the current document. Link to the owning
reference instead of copying option tables or status summaries. Avoid version numbers and
"latest" dates in link labels unless that page owns the version selection.

After a move, update inbound links, section anchors, the nearest index, and the published-site
navigation. Keep a redirect when a compatibility path still has active consumers. Inline-code
paths are useful identifiers but do not give readers a clickable route.

## Code Examples

Provide complete commands, supported flags, and expected results. Commands run from the repository
root unless explicitly stated otherwise. For example:

```bash
dotnet run --project src/Meridian/Meridian.csproj -- --help
```

This prints the current CLI help; link to it instead of copying a versioned help transcript.
Mark partial snippets as partial. Keep placeholders obvious and never put real credentials into
examples. Separate blocking server commands from client checks into named terminals.

## Diagrams and Visuals

Use a diagram when it explains relationships or a workflow more clearly than prose. Give it a
caption or text explanation. Follow the [diagram index](../diagrams/README.md) for asset ownership,
keep sources with rendered assets, and regenerate owned outputs instead of editing them by hand.

## Updating Documentation

1. Verify the affected behavior, registry, or command.
2. Update the owning document and its review metadata.
3. Update task links, references, and generated inputs affected by the change.
4. Run the checks below and inspect the diff, including any regenerated files.
5. Describe the changed behavior and validation limits in the pull request.

### Deprecating Documentation

Mark superseded guidance `deprecated` and link its replacement at the top. Once active consumers
have moved, preserve historical content in an indexed archive bucket. Record why it was archived
and which source now owns current guidance. Keep original evidence timestamps and commit references.

## Review Process

Run these from the repository root after document edits or moves:

```bash
python build/scripts/docs/validate-docs-structure.py --summary
python build/scripts/docs/repair-links.py --summary
python build/scripts/docs/check-docfx-navigation.py --summary
git diff --check
```

The link checker is read-only unless repair is explicitly requested. The DocFX check verifies
published navigation against configured inputs; see [DocFX validation](../docfx/README.md) for
checking built output. Neither check executes an application's setup or provider commands.

Use the relevant generator/registry checks when their inputs change. Follow
[Engineering validation](../engineering/README.md#buildtestrun) for the required repository gate;
focused documentation checks do not replace it. In the PR, distinguish passed checks, warnings,
blocked checks, and examples verified only by source inspection.

## Document Templates

### Guide Template

Use the [Operator Runbook Template](../operators/runbook-template.md) for operating procedures.
For a contributor guide, use the same task/prerequisites/steps/results/recovery structure with
contributor-specific commands and validation.

### Reference Template

Start with lifecycle metadata, audience, and scope. Follow with exact contract or option names,
types, defaults, constraints, precedence, a small example, and links to procedures that use the
reference. Generate inventories from source where an existing generator owns them.

## Questions and Feedback

Include the owning document, intended reader, and concrete unclear step in a documentation issue
or pull request. Route placement questions to the owner of the Documentation Ownership Contract.

## Additional Resources

- [Documentation Ownership Contract](../documentation-ownership.md)
- [Documentation Automation](documentation-automation.md)
- [GitHub Flavored Markdown Spec](https://github.github.com/gfm/)
