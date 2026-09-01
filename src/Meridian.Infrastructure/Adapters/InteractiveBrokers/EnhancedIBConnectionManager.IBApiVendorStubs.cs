#if IBAPI_VENDOR
using System;
using System.Collections.Generic;
using IBApi;
using Pb = IBApi.protobuf;
using Meridian.ProviderSdk;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Vendor-only EWrapper callbacks that Meridian does not actively consume yet.
/// Keeping them in a separate partial file lets smoke builds stay small while
/// satisfying the full official Interactive Brokers SDK surface.
/// </summary>
public sealed partial class EnhancedIBConnectionManager
{
    public void deltaNeutralValidation(int reqId, DeltaNeutralContract deltaNeutralContract)
    {
    }

    public void bondContractDetails(int reqId, ContractDetails contract)
    {
    }

    public void updateAccountValue(string key, string value, string currency, string accountName)
    {
    }

    public void updatePortfolio(Contract contract, decimal position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
    {
    }

    public void updateAccountTime(string timestamp)
    {
    }

    public void accountDownloadEnd(string account)
    {
    }

    public void commissionAndFeesReport(CommissionAndFeesReport commissionAndFeesReport)
    {
    }

    public void fundamentalData(int reqId, string data)
    {
    }

    public void updateNewsBulletin(int msgId, int msgType, string message, string origExchange)
    {
    }

    public void realtimeBar(int reqId, long date, double open, double high, double low, double close, decimal volume, decimal WAP, int count)
    {
        RecordMessageReceived();
        RealTimeBarReceived?.Invoke(this, (reqId, new ProviderRealTimeBar(DateTimeOffset.FromUnixTimeSeconds(date), (decimal)open, (decimal)high, (decimal)low, (decimal)close, volume, WAP, count, ProviderDataProvenance.Unattributed(DateTimeOffset.FromUnixTimeSeconds(date)))));
    }

    public void scannerParameters(string xml)
    {
    }

    public void scannerData(int reqId, int rank, ContractDetails contractDetails, string distance, string benchmark, string projection, string legsStr)
    {
        RecordMessageReceived();
        var contract = contractDetails.Contract;
        ScannerResultReceived?.Invoke(this, (reqId, new ProviderScannerResult(rank, contract.Symbol, contract.Exchange, contract.ConId.ToString(), distance, benchmark, projection, legsStr, ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow))));
    }

    public void scannerDataEnd(int reqId)
    {
        // A batch delimiter for a live subscription, not completion — see the IBAPI partial's
        // scannerDataEnd: cancellation is the scanner's only terminal transition.
        RecordMessageReceived();
    }

    public void receiveFA(int faDataType, string faXmlData)
    {
    }

    public void verifyMessageAPI(string apiData)
    {
    }

    public void verifyCompleted(bool isSuccessful, string errorText)
    {
    }

    public void verifyAndAuthMessageAPI(string apiData, string xyzChallenge)
    {
    }

    public void verifyAndAuthCompleted(bool isSuccessful, string errorText)
    {
    }

    public void displayGroupList(int reqId, string groups)
    {
    }

    public void displayGroupUpdated(int reqId, string contractInfo)
    {
    }

    public void connectAck()
    {
    }

    public void positionMulti(int requestId, string account, string modelCode, Contract contract, decimal pos, double avgCost)
    {
    }

    public void positionMultiEnd(int requestId)
    {
    }

