using System.Net;

namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>Completion evidence for one corporate-action endpoint, including acknowledged writes.</summary>
public sealed record PolygonCorporateActionEndpointResult(bool IsComplete, int PersistedCount);

public sealed record PolygonCorporateActionFetchResult(
    string Ticker,
    Guid SecurityId,
    PolygonCorporateActionEndpointResult Dividends,
    PolygonCorporateActionEndpointResult Splits)
{
    public bool IsComplete => Dividends.IsComplete && Splits.IsComplete;
    public int PersistedCount => Dividends.PersistedCount + Splits.PersistedCount;
}

public enum PolygonCorporateActionFailureKind
{
    NotConfigured,
    Authentication,
    Throttled,
    Http,
    InvalidResponse,
    Persistence
}

/// <summary>A failed fetch is never an empty successful fetch; acknowledged writes remain visible.</summary>
public sealed class PolygonCorporateActionFetchException : Exception
{
    public PolygonCorporateActionFetchException(
        PolygonCorporateActionFailureKind failureKind,
        PolygonCorporateActionFetchResult result,
        Exception? innerException = null,
        HttpStatusCode? statusCode = null,
        TimeSpan? retryAfter = null)
        : base($"Polygon corporate-action fetch failed ({failureKind}); {result.PersistedCount} actions were persisted.", innerException)
    {
        FailureKind = failureKind;
        Result = result;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public PolygonCorporateActionFailureKind FailureKind { get; }
    public PolygonCorporateActionFetchResult Result { get; }
    public HttpStatusCode? StatusCode { get; }
    public TimeSpan? RetryAfter { get; }
}

/// <summary>Caller cancellation retains completion evidence without becoming an upstream failure.</summary>
public sealed class PolygonCorporateActionFetchCanceledException : OperationCanceledException
{
    public PolygonCorporateActionFetchCanceledException(
        PolygonCorporateActionFetchResult result,
        OperationCanceledException innerException,
        CancellationToken cancellationToken)
        : base("Polygon corporate-action fetch was canceled; acknowledged writes are retained.", innerException, cancellationToken)
    {
        Result = result;
    }

    public PolygonCorporateActionFetchResult Result { get; }
}
