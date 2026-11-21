namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Explicit eligibility binding to two existing chart accounts and one already-promoted rule.
/// It neither creates accounts nor changes accounting configuration or rule-pack authority.
/// </summary>
public sealed record CashDividendAccountRoleBindingDto(
    Guid LedgerBookId,
    string DividendReceivableAccount,
    string DividendIncomeAccount,
    string RulePackId,
    string RulePackVersion,
    string SelectedRuleId,
    string SelectedRuleVersion,
    int SchemaVersion = 1,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? DividendReceivableAccountName = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? DividendIncomeAccountName = null);
