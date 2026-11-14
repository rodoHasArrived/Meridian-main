#!/usr/bin/env python3
"""Validate structured release work and render the compact production-readiness tracker."""
from __future__ import annotations

import argparse
import datetime as dt
import re
from pathlib import Path
from urllib.parse import urlsplit

from common import load_data, markdown_table, repo_root, write_text_if_changed

DATA = 'docs/roadmap/data/production-readiness.yml'
OUTPUT = 'docs/product/implementation-todo-list.md'
STATUSES = {'open', 'in_progress', 'implementation_complete', 'evidence_gated'}
RELEASE_STATUSES = {'open', 'complete'}
PROSE_CAPS = {'title': 200, 'owner': 160, 'remaining_gate': 320}
ITEM_FIELDS = set(PROSE_CAPS) | {'id', 'priority', 'status', 'release_status', 'decision_ids', 'evidence_links', 'pr_references', 'detail_anchor'}
PRIORITY_HEADINGS = {'P0': 'P0 — Release Blockers', 'P1': 'P1 — Required Hardening and Acceptance', 'P2': 'P2 — Cleanup After Critical Migrations'}


def validate_data(root: Path) -> list[str]:
    """Return fail-closed validation errors; callable by the canonical roadmap validator."""
    path = root / DATA
    if not path.is_file():
        return [f'{DATA}: missing registry']
    data = load_data(path)
    if not isinstance(data, dict):
        return [f'{DATA}: expected mapping']
    errors: list[str] = []
    if set(data) != {'schema', 'registry', 'items', 'detail_sections'}:
        errors.append('unexpected or missing top-level fields')
    if data.get('schema') != {'id': 'meridian.production-readiness', 'version': '1.0.0', 'minimum_renderer_version': '1.0.0'}:
        errors.append('unsupported production-readiness schema header')
    registry = data.get('registry')
    if not isinstance(registry, dict):
        return errors + ['missing registry metadata']
    if set(registry) != {'reviewed', 'migration_snapshot', 'baseline_commit', 'release_status', 'release_commit'}:
        errors.append('unexpected or missing registry fields')
    snapshot = registry.get('migration_snapshot')
    if not isinstance(snapshot, str) or not snapshot.startswith('docs/product/history/') or not (root / snapshot).is_file():
        errors.append('migration_snapshot must identify the retained local detail snapshot')
    if not re.fullmatch(r'[0-9a-f]{40}', str(registry.get('baseline_commit', ''))):
        errors.append('baseline_commit must be a full commit SHA')
    try:
        dt.date.fromisoformat(str(registry.get('reviewed', '')))
    except ValueError:
        errors.append('reviewed must be an ISO date')
    if not isinstance(registry.get('release_status'), str) or registry['release_status'] not in {'blocked', 'ready'}:
        errors.append('invalid registry release_status')
    if registry.get('release_commit') is not None and not re.fullmatch(r'[0-9a-f]{40}', str(registry['release_commit'])):
        errors.append('release_commit must be null or a full commit SHA')
    items = data.get('items')
    if not isinstance(items, list) or not items:
        return errors + ['items must be a non-empty list']
    seen: set[str] = set()
    for item in items:
        if not isinstance(item, dict):
            errors.append('item must be a mapping')
            continue
        ident = item.get('id')
        prefix = str(ident)
        if not isinstance(ident, str) or not re.fullmatch(r'PRD-\d{3}', ident) or ident in seen:
            errors.append(f'{prefix}: invalid or duplicate ID')
        seen.add(str(ident))
        if set(item) != ITEM_FIELDS:
            errors.append(f'{prefix}: unexpected or missing fields')
        for field, cap in PROSE_CAPS.items():
            value = item.get(field)
            if not isinstance(value, str) or not value.strip() or len(value) > cap:
                errors.append(f'{prefix}: {field} must contain 1-{cap} characters')
        if not isinstance(item.get('priority'), str) or item['priority'] not in PRIORITY_HEADINGS:
            errors.append(f'{prefix}: invalid priority')
        if not isinstance(item.get('status'), str) or item['status'] not in STATUSES:
            errors.append(f'{prefix}: invalid status')
        if not isinstance(item.get('release_status'), str) or item['release_status'] not in RELEASE_STATUSES:
            errors.append(f'{prefix}: invalid release_status')
        for field in ('decision_ids', 'evidence_links', 'pr_references'):
            values = item.get(field)
            if not isinstance(values, list):
                errors.append(f'{prefix}: {field} must be a list')
                continue
            if field == 'pr_references':
                valid = all(isinstance(v, int) and not isinstance(v, bool) and v > 0 for v in values)
            elif field == 'decision_ids':
                valid = all(isinstance(v, str) and re.fullmatch(r'DEC-[A-Z0-9-]+', v) for v in values)
            else:
                valid = all(isinstance(v, str) and 0 < len(v) <= 512 for v in values)
                if valid:
                    for value in values:
                        target = urlsplit(value)
                        if target.scheme:
                            if target.scheme not in {'http', 'https'} or not target.netloc:
                                errors.append(f'{prefix}: invalid evidence URL {value}')
                        elif not value.startswith(('docs/', 'archive/', 'src/', 'tests/')):
                            errors.append(f'{prefix}: evidence must use repository-relative paths or HTTP URLs')
                        elif not (root / target.path).resolve().is_relative_to(root.resolve()):
                            errors.append(f'{prefix}: evidence target is outside repository {value}')
                        elif not (root / target.path).is_file():
                            errors.append(f'{prefix}: missing evidence target {value}')
            if not valid:
                errors.append(f'{prefix}: invalid {field}')
            elif len(values) != len(set(values)):
                errors.append(f'{prefix}: duplicate {field}')
        anchor = item.get('detail_anchor')
        if not isinstance(anchor, str) or not re.fullmatch(r'[a-z0-9-]+', anchor):
            errors.append(f'{prefix}: invalid detail_anchor')
        # A historical implementation grade never completes a release requirement. A future
        # closure must name the frozen release commit and retain actual evidence explicitly.
        if item.get('release_status') == 'complete':
            if not re.fullmatch(r'[0-9a-f]{40}', str(registry.get('release_commit', ''))):
                errors.append(f'{prefix}: completed release evidence needs the frozen release_commit')
            if not item.get('evidence_links'):
                errors.append(f'{prefix}: completed release evidence requires evidence_links')
    if registry.get('release_status') == 'ready' and any(item.get('priority') == 'P0' and item.get('release_status') != 'complete' for item in items if isinstance(item, dict)):
        errors.append('release remains blocked until every P0 row is complete on the same release commit')
    sections = data.get('detail_sections')
    if not isinstance(sections, list) or not sections:
        errors.append('detail_sections must retain compatibility anchors')
    else:
        anchors: set[str] = set()
        for section in sections:
            if not isinstance(section, dict) or set(section) != {'heading', 'level', 'anchor'}:
                errors.append('invalid detail section')
                continue
            if not isinstance(section['heading'], str) or not 0 < len(section['heading']) <= 160 or not isinstance(section['level'], int) or section['level'] not in {2, 3}:
                errors.append('invalid detail section heading')
            if not isinstance(section['anchor'], str) or not re.fullmatch(r'[\w-]+', section['anchor']) or section['anchor'] in anchors:
                errors.append('invalid or duplicate detail section anchor')
            anchors.add(section['anchor'])
        headings = [section.get('heading') for section in sections if isinstance(section, dict)]
        for heading in PRIORITY_HEADINGS.values():
            if headings.count(heading) != 1:
                errors.append(f'exactly one required priority section must be retained: {heading}')
        for item in items:
            if isinstance(item, dict) and item.get('detail_anchor') not in anchors:
                errors.append(f"{item.get('id')}: detail_anchor is not a retained section")
    return [f'{DATA}: {error}' for error in errors]


