import importlib.util
import json
from dataclasses import dataclass
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

import yaml

ROOT = Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'build/scripts/ci' / f'{name}.py')
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


class EvidenceControlsTests(unittest.TestCase):
    def test_missing_empty_malformed_and_stale_trx_fail_even_when_process_succeeds(self):
        runner = load('run-dotnet-ci-tests')
        for evidence in (None, '<TestRun/>', '<broken', '<TestRun><UnitTestResult outcome="NotExecuted"/></TestRun>'):
            with self.subTest(evidence=evidence), tempfile.TemporaryDirectory() as tmp:
                output = Path(tmp) / 'required'
                output.mkdir()
                (output / 'required-old.trx').write_text('<TestRun><UnitTestResult outcome="Passed"/></TestRun>')
                def run(*args, **kwargs):
                    if evidence is not None:
                        (output / 'required.trx').write_text(evidence)
                    return type('Completed', (), {'returncode': 0})()
                with patch.object(runner.subprocess, 'run', side_effect=run):
                    result = runner.run_tests([runner.TestProject('required', 'test.csproj')], configuration='Release', test_filter='', results_dir=Path(tmp), dry_run=False)[0]
                self.assertNotEqual(result.exit_code, 0)
                self.assertIsNotNone(result.evidence_error)
                self.assertFalse((output / 'required-old.trx').exists())

    def test_trx_counts_and_identity_digest_ignore_run_guids_and_result_order(self):
        evidence = load('test_evidence')
        with tempfile.TemporaryDirectory() as tmp:
            file = Path(tmp) / 'slice.trx'
            rows = ['<UnitTestResult testName="first" outcome="Passed"/>', '<UnitTestResult testName="second" outcome="NotExecuted"/>']
            file.write_text('<TestRun>' + ''.join(rows) + '</TestRun>')
            first = evidence.collect_trx(Path(tmp), 'slice')
            file.write_text('<TestRun id="new">' + ''.join(reversed(rows)) + '</TestRun>')
            second = evidence.collect_trx(Path(tmp), 'slice')
            self.assertEqual(first['testIdentityDigest'], second['testIdentityDigest'])
            self.assertEqual(first['counts'], dict(passed=1, failed=0, skipped=1, other=0))

    def test_all_external_actions_including_subpaths_require_sha_and_version(self):
        hygiene = load('check-workflow-hygiene')
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            for ref, expected in [
                ('org/repo/sub@v4 # v4', False),
                ('org/repo/sub@' + 'a'*7 + ' # v4', False),
                ('org/repo/sub@' + 'g'*40 + ' # v4', False),
                ('org/repo/sub@' + 'a'*41 + ' # v4', False),
                ('org/repo@' + 'a'*40, False),
                ('org/repo/sub@' + 'a'*40 + ' # v4', True),
                ('./local.yml', True),
            ]:
                (directory / 'ci.yml').write_text('steps:\n  - uses: ' + ref + '\n')
                failures = []
                with patch.object(hygiene, 'WORKFLOW_DIR', directory), patch.object(hygiene, 'REPO_ROOT', directory):
                    hygiene.check_action_refs(failures)
                self.assertEqual(not failures, expected, ref)

    def test_untracked_or_expired_quarantine_is_rejected(self):
        runner = load('run-script-tests')
        valid = json.loads(runner.DEFAULT_QUARANTINE.read_text(encoding='utf-8'))
        name = next(iter(valid['quarantined_modules']))
        for bad in ('reason only', {'reason': 'no owner'}, dict(valid['quarantined_modules'][name], reviewBy='2000-01-01')):
            with tempfile.TemporaryDirectory() as tmp:
                file = Path(tmp) / 'quarantine.json'
                file.write_text(json.dumps({'quarantined_modules': {name: bad}}))
                with self.assertRaises(ValueError):
                    runner.load_quarantine(file)


@dataclass(frozen=True)
class WorkflowContract:
    filename: str
    test: str
    job: str
    actions: tuple[str, ...]
    settings: tuple[tuple[str, str, str, str], ...] = ()


