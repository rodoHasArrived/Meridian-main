using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.QuantScript.Api;

namespace Meridian.QuantScript.Runtime;

/// <summary>
/// Worker-side typed proxy. Untrusted script code can request only the existing
/// <see cref="IQuantDataContext"/> operations; it never receives a host service object.
/// </summary>
internal sealed class RemoteQuantDataContext(QuantScriptWorkerChannel channel) : IQuantDataContext
{
    public Task<PriceSeries> PricesAsync(
        string symbol,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
        => PricesAsync(symbol, from, to, provider: null, ct);

    public async Task<PriceSeries> PricesAsync(
        string symbol,
        DateOnly from,
        DateOnly to,
        string? provider,
        CancellationToken ct = default)
    {
        var value = await RequestAsync<WorkerPriceSeries>(
            new WorkerDataRequest(
                WorkerDataOperation.Prices,
                symbol,
                From: from,
                To: to,
                Provider: provider),
            ct).ConfigureAwait(false);
        return value.ToPriceSeries();
    }

    public async Task<IReadOnlyList<ScriptTrade>> TradesAsync(
        string symbol,
        DateOnly date,
        CancellationToken ct = default)
        => await RequestAsync<List<ScriptTrade>>(
            new WorkerDataRequest(WorkerDataOperation.Trades, symbol, Date: date),
            ct).ConfigureAwait(false);

    public async Task<ScriptOrderBook?> OrderBookAsync(
        string symbol,
        DateTimeOffset timestamp,
        CancellationToken ct = default)
    {
        var value = await RequestNullableAsync<WorkerOrderBook>(
            new WorkerDataRequest(WorkerDataOperation.OrderBook, symbol, Timestamp: timestamp),
            ct).ConfigureAwait(false);
        return value?.ToOrderBook();
    }

    public Task<SecurityDetailDto?> SecMasterAsync(string symbol, CancellationToken ct = default)
        => RequestNullableAsync<SecurityDetailDto>(
            new WorkerDataRequest(WorkerDataOperation.SecurityMaster, symbol),
            ct);

    public async Task<IReadOnlyList<CorporateActionDto>> CorporateActionsAsync(
        string symbol,
        CancellationToken ct = default)
        => await RequestAsync<List<CorporateActionDto>>(
            new WorkerDataRequest(WorkerDataOperation.CorporateActions, symbol),
            ct).ConfigureAwait(false);

    private async Task<T> RequestAsync<T>(WorkerDataRequest request, CancellationToken ct)
    {
        var value = await RequestCoreAsync(request, ct).ConfigureAwait(false);
        try
        {
            return QuantScriptWorkerProtocol.Deserialize<T>(value)
                ?? throw new WorkerProtocolException($"Host data response for {request.Operation} was null.");
        }
        catch (JsonException ex)
        {
            throw new WorkerProtocolException($"Host data response for {request.Operation} was malformed.", ex);
        }
    }

    private async Task<T?> RequestNullableAsync<T>(WorkerDataRequest request, CancellationToken ct)
        where T : class
    {
        var value = await RequestCoreAsync(request, ct).ConfigureAwait(false);
        if (value.ValueKind == JsonValueKind.Null)
            return null;

        try
        {
            return QuantScriptWorkerProtocol.Deserialize<T>(value);
        }
        catch (JsonException ex)
        {
            throw new WorkerProtocolException($"Host data response for {request.Operation} was malformed.", ex);
        }
    }

    private async Task<JsonElement> RequestCoreAsync(WorkerDataRequest request, CancellationToken ct)
    {
        var response = await channel.ExchangeAsync<WorkerDataRequest, WorkerDataResponse>(
            QuantScriptWorkerProtocol.DataRequest,
            QuantScriptWorkerProtocol.DataResponse,
            request,
            ct).ConfigureAwait(false);

        if (!response.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(response.Error)
                    ? $"Host data request {request.Operation} failed."
                    : response.Error);
        }

        return response.Value;
    }
}
