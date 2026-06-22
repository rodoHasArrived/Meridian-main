using Xunit;

namespace Meridian.Tests.Architecture;

public sealed class AccountingSemanticsBoundaryTests
{
    [Fact]
    public void UiShared_ShouldNot_Define_PrivateCapitalAccountingSemanticBuilders()
    {
        var repoRoot = FindRepositoryRoot();
        var uiSharedRoot = Path.Combine(repoRoot, "src", "Meridian.Ui.Shared");
        var financialOperationsRoot = Path.Combine(repoRoot, "src", "Meridian.FinancialOperations", "PrivateCapital");
        var forbiddenNeedles = new[]
        {
            "class PrivateCapitalFundEventLedgerReadinessBuilder",
            "class PrivateCapitalFundEventLedgerRecordBuilder",
            "class PrivateCapitalCapitalAccountSubledgerBuilder",
            "class PrivateCapitalEvidenceCategoryBuilder",
            "class PrivateCapitalPaymentIntentEvidenceBuilder",
            "BuildPaymentIntentWorkflows(",
            "ResolvePaymentIntentWorkflowStatus(",
            "BuildPrivateCapitalReportOutput(",
            "BuildReportOutputReadiness("
        };

        var offenders = Directory
            .EnumerateFiles(uiSharedRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file =>
            {
                var text = File.ReadAllText(file);
                return forbiddenNeedles
                    .Where(text.Contains)
                    .Select(needle => $"{Path.GetRelativePath(repoRoot, file)} contains '{needle}'");
            })
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "UI Shared must adapt private-capital accounting projections from Financial Operations instead of defining semantic readiness/evidence/payment classifiers:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, offenders));

        Assert.True(
            File.Exists(Path.Combine(financialOperationsRoot, "PrivateCapitalActivityProjectionBuilder.cs")),
            "Financial Operations must own the private-capital activity projection builder.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate Meridian repository root from test output directory.");
    }
}