CONTRACTS = (
    WorkflowContract(
        'schema-control.yml',
        'test_schema_control_workflow.SchemaControlWorkflowTests.test_workflow_uses_repository_pinned_actions',
        'schema-control', ('actions/checkout', 'actions/setup-python', 'actions/upload-artifact'),
        (('actions/checkout', 'with', 'persist-credentials', 'true'),
         ('actions/checkout', 'with', 'fetch-depth', '1'),
         ('actions/setup-python', 'with', 'python-version', '3.11')),
    ),
    WorkflowContract(
        'documentation.yml',
        'test_documentation_workflow.DocumentationWorkflowTests.test_regenerate_docs_job_fetches_history_for_dashboard_diff',
        'regenerate-docs', ('actions/checkout',),
        (('actions/checkout', 'with', 'persist-credentials', 'true'),
         ('actions/checkout', 'with', 'fetch-depth', '1')),
    ),
    WorkflowContract(
        'web-screenshot-capture.yml',
        'test_refresh_screenshots_workflow.RefreshScreenshotsWorkflowTests.test_web_screenshot_job_installs_optional_native_packages',
        'capture-web-screenshots', ('peter-evans/create-pull-request', 'actions/checkout'),
        (('actions/checkout', 'with', 'persist-credentials', 'true'),),
    ),
    WorkflowContract(
        'maintenance.yml',
        'test_project_target_framework_alignment.ProjectTargetFrameworkAlignmentTests.test_maintenance_workflow_validates_current_workflow_surface',
        'workflow-hygiene', ('rhysd/actionlint',),
    ),
    WorkflowContract(
        'ci.yml',
        'test_ci_workflow_contract.CiWorkflowContractTests.test_secret_scan_remains_pull_request_visible',
        'secret-scan', ('gitleaks/gitleaks-action', 'actions/checkout'),
        (('gitleaks/gitleaks-action', 'env', 'GITLEAKS_VERSION', 'latest'),
         ('gitleaks/gitleaks-action', 'env', 'GITLEAKS_CONFIG', 'other.toml'),
         ('actions/checkout', 'with', 'persist-credentials', 'true'),
         ('actions/checkout', 'with', 'fetch-depth', '1')),
    ),
)


def yaml_entry(mapping, key):
    if not isinstance(mapping, yaml.MappingNode):
        raise AssertionError(f'Expected YAML mapping containing {key!r}')
    for name, value in mapping.value:
        if name.value == key:
            return name, value
    raise AssertionError(f'Missing YAML key {key!r}')


def action_node(workflow, contract, action):
    document = yaml.compose(workflow, Loader=yaml.BaseLoader)
    jobs = yaml_entry(document, 'jobs')[1]
    job = yaml_entry(jobs, contract.job)[1]
    steps = yaml_entry(job, 'steps')[1]
    if not isinstance(steps, yaml.SequenceNode):
        raise AssertionError(f'{contract.job}: steps must be a YAML sequence')
    for step in steps.value:
        if not isinstance(step, yaml.MappingNode):
            raise AssertionError(f'{contract.job}: each step must be a YAML mapping')
        if any(name.value == 'uses' and isinstance(value, yaml.ScalarNode)
               and value.value.partition('@')[0] == action for name, value in step.value):
            return step
    raise AssertionError(f'{contract.job}: missing {action} step')


def replace_node(workflow, node, replacement):
    # Source marks retain comments and formatting needed by other contract checks.
    return workflow[:node.start_mark.index] + replacement + workflow[node.end_mark.index:]


