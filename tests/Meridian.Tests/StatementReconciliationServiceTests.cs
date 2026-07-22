using Meridian.FinancialOperations.Reconciliation;
using Meridian.Domain.Reconciliation;

namespace Meridian.Tests;

public sealed class StatementReconciliationServiceTests
{
    [Fact]
    public void Fingerprint_IsDeterministic()
    {
        var a = DeterministicFingerprint.Compute("abc");
        var b = DeterministicFingerprint.Compute("abc");
        Assert.Equal(a, b);
    }

    [Fact]
    public void MatchRows_ProducesMatchAndCaseBranches()
    {
        var svc = new StatementReconciliationService();
        var asOf = DateTimeOffset.UtcNow;
        var rows = new[]
        {
            new NormalizedStatementRow("1", StatementRowKind.Position, "AAPL", 10, 0, asOf, "USD", "f1", new Dictionary<string,string>()),
            new NormalizedStatementRow("2", StatementRowKind.CashBalance, string.Empty, 0, 100, asOf, "USD", "f2", new Dictionary<string,string>())
        };
        var internalPositions = new[]
        {
            new InternalPortfolioPosition("pos-1", "unknown-account", "AAPL", DateOnly.FromDateTime(asOf.UtcDateTime), 10, null, "internal:pos-1")
        };

        var result = svc.MatchRows(rows, internalPositions);
        var match = Assert.Single(result.Matches);
        Assert.Equal("1", match.RowId);
        Assert.Equal("internal:pos-1", match.PositionId);
        Assert.Equal("high", match.Confidence);
        Assert.Equal("statement-position-exact-v1", match.ToleranceRuleId);
        Assert.Single(result.Cases);
        var reconciliationCase = result.Cases[0];
        Assert.Equal("case:2", reconciliationCase.CaseId);
        Assert.Equal("fund-ops", reconciliationCase.Owner);
        Assert.Equal("NeedsInvestigation", reconciliationCase.Disposition);
        Assert.Equal(0, reconciliationCase.AgingDays);
        Assert.NotNull(reconciliationCase.DueAtUtc);
        var attachment = Assert.Single(reconciliationCase.Attachments);
        Assert.Equal("ExternalStatementRow", attachment.EvidenceKind);
        Assert.Equal("f2", attachment.ContentHash);
        Assert.NotNull(reconciliationCase.BreakExplanation);
        Assert.Contains("cash", reconciliationCase.BreakExplanation.ProbableCause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Meridian ledger", reconciliationCase.BreakExplanation.SourceSystems);
        Assert.Contains("cash ledger", reconciliationCase.BreakExplanation.LedgerImpact, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Compare the statement cash balance", reconciliationCase.BreakExplanation.SuggestedNextAction);
    }

    [Fact]
    public void MatchRows_PositionWithoutInternalPosition_CreatesCaseInsteadOfMatch()
    {
        // Position rows go through the shared StatementMatchingEngine; without a matching internal
        // portfolio position they must surface as break cases, never as auto high-confidence matches.
        var svc = new StatementReconciliationService();
        var rows = new[]
        {
            new NormalizedStatementRow("1", StatementRowKind.Position, "AAPL", 10, 0, DateTimeOffset.UtcNow, "USD", "f1", new Dictionary<string,string>())
        };

        var result = svc.MatchRows(rows);

        Assert.Empty(result.Matches);
        var reconciliationCase = Assert.Single(result.Cases);
        Assert.Equal("case:1", reconciliationCase.CaseId);
        Assert.NotNull(reconciliationCase.BreakExplanation);
        Assert.Contains("position", reconciliationCase.BreakExplanation.ProbableCause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_SampleBrokerProfile_PreservesSourceTradeCodes()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            // The sample-broker profile maps both BUY and SELL to the canonical "trade" activity.
            // Typed import must classify the row kind from the mapped value but retain the original
            // source code on the transaction, so buys and sells remain distinguishable downstream.
            await File.WriteAllLinesAsync(filePath,
            [
                "BrokerAccount,Ticker,Units,ExecutionPrice,NetCash,TxnCode,TradeDate",
                "BRK-1,MSFT,5,400,0,BUY,2026-05-27",
                "BRK-1,MSFT,3,410,0,SELL,2026-05-28"
            ]);

            var result = await svc.ImportAsync(
                "broker", filePath, StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId, CancellationToken.None);

            Assert.Equal(2, result.Transactions.Count);
            Assert.Contains(result.Transactions, t => t.TransactionType == "BUY");
            Assert.Contains(result.Transactions, t => t.TransactionType == "SELL");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_Returns_Typed_Normalized_Broker_Collections_With_Source_Trace()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate,accountId,externalAccountId,securityId,unresolvedIdentifier,currency,marketValue,settlementDate,amount,feesCommission,externalReference",
                "EXT-A1,SPY,10,500,0,position,2026-05-29,ACC-1,EXT-A1,SEC-SPY,,USD,5000,2026-06-01,5000,0,POS-1",
                "EXT-A1,,0,0,125.25,cash,2026-05-29,ACC-1,EXT-A1,,,USD,0,2026-05-29,125.25,0,CASH-1",
                "EXT-A1,QQQ,2,400,0,buy,2026-05-30,ACC-1,EXT-A1,,QQQ,USD,800,2026-06-02,800,1.25,TXN-1"
            ]);

            var result = await svc.ImportAsync("broker", filePath, CancellationToken.None);

            Assert.Equal(3, result.RowCount);
            Assert.Single(result.Positions);
            Assert.Single(result.CashBalances);
            Assert.Single(result.Transactions);
            Assert.Equal("ACC-1", result.Positions[0].AccountId);
            Assert.Equal("EXT-A1", result.Positions[0].ExternalAccountId);
            Assert.Equal("SEC-SPY", result.Positions[0].SecurityId);
            Assert.Equal(5000m, result.Positions[0].MarketValue);
            Assert.Equal(new DateOnly(2026, 6, 1), result.Positions[0].SettlementDate);
            Assert.Equal(result.ImportId, result.Positions[0].StatementRunId);
            Assert.False(string.IsNullOrWhiteSpace(result.Positions[0].SourceRowHash));
            Assert.Equal(125.25m, result.CashBalances[0].Amount);
            Assert.Equal(result.ImportId, result.CashBalances[0].StatementRunId);
            Assert.False(string.IsNullOrWhiteSpace(result.CashBalances[0].SourceRowHash));
            Assert.Equal("buy", result.Transactions[0].TransactionType);
            Assert.Equal(1.25m, result.Transactions[0].FeesCommission);
            Assert.Equal("TXN-1", result.Transactions[0].ExternalReference);
            Assert.Equal(result.ImportId, result.Transactions[0].StatementRunId);
            Assert.False(string.IsNullOrWhiteSpace(result.Transactions[0].SourceRowHash));
            Assert.Equal(result.ImportId, result.Securities[0].StatementRunId);
            Assert.False(string.IsNullOrWhiteSpace(result.Securities[0].SourceRowHash));
            Assert.All(result.SourceRows, row =>
            {
                Assert.Equal(result.ImportId, row.StatementRunId);
                Assert.False(string.IsNullOrWhiteSpace(row.SourceRowHash));
                Assert.True(row.RawSnapshot.ContainsKey("rawLine"));
            });
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_Observes_Cancellation_Before_Normalization()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
                "A1,SPY,10,500,0,position,2026-05-29"
            ]);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => svc.ImportAsync("broker", filePath, cts.Token));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ValidateAsync_ThrowsWhenLocalFileMissing()
    {
        var svc = new StatementReconciliationService();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            svc.ValidateAsync("local", "/tmp/does-not-exist.csv", CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_CountsRowsForLocalFile()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath, ["header", "row1", "row2"]);
            var result = await svc.ImportAsync("local", filePath, CancellationToken.None);
            Assert.Equal(2, result.RowCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void StatementMappingProfileRegistry_Exposes_Canonical_And_Sample_Broker_Profiles()
    {
        var registry = StatementMappingProfileRegistry.Defaults;

        var canonical = registry.Resolve(StatementMappingProfileRegistry.CanonicalCsvV1ProfileId);
        var sampleBroker = registry.Resolve(StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId);

        Assert.Equal("account", canonical.FindField(StatementCanonicalField.Account)?.SourceColumn);
        Assert.Equal("symbol", canonical.FindField(StatementCanonicalField.SecurityIdentifier)?.SourceColumn);
        Assert.Equal("cashAmount", canonical.FindField(StatementCanonicalField.CashAmount)?.SourceColumn);
        Assert.Equal("BrokerAccount", sampleBroker.FindField(StatementCanonicalField.Account)?.SourceColumn);
        Assert.Equal("Ticker", sampleBroker.FindField(StatementCanonicalField.SecurityIdentifier)?.SourceColumn);
        Assert.Equal("Commission", sampleBroker.FindField(StatementCanonicalField.FeesCommission)?.SourceColumn);
        Assert.Equal("trade", sampleBroker.MapActivityType("BUY"));
        Assert.Equal("dividend", sampleBroker.MapActivityType("DIV"));
    }

    [Fact]
    public async Task ReconcileAsync_Uses_Selected_SampleBrokerMappingProfile()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "BrokerAccount,Ticker,Units,ExecutionPrice,NetCash,TxnCode,TradeDate,SettleDate,CCY,Commission,BrokerTransactionId",
                "BRK-1,MSFT,12,410,0,POS,2026-05-27,2026-05-29,EUR,0,EXT-1",
                "BRK-1,MSFT,0,0,18.25,DIV,2026-05-28,2026-05-30,EUR,0,EXT-2"
            ]);

            var validation = await svc.ValidateAsync("broker", filePath, StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId, CancellationToken.None);
            var result = await svc.ReconcileAsync("broker", filePath, StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId, CancellationToken.None);
            var intake = await svc.CreateExternalStatementCasesAsync("broker", filePath, StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId, CancellationToken.None);

            Assert.Contains(StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId, validation);
            // Without internal position evidence the POS row is an unresolved break, not a match.
            Assert.Equal(0, result.MatchCount);
            Assert.Equal(2, result.UnresolvedCount);
            Assert.Equal(2, intake.RowCount);
            Assert.Equal(2, intake.Cases.Count);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ValidateAsync_Throws_When_SelectedMappingProfile_RequiredColumnIsMissing()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "BrokerAccount,Units,ExecutionPrice,NetCash,TxnCode,TradeDate",
                "BRK-1,12,410,0,POS,2026-05-27"
            ]);

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
                svc.ValidateAsync("broker", filePath, StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId, CancellationToken.None));

            Assert.Contains("sample-broker-csv-v1", ex.Message);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ReconcileAsync_LocalStatement_WithMappingProfile_ProducesMatchesAndCases()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
                "LOC-1,SPY,10,500,0,position,2026-05-29",
                "LOC-1,,0,0,125.25,cash,2026-05-29"
            ]);

            var result = await svc.ReconcileAsync(
                "local", filePath, StatementMappingProfileRegistry.CanonicalCsvV1ProfileId, CancellationToken.None);

            // With a mapping profile selected the 'local' source is parsed canonically and
            // reconciled; without internal position evidence both rows surface as cases.
            Assert.Equal(0, result.MatchCount);
            Assert.Equal(2, result.UnresolvedCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task CreateExternalStatementCasesAsync_LocalStatement_WithProfile_ProducesCases()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
                "LOC-1,,0,0,125.25,cash,2026-05-29"
            ]);

            var intake = await svc.CreateExternalStatementCasesAsync(
                "local", filePath, StatementMappingProfileRegistry.CanonicalCsvV1ProfileId, CancellationToken.None);

            Assert.Equal(1, intake.RowCount);
            Assert.Single(intake.Cases);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ReconcileAsync_LocalStatement_WithoutProfile_RemainsRawWithZeroCases()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            // A non-canonical local file with no mapping profile keeps the lenient raw passthrough:
            // it is accepted but produces no canonical rows, matches, or cases.
            await File.WriteAllLinesAsync(filePath, ["header", "row1", "row2"]);

            var result = await svc.ReconcileAsync("local", filePath, CancellationToken.None);

            Assert.Equal(0, result.MatchCount);
            Assert.Equal(0, result.UnresolvedCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ReconcileAsync_LocalStatement_WithCustomProfile_MapsArbitraryColumns()
    {
        var customProfile = new StatementMappingProfile(
            "custom-local-v1",
            "Custom Local v1",
            [
                new(StatementCanonicalField.Account, "acct"),
                new(StatementCanonicalField.SecurityIdentifier, "ticker"),
                new(StatementCanonicalField.Quantity, "qty"),
                new(StatementCanonicalField.Price, "px"),
                new(StatementCanonicalField.CashAmount, "cash"),
                new(StatementCanonicalField.ActivityType, "type"),
                new(StatementCanonicalField.TradeDate, "td")
            ],
            [
                new("position", "position"),
                new("cash", "cash")
            ]);
        var registry = new StatementMappingProfileRegistry([customProfile]);
        var svc = new StatementReconciliationService(registry);
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "acct,ticker,qty,px,cash,type,td",
                "LOC-1,SPY,10,500,0,position,2026-05-29",
                "LOC-1,,0,0,125.25,cash,2026-05-29"
            ]);

            var result = await svc.ReconcileAsync("local", filePath, "custom-local-v1", CancellationToken.None);

            Assert.Equal(0, result.MatchCount);
            Assert.Equal(2, result.UnresolvedCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ValidateAsync_LocalStatement_WithProfile_ValidatesHeaderAndThrowsOnMismatch()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            // With a profile selected, a 'local' source is now header-validated; a non-canonical
            // header against the canonical profile must be rejected rather than silently accepted.
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol",
                "LOC-1,SPY"
            ]);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                svc.ValidateAsync("local", filePath, StatementMappingProfileRegistry.CanonicalCsvV1ProfileId, CancellationToken.None));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_LocalStatement_WithProfile_ProducesTypedCollections()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
                "LOC-1,SPY,10,500,0,position,2026-05-29",
                "LOC-1,,0,0,125.25,cash,2026-05-29"
            ]);

            var result = await svc.ImportAsync(
                "local", filePath, StatementMappingProfileRegistry.CanonicalCsvV1ProfileId, CancellationToken.None);

            // With a profile, a 'local' source is normalized into typed collections rather than
            // returned as raw rows.
            Assert.Equal(2, result.RowCount);
            Assert.Single(result.Positions);
            Assert.Single(result.CashBalances);
            Assert.Equal(10m, result.Positions[0].Quantity);
            Assert.Equal(125.25m, result.CashBalances[0].Amount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_LocalStatement_WithoutProfile_RemainsRawRowCount()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            // Backward compatibility: no profile means the lenient raw passthrough is preserved.
            await File.WriteAllLinesAsync(filePath, ["header", "row1", "row2"]);

            var result = await svc.ImportAsync("local", filePath, CancellationToken.None);

            Assert.Equal(2, result.RowCount);
            Assert.Empty(result.Positions);
            Assert.Empty(result.CashBalances);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_RowOmittingOptionalTrailingColumns_DefaultsOptionalValues()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            // The header advertises optional trailing columns, but the data row stops after the
            // required tradeDate. Such rows must import with defaulted optional values rather than
            // being rejected for being shorter than the full header.
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate,currency,marketValue,settlementDate",
                "EXT-1,SPY,10,500,0,position,2026-05-29"
            ]);

            var result = await svc.ImportAsync("broker", filePath, CancellationToken.None);

            Assert.Equal(1, result.RowCount);
            var position = Assert.Single(result.Positions);
            Assert.Equal("USD", position.Currency);
            Assert.Equal(5000m, position.MarketValue);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_SymbolLessFeeRow_ImportsAsTransaction()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            // Account-level fee rows legitimately omit the symbol; import must accept them
            // (as transactions) rather than rejecting them for a missing SecurityIdentifier.
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
                "EXT-1,,0,0,-9.99,fee,2026-05-29"
            ]);

            var result = await svc.ImportAsync("broker", filePath, CancellationToken.None);

            Assert.Equal(1, result.RowCount);
            var transaction = Assert.Single(result.Transactions);
            Assert.Equal("fee", transaction.TransactionType);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task CreateExternalStatementCasesAsync_SymbolLessFeeRow_ParsesWithoutError()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            // Case intake must accept symbol-less fee rows just like the import path, so a local+profile
            // file does not import successfully but then throw during reconciliation.
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
                "EXT-1,,0,0,-9.99,fee,2026-05-29"
            ]);

            var intake = await svc.CreateExternalStatementCasesAsync(
                "local", filePath, StatementMappingProfileRegistry.CanonicalCsvV1ProfileId, CancellationToken.None);

            Assert.Equal(1, intake.RowCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_SymbolLessPositionRow_Throws()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            // A position row must carry a security identifier; a blank one is a mapping error and must
            // be rejected rather than auto-matched as a high-confidence position.
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
                "EXT-1,,10,500,0,position,2026-05-29"
            ]);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                svc.ImportAsync("broker", filePath, CancellationToken.None));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_SymbolLessTradeRow_Throws()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            // A trade is security-bearing and must not bypass downstream security-resolution
            // evidence merely because it is normalized as a transaction rather than a position.
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
                "EXT-1,,10,500,0,trade,2026-05-29"
            ]);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                svc.ImportAsync("broker", filePath, CancellationToken.None));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_BlankOptionalColumns_ApplyDefaults()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            // Optional columns present but blank must fall back to their defaults (currency -> USD,
            // accountId -> account), not become empty strings.
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate,accountId,currency",
                "EXT-1,SPY,10,500,0,position,2026-05-29,,"
            ]);

            var result = await svc.ImportAsync("broker", filePath, CancellationToken.None);

            var position = Assert.Single(result.Positions);
            Assert.Equal("USD", position.Currency);
            Assert.Equal("EXT-1", position.AccountId);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ReconcileAsync_FeeRow_WithAmountColumn_SurfacesMaterialCase()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            // The fee carries its value in the amount column with cashAmount=0. Case intake must use
            // the mapped amount so the material break is surfaced; if it derived a zero amount the
            // break would be classified as immaterial and no case would be created.
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate,amount",
                "EXT-1,,0,0,0,fee,2026-05-29,-50"
            ]);

            var result = await svc.ReconcileAsync(
                "local", filePath, StatementMappingProfileRegistry.CanonicalCsvV1ProfileId, CancellationToken.None);

            Assert.Equal(1, result.UnresolvedCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAndCaseIntake_EmptyProfiledStatement_ShareImportId()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            // Valid header, no data rows: import and intake must still refer to the same run id.
            await File.WriteAllLinesAsync(filePath,
            [
                "account,symbol,quantity,price,cashAmount,activityType,tradeDate"
            ]);

            var import = await svc.ImportAsync(
                "local", filePath, StatementMappingProfileRegistry.CanonicalCsvV1ProfileId, CancellationToken.None);
            var intake = await svc.CreateExternalStatementCasesAsync(
                "local", filePath, StatementMappingProfileRegistry.CanonicalCsvV1ProfileId, CancellationToken.None);

            Assert.Equal(0, import.RowCount);
            Assert.Equal(0, intake.RowCount);
            Assert.Equal(import.ImportId, intake.ImportId);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

}