    public void securityDefinitionOptionParameter(int reqId, string exchange, int underlyingConId, string tradingClass, string multiplier, HashSet<string> expirations, HashSet<double> strikes)
    {
        RecordMessageReceived();
        foreach (var expirationText in expirations)
        {
            if (!DateOnly.TryParseExact(expirationText, "yyyyMMdd", out var expiration)) continue;
            foreach (var strike in strikes)
                OptionContractReceived?.Invoke(this, (reqId, new ProviderOptionContract(string.Empty, underlyingConId.ToString(), expiration, (decimal)strike, string.Empty, exchange, tradingClass, multiplier, null, ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow))));
        }
    }

    public void securityDefinitionOptionParameterEnd(int reqId)
    {
        RecordMessageReceived();
        RequestCompleted?.Invoke(this, reqId);
    }

    public void softDollarTiers(int reqId, SoftDollarTier[] tiers)
    {
    }

    public void familyCodes(FamilyCode[] familyCodes)
    {
    }

    public void mktDepthExchanges(DepthMktDataDescription[] depthMktDataDescriptions)
    {
    }

    public void tickNews(int tickerId, long timeStamp, string providerCode, string articleId, string headline, string extraData)
    {
    }

    public void smartComponents(int reqId, Dictionary<int, KeyValuePair<string, char>> theMap)
    {
    }

    public void histogramData(int reqId, HistogramEntry[] data)
    {
    }

    public void rerouteMktDataReq(int reqId, int conId, string exchange)
    {
    }

    public void rerouteMktDepthReq(int reqId, int conId, string exchange)
    {
    }

    public void marketRule(int marketRuleId, PriceIncrement[] priceIncrements)
    {
        RecordMessageReceived();
        var requestId = _marketRuleRequests.TryRemove(marketRuleId, out var correlatedRequestId) ? correlatedRequestId : marketRuleId;
        MarketRuleReceived?.Invoke(this, (requestId, priceIncrements.Select(static x => new ProviderMarketRuleIncrement((decimal)x.LowEdge, (decimal)x.Increment, ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow))).ToArray()));
    }

    public void pnl(int reqId, double dailyPnL, double unrealizedPnL, double realizedPnL)
    {
        RecordMessageReceived();
        PnlReceived?.Invoke(this, (reqId, new ProviderAccountPnl(string.Empty, null, (decimal)dailyPnL, (decimal)unrealizedPnL, (decimal)realizedPnL, null, null, ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow))));
    }

    public void pnlSingle(int reqId, decimal pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value)
    {
        RecordMessageReceived();
        PnlReceived?.Invoke(this, (reqId, new ProviderAccountPnl(string.Empty, null, (decimal)dailyPnL, (decimal)unrealizedPnL, (decimal)realizedPnL, pos, (decimal)value, ProviderDataProvenance.Unattributed(DateTimeOffset.UtcNow))));
    }

    // IB routes reqHistoricalTicks results by WhatToShow: MIDPOINT arrives through
    // historicalTicks, TRADES (the request record's default) through historicalTicksLast, and
    // BID_ASK through historicalTicksBidAsk. Downstream, Completed is a per-request terminal
    // flag: the read model completes on the first element carrying it and stops routing the
    // rest, so only the final batch's last element may carry the vendor's done flag forward —
    // and the completion must release the rejection-routing ownership, because bounded requests
    // terminate here rather than through CancelDataRequest.

    public void historicalTicks(int reqId, HistoricalTick[] ticks, bool done)
    {
        RecordMessageReceived();
        for (var index = 0; index < ticks.Length; index++)
        {
            var tick = ticks[index];
            var completesRequest = done && index == ticks.Length - 1;
            HistoricalTickReceived?.Invoke(this, (reqId, new ProviderHistoricalTick(DateTimeOffset.FromUnixTimeSeconds(tick.Time), (decimal)tick.Price, tick.Size, "MIDPOINT", null, null, null, ProviderDataProvenance.Unattributed(DateTimeOffset.FromUnixTimeSeconds(tick.Time))), completesRequest));
        }
        if (done)
        {
            _dataServiceRequestIds.TryRemove(reqId, out _);
            RequestCompleted?.Invoke(this, reqId);
        }
    }

    public void historicalTicksBidAsk(int reqId, HistoricalTickBidAsk[] ticks, bool done)
    {
    }

    public void historicalTicksLast(int reqId, HistoricalTickLast[] ticks, bool done)
    {
        RecordMessageReceived();
        for (var index = 0; index < ticks.Length; index++)
        {
            var tick = ticks[index];
            var completesRequest = done && index == ticks.Length - 1;
            HistoricalTickReceived?.Invoke(this, (reqId, new ProviderHistoricalTick(DateTimeOffset.FromUnixTimeSeconds(tick.Time), (decimal)tick.Price, tick.Size, "TRADES", null, null, string.IsNullOrEmpty(tick.Exchange) ? null : tick.Exchange, ProviderDataProvenance.Unattributed(DateTimeOffset.FromUnixTimeSeconds(tick.Time))), completesRequest));
        }
        if (done)
        {
            _dataServiceRequestIds.TryRemove(reqId, out _);
            RequestCompleted?.Invoke(this, reqId);
        }
    }

    public void tickByTickBidAsk(int reqId, long time, double bidPrice, double askPrice, decimal bidSize, decimal askSize, TickAttribBidAsk tickAttribBidAsk)
    {
    }

    public void tickByTickMidPoint(int reqId, long time, double midPoint)
    {
    }

    public void orderBound(long permId, int clientId, int orderId)
    {
    }

    public void completedOrder(Contract contract, Order order, OrderState orderState)
    {
    }

    public void completedOrdersEnd()
    {
    }

    public void replaceFAEnd(int reqId, string text)
    {
    }

    public void wshMetaData(int reqId, string dataJson)
    {
    }

    public void wshEventData(int reqId, string dataJson)
    {
    }

    public void historicalSchedule(int reqId, string startDateTime, string endDateTime, string timeZone, HistoricalSession[] sessions)
    {
    }

    public void userInfo(int reqId, string whiteBrandingId)
    {
    }

    public void currentTimeInMillis(long timeInMillis)
    {
    }

    public void orderStatusProtoBuf(Pb.OrderStatus orderStatusProto)
    {
    }

    public void openOrderProtoBuf(Pb.OpenOrder openOrderProto)
    {
    }

    public void openOrdersEndProtoBuf(Pb.OpenOrdersEnd openOrdersEndProto)
    {
    }

    public void errorProtoBuf(Pb.ErrorMessage errorMessageProto)
    {
    }

    public void execDetailsProtoBuf(Pb.ExecutionDetails executionDetailsProto)
    {
    }

    public void execDetailsEndProtoBuf(Pb.ExecutionDetailsEnd executionDetailsEndProto)
    {
    }

    public void completedOrderProtoBuf(Pb.CompletedOrder completedOrderProto)
    {
    }

    public void completedOrdersEndProtoBuf(Pb.CompletedOrdersEnd completedOrdersEndProto)
    {
    }

    public void orderBoundProtoBuf(Pb.OrderBound orderBoundProto)
    {
    }

    public void contractDataProtoBuf(Pb.ContractData contractDataProto)
    {
    }

    public void bondContractDataProtoBuf(Pb.ContractData contractDataProto)
    {
    }

    public void contractDataEndProtoBuf(Pb.ContractDataEnd contractDataEndProto)
    {
    }

    public void tickPriceProtoBuf(Pb.TickPrice tickPriceProto)
    {
    }

    public void tickSizeProtoBuf(Pb.TickSize tickSizeProto)
    {
    }

    public void tickOptionComputationProtoBuf(Pb.TickOptionComputation tickOptionComputationProto)
    {
    }

    public void tickGenericProtoBuf(Pb.TickGeneric tickGenericProto)
    {
    }

    public void tickStringProtoBuf(Pb.TickString tickStringProto)
    {
    }

    public void tickSnapshotEndProtoBuf(Pb.TickSnapshotEnd tickSnapshotEndProto)
    {
    }

    public void updateMarketDepthProtoBuf(Pb.MarketDepth marketDepthProto)
    {
    }

    public void updateMarketDepthL2ProtoBuf(Pb.MarketDepthL2 marketDepthL2Proto)
    {
    }

    public void marketDataTypeProtoBuf(Pb.MarketDataType marketDataTypeProto)
    {
    }

    public void tickReqParamsProtoBuf(Pb.TickReqParams tickReqParamsProto)
    {
    }

    public void updateAccountValueProtoBuf(Pb.AccountValue accountValueProto)
    {
    }

    public void updatePortfolioProtoBuf(Pb.PortfolioValue portfolioValueProto)
    {
    }

    public void updateAccountTimeProtoBuf(Pb.AccountUpdateTime accountUpdateTimeProto)
    {
    }

    public void accountDataEndProtoBuf(Pb.AccountDataEnd accountDataEndProto)
    {
    }

    public void managedAccountsProtoBuf(Pb.ManagedAccounts managedAccountsProto)
    {
    }

    public void positionProtoBuf(Pb.Position positionProto)
    {
    }

    public void positionEndProtoBuf(Pb.PositionEnd positionEndProto)
    {
    }

    public void accountSummaryProtoBuf(Pb.AccountSummary accountSummaryProto)
    {
    }

    public void accountSummaryEndProtoBuf(Pb.AccountSummaryEnd accountSummaryEndProto)
    {
    }

    public void positionMultiProtoBuf(Pb.PositionMulti positionMultiProto)
    {
    }

    public void positionMultiEndProtoBuf(Pb.PositionMultiEnd positionMultiEndProto)
    {
    }

    public void accountUpdateMultiProtoBuf(Pb.AccountUpdateMulti accountUpdateMultiProto)
    {
    }

    public void accountUpdateMultiEndProtoBuf(Pb.AccountUpdateMultiEnd accountUpdateMultiEndProto)
    {
    }

    public void historicalDataProtoBuf(Pb.HistoricalData historicalDataProto)
    {
    }

    public void historicalDataUpdateProtoBuf(Pb.HistoricalDataUpdate historicalDataUpdateProto)
    {
    }

    public void historicalDataEndProtoBuf(Pb.HistoricalDataEnd historicalDataEndProto)
    {
    }

    public void realTimeBarTickProtoBuf(Pb.RealTimeBarTick realTimeBarTickProto)
    {
    }

    public void headTimestampProtoBuf(Pb.HeadTimestamp headTimestampProto)
    {
    }

    public void histogramDataProtoBuf(Pb.HistogramData histogramDataProto)
    {
    }

    public void historicalTicksProtoBuf(Pb.HistoricalTicks historicalTicksProto)
    {
    }

    public void historicalTicksBidAskProtoBuf(Pb.HistoricalTicksBidAsk historicalTicksBidAskProto)
    {
    }

    public void historicalTicksLastProtoBuf(Pb.HistoricalTicksLast historicalTicksLastProto)
    {
    }

    public void tickByTickDataProtoBuf(Pb.TickByTickData tickByTickDataProto)
    {
    }

    public void updateNewsBulletinProtoBuf(Pb.NewsBulletin newsBulletinProto)
    {
    }

    public void newsArticleProtoBuf(Pb.NewsArticle newsArticleProto)
    {
    }

    public void newsProvidersProtoBuf(Pb.NewsProviders newsProvidersProto)
    {
    }

    public void historicalNewsProtoBuf(Pb.HistoricalNews historicalNewsProto)
    {
    }

    public void historicalNewsEndProtoBuf(Pb.HistoricalNewsEnd historicalNewsEndProto)
    {
    }

    public void wshMetaDataProtoBuf(Pb.WshMetaData wshMetaDataProto)
    {
    }

    public void wshEventDataProtoBuf(Pb.WshEventData wshEventDataProto)
    {
    }

    public void tickNewsProtoBuf(Pb.TickNews tickNewsProto)
    {
    }

    public void scannerParametersProtoBuf(Pb.ScannerParameters scannerParametersProto)
    {
    }

    public void scannerDataProtoBuf(Pb.ScannerData scannerDataProto)
    {
    }

    public void fundamentalsDataProtoBuf(Pb.FundamentalsData fundamentalsDataProto)
    {
    }

    public void pnlProtoBuf(Pb.PnL pnlProto)
    {
    }

    public void pnlSingleProtoBuf(Pb.PnLSingle pnlSingleProto)
    {
    }

    public void receiveFAProtoBuf(Pb.ReceiveFA receiveFAProto)
    {
    }

    public void replaceFAEndProtoBuf(Pb.ReplaceFAEnd replaceFAEndProto)
    {
    }

    public void commissionAndFeesReportProtoBuf(Pb.CommissionAndFeesReport commissionAndFeesReportProto)
    {
    }

    public void historicalScheduleProtoBuf(Pb.HistoricalSchedule historicalScheduleProto)
    {
    }

    public void rerouteMarketDataRequestProtoBuf(Pb.RerouteMarketDataRequest rerouteMarketDataRequestProto)
    {
    }

    public void rerouteMarketDepthRequestProtoBuf(Pb.RerouteMarketDepthRequest rerouteMarketDepthRequestProto)
    {
    }

    public void secDefOptParameterProtoBuf(Pb.SecDefOptParameter secDefOptParameterProto)
    {
    }

    public void secDefOptParameterEndProtoBuf(Pb.SecDefOptParameterEnd secDefOptParameterEndProto)
    {
    }

    public void softDollarTiersProtoBuf(Pb.SoftDollarTiers softDollarTiersProto)
    {
    }

    public void familyCodesProtoBuf(Pb.FamilyCodes familyCodesProto)
    {
    }

    public void symbolSamplesProtoBuf(Pb.SymbolSamples symbolSamplesProto)
    {
    }

    public void smartComponentsProtoBuf(Pb.SmartComponents smartComponentsProto)
    {
    }

    public void marketRuleProtoBuf(Pb.MarketRule marketRuleProto)
    {
    }

    public void userInfoProtoBuf(Pb.UserInfo userInfoProto)
    {
    }

    public void nextValidIdProtoBuf(Pb.NextValidId nextValidIdProto)
    {
    }

    public void currentTimeProtoBuf(Pb.CurrentTime currentTimeProto)
    {
    }

    public void currentTimeInMillisProtoBuf(Pb.CurrentTimeInMillis currentTimeInMillisProto)
    {
    }

    public void verifyMessageApiProtoBuf(Pb.VerifyMessageApi verifyMessageApiProto)
    {
    }

    public void verifyCompletedProtoBuf(Pb.VerifyCompleted verifyCompletedProto)
    {
    }

    public void displayGroupListProtoBuf(Pb.DisplayGroupList displayGroupListProto)
    {
    }

    public void displayGroupUpdatedProtoBuf(Pb.DisplayGroupUpdated displayGroupUpdatedProto)
    {
    }

    public void marketDepthExchangesProtoBuf(Pb.MarketDepthExchanges marketDepthExchangesProto)
    {
    }

    public void configResponseProtoBuf(Pb.ConfigResponse configResponseProto)
    {
    }

    public void updateConfigResponseProtoBuf(Pb.UpdateConfigResponse updateConfigResponseProto)
    {
    }
}
#endif