class WorkflowPinContractRegressionTests(unittest.TestCase):
    def check_contract(self, contract, workflow):
        module_name, class_name, method_name = contract.test.split('.')
        module = importlib.import_module(f'tests.scripts.{module_name}')
        case = getattr(module, class_name)(method_name)
        workflow_path = ROOT / '.github/workflows' / contract.filename
        read_text = Path.read_text

        def read_workflow(path, *args, **kwargs):
            return workflow if path == workflow_path else read_text(path, *args, **kwargs)

        # A suite runs class fixtures as well as per-test setup, teardown and cleanups.
        # Only replace the selected workflow; other fixture reads keep their real data.
        result = unittest.TestResult()
        with patch.object(Path, 'read_text', new=read_workflow):
            unittest.TestSuite([case]).run(result)
        if result.errors:
            raise RuntimeError('\n'.join(detail for _, detail in result.errors))
        if result.testsRun != 1 or result.skipped or result.expectedFailures or result.unexpectedSuccesses:
            raise RuntimeError(f'Contract did not complete normally: {result}; skips={result.skipped}')
        self.assertFalse(result.failures, '\n'.join(detail for _, detail in result.failures))

    def test_documentation_requires_checkout_before_history_comparison(self):
        contract = next(item for item in CONTRACTS if item.filename == 'documentation.yml')
        original = (ROOT / '.github/workflows' / contract.filename).read_text(encoding='utf-8')
        document = yaml.load(original, Loader=yaml.BaseLoader)
        steps = document['jobs'][contract.job]['steps']
        checkout = next(step for step in steps if step.get('uses', '').startswith('actions/checkout@'))
        steps.remove(checkout)
        steps.append(checkout)
        with self.assertRaises(AssertionError):
            self.check_contract(contract, yaml.safe_dump(document))

    def test_contracts_accept_replacement_full_sha(self):
        for contract in CONTRACTS:
            original = (ROOT / '.github/workflows' / contract.filename).read_text(encoding='utf-8')
            for action in contract.actions:
                for alphabet in ('0123456789abcdef', '0123456789ABCDEF', '0123456789aBcDeF'):
                    with self.subTest(workflow=contract.filename, action=action, alphabet=alphabet):
                        node = yaml_entry(action_node(original, contract, action), 'uses')[1]
                        replacement = action + '@' + alphabet * 2 + '01234567'
                        self.assertNotEqual(node.value, replacement)
                        self.check_contract(contract, replace_node(original, node, replacement))

    def test_yaml_helpers_reject_missing_structure_with_assertions(self):
        contract = CONTRACTS[0]
        for workflow, message in (
            ('', "Expected YAML mapping containing 'jobs'"),
            ('{}', "Missing YAML key 'jobs'"),
            ('jobs: {}', "Missing YAML key 'schema-control'"),
            ('jobs: {schema-control: {}}', "Missing YAML key 'steps'"),
            ('jobs: {schema-control: {steps: {}}}', 'schema-control: steps must be a YAML sequence'),
            ('jobs: {schema-control: {steps: [null]}}', 'schema-control: each step must be a YAML mapping'),
            ('jobs: {schema-control: {steps: []}}', 'schema-control: missing actions/checkout step'),
            ('jobs: {schema-control: {steps: [{run: echo ok}]}}', 'schema-control: missing actions/checkout step'),
        ):
            with self.subTest(workflow=workflow):
                with self.assertRaises(AssertionError) as raised:
                    action_node(workflow, contract, 'actions/checkout')
                self.assertEqual(str(raised.exception), message)

        step = yaml.compose('uses: actions/checkout@' + 'a' * 40, Loader=yaml.BaseLoader)
        with self.assertRaisesRegex(AssertionError, "Missing YAML key 'with'"):
            yaml_entry(step, 'with')

    def test_documentation_requires_history_comparison_step(self):
        contract = next(item for item in CONTRACTS if item.filename == 'documentation.yml')
        original = (ROOT / '.github/workflows' / contract.filename).read_text(encoding='utf-8')
        document = yaml.load(original, Loader=yaml.BaseLoader)
        steps = document['jobs'][contract.job]['steps']
        steps[:] = [step for step in steps
                    if step.get('name') != 'Compare dashboard readiness deltas vs previous commit']
        with self.assertRaisesRegex(AssertionError, 'history comparison step'):
            self.check_contract(contract, yaml.safe_dump(document))

    def test_contract_harness_runs_unittest_lifecycle_and_preserves_result_kinds(self):
        contract = WorkflowContract(
            CONTRACTS[0].filename,
            'test_ci_evidence_controls.LifecycleProbe.test_contract',
            CONTRACTS[0].job, (),
        )
        workflow_path = ROOT / '.github/workflows' / contract.filename
        unrelated_path = ROOT / 'tests/scripts/workflow_assertions.py'
        unrelated = unrelated_path.read_text(encoding='utf-8')
        for outcome in ('success', 'failure', 'error', 'skip'):
            with self.subTest(outcome=outcome):
                events = []

                class LifecycleProbe(unittest.TestCase):
                    @classmethod
                    def setUpClass(cls):
                        events.append('setUpClass')
                        cls.addClassCleanup(events.append, 'class cleanup')
                        cls.workflow = workflow_path.read_text(encoding='utf-8')

                    def setUp(self):
                        events.append('setUp')
                        self.addCleanup(events.append, 'cleanup')
                        self.assertEqual(self.workflow, 'mutated workflow')
                        self.assertEqual(unrelated_path.read_text(encoding='utf-8'), unrelated)

                    def test_contract(self):
                        events.append('test')
                        if outcome == 'failure':
                            with self.subTest(setting='pin'):
                                self.fail('rejected mutation')
                        elif outcome == 'error':
                            raise ValueError('unexpected harness error')
                        elif outcome == 'skip':
                            self.skipTest('contract did not run')

                    def tearDown(self):
                        events.append('tearDown')

                    @classmethod
                    def tearDownClass(cls):
                        events.append('tearDownClass')

                module = importlib.import_module('tests.scripts.test_ci_evidence_controls')
                with patch.object(module, 'LifecycleProbe', LifecycleProbe, create=True):
                    if outcome == 'success':
                        self.check_contract(contract, 'mutated workflow')
                    else:
                        exception = AssertionError if outcome == 'failure' else RuntimeError
                        message = {'failure': 'rejected mutation', 'error': 'unexpected harness error',
                                   'skip': 'contract did not run'}[outcome]
                        with self.assertRaisesRegex(exception, message):
                            self.check_contract(contract, 'mutated workflow')
                self.assertEqual(events, ['setUpClass', 'setUp', 'test', 'tearDown', 'cleanup',
                                          'tearDownClass', 'class cleanup'])

    def test_contracts_reject_mutable_malformed_and_wrong_action_refs(self):
        for contract in CONTRACTS:
            original = (ROOT / '.github/workflows' / contract.filename).read_text(encoding='utf-8')
            for action in contract.actions:
                node = yaml_entry(action_node(original, contract, action), 'uses')[1]
                for replacement in (
                    action + '@v7', action + '@' + 'a'*7,
                    action + '@' + 'g'*40, action + '@' + 'a'*41,
                    'wrong-owner/wrong-action@' + 'a'*40,
                    action + '/unexpected-subpath@' + 'a'*40,
                ):
                    with self.subTest(workflow=contract.filename, action=action, ref=replacement):
                        with self.assertRaises(AssertionError):
                            self.check_contract(contract, replace_node(original, node, replacement))

    def test_contracts_reject_missing_or_incorrect_required_settings(self):
        for contract in CONTRACTS:
            original = (ROOT / '.github/workflows' / contract.filename).read_text(encoding='utf-8')
            for action, section, key, incorrect in contract.settings:
                step = action_node(original, contract, action)
                section_key, settings = yaml_entry(step, section)
                setting_key, value = yaml_entry(settings, key)
                # Remove the mapping too when its only setting disappears.
                first = section_key if len(settings.value) == 1 else setting_key
                missing = original[:first.start_mark.index] + original[value.end_mark.index:]
                for label, mutated in (
                    ('missing', missing), ('incorrect', replace_node(original, value, incorrect)),
                ):
                    with self.subTest(workflow=contract.filename, action=action, setting=key, mutation=label):
                        with self.assertRaises(AssertionError):
                            self.check_contract(contract, mutated)
