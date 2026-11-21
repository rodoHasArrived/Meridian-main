#!/usr/bin/env python3
"""Map one immutable release attempt to the P0 candidate-evidence requirements.

This verifies release evidence, not owner sign-off or activation of repository
rules. Those decisions remain explicit in the tracker and the support ADR.
Nested artifact archives are inspected in memory and never extracted/executed.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import re
import sys
import zipfile

REPORT = 'release-candidate-evidence.json'
POLICY = 'p0-candidate-v1'
MAX_REPORT = 8 * 1024**2


def load_module(name, file):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).parent / file)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def require(condition, message):
    if not condition:
        raise ValueError(message)


def json_report(archive, filename):
    entries = [entry for entry in archive.infolist() if entry.filename == filename]
    require(len(entries) == 1 and entries[0].file_size <= MAX_REPORT,
            f'Missing, duplicate or oversized report: {filename}')
    value = json.loads(archive.read(entries[0]).decode('utf-8-sig'))
    require(isinstance(value, dict), f'Report is not an object: {filename}')
    return value


def evaluate(root: Path, gate: dict) -> dict:
    """Called only after the publisher validates bundle provenance and digests."""
    promotion = load_module('candidate_promotion', 'verify-release-promotion.py')
    commit, run, attempt = gate['commitSha'], str(gate['runId']), str(gate['runAttempt'])
    require(bool(re.fullmatch('[0-9a-f]{40}', commit)), 'Candidate requires a full source commit.')
    require(isinstance(gate.get('version'), str), 'Candidate version is missing.')
    expected_scope = promotion.envelope.support_scope(gate['version'])
    expected_runtimes = sorted({runtime for _, runtime in promotion.required_families(gate['version'])})
    require(gate.get('supportScope') == expected_scope and gate.get('supportedRuntimes') == expected_runtimes,
            'Candidate support envelope does not match the version policy.')
    require(all(gate['validations'].get(name, {}).get('result') == 'success'
                for name in promotion.GATES), 'Candidate dependencies must all succeed.')
    rows = {}
    with zipfile.ZipFile(root / 'release-audit-evidence.zip') as bundle:
        manifest = json_report(bundle, 'manifest.json')
        number = str(manifest['runNumber'])

        def report(artifact, filename):
            entry = bundle.getinfo(f'artifacts/{artifact}.zip')
            # Only small reports are inflated, never installers or backup payloads.
            require(entry.file_size <= promotion.MAX_EVIDENCE_BYTES, 'Oversized artifact archive.')
            with bundle.open(entry) as stream, zipfile.ZipFile(stream) as archive:
                return json_report(archive, filename)

        def numbered(prefix, file):
            return report(f'{prefix}-{number}-{attempt}', file)

        def identified(prefix, file):
            return report(f'{prefix}-{run}-{attempt}', file)

        def bind(value, commit_key, run_key, attempt_key):
            require(value.get(commit_key) == commit and str(value.get(run_key)) == run
                    and str(value.get(attempt_key)) == attempt, 'Report belongs to another commit/run/attempt.')

        families = promotion.required_families(gate['version'])
        lifecycle = gate.get('lifecycle', [])
        def family(receipt):
            if receipt.get('project') == 'consumer-setup':
                return ('consumer-setup', receipt.get('runtime'))
            # The installed MSIX harness predates project/runtime receipt fields.
            return ('desktop-installer', f"win-{str(receipt.get('architecture', '')).lower()}")

        require({family(r) for r in lifecycle} == families
                and len(lifecycle) == len(families), 'Candidate lifecycle families do not match the envelope.')
        for receipt in lifecycle:
            bind(receipt, 'sourceCommit', 'workflowRunId', 'workflowRunAttempt')
            require(receipt.get('status') == 'passed', 'Installed lifecycle did not pass.')
        rows['PRD-000'] = dict(evidenceStatus='verified', supportDecision='ADR-019',
                               runtimes=sorted({runtime for _, runtime in families}),
                               lifecycleModes=sorted({r['mode'] for r in lifecycle}))

        startup = gate['installedStartup']
        bind(startup, 'commitSha', 'workflowRunId', 'workflowRunAttempt')
        require(startup.get('project') == 'web-workstation' and startup.get('runtime') == 'win-x64'
                and 'web-workstation-installed-startup' in startup.get('validationLanes', []),
                'PRD-013 requires installed web-workstation startup.')
        rows['PRD-013'] = dict(evidenceStatus='verified', project='web-workstation', runtime='win-x64')

        signing = identified('release-signing-readiness', 'signing.json')
        bind(signing, 'commitSha', 'runId', 'runAttempt')
        require(signing.get('status') == 'passed' and signing.get('hasPrivateKey') is True
                and signing.get('publisherMatches') is True, 'PRD-014 production signing is unproven.')
        rows['PRD-014'] = dict(evidenceStatus='verified',
                               packages=sorted(f'{project}/{runtime}' for project, runtime in families),
                               publisher='protected-production-certificate')

        source = identified('production-recovery-drill', 'recovery-drill-receipt.json')
        recovery = identified('production-recovery-objectives', 'recovery-objective-evaluation.json')
        require(source.get('sourceCommit') == commit and source.get('drillSourceCommit') == commit,
                'PRD-015 recovery source is from another commit.')
        for field in ('sourceCommit', 'drillSourceCommit', 'manifestSha256', 'backupId',
                      'simulatedLossAtUtc', 'lossDeclaredAtUtc'):
            require(source.get(field) is not None and recovery.get(field) == source[field],
                    f'PRD-015 evaluated receipt changed immutable {field}.')
        require(recovery.get('objectiveStatus') == 'proven' and recovery.get('rpoStatus') == 'proven'
                and recovery.get('rtoStatus') == 'proven' and recovery.get('objectiveErrors') == [],
                'PRD-015 requires independently evaluated recovery objectives and operator acceptance.')
        for measured, maximum, cap in (('measuredRpoSeconds', 'effectiveMaximumRpoSeconds', 3600),
                                       ('measuredRtoSeconds', 'effectiveMaximumRtoSeconds', 7200)):
            values = [recovery.get(key) for key in (measured, maximum)]
            require(all(type(v) in (int, float) and math.isfinite(v) for v in values)
                    and 0 <= values[0] <= values[1] <= cap and values[1] > 0,
                    f'PRD-015 invalid or breached {measured}.')
        require(all(isinstance(recovery.get(field), str) and recovery[field].strip() for field in
                    ('operatorAcceptedBy', 'operatorAcceptanceEvidence', 'reconciliationEvidence')),
                'PRD-015 owner acceptance and reconciliation references are missing.')
        rows['PRD-015'] = dict(evidenceStatus='verified', measuredRpoSeconds=recovery['measuredRpoSeconds'],
                               measuredRtoSeconds=recovery['measuredRtoSeconds'],
                               evidenceScope='synthetic-fixture-with-owner-acceptance',
                               operatorAcceptedBy=recovery['operatorAcceptedBy'])

        integration = numbered('production-certification', 'integration-receipt.json')
        bind(integration.get('identity', {}), 'commit', 'runId', 'runAttempt')
        require(integration.get('result') == 'success', 'PRD-016 PostgreSQL certification failed.')
        totals = integration.get('totals', {})
        require(type(totals.get('passed')) is int and totals['passed'] > 0
                and all(type(totals.get(k)) is int and totals[k] == 0 for k in ('failed', 'skipped', 'other')),
                'PRD-016 requires passing, zero-skip TRX evidence.')
        require(set(integration.get('suites', {})) == {'meridian-integrations', 'direct-lending-integrations', 'demo-integrations'},
                'PRD-016 integration suites are incomplete.')
        npm = numbered('production-dependency-evidence', 'npm-audit-gate.json')
        require(npm.get('passed') is True, 'PRD-016 dependency gate failed.')
        rows['PRD-016'] = dict(evidenceStatus='verified', passedTests=totals['passed'],
                               ownerRequirement='Repository administrator must activate and retain required checks; this packet does not attest repository settings.')

        docs = numbered('production-documentation-evidence', 'production-certification.json')
        results = docs.get('results', [])
        docs_module = load_module('candidate_docs', '../docs/run-docs-automation.py')
        expected_scripts = set(docs_module.PROFILE_CONFIG['core']) - {docs_module.PILOT_READINESS_TASK}
        require(docs.get('profile') == 'core' and docs.get('dry_run') is False and results
                and len(results) == len(expected_scripts)
                and set(docs.get('selected_scripts', [])) == {r.get('name') for r in results} == expected_scripts
                and all(r.get('status') == 'success' and r.get('return_code') == 0 for r in results),
                'PRD-017 requires a complete successful core docs run.')
        rows['PRD-017'] = dict(evidenceStatus='verified', profile='core', checks=len(results))

    return dict(schemaVersion=1, policy=POLICY, repository=gate['repository'], commitSha=commit,
                runId=run, runAttempt=attempt, version=gate['version'],
                status='candidate-evidence-verified', rows=rows,
                limitations=['Candidate evidence does not itself close tracker rows, grant owner sign-off, or authorize production activation.'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    require(args.output.resolve() == (args.root / REPORT).resolve(), 'Candidate report must be in the release inventory.')
    publisher = load_module('candidate_publisher', 'publish-certified-release.py')
    gate_path = args.root / publisher.GATE_FILE
    gate = json.loads(gate_path.read_text(encoding='utf-8'))
    publisher.validate_audit_bundle(args.root / publisher.BUNDLE_FILE, gate)
    require(publisher.digest(args.root / publisher.BUNDLE_FILE) == gate['assets'][publisher.BUNDLE_FILE],
            'Audit bundle digest changed.')
    report = evaluate(args.root, gate)
    data = (json.dumps(report, indent=2, sort_keys=True) + '\n').encode()
    if args.output.exists():
        require(args.output.read_bytes() == data, 'Resume must retain the original candidate evidence.')
    else:
        args.output.write_bytes(data)
    gate['candidatePolicy'] = POLICY
    gate['assets'][REPORT] = hashlib.sha256(data).hexdigest()
    gate_path.write_text(json.dumps(gate, indent=2) + '\n', encoding='utf-8')
    publisher.inventory(args.root, gate['commitSha'], gate['repository'])
    print(f"P0 candidate evidence verified for {gate['commitSha']} run {gate['runId']}/{gate['runAttempt']}.")


if __name__ == '__main__':
    main()