def render(root: Path) -> str:
    errors = validate_data(root)
    if errors:
        raise ValueError('\n'.join(errors))
    data = load_data(root / DATA)
    registry = data['registry']
    snapshot = 'history/' + Path(registry['migration_snapshot']).name
    parts = [
        '# Meridian Production-Readiness Master TODO\n\n',
        '<!-- Generated by build/scripts/docs/render-production-readiness.py; edit docs/roadmap/data/production-readiness.yml. -->\n\n',
        f"**Status:** production certification {registry['release_status']}\n\n**Reviewed:** {registry['reviewed']} (retained review scope; migration is not a new verification)\n\n",
        f"**Historical certification baseline:** `{registry['baseline_commit']}`\n\n",
        f'The [migration snapshot]({snapshot}) retains all detailed implementation evidence, reservations, accepted scope and historical review dates. Its current boundary details remain applicable unless an explicit later decision supersedes them.\n\n',
        'Development verification uses reproducible synthetic evidence and the proportional [solo-developer workflow](../ai/solo-developer-workflow.md). Production certification is a separate release decision. An implementation-complete row is not a certified release.\n\n',
        '**Release gate:** Every P0 row must be complete on the same release commit. The supported deployment, authentication, tenancy, storage, provider, browser and WPF envelope must be explicit; unsupported paths fail closed. Retain signed artifact/lifecycle, recovery, operator and required GitHub Actions evidence. Historical green runs do not certify later changes.\n\n',
        f"**Open P0 release requirements:** {sum(i['priority'] == 'P0' and i['release_status'] != 'complete' for i in data['items'])}.\n\n",
        'Implementation states below preserve the recorded source/candidate grades; `open` means the parent item remains open. Release status is tracked independently.\n\n',
    ]
    # Keep source headings and resulting anchors stable for existing deep links. Detailed prose
    # moves to one linked snapshot; generated rows own the current structured status.
    for section in data['detail_sections']:
        heading = section['heading']
        parts.append('#' * section['level'] + ' ' + heading + '\n\n')
        if heading in PRIORITY_HEADINGS.values():
            priority = next(key for key, value in PRIORITY_HEADINGS.items() if value == heading)
            rows = []
            for item in data['items']:
                if item['priority'] != priority:
                    continue
                refs = [f'`{d}`' for d in item['decision_ids']]
                refs += [f'[#{p}](https://github.com/rodoHasArrived/Meridian-main/pull/{p})' for p in item['pr_references']]
                evidence = []
                for index, target in enumerate(item['evidence_links'], 1):
                    url = target if urlsplit(target).scheme else '../../' + target
                    evidence.append(f'[E{index}]({url})')
                rows.append([f"`{item['id']}`", item['title'], item['owner'], item['status'], item['release_status'], item['remaining_gate'], ', '.join(refs) or '-', ', '.join(evidence) or '-'])
            parts.append(markdown_table(['ID', 'Scope', 'Owner', 'Implementation state', 'Release status', 'Remaining gate', 'Decisions / PRs', 'Evidence'], rows) + '\n\n')
        parts.append(f"[Detailed evidence and boundaries]({snapshot}#{section['anchor']}).\n\n")
    return ''.join(parts).rstrip() + '\n'


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=repo_root())
    parser.add_argument('--summary', action='store_true')
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    try:
        expected = render(args.root)
    except ValueError as exc:
        print(exc)
        return 1
    output = args.root / OUTPUT
    if args.check:
        if not output.is_file() or output.read_text(encoding='utf-8') != expected:
            print(f'{OUTPUT}: generated tracker is out of date')
            return 1
    else:
        write_text_if_changed(output, expected)
    if args.summary:
        print('production-readiness registry valid; generated tracker current')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
