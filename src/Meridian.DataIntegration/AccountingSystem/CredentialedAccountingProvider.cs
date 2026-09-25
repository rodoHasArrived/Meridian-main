using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.DataIntegration.Credentials;
using Meridian.ProviderSdk.AccountingSystem;
using static Meridian.DataIntegration.AccountingSystem.AccountingProviderJson;

namespace Meridian.DataIntegration.AccountingSystem;

/// <summary>Serializes refresh-token rotation and imports for one registered read-only provider.</summary>
public abstract class CredentialedAccountingProvider : IAccountingSystemProvider,
    IAccountingSystemConnectionMetadataProvider, IAccountingSystemConnectionVerifier, IAccountingSystemExportValidator
{
    private readonly IProviderCredentialStore _store;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    protected readonly HttpClient Client;

    protected CredentialedAccountingProvider(IProviderCredentialStore store, HttpClient client)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public abstract string ProviderId { get; }
    public abstract string DisplayName { get; }
    public abstract AccountingSystemProviderCapabilities Capabilities { get; }
    protected abstract string CompanyField { get; }
    protected abstract string[] ExportControls { get; }
    protected abstract string ConnectionScope(ProviderCredentialReadResult connection);
    protected abstract Uri TokenEndpoint(ProviderCredentialReadResult connection);
    protected abstract Task<AccountingSystemImportDetailDto> ReadAsync(ProviderCredentialReadResult connection,
        string token, AccountingSystemImportRequestDto request, CancellationToken ct);
    protected abstract Task VerifyScopeAsync(ProviderCredentialReadResult connection, string token, CancellationToken ct);

    protected static string Field(ProviderCredentialReadResult connection, string name)
        => !string.IsNullOrWhiteSpace(connection.Get(name)) ? connection.Get(name)!.Trim()
            : throw new InvalidOperationException("Required provider credentials are missing.");

    private async Task<ProviderCredentialReadResult> ConnectionAsync(CancellationToken ct)
    {
        var connection = await _store.ReadForProviderAsync(ProviderId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Provider credentials are not configured.");
        _ = Field(connection, "ClientId");
        _ = Field(connection, "ClientSecret");
        _ = Field(connection, "RefreshToken");
        _ = ConnectionScope(connection);
        return connection;
    }

    public async Task<AccountingSystemConnectionMetadataDto> GetConnectionMetadataAsync(CancellationToken ct = default)
    {
        var status = await _store.GetStatusAsync(ProviderId, ct).ConfigureAwait(false);
        var read = await _store.ReadForProviderAsync(ProviderId, ct).ConfigureAwait(false);
        var complete = false;
        try
        { _ = await ConnectionAsync(ct).ConfigureAwait(false); complete = true; }
        catch (InvalidOperationException) { }
        return new(ProviderId, status.Environment, read?.Get(CompanyField), read?.Get("CompanyName"),
            complete, !string.IsNullOrWhiteSpace(read?.Get("RefreshToken")), status.LastSuccessfulAt,
            complete ? "Read-only credentials configured" : "Credentials required",
            "Verify the selected external company before importing GL evidence. Live posting is disabled.", status.MissingFields);
    }

    public async Task<AccountingSystemConnectionVerificationResult> VerifyConnectionAsync(CancellationToken ct = default)
    {
        await _connectionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var connection = await ConnectionAsync(ct).ConfigureAwait(false);
            var token = await RefreshAsync(connection, ct).ConfigureAwait(false);
            await VerifyScopeAsync(connection, token, ct).ConfigureAwait(false);
            await RecordAsync(true, connection.Get(CompanyField), ct).ConfigureAwait(false);
            return new(true, connection.Get(CompanyField), null, DateTimeOffset.UtcNow, ["Read-only connection verified; live posting remains disabled."]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordAsync(false, null, ct).ConfigureAwait(false);
            return new(false, null, "Provider verification failed. Check credentials, account scope, permissions and availability.",
                DateTimeOffset.UtcNow, []);
        }
        finally { _connectionGate.Release(); }
    }

    public async Task<AccountingSystemImportDetailDto> ImportAsync(AccountingSystemImportRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var end = request.PeriodEnd ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = request.PeriodStart ?? new DateOnly(end.Year, end.Month, 1);
        if (start > end)
            throw new ArgumentException("Import period start must not follow period end.", nameof(request));
        if (request.ProviderId is not null && !string.Equals(request.ProviderId.Trim(), ProviderId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Import provider does not match this adapter.", nameof(request));
        await _connectionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var connection = await ConnectionAsync(ct).ConfigureAwait(false);
            var token = await RefreshAsync(connection, ct).ConfigureAwait(false);
            var detail = await ReadAsync(connection, token, request with { PeriodStart = start, PeriodEnd = end }, ct).ConfigureAwait(false);
            await RecordAsync(true, connection.Get(CompanyField), ct).ConfigureAwait(false);
            return detail;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordAsync(false, null, ct).ConfigureAwait(false);
            // Provider bodies, tokens, and transport exception messages never cross the public boundary.
            throw new InvalidOperationException("Read-only GL import failed. Check credentials, scope, permissions and provider response completeness.");
        }
        finally { _connectionGate.Release(); }
    }

    private Task RecordAsync(bool success, string? company, CancellationToken ct)
        => _store.RecordVerificationAsync(new(ProviderId, success,
            ErrorMessage: success ? null : "Read-only GL verification or import failed.", ExternalAccountId: company,
            VerifiedAt: DateTimeOffset.UtcNow, Actor: $"{ProviderId}-read-only-import"), ct);

    private async Task<string> RefreshAsync(ProviderCredentialReadResult connection, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint(connection));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Field(connection, "ClientId")}:{Field(connection, "ClientSecret")}")));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = Field(connection, "RefreshToken")
        });
        using var document = await SendAsync(request, ct).ConfigureAwait(false);
        var token = RequiredText(document.RootElement, "access_token");
        var refresh = Text(document.RootElement, "refresh_token");
        if (!string.IsNullOrWhiteSpace(refresh))
            await _store.SaveAsync(new(ProviderId, new Dictionary<string, string?> { ["RefreshToken"] = refresh },
                Actor: $"{ProviderId}-token-exchange"), ct).ConfigureAwait(false);
        return token;
    }

    protected async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await Client.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Provider request returned HTTP {(int)response.StatusCode}.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    protected AccountingSystemImportDetailDto Detail(ProviderCredentialReadResult connection, AccountingSystemImportRequestDto request,
        IReadOnlyList<AccountingSystemChartAccountDto> accounts, IReadOnlyList<AccountingSystemJournalEntryDto> journals,
        IReadOnlyList<AccountingSystemTrialBalanceLineDto> balances, params string[] warnings)
    {
        if (accounts.Select(a => a.ExternalAccountId).Distinct(StringComparer.Ordinal).Count() != accounts.Count ||
            journals.Select(j => j.ExternalJournalEntryId).Distinct(StringComparer.Ordinal).Count() != journals.Count ||
            balances.Select(b => b.ExternalAccountId).Distinct(StringComparer.Ordinal).Count() != balances.Count ||
            journals.Any(j => j.TotalDebits != j.TotalCredits))
            throw new InvalidOperationException("Provider evidence has duplicate identities or unbalanced journals.");
        return new(new($"{ProviderId}-{Guid.NewGuid():N}", ProviderId, DisplayName,
            string.IsNullOrWhiteSpace(request.FundProfileId) ? "default-fund" : request.FundProfileId.Trim(), request.LedgerBookId,
            request.PersistPreview ? AccountingSystemImportStateDto.Imported : AccountingSystemImportStateDto.Previewed,
            request.PeriodStart!.Value, request.PeriodEnd!.Value, DateTimeOffset.UtcNow, accounts.Count, journals.Count, balances.Count,
            [ConnectionScope(connection)], ["External evidence only; Meridian owns ledger truth and live posting is disabled.", .. warnings],
            request.TenantId, request.CompanyId), accounts, journals, balances);
    }

    public async Task<IReadOnlyList<AccountingConfigurationValidationIssueDto>> ValidateExportAsync(
        AccountingSystemExportValidationContext context, CancellationToken ct = default)
    {
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        void Block(string code, string message) => issues.Add(new(code, AccountingConfigurationValidationSeverityDto.Critical, message));
        var import = context.Import;
        string? scope = null;
        try
        { scope = ConnectionScope(await ConnectionAsync(ct).ConfigureAwait(false)); }
        catch (InvalidOperationException) { Block("ExternalGlProviderConnectionMissing", "Provider credentials and account scope are required for export review."); }
        if (import is null || import.Summary.ProviderId != ProviderId || import.Summary.State != AccountingSystemImportStateDto.Imported ||
            import.Summary.LedgerBookId != context.LedgerBookId || context.LedgerBookId is null ||
            import.Summary.PeriodStart != context.PeriodStart || import.Summary.PeriodEnd != context.PeriodEnd ||
            scope is null || !import.Summary.EvidenceReferences.Contains(scope, StringComparer.Ordinal))
        {
            Block("ExternalGlProviderImportScopeMismatch", "Retain a live import for the current provider connection, ledger book and exact period before export certification.");
            return issues;
        }
        foreach (var control in ExportControls)
        {
            var evidence = $"approval:external-gl-provider:{ProviderId}:{control}:import:{import.Summary.ImportId}:ledger-book:{context.LedgerBookId:D}:{context.PeriodStart:yyyy-MM-dd}:{context.PeriodEnd:yyyy-MM-dd}";
            if (!context.EvidenceLinks.Contains(evidence, StringComparer.Ordinal))
                Block($"ExternalGlProviderControlMissing:{control}", $"Provider review evidence is required: {evidence}");
        }
        var accounts = import.ChartAccounts.ToDictionary(a => a.ExternalAccountId, StringComparer.Ordinal);
        var currencies = import.TrialBalance.Select(b => b.Currency).Distinct(StringComparer.Ordinal).ToArray();
        if (context.Lines.Count == 0 || context.Lines.Any(line => !accounts.TryGetValue(line.ExternalAccountId, out var account) ||
                !account.IsActive || !currencies.Contains(line.Currency, StringComparer.Ordinal) ||
                line.Debit < 0 || line.Credit < 0 || (line.Debit != 0 && line.Credit != 0)) ||
            context.Lines.Sum(line => line.Debit) != context.Lines.Sum(line => line.Credit))
            Block("ExternalGlProviderExportLinesInvalid", "Provider export lines must balance in imported currencies and target active, imported accounts.");
        return issues;
    }
}
