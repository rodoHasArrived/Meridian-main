using System.Globalization;
using System.Text;
using Meridian.Contracts.Integrity;

namespace Meridian.FinancialOperations.Reconciliation.Connectors.Bai2;

/// <summary>
/// BAI2 (Cash Management Balance Reporting) connector. Reads the group as-of date and currency,
/// each account's closing ledger balance (status code 015), and every transaction-detail (16) record,
/// signing amounts by BAI type-code class (100–399 credit, 400–799 debit). Amounts are expressed in
/// the currency's minor units (cents) per the BAI2 convention and scaled to major units. This lets
/// institutional bank cash statements reconcile without hand-conversion to CSV.
/// </summary>
public sealed class Bai2StatementConnector : IStatementConnector
{
    public const string ConnectorId = "bai2";
    private const string ClosingLedgerStatusCode = "015";
    private const string ClosingAvailableStatusCode = "045";

    public StatementConnectorDescriptor Descriptor { get; } = new(
        ConnectorId,
        "BAI2 cash management statement",
        [".bai", ".bai2"],
        SupportsFileImport: true,
        SupportsRemoteFetch: false,
        RequiresMappingProfile: false,
        DefaultProfileId: null);

    public bool CanHandle(StatementSourceDocument document)
    {
        // BAI2 files begin with a "01," File Header record. Content-sniff so a .txt export is routed
        // here rather than to the CSV catch-all.
        var text = Sniff(document).TrimStart();
        return text.StartsWith("01,", StringComparison.Ordinal);
    }

