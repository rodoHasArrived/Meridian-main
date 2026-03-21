using System.Net;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Utility class for handling HTTP responses from historical data providers.
/// Provides common response validation and error handling.
/// </summary>
public static class ResponseHandler
{
    /// <summary>
    /// Handles HTTP response and returns result information.
    /// </summary>
    public static async Task<ResponseResult> HandleResponseAsync(
        HttpResponseMessage response,
        string symbol,
        string dataType,
        CancellationToken ct = default)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ResponseResult(
                IsSuccess: false,
                IsNotFound: true,
                StatusCode: response.StatusCode,
                ReasonPhrase: response.ReasonPhrase ?? "Not Found"
            );
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new ResponseResult(
                IsSuccess: false,
                IsNotFound: false,
                StatusCode: response.StatusCode,
                ReasonPhrase: response.ReasonPhrase ?? "Error",
                ErrorContent: errorContent
            );
        }

        return new ResponseResult(
            IsSuccess: true,
            IsNotFound: false,
            StatusCode: response.StatusCode,
            ReasonPhrase: response.ReasonPhrase ?? "OK"
        );
    }
}

/// <summary>
/// Result of HTTP response handling.
/// </summary>
public sealed record ResponseResult(
    bool IsSuccess,
    bool IsNotFound,
    HttpStatusCode StatusCode,
    string ReasonPhrase,
    string? ErrorContent = null
);
