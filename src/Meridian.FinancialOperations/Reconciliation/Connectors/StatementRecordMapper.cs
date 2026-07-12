namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Turns one row of mapped canonical-field values into a <see cref="StatementCanonicalRecord"/>,
/// applying the profile's activity-code map, kind classification, and per-row diagnostics.
/// Shared by the columnar connectors (CSV, OFX pseudo-columns) so mixed-kind rows classify
/// identically everywhere.
/// </summary>
internal static class StatementRecordMapper
{
    private static readonly HashSet<string> CanonicalActivityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "position", "cash", "cashbalance", "trade", "fee", "dividend", "div"
    };

    public static IReadOnlyDictionary<string, string> BuildActivityCodeMap(StatementMappingProfileDocument profile)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in profile.ActivityCodes ?? [])
        {
            map.TryAdd(code.SourceCode.Trim(), code.CanonicalActivityType.Trim());
        }

        return map;
    }

    public static StatementCanonicalRecord? MapRecord(
        IReadOnlyDictionary<StatementCanonicalField, string> values,
        StatementMappingProfileDocument profile,
        IReadOnlyDictionary<string, string> activityCodeMap,
        int rowNumber,
        ICollection<StatementParseIssue> issues,
        ISet<string> reportedUnknownActivityCodes)
    {
        var account = GetValue(values, StatementCanonicalField.Account);
        if (string.IsNullOrWhiteSpace(account))
        {
            issues.Add(StatementParseIssue.Error("ROW_MISSING_ACCOUNT", "Row is missing the account value.", rowNumber, "Account"));
            return null;
        }

        var sourceActivity = GetValue(values, StatementCanonicalField.ActivityType);
        if (string.IsNullOrWhiteSpace(sourceActivity))
        {
            issues.Add(StatementParseIssue.Error("ROW_MISSING_ACTIVITY", "Row is missing the activity type value.", rowNumber, "ActivityType"));
            return null;
        }

        if (!activityCodeMap.TryGetValue(sourceActivity, out var canonicalActivity))
        {
            canonicalActivity = sourceActivity;
            if (!CanonicalActivityTypes.Contains(sourceActivity) && reportedUnknownActivityCodes.Add(sourceActivity))
            {
                issues.Add(StatementParseIssue.Warning(
                    "UNKNOWN_ACTIVITY_CODE",
                    $"Activity code '{sourceActivity}' is not mapped in profile '{profile.ProfileId}'; rows with it are treated as transactions. Add the code to the profile's activity codes to classify it.",
                    rowNumber,
                    "ActivityType"));
            }
        }

        var kind = StatementRecordKindResolver.Resolve(canonicalActivity);

        if (!StatementValueParser.TryParseDate(GetValue(values, StatementCanonicalField.TradeDate), profile, out var tradeDate))
        {
            issues.Add(StatementParseIssue.Error(
                "ROW_INVALID_DATE",
                $"Row has an unparseable trade date '{GetValue(values, StatementCanonicalField.TradeDate)}'.",
                rowNumber,
                "TradeDate"));
            return null;
        }

        if (!TryParseOptionalDecimal(values, StatementCanonicalField.Quantity, profile, rowNumber, issues, out var quantity)
            || !TryParseOptionalDecimal(values, StatementCanonicalField.Price, profile, rowNumber, issues, out var price)
            || !TryParseOptionalDecimal(values, StatementCanonicalField.CashAmount, profile, rowNumber, issues, out var cashAmount))
        {
            return null;
        }

        var symbol = GetValue(values, StatementCanonicalField.SecurityIdentifier)?.Trim() ?? string.Empty;
        if (kind == StatementRecordKind.Position && symbol.Length == 0)
        {
            issues.Add(StatementParseIssue.Error(
                "POSITION_SYMBOL_MISSING",
                "Position rows must carry a security identifier.",
                rowNumber,
                "SecurityIdentifier"));
            return null;
        }

        DateOnly? settlementDate = null;
        var settlementValue = GetValue(values, StatementCanonicalField.SettlementDate);
        if (!string.IsNullOrWhiteSpace(settlementValue))
        {
            if (StatementValueParser.TryParseDate(settlementValue, profile, out var parsedSettlement))
            {
                settlementDate = parsedSettlement;
            }
            else
            {
                issues.Add(StatementParseIssue.Warning(
                    "ROW_INVALID_SETTLEMENT_DATE",
                    $"Row has an unparseable settlement date '{settlementValue}'; it was ignored.",
                    rowNumber,
                    "SettlementDate"));
            }
        }

        decimal? fees = null;
        var feesValue = GetValue(values, StatementCanonicalField.FeesCommission);
        if (!string.IsNullOrWhiteSpace(feesValue))
        {
            if (StatementValueParser.TryParseDecimal(feesValue, profile, out var parsedFees))
            {
                fees = parsedFees;
            }
            else
            {
                issues.Add(StatementParseIssue.Warning(
                    "ROW_INVALID_FEES",
                    $"Row has an unparseable fees/commission value '{feesValue}'; it was ignored.",
                    rowNumber,
                    "FeesCommission"));
            }
        }

        var currency = GetValue(values, StatementCanonicalField.Currency)?.Trim().ToUpperInvariant();
        var externalTransactionId = GetValue(values, StatementCanonicalField.ExternalTransactionId)?.Trim();

        return new StatementCanonicalRecord(
            kind,
            account.Trim(),
            symbol,
            quantity,
            price,
            cashAmount,
            ToArtifactActivityType(kind),
            tradeDate,
            settlementDate,
            string.IsNullOrWhiteSpace(currency) ? null : currency,
            fees,
            string.IsNullOrWhiteSpace(externalTransactionId) ? null : externalTransactionId);
    }

    /// <summary>
    /// The activity value rendered to the canonical artifact: exactly one deterministic
    /// spelling per kind so byte-identical artifacts hash to the same duplicate key.
    /// </summary>
    public static string ToArtifactActivityType(StatementRecordKind kind) => kind switch
    {
        StatementRecordKind.Position => "position",
        StatementRecordKind.CashBalance => "cash",
        StatementRecordKind.Fee => "fee",
        StatementRecordKind.Dividend => "dividend",
        _ => "trade"
    };

    private static bool TryParseOptionalDecimal(
        IReadOnlyDictionary<StatementCanonicalField, string> values,
        StatementCanonicalField field,
        StatementMappingProfileDocument profile,
        int rowNumber,
        ICollection<StatementParseIssue> issues,
        out decimal result)
    {
        result = 0m;
        var value = GetValue(values, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (StatementValueParser.TryParseDecimal(value, profile, out result))
        {
            return true;
        }

        issues.Add(StatementParseIssue.Error(
            "ROW_INVALID_NUMBER",
            $"Row has an unparseable {field} value '{value}'.",
            rowNumber,
            field.ToString()));
        return false;
    }

    private static string? GetValue(IReadOnlyDictionary<StatementCanonicalField, string> values, StatementCanonicalField field)
        => values.TryGetValue(field, out var value) ? value : null;
}
