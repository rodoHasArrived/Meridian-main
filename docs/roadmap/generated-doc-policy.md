# Generated Documentation Policy

Generated roadmap and source docs are deterministic engineering artifacts.

## Determinism rules

| Area | Rule |
| --- | --- |
| Ordering | Sort by stable registry IDs or explicit registry order. |
| Dates | Use registry snapshot dates, not wall-clock time, unless the report is explicitly operational. |
| Paths | Emit repo-relative POSIX-style paths. |
| Encoding | Write UTF-8 with LF line endings. |
| Empty values | Emit `-`. |
| Markdown tables | Keep stable column order. |
| Random IDs | Never emit random IDs. |
| Generated manifests | Include input and output hashes. |

## Generated header

Generated Markdown must start with a metadata comment naming the generator, render contract, schema versions, inputs, and `do_not_edit: true`.

## Editing rule

Do not hand-edit generated docs. Update registry data or renderer logic, then rerun the generator. Source READMEs are human-authored except for explicitly marked generated blocks.