    public Task<StatementParseResult> ParseAsync(StatementSourceDocument document, CancellationToken ct = default)
    {
        var issues = new List<StatementParseIssue>();
        if (document.Content.Length > StatementConnectorLimits.MaxFileBytes)
        {
            issues.Add(StatementParseIssue.Error(
                "STATEMENT_FILE_TOO_LARGE",
                $"Statement exceeds the {StatementConnectorLimits.MaxFileBytes}-byte import limit."));
            return Task.FromResult(EmptyResult(document, issues));
        }

        var records = new List<StatementCanonicalRecord>();

        var groupCurrency = "USD";
        DateOnly? asOfDate = null;
        string? account = null;
        var accountCurrency = groupCurrency;
        var rowNumber = 0;

        // Trailer bookkeeping. A truncated file drops its 49/98/99 trailers, so every opener must be
        // matched by its trailer and the file trailer's declared group count must agree; otherwise the
        // file is incomplete and must not be accepted as a reconciled statement.
        var groupCount = 0;
        var accountCount = 0;
        var groupTrailerCount = 0;
        var accountTrailerCount = 0;
        var fileTrailerCount = 0;
        int? declaredFileGroupCount = null;
        var hasBlankAccountId = false;
        var inAccountSection = false;
        var transactionOutsideAccount = false;
        var sourceRecordCount = 0;

        // Decode one logical line at a time from the UTF-8 payload. This avoids constructing a second
        // whole-file string and the array created by Split while preserving BAI2's record boundary.
        var content = document.Content.Span;
        for (var lineStart = 0; lineStart < content.Length;)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = content[lineStart..];
            var newlineOffset = remaining.IndexOf((byte)'\n');
            var lineLength = newlineOffset >= 0 ? newlineOffset : remaining.Length;
            var rawLine = Encoding.UTF8.GetString(remaining[..lineLength]);
            lineStart += lineLength + (newlineOffset >= 0 ? 1 : 0);
            var line = rawLine.Trim().TrimEnd('/').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            sourceRecordCount++;
            if (sourceRecordCount > StatementConnectorLimits.MaxRecords)
            {
                issues.Add(StatementParseIssue.Error(
                    "STATEMENT_RECORD_LIMIT_EXCEEDED",
                    $"Statement exceeds the {StatementConnectorLimits.MaxRecords}-record import limit."));
                return Task.FromResult(EmptyResult(document, issues));
            }

            var fields = line.Split(',');
            switch (fields[0])
            {
                case "02":
                    groupCount++;
                    asOfDate = ParseBaiDate(FieldAt(fields, 4)) ?? asOfDate;
                    groupCurrency = NormalizeCurrency(FieldAt(fields, 6), groupCurrency);
                    accountCurrency = groupCurrency;
                    break;

                case "03":
                    accountCount++;
                    inAccountSection = true;
                    var accountId = FieldAt(fields, 1);
                    // A blank 03 account number cannot identify the account being reconciled. Flag it so
                    // the file is rejected below rather than sharing an "unknown-account" placeholder: two
                    // blank sections would otherwise collapse to one distinct account and slip past the
                    // multi-account guard, letting one section reconcile against the selected account.
                    if (string.IsNullOrWhiteSpace(accountId))
                    {
                        hasBlankAccountId = true;
                    }

                    account = string.IsNullOrWhiteSpace(accountId) ? null : accountId.Trim();
                    accountCurrency = NormalizeCurrency(FieldAt(fields, 2), groupCurrency);
                    if (account is { } identifiedAccount &&
                        TryResolveClosingBalance(fields, out var balanceMinorUnits) &&
                        asOfDate is { } balanceDate)
                    {
                        rowNumber++;
                        records.Add(new StatementCanonicalRecord(
                            StatementRecordKind.CashBalance,
                            identifiedAccount,
                            Symbol: string.Empty,
                            Quantity: 0m,
                            Price: 0m,
                            CashAmount: ToMajorUnits(balanceMinorUnits, accountCurrency),
                            ActivityType: "cashbalance",
                            TradeDate: balanceDate,
                            SettlementDate: null,
                            Currency: accountCurrency,
                            FeesCommission: null,
                            ExternalTransactionId: null));
                    }

                    break;

                case "16":
                    // A transaction must belong to an identified account section (03..49). One appearing
                    // outside a section would be emitted under the initial "unknown-account" and normalized
                    // to the run's account, so flag it for rejection rather than reconcile it.
                    if (!inAccountSection)
                    {
                        transactionOutsideAccount = true;
                        break;
                    }

                    // Do not construct canonical rows under a shared placeholder account. The parse
                    // result is rejected below, but keeping malformed sections out of records also
                    // prevents a future validation-path change from accidentally exposing them.
                    if (account is not { } identifiedTransactionAccount)
                    {
                        hasBlankAccountId = true;
                        break;
                    }

                    if (asOfDate is not { } transactionDate)
                    {
                        issues.Add(StatementParseIssue.Warning("BAI2_NO_ASOF_DATE", "Transaction detail appeared before a group as-of date; skipped.", rowNumber + 1));
                        break;
                    }

                    var typeCode = FieldAt(fields, 1);
                    if (!TryParseMinorUnits(FieldAt(fields, 2), out var amountMinorUnits))
                    {
                        issues.Add(StatementParseIssue.Warning("BAI2_BAD_AMOUNT", "Transaction detail has no parseable amount; skipped.", rowNumber + 1));
                        break;
                    }

                    rowNumber++;
                    var signedAmount = SignByTypeCode(ToMajorUnits(amountMinorUnits, accountCurrency), typeCode);
                    records.Add(new StatementCanonicalRecord(
                        StatementRecordKind.Transaction,
                        identifiedTransactionAccount,
                        Symbol: string.Empty,
                        Quantity: 0m,
                        Price: 0m,
                        CashAmount: signedAmount,
                        ActivityType: "transaction",
                        TradeDate: transactionDate,
                        SettlementDate: null,
                        Currency: accountCurrency,
                        FeesCommission: null,
                        ExternalTransactionId: ResolveReference(fields)));
                    break;

                case "49":
                    accountTrailerCount++;
                    inAccountSection = false;
                    break;

                case "98":
                    groupTrailerCount++;
                    break;

                case "99":
                    fileTrailerCount++;
                    // 99,<file control total>,<number of groups>,<number of records>
                    if (int.TryParse(FieldAt(fields, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileGroups))
                    {
                        declaredFileGroupCount = fileGroups;
                    }

                    break;
            }
        }

        var detectedColumns = new[] { "03/015", "16/TypeCode", "16/Amount", "16/CustomerRef" };
        var fingerprint = new StatementFormatFingerprint(
            Sha256Digest.Compute(document.Content.Span),
            detectedColumns.Select(static column => column.ToLowerInvariant()).ToArray(),
            "bai2");

        // Every transaction must belong to an account section. A 16 record outside one (no preceding 03,
        // or after the 49 that closed the section) would be emitted under the "unknown-account" placeholder
        // and normalized to the run's account, so reject the file rather than reconcile an unidentifiable
        // statement against the selected Meridian account.
        if (transactionOutsideAccount)
        {
            issues.Add(StatementParseIssue.Error(
                "BAI2_TRANSACTION_WITHOUT_ACCOUNT",
                "A BAI2 transaction (16) record appears outside an account section (no preceding 03 account identifier); every transaction must belong to an identified account. Repair the file before importing."));
            return Task.FromResult(new StatementParseResult(
                ConnectorId,
                ProfileId: null,
                detectedColumns,
                ColumnMappings: [],
                [],
                issues,
                fingerprint));
        }

        // A structurally complete file with a group but no account section cannot be associated with
        // the account selected for the run. Do not accept it merely because it has no 16 records: it
        // is still an unidentifiable statement, and accepting it would report a misleading successful
        // import with no usable account evidence.
        if (accountCount == 0)
        {
            issues.Add(StatementParseIssue.Error(
                "BAI2_MISSING_ACCOUNT_SECTION",
                "The BAI2 file has no 03 account identifier record; a statement run must contain at least one identified account section. Repair the file before importing."));
            return Task.FromResult(new StatementParseResult(
                ConnectorId,
                ProfileId: null,
                detectedColumns,
                ColumnMappings: [],
                [],
                issues,
                fingerprint));
        }

        // Every 03 account-identifier record must carry its account number. A blank one cannot identify
        // the account being reconciled and would share the "unknown-account" placeholder with any other
        // blank section, so reject the file rather than reconcile an unidentifiable account.
        if (hasBlankAccountId)
        {
            issues.Add(StatementParseIssue.Error(
                "BAI2_MISSING_ACCOUNT_ID",
                "A BAI2 account identifier (03) record has no account number; a statement run must reconcile a single, identified account. Repair the file so every 03 record carries its account number before importing."));
            return Task.FromResult(new StatementParseResult(
                ConnectorId,
                ProfileId: null,
                detectedColumns,
                ColumnMappings: [],
                [],
                issues,
                fingerprint));
        }

        // A BAI2 file can carry several 03 account-identifier records, but a statement run reconciles a
        // single account and the matcher normalizes every imported row to the run's one external account.
        // Committing a multi-account file would compare (and coincidentally match) one account's balances
        // and entries against another account's Meridian records, so reject it: the operator must split
        // the file into one document per account before importing.
        var distinctAccounts = records
            .Select(static record => record.Account)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctAccounts.Length > 1)
        {
            issues.Add(StatementParseIssue.Error(
                "BAI2_MULTIPLE_ACCOUNTS",
                $"The BAI2 file contains records for {distinctAccounts.Length} different accounts, but a statement run reconciles a single account. Split the file into one document per account before importing."));
            return Task.FromResult(new StatementParseResult(
                ConnectorId,
                ProfileId: null,
                detectedColumns,
                ColumnMappings: [],
                [],
                issues,
                fingerprint));
        }

