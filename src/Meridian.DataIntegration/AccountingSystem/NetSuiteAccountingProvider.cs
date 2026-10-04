using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.DataIntegration.Credentials;
using Meridian.ProviderSdk.AccountingSystem;
using static Meridian.DataIntegration.AccountingSystem.AccountingProviderJson;

namespace Meridian.DataIntegration.AccountingSystem;

/// <summary>Accrual accounting-line evidence for one NetSuite subsidiary and accounting book.</summary>
public sealed class NetSuiteAccountingProvider(IProviderCredentialStore store, HttpClient client)
    : CredentialedAccountingProvider(store, client)
{
    public const string Id = "netsuite";
    public override string ProviderId => Id;
    public override string DisplayName => "NetSuite";
    public override AccountingSystemProviderCapabilities Capabilities { get; } = new(true, true, true, false,
        ["NetSuiteAccount", "NetSuiteJournalEntry", "NetSuiteTrialBalance"], RequiresCredentials: true);
    protected override string CompanyField => "AccountId";
    protected override string[] ExportControls => ["subsidiary-scope", "classification-segments", "entity-mapping", "intercompany-controls"];
    protected override string ConnectionScope(ProviderCredentialReadResult connection)
        => $"netsuite:account:{Account(connection)}:subsidiary:{NumericField(connection, "SubsidiaryId")}:book:{NumericField(connection, "AccountingBookId")}:basis:date-accrual-calendar-year";
    protected override Uri TokenEndpoint(ProviderCredentialReadResult connection)
        => new($"{BaseUrl(connection)}/services/rest/auth/oauth2/v1/token");

    private static string Account(ProviderCredentialReadResult connection)
    {
        var account = Field(connection, "AccountId").ToLowerInvariant().Replace('_', '-');
        if (account.Length > 63 || !char.IsAsciiLetterOrDigit(account[0]) || !char.IsAsciiLetterOrDigit(account[^1]) ||
            account.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new InvalidOperationException("NetSuite account ID is invalid.");
        return account;
    }

    private static string NumericField(ProviderCredentialReadResult connection, string field)
        => long.TryParse(Field(connection, field), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id.ToString(CultureInfo.InvariantCulture) : throw new InvalidOperationException("NetSuite subsidiary and book IDs must be positive integers.");

    private static string BaseUrl(ProviderCredentialReadResult connection) => $"https://{Account(connection)}.suitetalk.api.netsuite.com";

    private async Task<List<JsonElement>> QueryAsync(ProviderCredentialReadResult connection, string token, string query, CancellationToken ct)
    {
        const int limit = 1000;
        var result = new List<JsonElement>();
        for (var offset = 0; offset < 100_000; offset += limit)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{BaseUrl(connection)}/services/rest/query/v1/suiteql?limit={limit}&offset={offset}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Prefer", "transient");
            request.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, string> { ["q"] = query }), Encoding.UTF8, "application/json");
            using var document = await SendAsync(request, ct).ConfigureAwait(false);
            var root = document.RootElement;
            var items = Rows(root, "items");
            if (root.GetProperty("offset").GetInt32() != offset || root.GetProperty("count").GetInt32() != items.Length || items.Length > limit)
                throw new InvalidOperationException("NetSuite returned inconsistent pagination.");
            result.AddRange(items.Select(item => item.Clone()));
            if (!root.GetProperty("hasMore").GetBoolean())
                return result;
            if (items.Length != limit)
                throw new InvalidOperationException("NetSuite returned an incomplete page.");
        }
        throw new InvalidOperationException("NetSuite SuiteQL row limit reached; incomplete evidence cannot be retained.");
    }

    private async Task<(string Currency, IReadOnlyList<string> Ancestors)> ScopeAsync(ProviderCredentialReadResult connection, string token, CancellationToken ct)
    {
        var subsidiaries = await QueryAsync(connection, token,
            $"SELECT s.id, s.parent, c.symbol AS currency FROM subsidiary s INNER JOIN currency c ON c.id = s.currency WHERE s.id = {NumericField(connection, "SubsidiaryId")} ORDER BY s.id", ct).ConfigureAwait(false);
        if (subsidiaries.Count != 1 || RequiredText(subsidiaries[0], "id") != NumericField(connection, "SubsidiaryId"))
            throw new InvalidOperationException("NetSuite subsidiary scope is unavailable.");
        var books = await QueryAsync(connection, token,
            $"SELECT id FROM accountingbook WHERE id = {NumericField(connection, "AccountingBookId")} AND isprimary = 'T' ORDER BY id", ct).ConfigureAwait(false);
        if (books.Count != 1 || RequiredText(books[0], "id") != NumericField(connection, "AccountingBookId"))
            throw new InvalidOperationException("Only the subsidiary primary accounting book is supported.");
        var ancestors = new List<string>();
        var parent = Text(subsidiaries[0], "parent");
        while (!string.IsNullOrWhiteSpace(parent))
        {
            if (!long.TryParse(parent, NumberStyles.None, CultureInfo.InvariantCulture, out var parentId) || parentId <= 0 ||
                parent == NumericField(connection, "SubsidiaryId") || ancestors.Contains(parent) || ancestors.Count >= 100)
                throw new InvalidOperationException("NetSuite subsidiary hierarchy is invalid.");
            ancestors.Add(parent);
            var parents = await QueryAsync(connection, token,
                $"SELECT id, parent FROM subsidiary WHERE id = {parentId} ORDER BY id", ct).ConfigureAwait(false);
            if (parents.Count != 1 || RequiredText(parents[0], "id") != parent)
                throw new InvalidOperationException("NetSuite subsidiary hierarchy is unavailable.");
            parent = Text(parents[0], "parent");
        }
        return (RequiredText(subsidiaries[0], "currency"), ancestors);
    }

    protected override async Task VerifyScopeAsync(ProviderCredentialReadResult connection, string token, CancellationToken ct)
        => _ = await ScopeAsync(connection, token, ct).ConfigureAwait(false);

    protected override async Task<AccountingSystemImportDetailDto> ReadAsync(ProviderCredentialReadResult connection,
        string token, AccountingSystemImportRequestDto request, CancellationToken ct)
    {
        var (currency, ancestors) = await ScopeAsync(connection, token, ct).ConfigureAwait(false);
        var scope = ConnectionScope(connection);
        static string AssignedTo(string id) => $"BUILTIN.MNFILTER(subsidiary, 'MN_INCLUDE', '', 'TRUE', '{id}') = 'T'";
        var accountScope = AssignedTo(NumericField(connection, "SubsidiaryId"));
        if (ancestors.Count > 0)
            accountScope += $" OR (includechildren = 'T' AND ({string.Join(" OR ", ancestors.Select(AssignedTo))}))";
        var accountRows = await QueryAsync(connection, token,
            $"SELECT id, acctnumber, acctname, accttype, isinactive, parent, NVL(sspecacct, 'NONE') AS specialaccounttype FROM account WHERE ({accountScope}) ORDER BY id", ct).ConfigureAwait(false);
        var accounts = accountRows.Select(row => new AccountingSystemChartAccountDto(RequiredText(row, "id"),
            string.IsNullOrWhiteSpace(Text(row, "acctnumber")) ? $"netsuite-account:{RequiredText(row, "id")}" : Text(row, "acctnumber").Trim(), RequiredText(row, "acctname"), RequiredText(row, "accttype"), currency,
            RequiredText(row, "isinactive") == "F", Text(row, "parent"), $"{scope}:account:{RequiredText(row, "id")}")).ToArray();
        var lookup = accounts.ToDictionary(a => a.ExternalAccountId, StringComparer.Ordinal);
        // The system account can be renamed or renumbered. Never infer its identity from a label.
        var retainedEarningsIds = accountRows.Where(row => RequiredText(row, "specialaccounttype") == "RetEarnings")
            .Select(row => RequiredText(row, "id")).ToArray();
        if (retainedEarningsIds.Length != 1 || !lookup[retainedEarningsIds[0]].IsActive ||
            !string.Equals(lookup[retainedEarningsIds[0]].AccountType, "Equity", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("NetSuite must expose exactly one active system retained-earnings equity account.");
        var joins = "FROM transaction t INNER JOIN transactionaccountingline al ON al.transaction = t.id " +
            "INNER JOIN transactionline tl ON tl.transaction = al.transaction AND tl.id = al.transactionline ";
        var filters = $"WHERE t.posting = 'T' AND t.type <> 'PEJrnl' AND al.posting = 'T' AND al.account IS NOT NULL AND tl.subsidiary = {NumericField(connection, "SubsidiaryId")} " +
            $"AND al.accountingbook = {NumericField(connection, "AccountingBookId")} AND t.trandate <= TO_DATE('{request.PeriodEnd:yyyy-MM-dd}', 'YYYY-MM-DD') ";
        var journalRows = await QueryAsync(connection, token,
            "SELECT t.id AS journalid, TO_CHAR(t.trandate, 'YYYY-MM-DD') AS accountingdate, t.memo, al.transactionline AS lineid, al.account AS accountid, NVL(al.debit, 0) AS debit, NVL(al.credit, 0) AS credit " +
            joins + filters + $"AND t.trandate >= TO_DATE('{request.PeriodStart:yyyy-MM-dd}', 'YYYY-MM-DD') ORDER BY t.id, al.transactionline, al.account", ct).ConfigureAwait(false);
        var journals = journalRows.GroupBy(row => RequiredText(row, "journalid")).Select(group =>
        {
            var evidence = $"{scope}:journal:{group.Key}";
            var lines = group.Select(row =>
            {
                var accountId = RequiredText(row, "accountid");
                var account = lookup[accountId];
                var lineId = $"{RequiredText(row, "lineid")}:{accountId}";
                var debit = Amount(RequiredText(row, "debit"));
                var credit = Amount(RequiredText(row, "credit"));
                if (debit < 0 || credit < 0 || (debit != 0 && credit != 0))
                    throw new InvalidOperationException("NetSuite returned invalid accounting-line amounts.");
                return new AccountingSystemJournalLineDto(lineId, accountId, account.AccountCode, Text(row, "memo"), debit, credit, currency, $"{evidence}:line:{lineId}");
            }).ToArray();
            if (lines.Select(l => l.ExternalLineId).Distinct().Count() != lines.Length || group.Select(r => RequiredText(r, "accountingdate")).Distinct().Count() != 1)
                throw new InvalidOperationException("NetSuite journal identities or dates are inconsistent.");
            var date = Date(RequiredText(group.First(), "accountingdate"));
            if (date < request.PeriodStart || date > request.PeriodEnd)
                throw new InvalidOperationException("NetSuite returned an out-of-period journal.");
            return new AccountingSystemJournalEntryDto(group.Key, date, Text(group.First(), "memo"), currency,
                lines.Sum(l => l.Debit), lines.Sum(l => l.Credit), lines, evidence);
        }).ToArray();
        // Standard date-based Trial Balance uses the calendar year, independently of the
        // requested journal start and subsidiary fiscal calendar. Period-end journals belong
        // to the separate post-closing report and are excluded from both read populations.
        var calendarYearStart = new DateOnly(request.PeriodEnd!.Value.Year, 1, 1);
        var balanceRows = await QueryAsync(connection, token,
            "SELECT al.account AS accountid, SUM(NVL(al.debit, 0) - NVL(al.credit, 0)) AS balance, " +
            $"SUM(CASE WHEN t.trandate < TO_DATE('{calendarYearStart:yyyy-MM-dd}', 'YYYY-MM-DD') " +
            "THEN NVL(al.debit, 0) - NVL(al.credit, 0) ELSE 0 END) AS prioryearbalance " +
            joins + filters + "GROUP BY al.account ORDER BY al.account", ct).ConfigureAwait(false);
        var amounts = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var priorIncome = 0m;
        foreach (var row in balanceRows)
        {
            var account = lookup[RequiredText(row, "accountid")];
            var amount = Amount(RequiredText(row, "balance"));
            var priorYear = Amount(RequiredText(row, "prioryearbalance"));
            if (IsIncomeStatement(account))
            {
                amount -= priorYear;
                priorIncome += priorYear;
            }
            // Add rejects duplicate identities even if the duplicate amounts would offset.
            amounts.Add(account.ExternalAccountId, amount);
        }
        var retainedId = retainedEarningsIds[0];
        if (priorIncome != 0m)
            amounts[retainedId] = amounts.GetValueOrDefault(retainedId) + priorIncome;
        var balances = amounts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
        {
            var account = lookup[pair.Key];
            var amount = pair.Value;
            return new AccountingSystemTrialBalanceLineDto(account.ExternalAccountId, account.AccountCode, account.DisplayName,
                account.AccountType, Math.Max(amount, 0), Math.Max(-amount, 0), currency, request.PeriodEnd!.Value,
                $"{scope}:trial-balance:date-accrual-calendar-year:{request.PeriodEnd:yyyy-MM-dd}:{account.ExternalAccountId}:retained-earnings:{retainedId}");
        }).ToArray();
        var detail = Detail(connection, request, accounts, journals, balances,
            "NetSuite standard date-based accrual trial balance uses calendar-year income and expense balances with prior-year net income in system retained earnings; period-end journals are excluded.",
            "NetSuite evidence is limited to the selected subsidiary and primary accounting book in subsidiary base currency; consolidated, secondary-book, period-based and post-closing reporting are not supported.");
        return detail with
        {
            Summary = detail.Summary with
            {
                TrialBalanceBasis = new(new(request.PeriodEnd!.Value.Year, 1, 1),
            accounts.Where(IsIncomeStatement).Select(a => a.AccountCode).ToArray(), lookup[retainedId].AccountCode)
            }
        };
    }

    private static bool IsIncomeStatement(AccountingSystemChartAccountDto account)
        => account.AccountType.ToUpperInvariant() is "INCOME" or "EXPENSE" or "OTHINCOME" or "OTHEXPENSE" or "COGS";
}
