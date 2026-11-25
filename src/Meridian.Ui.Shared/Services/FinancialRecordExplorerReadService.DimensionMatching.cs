using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    private static bool RowMatchesFilters(
        FinancialRecordExplorerRowDto row,
        IReadOnlyList<FinancialRecordExplorerFilterDto> filters)
        => filters.Count == 0 || filters.All(filter =>
        {
            var filterId = NormalizeSearchToken(filter.FilterId);
            // Agreement identity is dimensional scope: matching another field's text (or version
            // 10 when version 1 was requested) must not admit an unrelated row.
            var agreementField = filterId switch
            {
                "intercompanyagreementid" => "Agreement",
                "intercompanyagreementversion" => "Agreement version",
                "accountingarrangementid" => "Accounting arrangement",
                "accountingarrangementrevisionid" => "Accounting arrangement revision",
                "chartaccountnodeid" => "Chart account node",
                "accountbindingrevisionid" => "Account binding revision",
                "intercompanytransactionid" => "Intercompany transaction",
                _ when filter.FilterId.StartsWith("dimension-accounting-arrangement-revision-", StringComparison.OrdinalIgnoreCase) => "Accounting arrangement revision",
                _ when filter.FilterId.StartsWith("dimension-account-binding-revision-", StringComparison.OrdinalIgnoreCase) => "Account binding revision",
                _ when filter.FilterId.StartsWith("dimension-intercompany-transaction-", StringComparison.OrdinalIgnoreCase) => "Intercompany transaction",
                _ when filter.FilterId.StartsWith("dimension-accounting-arrangement-", StringComparison.OrdinalIgnoreCase) => "Accounting arrangement",
                _ when filter.FilterId.StartsWith("dimension-chart-account-node-", StringComparison.OrdinalIgnoreCase) => "Chart account node",
                _ when filter.FilterId.StartsWith("dimension-intercompany-agreement-version-", StringComparison.OrdinalIgnoreCase) => "Agreement version",
                _ when filter.FilterId.StartsWith("dimension-intercompany-agreement-", StringComparison.OrdinalIgnoreCase) => "Agreement",
                _ => null
            };
            if (agreementField is not null)
            {
                return row.Detail.Fields.Any(field =>
                    string.Equals(field.Label, agreementField, StringComparison.Ordinal) &&
                    string.Equals(field.Value.Trim(), filter.Value.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            var expected = NormalizeSearchToken(filter.Value);
            if (string.IsNullOrWhiteSpace(expected))
            {
                return true;
            }

            var directCell = string.IsNullOrWhiteSpace(filterId)
                ? null
                : row.Cells.FirstOrDefault(cell => string.Equals(NormalizeSearchToken(cell.ColumnId), filterId, StringComparison.Ordinal));
            return directCell is not null
                ? CellMatchesToken(directCell, expected)
                : RowMatchesSearch(row, expected);
        });
}
