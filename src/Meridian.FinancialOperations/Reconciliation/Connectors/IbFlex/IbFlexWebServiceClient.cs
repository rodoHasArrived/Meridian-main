using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Meridian.FinancialOperations.Reconciliation.Connectors.IbFlex;

public interface IIbFlexWebServiceClient
{
    Task<ReadOnlyMemory<byte>> FetchStatementAsync(
        string token,
        string queryId,
        CancellationToken ct = default);
}

/// <summary>
/// Implements the IB Flex Web Service v3 send-and-retrieve protocol. Retrieval is polled
/// only for IB's documented generation-in-progress response; every other error fails closed.
/// </summary>
public sealed class IbFlexWebServiceClient(
    HttpClient httpClient,
    TimeSpan? pollDelay = null,
    int maxRetrieveAttempts = 12) : IIbFlexWebServiceClient
{
    private static readonly Uri SendRequestEndpoint = new(
        "https://ndcdyn.interactivebrokers.com/AccountManagement/FlexWebService/SendRequest",
        UriKind.Absolute);
    private const int MaximumResponseBytes = 50 * 1024 * 1024;

    private readonly TimeSpan _pollDelay = pollDelay ?? TimeSpan.FromSeconds(1);
    private readonly int _maxRetrieveAttempts = maxRetrieveAttempts > 0
        ? maxRetrieveAttempts
        : throw new ArgumentOutOfRangeException(nameof(maxRetrieveAttempts));

    public async Task<ReadOnlyMemory<byte>> FetchStatementAsync(
        string token,
        string queryId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);

        var sendUri = AddQuery(
            SendRequestEndpoint,
            ("t", token.Trim()),
            ("q", queryId.Trim()),
            ("v", "3"));
        var sendContent = await GetContentAsync(sendUri, ct).ConfigureAwait(false);
        var control = ParseControlResponse(sendContent.Span, "send request");
        if (!string.Equals(control.Status, "Success", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(control.ReferenceCode) ||
            string.IsNullOrWhiteSpace(control.RetrievalUrl))
        {
            throw BuildServiceException(control, "IB Flex rejected the statement request");
        }

        var retrievalBase = ValidateRetrievalUrl(control.RetrievalUrl);
        var retrievalUri = AddQuery(
            retrievalBase,
            ("q", control.ReferenceCode),
            ("t", token.Trim()),
            ("v", "3"));

        for (var attempt = 1; attempt <= _maxRetrieveAttempts; attempt++)
        {
            var content = await GetContentAsync(retrievalUri, ct).ConfigureAwait(false);
            if (LooksLikeFlexQueryResponse(content.Span))
                return content;

            var retrieval = ParseControlResponse(content.Span, "statement retrieval");
            if (!IsGenerationInProgress(retrieval))
                throw BuildServiceException(retrieval, "IB Flex could not retrieve the statement");

            if (attempt < _maxRetrieveAttempts)
                await Task.Delay(_pollDelay, ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"IB Flex statement generation was still in progress after {_maxRetrieveAttempts.ToString(CultureInfo.InvariantCulture)} retrieval attempts.");
    }

    private async Task<ReadOnlyMemory<byte>> GetContentAsync(Uri uri, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (bytes.Length > MaximumResponseBytes)
            throw new InvalidDataException("IB Flex response exceeded the 50 MiB statement safety limit.");
        return bytes;
    }

    private static IbFlexControlResponse ParseControlResponse(ReadOnlySpan<byte> content, string operation)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            return new IbFlexControlResponse(
                Status: ElementValue(document, "Status"),
                ReferenceCode: ElementValue(document, "ReferenceCode"),
                RetrievalUrl: ElementValue(document, "Url"),
                ErrorCode: ElementValue(document, "ErrorCode"),
                ErrorMessage: ElementValue(document, "ErrorMessage"));
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"IB Flex {operation} returned invalid XML.", ex);
        }
    }

    private static bool LooksLikeFlexQueryResponse(ReadOnlySpan<byte> content)
    {
        var head = Encoding.UTF8.GetString(content[..Math.Min(content.Length, 1024)]);
        return head.Contains("<FlexQueryResponse", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGenerationInProgress(IbFlexControlResponse response)
        => string.Equals(response.ErrorCode, "1019", StringComparison.OrdinalIgnoreCase) ||
           response.ErrorMessage?.Contains("in progress", StringComparison.OrdinalIgnoreCase) == true;

    private static Exception BuildServiceException(IbFlexControlResponse response, string prefix)
    {
        var code = string.IsNullOrWhiteSpace(response.ErrorCode) ? "unknown" : response.ErrorCode;
        var message = string.IsNullOrWhiteSpace(response.ErrorMessage) ? response.Status ?? "unknown error" : response.ErrorMessage;
        return new InvalidOperationException($"{prefix} ({code}): {message}");
    }

    private static Uri ValidateRetrievalUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !(uri.Host.EndsWith(".interactivebrokers.com", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(uri.Host, "interactivebrokers.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("IB Flex returned an untrusted statement retrieval URL.");
        }

        return uri;
    }

    private static Uri AddQuery(Uri uri, params (string Name, string Value)[] values)
    {
        var builder = new UriBuilder(uri);
        var existing = builder.Query.TrimStart('?');
        var query = string.Join("&", values.Select(static item =>
            $"{Uri.EscapeDataString(item.Name)}={Uri.EscapeDataString(item.Value)}"));
        builder.Query = string.IsNullOrEmpty(existing) ? query : $"{existing}&{query}";
        return builder.Uri;
    }

    private static string? ElementValue(XDocument document, string localName)
        => document.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();

    private sealed record IbFlexControlResponse(
        string? Status,
        string? ReferenceCode,
        string? RetrievalUrl,
        string? ErrorCode,
        string? ErrorMessage);
}
