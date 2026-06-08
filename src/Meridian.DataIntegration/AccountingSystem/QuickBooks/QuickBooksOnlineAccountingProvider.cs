using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.ProviderSdk.AccountingSystem;

namespace Meridian.DataIntegration.AccountingSystem.QuickBooks;

public sealed class QuickBooksOnlineAccountingProvider :
    IAccountingSystemProvider,
    IAccountingSystemConnectionMetadataProvider,
    IAccountingSystemConnectionVerifier
{
    public const string Id = "quickbooks";

    private readonly IQuickBooksOnlineConnectionStore _connectionStore;
    private readonly IQuickBooksOnlineClient _client;

    public QuickBooksOnlineAccountingProvider(
        IQuickBooksOnlineConnectionStore connectionStore,
        IQuickBooksOnlineClient client)
    {
        _connectionStore = connectionStore ?? throw new ArgumentNullException(nameof(connectionStore));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public string ProviderId => Id;

    public string DisplayName => "QuickBooks Online";

    public AccountingSystemProviderCapabilities Capabilities { get; } = new(
        SupportsChartOfAccounts: true,
        SupportsJournalEntries: true,
        SupportsTrialBalance: true,
        SupportsPosting: false,
        EvidenceKinds:
        [
            "QuickBooksAccount",
            "QuickBooksJournalEntry",
            "QuickBooksTrialBalance"
        ],
        RequiresCredentials: true);

    public Task<AccountingSystemConnectionMetadataDto> GetConnectionMetadataAsync(CancellationToken ct = default)
        => _connectionStore.GetMetadataAsync(ct);

    public async Task<AccountingSystemConnectionVerificationResult> VerifyConnectionAsync(CancellationToken ct = default)
    {
        var connection = await RequireConnectionAsync(ct).ConfigureAwait(false);

        try
        {
            var token = await _client.RefreshAccessTokenAsync(connection, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                await _connectionStore.SaveRefreshTokenAsync(token.RefreshToken, ct).ConfigureAwait(false);
            }

            var verifiedAt = DateTimeOffset.UtcNow;
            await _connectionStore.RecordConnectionAsync(
                success: true,
                externalCompanyId: connection.RealmId,
                error: null,
                occurredAtUtc: verifiedAt,
                ct).ConfigureAwait(false);

            return new AccountingSystemConnectionVerificationResult(
                Success: true,
                ExternalCompanyId: connection.RealmId,
                LastError: null,
                VerifiedAtUtc: verifiedAt,
                Warnings: BuildReadOnlyWarnings(connection, token.Warnings));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var verifiedAt = DateTimeOffset.UtcNow;
            var message = $"QuickBooks Online token exchange failed: {ex.Message}";
            await _connectionStore.RecordConnectionAsync(
                success: false,
                externalCompanyId: connection.RealmId,
                error: message,
                occurredAtUtc: verifiedAt,
                ct).ConfigureAwait(false);

            return new AccountingSystemConnectionVerificationResult(
                Success: false,
                ExternalCompanyId: connection.RealmId,
                LastError: message,
                VerifiedAtUtc: verifiedAt,
                Warnings: ["QuickBooks Online remains unavailable until OAuth credentials and the selected company realm are repaired."]);
        }
    }

    public async Task<AccountingSystemImportDetailDto> ImportAsync(
        AccountingSystemImportRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        request ??= new AccountingSystemImportRequestDto();
        var connection = await RequireConnectionAsync(ct).ConfigureAwait(false);

        try
        {
            var token = await _client.RefreshAccessTokenAsync(connection, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                await _connectionStore.SaveRefreshTokenAsync(token.RefreshToken, ct).ConfigureAwait(false);
            }

            var evidence = await _client.ReadCompanyEvidenceAsync(connection, token.AccessToken, request, ct)
                .ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            await _connectionStore.RecordConnectionAsync(
                success: true,
                externalCompanyId: connection.RealmId,
                error: null,
                occurredAtUtc: now,
                ct).ConfigureAwait(false);

            var periodStart = request.PeriodStart ?? DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-30));
            var periodEnd = request.PeriodEnd ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var fundProfileId = string.IsNullOrWhiteSpace(request.FundProfileId)
                ? "default-fund"
                : request.FundProfileId.Trim();
            var importId = $"qbo-{SanitizeId(connection.RealmId)}-{periodEnd:yyyyMMdd}";
            var displayName = string.IsNullOrWhiteSpace(connection.CompanyName)
                ? DisplayName
                : $"{DisplayName} - {connection.CompanyName}";

            var summary = new AccountingSystemImportSummaryDto(
                importId,
                ProviderId,
                displayName,
                fundProfileId,
                request.LedgerBookId,
                request.PersistPreview ? AccountingSystemImportStateDto.Imported : AccountingSystemImportStateDto.Previewed,
                periodStart,
                periodEnd,
                now,
                evidence.ChartAccounts.Count,
                evidence.JournalEntries.Count,
                evidence.TrialBalance.Count,
                evidence.EvidenceReferences,
                BuildReadOnlyWarnings(connection, token.Warnings.Concat(evidence.Warnings)));

            return new AccountingSystemImportDetailDto(
                summary,
                evidence.ChartAccounts,
                evidence.JournalEntries,
                evidence.TrialBalance);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var occurredAt = DateTimeOffset.UtcNow;
            var message = $"QuickBooks Online read-only import failed: {ex.Message}";
            await _connectionStore.RecordConnectionAsync(
                success: false,
                externalCompanyId: connection.RealmId,
                error: message,
                occurredAtUtc: occurredAt,
                ct).ConfigureAwait(false);
            throw new InvalidOperationException(message, ex);
        }
    }

    private async Task<QuickBooksOnlineConnection> RequireConnectionAsync(CancellationToken ct)
    {
        var connection = await _connectionStore.ReadAsync(ct).ConfigureAwait(false);
        if (connection is not null)
        {
            return connection;
        }

        var metadata = await _connectionStore.GetMetadataAsync(ct).ConfigureAwait(false);
        var missing = metadata.MissingFields.Count == 0
            ? "local QuickBooks OAuth credentials"
            : string.Join(", ", metadata.MissingFields);
        throw new InvalidOperationException($"QuickBooks Online local config is incomplete. Missing: {missing}.");
    }

    private static IReadOnlyList<string> BuildReadOnlyWarnings(
        QuickBooksOnlineConnection connection,
        IEnumerable<string> warnings)
    {
        var rows = new List<string>
        {
            "QuickBooks Online import is read-only; Meridian does not post, update, or export ledger entries through this adapter.",
            "Meridian remains the source of all ledger truth; external GL rows are retained as evidence and reconciliation inputs."
        };

        if (string.Equals(connection.Environment, "production", StringComparison.OrdinalIgnoreCase))
        {
            rows.Add("Production QuickBooks Online company selected. Mutating accounting actions remain disabled.");
        }

        rows.AddRange(warnings.Where(static warning => !string.IsNullOrWhiteSpace(warning)).Select(static warning => warning.Trim()));
        return rows.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string SanitizeId(string value)
        => string.Concat(value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')).Trim('-');
}

public interface IQuickBooksOnlineConnectionStore
{
    Task<QuickBooksOnlineConnection?> ReadAsync(CancellationToken ct = default);

    Task<AccountingSystemConnectionMetadataDto> GetMetadataAsync(CancellationToken ct = default);

    Task SaveRefreshTokenAsync(string refreshToken, CancellationToken ct = default);

    Task RecordConnectionAsync(
        bool success,
        string? externalCompanyId,
        string? error,
        DateTimeOffset? occurredAtUtc = null,
        CancellationToken ct = default);
}

public sealed record QuickBooksOnlineConnection(
    string ClientId,
    string ClientSecret,
    string RefreshToken,
    string RealmId,
    string Environment,
    string? CompanyName);

public interface IQuickBooksOnlineClient
{
    Task<QuickBooksOnlineTokenExchangeResult> ExchangeAuthorizationCodeAsync(
        QuickBooksOnlineAuthorizationCodeRequest request,
        CancellationToken ct = default);

    Task<QuickBooksOnlineTokenExchangeResult> RefreshAccessTokenAsync(
        QuickBooksOnlineConnection connection,
        CancellationToken ct = default);

    Task<QuickBooksOnlineCompanyEvidence> ReadCompanyEvidenceAsync(
        QuickBooksOnlineConnection connection,
        string accessToken,
        AccountingSystemImportRequestDto request,
        CancellationToken ct = default);
}

public sealed record QuickBooksOnlineAuthorizationCodeRequest(
    string ClientId,
    string ClientSecret,
    string Code,
    string RedirectUri);

public sealed record QuickBooksOnlineTokenExchangeResult(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<string> Warnings);

public sealed record QuickBooksOnlineCompanyEvidence(
    IReadOnlyList<AccountingSystemChartAccountDto> ChartAccounts,
    IReadOnlyList<AccountingSystemJournalEntryDto> JournalEntries,
    IReadOnlyList<AccountingSystemTrialBalanceLineDto> TrialBalance,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> Warnings);

public sealed class QuickBooksOnlineHttpClient : IQuickBooksOnlineClient
{
    private const string TokenEndpoint = "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer";
    private const string ProductionAccountingBaseUrl = "https://quickbooks.api.intuit.com";
    private const string SandboxAccountingBaseUrl = "https://sandbox-quickbooks.api.intuit.com";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true
    };

    private readonly HttpClient _httpClient;

    public QuickBooksOnlineHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<QuickBooksOnlineTokenExchangeResult> RefreshAccessTokenAsync(
        QuickBooksOnlineConnection connection,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ct.ThrowIfCancellationRequested();

        return await SendTokenRequestAsync(
            connection.ClientId,
            connection.ClientSecret,
            [
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", connection.RefreshToken)
            ],
            ct).ConfigureAwait(false);
    }

    public async Task<QuickBooksOnlineTokenExchangeResult> ExchangeAuthorizationCodeAsync(
        QuickBooksOnlineAuthorizationCodeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RedirectUri);
        ct.ThrowIfCancellationRequested();

        return await SendTokenRequestAsync(
            request.ClientId,
            request.ClientSecret,
            [
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", request.Code),
                new KeyValuePair<string, string>("redirect_uri", request.RedirectUri)
            ],
            ct).ConfigureAwait(false);
    }

    private async Task<QuickBooksOnlineTokenExchangeResult> SendTokenRequestAsync(
        string clientId,
        string clientSecret,
        IReadOnlyList<KeyValuePair<string, string>> fields,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
        var credentialBytes = Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentialBytes));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("x-include-refresh-token-hard-expires-in", "true");
        request.Content = new FormUrlEncodedContent(fields);

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OAuth token exchange returned status {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, DocumentOptions, ct).ConfigureAwait(false);
        var accessToken = ReadString(document.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("OAuth token exchange response did not include an access token.");
        }

        var expiresIn = ReadInt(document.RootElement, "expires_in") ?? 3600;
        var warnings = new List<string>();
        var refreshToken = ReadString(document.RootElement, "refresh_token");
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            warnings.Add("QuickBooks token refresh response did not include a rotated refresh token; existing token was retained.");
        }

        return new QuickBooksOnlineTokenExchangeResult(
            accessToken.Trim(),
            string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken.Trim(),
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn)),
            warnings);
    }

    public async Task<QuickBooksOnlineCompanyEvidence> ReadCompanyEvidenceAsync(
        QuickBooksOnlineConnection connection,
        string accessToken,
        AccountingSystemImportRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        request ??= new AccountingSystemImportRequestDto();

        var periodStart = request.PeriodStart ?? DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-30));
        var periodEnd = request.PeriodEnd ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var accounts = await ReadAccountsAsync(connection, accessToken, ct).ConfigureAwait(false);
        var accountLookup = accounts.ToDictionary(static row => row.ExternalAccountId, StringComparer.OrdinalIgnoreCase);
        var journalEntries = await ReadJournalEntriesAsync(connection, accessToken, accountLookup, periodStart, periodEnd, ct).ConfigureAwait(false);
        var trialBalance = await ReadTrialBalanceAsync(connection, accessToken, accountLookup, periodStart, periodEnd, ct).ConfigureAwait(false);

        var evidenceRefs = new[]
        {
            $"quickbooks:company:{connection.RealmId}:chart-of-accounts",
            $"quickbooks:company:{connection.RealmId}:journal",
            $"quickbooks:company:{connection.RealmId}:trial-balance"
        };

        return new QuickBooksOnlineCompanyEvidence(
            accounts,
            journalEntries,
            trialBalance,
            evidenceRefs,
            Warnings: []);
    }

    private async Task<IReadOnlyList<AccountingSystemChartAccountDto>> ReadAccountsAsync(
        QuickBooksOnlineConnection connection,
        string accessToken,
        CancellationToken ct)
    {
        using var document = await GetJsonAsync(
            BuildQueryUri(connection, "select * from Account"),
            accessToken,
            ct).ConfigureAwait(false);

        if (!TryGetProperty(document.RootElement, "QueryResponse", out var queryResponse) ||
            !TryGetProperty(queryResponse, "Account", out var accountsElement) ||
            accountsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<AccountingSystemChartAccountDto>();
        foreach (var account in accountsElement.EnumerateArray())
        {
            var id = ReadString(account, "Id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var accountCode = FirstNonBlank(
                ReadString(account, "AcctNum"),
                ReadString(account, "FullyQualifiedName"),
                ReadString(account, "Name"),
                id);
            var displayName = FirstNonBlank(ReadString(account, "Name"), ReadString(account, "FullyQualifiedName"), accountCode);
            var accountType = FirstNonBlank(ReadString(account, "Classification"), ReadString(account, "AccountType"), "Unknown");
            var currency = ReadReferenceValue(account, "CurrencyRef") ?? "USD";
            var parentId = ReadReferenceValue(account, "ParentRef");

            rows.Add(new AccountingSystemChartAccountDto(
                id.Trim(),
                accountCode,
                displayName,
                accountType,
                currency,
                ReadBool(account, "Active") ?? true,
                ParentExternalAccountId: parentId,
                EvidenceRef: $"quickbooks:company:{connection.RealmId}:account:{id.Trim()}"));
        }

        return rows;
    }

    private async Task<IReadOnlyList<AccountingSystemJournalEntryDto>> ReadJournalEntriesAsync(
        QuickBooksOnlineConnection connection,
        string accessToken,
        IReadOnlyDictionary<string, AccountingSystemChartAccountDto> accountLookup,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct)
    {
        var query = FormattableString.Invariant(
            $"select * from JournalEntry where TxnDate >= '{periodStart:yyyy-MM-dd}' and TxnDate <= '{periodEnd:yyyy-MM-dd}'");
        using var document = await GetJsonAsync(BuildQueryUri(connection, query), accessToken, ct).ConfigureAwait(false);

        if (!TryGetProperty(document.RootElement, "QueryResponse", out var queryResponse) ||
            !TryGetProperty(queryResponse, "JournalEntry", out var entriesElement) ||
            entriesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<AccountingSystemJournalEntryDto>();
        foreach (var entry in entriesElement.EnumerateArray())
        {
            var id = ReadString(entry, "Id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var accountingDate = ParseDateOnly(ReadString(entry, "TxnDate")) ?? periodEnd;
            var currency = ReadReferenceValue(entry, "CurrencyRef") ?? "USD";
            var lines = ReadJournalLines(connection, id.Trim(), entry, accountLookup, currency);
            if (lines.Count == 0)
            {
                continue;
            }

            var totalDebits = lines.Sum(static line => line.Debit);
            var totalCredits = lines.Sum(static line => line.Credit);
            rows.Add(new AccountingSystemJournalEntryDto(
                id.Trim(),
                accountingDate,
                FirstNonBlank(ReadString(entry, "PrivateNote"), ReadString(entry, "DocNumber"), $"QuickBooks journal entry {id.Trim()}"),
                currency,
                totalDebits,
                totalCredits,
                lines,
                EvidenceRef: $"quickbooks:company:{connection.RealmId}:journal:{id.Trim()}"));
        }

        return rows;
    }

    private static IReadOnlyList<AccountingSystemJournalLineDto> ReadJournalLines(
        QuickBooksOnlineConnection connection,
        string entryId,
        JsonElement entry,
        IReadOnlyDictionary<string, AccountingSystemChartAccountDto> accountLookup,
        string currency)
    {
        if (!TryGetProperty(entry, "Line", out var linesElement) || linesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var lines = new List<AccountingSystemJournalLineDto>();
        foreach (var line in linesElement.EnumerateArray())
        {
            if (!TryGetProperty(line, "JournalEntryLineDetail", out var detail))
            {
                continue;
            }

            var lineId = FirstNonBlank(ReadString(line, "Id"), $"{entryId}-{lines.Count + 1}");
            var accountId = ReadReferenceValue(detail, "AccountRef") ?? string.Empty;
            var accountName = ReadReferenceName(detail, "AccountRef");
            accountLookup.TryGetValue(accountId, out var account);
            var accountCode = account?.AccountCode ?? FirstNonBlank(accountName, accountId, "Unknown");
            var postingType = ReadString(detail, "PostingType");
            var amount = Math.Abs(ReadDecimal(line, "Amount") ?? 0m);
            var debit = string.Equals(postingType, "Debit", StringComparison.OrdinalIgnoreCase) ? amount : 0m;
            var credit = string.Equals(postingType, "Credit", StringComparison.OrdinalIgnoreCase) ? amount : 0m;
            if (debit == 0m && credit == 0m)
            {
                continue;
            }

            lines.Add(new AccountingSystemJournalLineDto(
                lineId,
                accountId,
                accountCode,
                FirstNonBlank(ReadString(line, "Description"), account?.DisplayName, accountCode),
                debit,
                credit,
                currency,
                EvidenceRef: $"quickbooks:company:{connection.RealmId}:journal:{entryId}:line:{lineId}"));
        }

        return lines;
    }

    private async Task<IReadOnlyList<AccountingSystemTrialBalanceLineDto>> ReadTrialBalanceAsync(
        QuickBooksOnlineConnection connection,
        string accessToken,
        IReadOnlyDictionary<string, AccountingSystemChartAccountDto> accountLookup,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct)
    {
        using var document = await GetJsonAsync(
            BuildReportUri(connection, "TrialBalance", periodStart, periodEnd),
            accessToken,
            ct).ConfigureAwait(false);

        var rows = new List<AccountingSystemTrialBalanceLineDto>();
        if (TryGetProperty(document.RootElement, "Rows", out var rowsElement))
        {
            CollectTrialBalanceRows(connection, rowsElement, accountLookup, periodEnd, rows);
        }

        return rows;
    }

    private static void CollectTrialBalanceRows(
        QuickBooksOnlineConnection connection,
        JsonElement element,
        IReadOnlyDictionary<string, AccountingSystemChartAccountDto> accountLookup,
        DateOnly asOfDate,
        List<AccountingSystemTrialBalanceLineDto> rows)
    {
        if (TryGetProperty(element, "Row", out var rowArray) && rowArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowArray.EnumerateArray())
            {
                CollectTrialBalanceRows(connection, row, accountLookup, asOfDate, rows);
            }

            return;
        }

        if (TryGetProperty(element, "Rows", out var nestedRows))
        {
            CollectTrialBalanceRows(connection, nestedRows, accountLookup, asOfDate, rows);
        }

        if (!TryGetProperty(element, "ColData", out var colData) || colData.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var columns = colData.EnumerateArray().ToArray();
        if (columns.Length < 3)
        {
            return;
        }

        var accountName = ReadString(columns[0], "value");
        if (string.IsNullOrWhiteSpace(accountName) ||
            accountName.Contains("total", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var externalAccountId = ReadString(columns[0], "id") ?? accountName.Trim();
        var debit = ParseDecimal(ReadString(columns[1], "value")) ?? 0m;
        var credit = ParseDecimal(ReadString(columns[2], "value")) ?? 0m;
        accountLookup.TryGetValue(externalAccountId, out var account);
        var accountCode = account?.AccountCode ?? accountName.Trim();

        rows.Add(new AccountingSystemTrialBalanceLineDto(
            externalAccountId,
            accountCode,
            account?.DisplayName ?? accountName.Trim(),
            account?.AccountType ?? "Unknown",
            debit,
            credit,
            account?.Currency ?? "USD",
            asOfDate,
            EvidenceRef: $"quickbooks:company:{connection.RealmId}:trial-balance:{SanitizeEvidenceId(externalAccountId)}"));
    }

    private async Task<JsonDocument> GetJsonAsync(Uri uri, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"QuickBooks API GET {uri.AbsolutePath} returned status {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, DocumentOptions, ct).ConfigureAwait(false);
    }

    private static Uri BuildQueryUri(QuickBooksOnlineConnection connection, string query)
        => new($"{AccountingBaseUrl(connection)}/v3/company/{Uri.EscapeDataString(connection.RealmId)}/query?query={Uri.EscapeDataString(query)}");

    private static Uri BuildReportUri(
        QuickBooksOnlineConnection connection,
        string reportName,
        DateOnly periodStart,
        DateOnly periodEnd)
        => new(
            $"{AccountingBaseUrl(connection)}/v3/company/{Uri.EscapeDataString(connection.RealmId)}/reports/{reportName}?start_date={periodStart:yyyy-MM-dd}&end_date={periodEnd:yyyy-MM-dd}");

    private static string AccountingBaseUrl(QuickBooksOnlineConnection connection)
        => string.Equals(connection.Environment, "production", StringComparison.OrdinalIgnoreCase)
            ? ProductionAccountingBaseUrl
            : SandboxAccountingBaseUrl;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        var raw = ReadString(element, propertyName);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
        => ParseDecimal(ReadString(element, propertyName));

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        var negative = value.StartsWith('(') && value.EndsWith(')');
        value = value.Trim('(', ')').Replace(",", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? negative ? -parsed : parsed
            : null;
    }

    private static DateOnly? ParseDateOnly(string? raw)
        => DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static string? ReadReferenceValue(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var reference) || reference.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return FirstNonBlank(ReadString(reference, "value"), ReadString(reference, "Value"));
    }

    private static string? ReadReferenceName(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var reference) || reference.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return FirstNonBlank(ReadString(reference, "name"), ReadString(reference, "Name"));
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string SanitizeEvidenceId(string value)
        => string.Concat(value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')).Trim('-');
}
