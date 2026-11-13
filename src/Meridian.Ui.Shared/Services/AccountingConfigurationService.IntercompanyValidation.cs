using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class AccountingConfigurationService
{
    private static IEnumerable<AccountingConfigurationValidationIssueDto> ValidateIntercompanyAccountRoles(
        IReadOnlyList<ChartOfAccountsNodeDto> nodes)
    {
        foreach (var node in nodes.Where(x => !x.IsArchived && x.IntercompanyRole.HasValue))
        {
            if (!IntercompanyAccountBindingRules.IsCompatible(node.IntercompanyRole!.Value, node.AccountType) ||
                node.Symbol is not null || node.FinancialAccountId is not null)
                yield return Issue("chart.intercompany-role-invalid", AccountingConfigurationValidationSeverityDto.Critical,
                    "An intercompany role requires its compatible account type and an unscoped chart node.", node.NodeId,
                    "Use Asset for receivable, Liability for payable, Revenue/Expense for income treatments, or Equity for translation reserve; remove instrument and financial-account scope.");
        }
    }
}
