using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Integrity;
using Meridian.ProviderSdk.AccountingSystem;

namespace Meridian.DataIntegration.AccountingSystem.QuickBooks;

public sealed record QuickBooksPostingConnection(
    ExternalGlDestination Destination, IQuickBooksOnlineConnectionStore Credentials);

/// <summary>Resolves the encrypted credential store for one retained, tenant-owned connection.</summary>
public interface IQuickBooksPostingConnectionResolver
{
    QuickBooksPostingConnection Resolve(string tenantId, string connectionId);
}

/// <summary>Creates and recovers balanced QuickBooks Online JournalEntry documents.</summary>
public sealed class QuickBooksOnlinePostingProvider(
    IQuickBooksPostingConnectionResolver connections, IQuickBooksOnlineClient oauth, HttpClient client)
    : IExternalGlPostingProvider
{
    private const string LineDetailType = "JournalEntryLineDetail";
    private readonly ConcurrentDictionary<ExternalGlDestination, SemaphoreSlim> _refreshGates = new();

    public string ProviderId => QuickBooksOnlineAccountingProvider.Id;

    public async Task<ExternalGlDestination> ResolveDestinationAsync(string tenantId, string connectionId, CancellationToken ct)
    {
        var resolved = connections.Resolve(tenantId, connectionId);
        var connection = await resolved.Credentials.ReadAsync(ct).ConfigureAwait(false);
        var metadata = await resolved.Credentials.GetMetadataAsync(ct).ConfigureAwait(false);
        if (!Matches(connection, resolved.Destination) || !metadata.HasLocalConfig || metadata.LastConnectedAtUtc is null)
            throw new InvalidOperationException("Verify the owned QuickBooks connection before configuring or preparing an external GL package.");
        var token = await GetAccessTokenAsync(tenantId, resolved.Destination, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The verified QuickBooks connection could not authorize posting.");
        using var response = await SendAsync(resolved.Destination, "preferences", HttpMethod.Get, token, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("QuickBooks posting preferences could not be verified.");
        using var settings = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        if (!WarnsOnDuplicateJournalNumber(settings.RootElement))
            throw new InvalidOperationException("Enable duplicate journal number protection in the selected QuickBooks company.");
        return resolved.Destination;
    }

    public void ValidateDocument(ExternalGlPostingDocument document)
    {
        if (document.Currency != "USD" || document.Lines.Count < 2 ||
            document.Lines.Sum(line => line.Debit) != document.Lines.Sum(line => line.Credit))
            throw new InvalidOperationException("QuickBooks posting requires an exactly balanced USD journal.");
        if (document.Lines.Select(line => line.ExternalDimensions.GetValueOrDefault("department")).Distinct().Count() > 1)
            throw new InvalidOperationException("QuickBooks supports one location per journal. The certified location must be the same on every line.");

        foreach (var line in document.Lines)
        {
            if (!IsReference(line.ExternalAccountId) || line.Debit < 0 || line.Credit < 0 || (line.Debit > 0) == (line.Credit > 0) ||
                line.Debit != decimal.Round(line.Debit, 2) || line.Credit != decimal.Round(line.Credit, 2))
                throw new InvalidOperationException($"QuickBooks line {line.MeridianLineId:D} needs a numeric account ID and an exact, single-sided cent amount.");

            if (line.ExternalDimensions.Any(pair => pair.Key is not ("class" or "department") || !IsReference(pair.Value)))
                throw new InvalidOperationException($"QuickBooks line {line.MeridianLineId:D} supports numeric class and department mappings.");
        }
    }

    public async Task<ExternalGlProviderReceipt> PostAsync(
        string tenantId, ExternalGlDestination destination, ExternalGlPostingDocument document, CancellationToken ct)
    {
        ValidateDocument(document);
        var token = await GetAccessTokenAsync(tenantId, destination, ct).ConfigureAwait(false);
        if (token is null)
            return Unknown(ExternalGlErrorCode.CredentialsUnavailable);

        var realm = Uri.EscapeDataString(destination.ExternalLedgerId);
        using var company = await SendAsync(destination, $"companyinfo/{realm}", HttpMethod.Get, token, ct).ConfigureAwait(false);
        if (!company.IsSuccessStatusCode)
            return await ReadFaultAsync(company, document, false, ct).ConfigureAwait(false);
        using var companyJson = await ReadJsonAsync(company, ct).ConfigureAwait(false);
        using var preferences = await SendAsync(destination, "preferences", HttpMethod.Get, token, ct).ConfigureAwait(false);
        if (!preferences.IsSuccessStatusCode)
            return await ReadFaultAsync(preferences, document, false, ct).ConfigureAwait(false);
        using var preferenceJson = await ReadJsonAsync(preferences, ct).ConfigureAwait(false);

        if (!SupportsDocument(companyJson.RootElement, preferenceJson.RootElement, document))
            return Rejected(document, ExternalGlErrorCode.UnsupportedConfiguration, Trace(preferences), null);

        using var response = await SendAsync(destination, $"journalentry?requestid={Uri.EscapeDataString(document.IdempotencyKey)}",
            HttpMethod.Post, token, ct, JsonContent.Create(BuildJournal(document), options: JsonSerializerOptions.Default)).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return await ReadFaultAsync(response, document, true, ct).ConfigureAwait(false);
        using var json = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        return ParseJournal(Property(json.RootElement, "JournalEntry"), document, Trace(response));
    }

    public async Task<ExternalGlProviderReceipt> FindAsync(
        string tenantId, ExternalGlDestination destination, ExternalGlPostingDocument document, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(tenantId, destination, ct).ConfigureAwait(false);
        if (token is null)
            return Unknown(ExternalGlErrorCode.CredentialsUnavailable);
        var query = $"select * from JournalEntry where DocNumber = '{DocumentNumber(document)}' maxresults 2";
        using var response = await SendAsync(destination, $"query?query={Uri.EscapeDataString(query)}", HttpMethod.Get, token, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return await ReadFaultAsync(response, document, false, ct).ConfigureAwait(false);

        using var json = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        var queryResponse = Property(json.RootElement, "QueryResponse");
        if (queryResponse.ValueKind != JsonValueKind.Object)
            return Unknown(ExternalGlErrorCode.UnrecognizedResponse, Trace(response));
        var journals = Property(queryResponse, "JournalEntry");
        if (journals.ValueKind == JsonValueKind.Array && journals.GetArrayLength() == 1)
            return ParseJournal(journals[0], document, Trace(response));
        if (journals.ValueKind != JsonValueKind.Undefined && (journals.ValueKind != JsonValueKind.Array || journals.GetArrayLength() != 0))
            return Unknown(ExternalGlErrorCode.ReceiptMismatch, Trace(response));

        // A missing document can reflect visibility lag. Require QuickBooks' duplicate document
        // protection as well as the retained requestid before allowing a replay.
        using var preferences = await SendAsync(destination, "preferences", HttpMethod.Get, token, ct).ConfigureAwait(false);
        var safe = false;
        if (preferences.IsSuccessStatusCode)
        {
            using var settings = await ReadJsonAsync(preferences, ct).ConfigureAwait(false);
            safe = WarnsOnDuplicateJournalNumber(settings.RootElement);
        }

        return new(ExternalGlReceiptKind.NotFound, [], ProviderTraceId: Trace(response),
            ErrorCode: ExternalGlErrorCode.DocumentNotFound, SafeToReplay: safe);
    }

    private async Task<string?> GetAccessTokenAsync(string tenantId, ExternalGlDestination destination, CancellationToken ct)
    {
        var gate = _refreshGates.GetOrAdd(destination, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var resolved = connections.Resolve(tenantId, destination.ConnectionId);
            if (resolved.Destination != destination)
                return null;
            var connection = await resolved.Credentials.ReadAsync(ct).ConfigureAwait(false);
            if (!Matches(connection, destination))
                return null;
            var token = await oauth.RefreshAccessTokenAsync(connection!, ct).ConfigureAwait(false);
            await resolved.Credentials.SaveRefreshTokenAsync(token.RefreshToken, connection!, CancellationToken.None).ConfigureAwait(false);
            return token.AccessToken;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool Matches(QuickBooksOnlineConnection? connection, ExternalGlDestination destination) =>
        connection is not null && connection.RealmId == destination.ExternalLedgerId && connection.Environment == destination.Environment;

    private async Task<HttpResponseMessage> SendAsync(ExternalGlDestination destination, string path, HttpMethod method,
        string token, CancellationToken ct, HttpContent? content = null)
    {
        var host = destination.Environment switch
        {
            "production" => "quickbooks.api.intuit.com",
            "sandbox" => "sandbox-quickbooks.api.intuit.com",
            _ => throw new InvalidOperationException("Unsupported QuickBooks environment.")
        };
        using var request = new HttpRequestMessage(method, $"https://{host}/v3/company/{Uri.EscapeDataString(destination.ExternalLedgerId)}/{path}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = content;
        return await client.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static bool SupportsDocument(JsonElement company, JsonElement preferences, ExternalGlPostingDocument document)
    {
        var settings = Property(preferences, "Preferences");
        var currency = Property(settings, "CurrencyPrefs");
        var accounting = Property(settings, "AccountingInfoPrefs");
        return Text(Property(company, "CompanyInfo"), "Country") == "US" &&
            WarnsOnDuplicateJournalNumber(preferences) &&
            Property(currency, "MultiCurrencyEnabled").ValueKind == JsonValueKind.False &&
            Reference(currency, "HomeCurrency") == document.Currency &&
            (!document.Lines.Any(line => line.ExternalDimensions.ContainsKey("class")) ||
                Property(accounting, "ClassTrackingPerTxnLine").ValueKind == JsonValueKind.True) &&
            (!document.Lines.Any(line => line.ExternalDimensions.ContainsKey("department")) ||
                Property(accounting, "TrackDepartments").ValueKind == JsonValueKind.True);
    }

    private static bool WarnsOnDuplicateJournalNumber(JsonElement root)
    {
        var preferenceRows = Property(Property(Property(root, "Preferences"), "OtherPrefs"), "NameValue");
        return preferenceRows.ValueKind == JsonValueKind.Array && preferenceRows.EnumerateArray().Any(row =>
            Text(row, "Name") == "WarnDuplicateJournalNumber" &&
            string.Equals(Text(row, "Value"), "true", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, object> BuildJournal(ExternalGlPostingDocument document)
    {
        var payload = new Dictionary<string, object>
        {
            ["DocNumber"] = DocumentNumber(document),
            ["PrivateNote"] = Marker(document),
            ["TxnDate"] = document.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Line"] = document.Lines.Select(line =>
            {
                var detail = new Dictionary<string, object>
                {
                    ["PostingType"] = line.Debit > 0 ? "Debit" : "Credit",
                    ["AccountRef"] = new { value = line.ExternalAccountId }
                };
                if (line.ExternalDimensions.TryGetValue("class", out var classId))
                    detail["ClassRef"] = new { value = classId };
                return new
                {
                    DetailType = LineDetailType,
                    Description = LineMarker(line.MeridianLineId),
                    Amount = line.Debit + line.Credit,
                    JournalEntryLineDetail = detail
                };
            }).ToArray()
        };
        if (document.Lines[0].ExternalDimensions.TryGetValue("department", out var department))
            payload["DepartmentRef"] = new { value = department };
        return payload;
    }

    private static ExternalGlProviderReceipt ParseJournal(JsonElement journal, ExternalGlPostingDocument expected, string? trace)
    {
        if (journal.ValueKind != JsonValueKind.Object)
            return Unknown(ExternalGlErrorCode.UnrecognizedResponse, trace);
        var id = Text(journal, "Id");
        var recorded = DateTimeOffset.TryParse(Text(Property(journal, "MetaData"), "CreateTime"),
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) ? created.ToUniversalTime() : (DateTimeOffset?)null;
        var lines = Property(journal, "Line");
        if (string.IsNullOrWhiteSpace(id) || Text(journal, "DocNumber") != DocumentNumber(expected) ||
            Text(journal, "PrivateNote") != Marker(expected) ||
            Text(journal, "TxnDate") != expected.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ||
            Reference(journal, "CurrencyRef") is { } currency && currency != expected.Currency ||
            lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() != expected.Lines.Count)
            return new(ExternalGlReceiptKind.OutcomeUnknown, [], ProviderDocumentId: id, ProviderTraceId: trace,
                ProviderRecordedAtUtc: recorded, ErrorCode: ExternalGlErrorCode.ReceiptMismatch);

        var returnedByIdentity = lines.EnumerateArray().ToLookup(line => Text(line, "Description"), StringComparer.Ordinal);
        var returned = new List<ExternalGlLineReceipt>(expected.Lines.Count);
        foreach (var line in expected.Lines)
        {
            var matches = returnedByIdentity[LineMarker(line.MeridianLineId)].ToArray();
            if (matches.Length != 1)
            {
                returned.Add(new(line.MeridianLineId, ExternalGlLineStatus.OutcomeUnknown, ErrorCode: ExternalGlErrorCode.ReceiptMismatch));
                continue;
            }

            var row = matches[0];
            var detail = Property(row, LineDetailType);
            var amountField = Property(row, "Amount");
            var amount = amountField.ValueKind == JsonValueKind.Number && amountField.TryGetDecimal(out var value) ? value : (decimal?)null;
            var side = Text(detail, "PostingType");
            var debit = side == "Debit" ? amount : side == "Credit" ? 0m : null;
            var credit = side == "Credit" ? amount : side == "Debit" ? 0m : null;
            var account = Reference(detail, "AccountRef");
            var classId = Reference(detail, "ClassRef");
            var journalDepartment = Reference(journal, "DepartmentRef");
            var lineDepartment = Reference(detail, "DepartmentRef");
            var department = lineDepartment ?? journalDepartment;
            var dimensions = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (classId is not null)
                dimensions.Add("class", classId);
            if (department is not null)
                dimensions.Add("department", department);

            var accepted = Text(row, "DetailType") == LineDetailType && account == line.ExternalAccountId &&
                debit == line.Debit && credit == line.Credit && dimensions.SequenceEqual(line.ExternalDimensions.OrderBy(pair => pair.Key, StringComparer.Ordinal)) &&
                (lineDepartment is null || journalDepartment is null || lineDepartment == journalDepartment) &&
                Property(detail, "Entity").ValueKind is JsonValueKind.Undefined or JsonValueKind.Null &&
                Property(detail, "TaxCodeRef").ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;
            returned.Add(new(line.MeridianLineId, accepted ? ExternalGlLineStatus.Accepted : ExternalGlLineStatus.OutcomeUnknown,
                Text(row, "Id"), accepted ? null : ExternalGlErrorCode.ReceiptMismatch, account, debit, credit, dimensions));
        }

        var verified = returned.All(line => line.Status == ExternalGlLineStatus.Accepted);
        return new(verified ? ExternalGlReceiptKind.Completed : ExternalGlReceiptKind.OutcomeUnknown, returned,
            ProviderDocumentId: id, ProviderTraceId: trace, ProviderRecordedAtUtc: recorded,
            ErrorCode: verified ? null : ExternalGlErrorCode.ReceiptMismatch);
    }

    private static async Task<ExternalGlProviderReceipt> ReadFaultAsync(
        HttpResponseMessage response, ExternalGlPostingDocument document, bool wasCreate, CancellationToken ct)
    {
        var trace = Trace(response);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return Unknown(ExternalGlErrorCode.RateLimited, trace);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Unknown(ExternalGlErrorCode.CredentialsUnavailable, trace);
        if (response.StatusCode != HttpStatusCode.BadRequest)
            return Unknown(ExternalGlErrorCode.ProviderUnavailable, trace);

        using var json = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        var fault = Property(json.RootElement, "Fault");
        var errors = Property(fault, "Error");
        var codes = errors.ValueKind == JsonValueKind.Array ? errors.EnumerateArray()
            .Select(error => Text(error, "code")).Where(code => code is not null && code.All(char.IsAsciiDigit)).ToArray() : [];
        var code = codes.FirstOrDefault();
        if (codes.Any(value => value is "600" or "630" or "6140"))
            return Unknown(ExternalGlErrorCode.DuplicateReference, trace, code);
        if (wasCreate && Text(fault, "type") == "ValidationFault" && codes.Length > 0 &&
            codes.All(value => value is "2010" or "2020" or "2030" or "2090" or "2300" or "2500" or "6000" or "6210"))
            return Rejected(document, ExternalGlErrorCode.ValidationFailed, trace, code);
        return Unknown(ExternalGlErrorCode.UnrecognizedResponse, trace, code);
    }

    private static ExternalGlProviderReceipt Rejected(
        ExternalGlPostingDocument document, ExternalGlErrorCode error, string? trace, string? code) =>
        new(ExternalGlReceiptKind.Rejected, document.Lines.Select(line =>
            new ExternalGlLineReceipt(line.MeridianLineId, ExternalGlLineStatus.Rejected, ErrorCode: error)).ToArray(),
            ProviderTraceId: trace, ErrorCode: error, ProviderErrorCode: code);

    private static ExternalGlProviderReceipt Unknown(ExternalGlErrorCode error, string? trace = null, string? code = null) =>
        new(ExternalGlReceiptKind.OutcomeUnknown, [], ProviderTraceId: trace, ErrorCode: error, ProviderErrorCode: code);

    private static Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct) =>
        ReadContentAsync(response.Content, ct);

    private static async Task<JsonDocument> ReadContentAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private static string? Trace(HttpResponseMessage response) =>
        response.Headers.TryGetValues("intuit_tid", out var values)
            ? values.FirstOrDefault(value => value.Length <= 100 && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            : null;

    private static string DocumentNumber(ExternalGlPostingDocument doc) =>
        "M" + Sha256Digest.ComputeUtf8(doc.IdempotencyKey)[..20].ToUpperInvariant();

    private static string Marker(ExternalGlPostingDocument doc) => $"Meridian:{doc.IdempotencyKey}";

    private static string LineMarker(Guid lineId) => $"Meridian:{lineId:N}";

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string? Text(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static string? Reference(JsonElement element, string name) => Text(Property(element, name), "value");

    private static bool IsReference(string value) => value.Length is > 0 and <= 64 && value.All(char.IsAsciiDigit);
}
