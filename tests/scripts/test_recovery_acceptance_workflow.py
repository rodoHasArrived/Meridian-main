"""Pin the release train's real acceptance boundary and immutable recovery inputs."""
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / '.github/workflows/production-certification.yml'


class RecoveryAcceptanceWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.workflow = WORKFLOW.read_text(encoding='utf-8')
        self.job = self.workflow.split('  recovery-acceptance:\n', 1)[1].split('  documentation-evidence:\n', 1)[0]
        self.validation = self.job.split('        run: |\n', 1)[1].split('      - name:', 1)[0]

    def test_standalone_certification_preserves_automation_scope(self):
        input_block = self.workflow.split('      require-recovery-acceptance:\n', 1)[1].split('      integration-artifact-id:', 1)[0]
        self.assertIn('type: boolean', input_block)
        self.assertIn('default: false', input_block)
        self.assertIn('if: inputs.require-recovery-acceptance', self.job)
        self.assertIn('needs: recovery-drill', self.job)
        self.assertIn('environment: desktop-release-signing', self.job)

    def test_completion_is_data_and_never_inferred_from_approval(self):
        self.assertIn('RECOVERY_COMPLETION_JSON: ${{ vars.MDC_RECOVERY_COMPLETION_JSON }}', self.job)
        self.assertNotIn('${{', self.validation)
        self.assertIn('IsNullOrWhiteSpace($env:RECOVERY_COMPLETION_JSON)', self.validation)
        self.assertIn('environment approval is insufficient', self.validation)
        self.assertIn('WriteAllText($completionPath, $env:RECOVERY_COMPLETION_JSON', self.validation)
        self.assertNotIn('operatorAcceptedAtUtc =', self.validation)
        self.assertNotIn('operatorAcceptedBy =', self.validation)

    def test_same_attempt_source_is_preserved_and_evaluated_separately(self):
        self.assertIn('name: production-recovery-drill-${{ github.run_id }}-${{ github.run_attempt }}', self.job)
        self.assertIn('ref: ${{ github.sha }}', self.job)
        self.assertIn('$source.sourceCommit -cne $env:GITHUB_SHA', self.validation)
        self.assertIn('$source.drillSourceCommit -cne $env:GITHUB_SHA', self.validation)
        self.assertIn('Copy-Item -LiteralPath $sourcePath -Destination $retainedPath', self.validation)
        self.assertIn('validate-recovery-receipt.ps1', self.validation)
        self.assertIn('-ReceiptPath $retainedPath', self.validation)
        self.assertIn('-RecoveryEvidencePath $completionPath', self.validation)
        self.assertIn('-OutputPath artifacts/recovery-objectives/recovery-objective-evaluation.json', self.validation)
        self.assertIn('-MaximumRpoSeconds 3600', self.validation)
        self.assertIn('-MaximumRtoSeconds 7200', self.validation)
        self.assertIn('if ($LASTEXITCODE -ne 0)', self.validation)
        self.assertIn('Get-FileHash -LiteralPath $retainedPath', self.validation)
        self.assertIn('name: production-recovery-objectives-${{ github.run_id }}-${{ github.run_attempt }}', self.job)
        self.assertIn('if: always()', self.job)
        self.assertIn('if-no-files-found: error', self.job)


if __name__ == '__main__':
    unittest.main()