        // A BAI2 statement import must carry exactly one 02 group. Multiple groups usually represent
        // distinct statement dates, while no group has no authoritative as-of date or currency. Either
        // case would let rows be normalized into one operator-supplied run without a single statement
        // boundary, so reject the file rather than reconcile a mixed or unscoped population.
        if (groupCount != 1)
        {
            var groupIssue = groupCount > 1 ? "BAI2_MULTIPLE_GROUPS" : "BAI2_INVALID_GROUP_COUNT";
            var groupMessage = groupCount > 1
                ? $"The BAI2 file contains {groupCount} statement groups (02) for one account, but a statement run reconciles a single statement period. Split the file into one document per group before importing."
                : "The BAI2 file contains no statement group (02); a statement run requires exactly one group with an as-of date and currency.";
            issues.Add(StatementParseIssue.Error(groupIssue, groupMessage));
            return Task.FromResult(new StatementParseResult(
                ConnectorId,
                ProfileId: null,
                detectedColumns,
                ColumnMappings: [],
                [],
                issues,
                fingerprint));
        }

        // Validate the file's trailer structure so a truncated or corrupt file (valid 03/16 records but
        // missing or mismatched 49/98/99 trailers) is rejected rather than reconciled as a complete
        // statement. Structural counts are used, not the trailer control totals whose composition varies
        // by bank and would risk false rejections of otherwise valid files.
        var trailerErrors = new List<StatementParseIssue>();
        if (fileTrailerCount == 0)
        {
            trailerErrors.Add(StatementParseIssue.Error(
                "BAI2_MISSING_FILE_TRAILER",
                "The BAI2 file has no 99 file trailer; it may be truncated or incomplete."));
        }

        if (accountTrailerCount != accountCount)
        {
            trailerErrors.Add(StatementParseIssue.Error(
                "BAI2_ACCOUNT_TRAILER_MISMATCH",
                $"The BAI2 file has {accountCount} account identifier(s) (03) but {accountTrailerCount} account trailer(s) (49); it may be truncated or corrupt."));
        }

        if (groupTrailerCount != groupCount)
        {
            trailerErrors.Add(StatementParseIssue.Error(
                "BAI2_GROUP_TRAILER_MISMATCH",
                $"The BAI2 file has {groupCount} group header(s) (02) but {groupTrailerCount} group trailer(s) (98); it may be truncated or corrupt."));
        }

        if (declaredFileGroupCount is { } declaredGroups && declaredGroups != groupCount)
        {
            trailerErrors.Add(StatementParseIssue.Error(
                "BAI2_FILE_TRAILER_GROUP_COUNT",
                $"The BAI2 file trailer declares {declaredGroups} group(s) but {groupCount} were found; the file may be truncated or corrupt."));
        }

