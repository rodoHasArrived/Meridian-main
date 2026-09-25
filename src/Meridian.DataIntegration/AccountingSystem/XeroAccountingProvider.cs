using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.DataIntegration.Credentials;
using Meridian.ProviderSdk.AccountingSystem;
using static Meridian.DataIntegration.AccountingSystem.AccountingProviderJson;

namespace Meridian.DataIntegration.AccountingSystem;

public sealed class XeroAccountingProvider(IProviderCredentialStore store, HttpClient client)
    : CredentialedAccountingProvider(store, client)
{
    public const string Id = "xero";
    public override string ProviderId => Id;
    public override string DisplayName => "Xero";
    public override AccountingSystemProviderCapabilities Capabilities { get; } = new(true, true, true, false,
        ["XeroAccount", "XeroJournal", "XeroTrialBalance"], RequiresCredentials: true);
    protected override string CompanyField => "TenantId";
    protected override string[] ExportControls => ["tracking-category-options", "contact-mapping", "tax-rate-mapping", "bank-account-scope"];
    protected override string ConnectionScope(ProviderCredentialReadResult connection)
        => $"xero:tenant:{Tenant(connection)}";
    protected override Uri TokenEndpoint(ProviderCredentialReadResult connection) => new("https://identity.xero.com/connect/token");

    private static string Tenant(ProviderCredentialReadResult connection)
        => Guid.TryParse(Field(connection, "TenantId"), out var id) && id != Guid.Empty ? id.ToString("D")
            : throw new InvalidOperationException("A valid Xero tenant ID is required.");

    private async Task<JsonDocument> GetAsync(ProviderCredentialReadResult connection, string token, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.xero.com/api.xro/2.0/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("xero-tenant-id", Tenant(connection));
        return await SendAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<string> BaseCurrencyAsync(ProviderCredentialReadResult connection, string token, CancellationToken ct)
    {
        using var document = await GetAsync(connection, token, "Organisation", ct).ConfigureAwait(false);
        var organisations = Rows(document.RootElement, "Organisations");
        if (organisations.Length != 1 || !string.Equals(RequiredText(organisations[0], "OrganisationID"), Tenant(connection), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Xero returned a different organisation.");
        return RequiredText(organisations[0], "BaseCurrency");
    }

    protected override async Task VerifyScopeAsync(ProviderCredentialReadResult connection, string token, CancellationToken ct)
        => _ = await BaseCurrencyAsync(connection, token, ct).ConfigureAwait(false);

    protected override async Task<AccountingSystemImportDetailDto> ReadAsync(ProviderCredentialReadResult connection,
        string token, AccountingSystemImportRequestDto request, CancellationToken ct)
    {
        var currency = await BaseCurrencyAsync(connection, token, ct).ConfigureAwait(false);
        var scope = ConnectionScope(connection);
        using var accountDocument = await GetAsync(connection, token, "Accounts", ct).ConfigureAwait(false);
        var accounts = Rows(accountDocument.RootElement, "Accounts").Select(row => new AccountingSystemChartAccountDto(
            RequiredText(row, "AccountID"), Text(row, "Code"), RequiredText(row, "Name"), RequiredText(row, "Type"),
            currency, RequiredText(row, "Status") == "ACTIVE", EvidenceRef: $"{scope}:account:{RequiredText(row, "AccountID")}")).ToArray();
        var accountLookup = accounts.ToDictionary(a => a.ExternalAccountId, StringComparer.OrdinalIgnoreCase);
        var journals = await JournalsAsync(connection, token, request, currency, accountLookup, ct).ConfigureAwait(false);
        using var report = await GetAsync(connection, token, $"Reports/TrialBalance?date={request.PeriodEnd:yyyy-MM-dd}&paymentsOnly=false", ct).ConfigureAwait(false);
        var balances = TrialBalance(report.RootElement, request.PeriodEnd!.Value, currency, scope, accountLookup);
        return Detail(connection, request, accounts, journals, balances,
            "Xero Journals access requires accounting.journals.read and provider entitlement. Trial balance uses accrual YTD balances in organisation base currency.");
    }

    private async Task<IReadOnlyList<AccountingSystemJournalEntryDto>> JournalsAsync(ProviderCredentialReadResult connection,
        string token, AccountingSystemImportRequestDto request, string currency,
        IReadOnlyDictionary<string, AccountingSystemChartAccountDto> accounts, CancellationToken ct)
    {
        var result = new List<AccountingSystemJournalEntryDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long offset = 0;
        // Journal dates need not follow journal numbers (backdated entries); scan every page.
        for (var page = 0; page < 1000; page++)
        {
            using var document = await GetAsync(connection, token, $"Journals?offset={offset}&paymentsOnly=false", ct).ConfigureAwait(false);
            var rows = Rows(document.RootElement, "Journals");
            if (rows.Length == 0)
                return result;
            var nextOffset = offset;
            foreach (var row in rows)
            {
                var id = RequiredText(row, "JournalID");
                if (!seen.Add(id) || !long.TryParse(RequiredText(row, "JournalNumber"), CultureInfo.InvariantCulture, out var number) || number <= offset)
                    throw new InvalidOperationException("Xero journal pagination repeated or regressed.");
                nextOffset = Math.Max(number, nextOffset);
                var date = Date(RequiredText(row, "JournalDate"));
                if (date < request.PeriodStart || date > request.PeriodEnd)
                    continue;
                var evidence = $"{ConnectionScope(connection)}:journal:{id}";
                var lines = Rows(row, "JournalLines").Select(line =>
                {
                    var accountId = RequiredText(line, "AccountID");
                    if (!accounts.TryGetValue(accountId, out var account))
                        throw new InvalidOperationException("Xero journal references an unknown account.");
                    var amount = Amount(RequiredText(line, "NetAmount"));
                    var lineId = RequiredText(line, "JournalLineID");
                    return new AccountingSystemJournalLineDto(lineId, accountId, account.AccountCode, Text(line, "Description"),
                        Math.Max(amount, 0), Math.Max(-amount, 0), currency, $"{evidence}:line:{lineId}");
                }).ToArray();
                if (lines.Length == 0 || lines.Select(l => l.ExternalLineId).Distinct().Count() != lines.Length)
                    throw new InvalidOperationException("Xero journal lines are missing or duplicated.");
                result.Add(new(id, date, Text(row, "Reference"), currency, lines.Sum(l => l.Debit), lines.Sum(l => l.Credit), lines, evidence));
            }
            offset = nextOffset;
        }
        throw new InvalidOperationException("Xero pagination limit reached; incomplete evidence cannot be retained.");
    }

    private static IReadOnlyList<AccountingSystemTrialBalanceLineDto> TrialBalance(JsonElement root, DateOnly asOf,
        string currency, string scope, IReadOnlyDictionary<string, AccountingSystemChartAccountDto> accounts)
    {
        var reports = Rows(root, "Reports");
        if (reports.Length != 1 || RequiredText(reports[0], "ReportType") != "TrialBalance")
            throw new InvalidOperationException("Xero trial balance report is missing.");
        var rows = Flatten(Rows(reports[0], "Rows")).ToArray();
        var header = rows.Single(r => Text(r, "RowType") == "Header");
        var labels = Rows(header, "Cells").Select(c => Text(c, "Value")).ToArray();
        var debitIndex = Array.IndexOf(labels, "YTD Debit");
        var creditIndex = Array.IndexOf(labels, "YTD Credit");
        if (debitIndex < 0 || creditIndex < 0)
            throw new InvalidOperationException("Xero trial balance YTD columns are missing.");
        var result = new List<AccountingSystemTrialBalanceLineDto>();
        foreach (var row in rows.Where(r => Text(r, "RowType") == "Row"))
        {
            var cells = Rows(row, "Cells");
            if (cells.Length != labels.Length)
                throw new InvalidOperationException("Xero trial balance columns are incomplete.");
            var accountId = Rows(cells[0], "Attributes").Where(a => Text(a, "Id") == "account")
                .Select(a => RequiredText(a, "Value")).Single();
            if (!accounts.TryGetValue(accountId, out var account))
                throw new InvalidOperationException("Xero trial balance references an unknown account.");
            result.Add(new(accountId, account.AccountCode, account.DisplayName, account.AccountType,
                Amount(Text(cells[debitIndex], "Value"), true), Amount(Text(cells[creditIndex], "Value"), true),
                currency, asOf, $"{scope}:trial-balance:{asOf:yyyy-MM-dd}:{accountId}"));
        }
        return result;
    }

    private static IEnumerable<JsonElement> Flatten(IEnumerable<JsonElement> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            if (row.TryGetProperty("Rows", out var children))
                foreach (var child in Flatten(children.EnumerateArray()))
                    yield return child;
        }
    }
}