        if (trailerErrors.Count > 0)
        {
            issues.AddRange(trailerErrors);
            return Task.FromResult(new StatementParseResult(
                ConnectorId,
                ProfileId: null,
                detectedColumns,
                ColumnMappings: [],
                [],
                issues,
                fingerprint));
        }

        if (records.Count == 0)
        {
            issues.Add(StatementParseIssue.Warning("BAI2_NO_RECORDS", "The BAI2 file produced no closing balances or transaction details."));
        }

        return Task.FromResult(new StatementParseResult(
            ConnectorId,
            ProfileId: null,
            detectedColumns,
            ColumnMappings: [],
            records,
            issues,
            fingerprint));
    }

    private static bool TryResolveClosingBalance(string[] fields, out long minorUnits)
    {
        // The account-identifier record carries repeating (status-code, amount, item-count, funds-type)
        // groups whose funds-type can span a variable number of sub-fields. Rather than assume a fixed
        // stride, scan for the closing-ledger status code and take the following field as the amount,
        // falling back to closing-available.
        if (TryResolveBalanceForStatus(fields, ClosingLedgerStatusCode, out minorUnits))
        {
            return true;
        }

        return TryResolveBalanceForStatus(fields, ClosingAvailableStatusCode, out minorUnits);
    }

    private static bool TryResolveBalanceForStatus(string[] fields, string statusCode, out long minorUnits)
    {
        for (var index = 3; index < fields.Length - 1; index++)
        {
            if (string.Equals(fields[index].Trim(), statusCode, StringComparison.Ordinal)
                && TryParseMinorUnits(fields[index + 1], out minorUnits))
            {
                return true;
            }
        }

        minorUnits = 0;
        return false;
    }

    private static decimal SignByTypeCode(decimal amount, string? typeCode)
    {
        // BAI type-code classes: 100–399 are credits (positive), 400–799 are debits (negative).
        if (int.TryParse(typeCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) && code is >= 400 and <= 799)
        {
            return -Math.Abs(amount);
        }

        return amount;
    }

    private static string? ResolveReference(string[] fields)
    {
        var customerReference = FieldAt(fields, 5);
        if (!string.IsNullOrWhiteSpace(customerReference))
        {
            return customerReference.Trim();
        }

        var bankReference = FieldAt(fields, 4);
        return string.IsNullOrWhiteSpace(bankReference) ? null : bankReference.Trim();
    }

    private static bool TryParseMinorUnits(string? value, out long minorUnits)
        => long.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out minorUnits);

    // BAI2 expresses amounts in the currency's minor units, so the scale to major units follows the
    // declared ISO 4217 currency, not an assumed two decimals: JPY has no minor unit (10000 is 10000
    // yen, not 100), while the Gulf dinars use three. Assuming cents would misstate every balance and
    // transaction for those currencies by one or two orders of magnitude.
    private static decimal ToMajorUnits(long minorUnits, string currency) => currency.Trim().ToUpperInvariant() switch
    {
        "JPY" or "KRW" or "CLP" or "ISK" or "VND" or "XAF" or "XOF" or "XPF"
            or "BIF" or "DJF" or "GNF" or "KMF" or "PYG" or "RWF" or "UGX" or "VUV" => minorUnits,
        "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => minorUnits / 1_000m,
        _ => minorUnits / 100m
    };

    private static DateOnly? ParseBaiDate(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed is null || trimmed.Length != 6
            || !int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        var year = 2000 + int.Parse(trimmed[..2], CultureInfo.InvariantCulture);
        var month = int.Parse(trimmed.Substring(2, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(trimmed.Substring(4, 2), CultureInfo.InvariantCulture);
        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            return null;
        }

        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string NormalizeCurrency(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();

    private static string? FieldAt(string[] fields, int index) => index < fields.Length ? fields[index] : null;

    private static string Sniff(StatementSourceDocument document)
    {
        var span = document.Content.Span;
        return Encoding.UTF8.GetString(span.Length > 256 ? span[..256] : span);
    }

    private static StatementParseResult EmptyResult(
        StatementSourceDocument document,
        IReadOnlyList<StatementParseIssue> issues)
    {
        var detectedColumns = new[] { "03/015", "16/TypeCode", "16/Amount", "16/CustomerRef" };
        return new StatementParseResult(
            ConnectorId,
            ProfileId: null,
            detectedColumns,
            ColumnMappings: [],
            Records: [],
            issues,
            new StatementFormatFingerprint(
                Sha256Digest.Compute(document.Content.Span),
                detectedColumns.Select(static column => column.ToLowerInvariant()).ToArray(),
                "bai2"));
    }
}
