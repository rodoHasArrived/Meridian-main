using System.Text.Json;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Query;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class WorkstationRetainedQueryExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-query-export-" + Guid.NewGuid().ToString("N"));
    private DataQueryOptions Options => new() { MaxRows = 2, RetainedResultsRoot = Path.Combine(_root, "private") };
    private string Queryable => Path.Combine(_root, "queryable");

    [Fact]
    public async Task Export_UsesEveryRowFromOneExactExecutionBeyondPreviewAndDoesNotRerunSql()
    {
        Directory.CreateDirectory(Queryable);
        var catalog = new Mock<IStorageCatalogService>();
        catalog.Setup(value => value.SearchFiles(It.IsAny<CatalogSearchCriteria>())).Returns(Array.Empty<IndexedFileEntry>());
        var service = new DuckDbQueryService(catalog.Object, new StorageOptions { RootPath = Queryable });
        var options = Options;
        using var services = Services(service, options);
        var context = Context(services);
        var query = await WorkstationEndpoints.ExecuteRetainedWorkstationQueryAsync(service,
            "SELECT range AS ordinal, CAST(range / 100 AS DECIMAL(20,8)) AS amount FROM range(5)", options, context);
        Assert.True(query.Success, query.Error);
        Assert.True(query.Truncated);
        Assert.Equal(2, query.RowCount);
        Assert.Equal(5, query.RetainedRowCount);
        Assert.NotNull(query.ExecutionId);
        var request = new WorkstationTableExportRequest(["0:ordinal", "1:amount"], SortColumnId: "0:ordinal", SortDirection: "desc",
            Filters: new Dictionary<string, string> { ["executionId"] = query.ExecutionId! });
        var result = await WorkstationEndpoints.TryExportRetainedDataQueryAsync("data-query-results", request, context, new(JsonSerializerDefaults.Web));
        var exported = Assert.IsType<JsonHttpResult<WorkstationTableExportDto>>(result).Value!;
        Assert.Equal(5, exported.RowCount);
        Assert.StartsWith("4,0.04000000,", exported.Csv.Split("\r\n")[1]);
        Assert.Contains("Context · Executed SQL SHA256", exported.Csv);
        catalog.Verify(value => value.SearchFiles(It.IsAny<CatalogSearchCriteria>()), Times.Once, "export reads the retained execution, never the source or current SQL editor");
        var editedSql = request with { Filters = new Dictionary<string, string> { ["executionId"] = query.ExecutionId!, ["sql"] = "SELECT 'edited'" } };
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await WorkstationEndpoints.TryExportRetainedDataQueryAsync(
            "data-query-results", editedSql, context, new(JsonSerializerDefaults.Web))).StatusCode);
    }

    [Fact]
    public async Task Export_PreservesExactDecimalTypesProtectsTextAndFiltersCompleteSource()
    {
        var options = Options;
        var store = new RetainedDataQueryStore(options, Queryable);
        await using var sink = await store.CreateAsync(new("tenant-a", "company-a", "operator-a"));
        await sink.BeginAsync("SELECT exact_amount, memo FROM source", ["amount", "memo"], ["DECIMAL(38,8)", "VARCHAR"], default);
        await sink.WriteRowAsync(["-123456789012345678901234567890.12345678", "=SUM(A1:A2) keep"], default);
        await sink.WriteRowAsync(["-2.00000000", "drop"], default);
        var receipt = await sink.CompleteAsync(2, default);
        var service = new DuckDbQueryService(Mock.Of<IStorageCatalogService>(), new StorageOptions { RootPath = Queryable });
        using var services = Services(service, options);
        var request = new WorkstationTableExportRequest(["0:amount", "1:memo"], SearchText: "keep",
            Filters: new Dictionary<string, string> { ["executionId"] = receipt.ExecutionId });
        var result = await WorkstationEndpoints.TryExportRetainedDataQueryAsync("data-query-results", request, Context(services), new(JsonSerializerDefaults.Web));
        var exported = Assert.IsType<JsonHttpResult<WorkstationTableExportDto>>(result).Value!;
        Assert.Equal(1, exported.RowCount);
        Assert.Contains("-123456789012345678901234567890.12345678,'=SUM(A1:A2) keep", exported.Csv);
        Assert.DoesNotContain("drop", exported.Csv);
    }

    [Fact]
    public async Task ExecuteRetained_PreservesWideDecimalDigitsWithoutSystemDecimalRounding()
    {
        Directory.CreateDirectory(Queryable);
        var catalog = new Mock<IStorageCatalogService>();
        catalog.Setup(value => value.SearchFiles(It.IsAny<CatalogSearchCriteria>())).Returns(Array.Empty<IndexedFileEntry>());
        var service = new DuckDbQueryService(catalog.Object, new StorageOptions { RootPath = Queryable });
        var options = Options;
        using var services = Services(service, options);
        var result = await WorkstationEndpoints.ExecuteRetainedWorkstationQueryAsync(service,
            "SELECT CAST('-123456789012345678901234567890.12345678' AS DECIMAL(38,8)) AS amount", options, Context(services));
        Assert.True(result.Success, result.Error);
        Assert.Equal("-123456789012345678901234567890.12345678", result.Rows[0][0]);
        var snapshot = await new RetainedDataQueryStore(options, Queryable).ReadAsync(new("tenant-a", "company-a", "operator-a"), result.ExecutionId!);
        Assert.Equal(result.Rows[0][0], snapshot.Rows[0][0]);
    }

    [Fact]
    public async Task ExecuteRetained_RefusesNestedTypesInsteadOfExportingClrTypeNames()
    {
        Directory.CreateDirectory(Queryable);
        var catalog = new Mock<IStorageCatalogService>();
        catalog.Setup(value => value.SearchFiles(It.IsAny<CatalogSearchCriteria>())).Returns(Array.Empty<IndexedFileEntry>());
        var service = new DuckDbQueryService(catalog.Object, new StorageOptions { RootPath = Queryable });
        var options = Options;
        using var services = Services(service, options);
        var result = await WorkstationEndpoints.ExecuteRetainedWorkstationQueryAsync(service,
            "SELECT [1,2] AS parts, {'amount':12.34} AS detail", options, Context(services));
        Assert.False(result.Success);
        Assert.Contains("Cast", result.Error);
        Assert.Null(result.ExecutionId);
        Assert.Empty(Directory.GetFiles(options.RetainedResultsRoot!, "*.manifest.json"));
    }

    [Fact]
    public async Task ExecuteRetained_RespectsUserLimitAndPreservesTimestampFractions()
    {
        Directory.CreateDirectory(Queryable);
        var catalog = new Mock<IStorageCatalogService>();
        catalog.Setup(value => value.SearchFiles(It.IsAny<CatalogSearchCriteria>())).Returns(Array.Empty<IndexedFileEntry>());
        var service = new DuckDbQueryService(catalog.Object, new StorageOptions { RootPath = Queryable });
        var options = Options;
        using var services = Services(service, options);
        var result = await WorkstationEndpoints.ExecuteRetainedWorkstationQueryAsync(service,
            "SELECT TIME '12:34:56.123456' AS clock, TIMESTAMP '2026-11-11 12:34:56.123456' AS stamp FROM range(5) LIMIT 1", options, Context(services));
        Assert.True(result.Success, result.Error);
        Assert.False(result.Truncated);
        Assert.Equal(1, result.RetainedRowCount);
        Assert.Contains("12:34:56.123456", result.Rows[0][0]);
        Assert.Contains("2026-11-11T12:34:56.123456", result.Rows[0][1]);
    }

    [Fact]
    public async Task Export_RefusesOtherScopeChangedSchemaAndIncompleteSpool()
    {
        var options = Options;
        var store = new RetainedDataQueryStore(options, Queryable);
        await using var sink = await store.CreateAsync(new("tenant-a", "company-a", "operator-a"));
        await sink.BeginAsync("SELECT 1", ["amount"], ["INTEGER"], default);
        await sink.WriteRowAsync(["1"], default);
        var receipt = await sink.CompleteAsync(1, default);
        var service = new DuckDbQueryService(Mock.Of<IStorageCatalogService>(), new StorageOptions { RootPath = Queryable });
        using var services = Services(service, options);
        var request = new WorkstationTableExportRequest(["0:amount"], Filters: new Dictionary<string, string> { ["executionId"] = receipt.ExecutionId });
        var foreign = Context(services);
        foreign.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-b";
        Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await WorkstationEndpoints.TryExportRetainedDataQueryAsync(
            "data-query-results", request, foreign, new(JsonSerializerDefaults.Web))).StatusCode);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await WorkstationEndpoints.TryExportRetainedDataQueryAsync(
            "data-query-results", request with { ColumnIds = ["0:edited_column"] }, Context(services), new(JsonSerializerDefaults.Web))).StatusCode);
        File.Delete(Path.Combine(store.RootPath, receipt.ExecutionId + ".rows"));
        Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(await WorkstationEndpoints.TryExportRetainedDataQueryAsync(
            "data-query-results", request, Context(services), new(JsonSerializerDefaults.Web))).StatusCode);
    }

    [Theory]
    [InlineData("-999999999999999999999999999999.00000001", "-999999999999999999999999999999.00000000", -1)]
    [InlineData("9007199254740993", "9007199254740992", 1)]
    [InlineData("1E-20", "0.00000000000000000001", 0)]
    public void NumericSort_UsesExactSourcePrecision(string left, string right, int expected)
        => Assert.Equal(expected, Math.Sign(WorkstationEndpoints.CompareRetainedSqlCells(left, right, "DECIMAL(38,8)")));

    private static ServiceProvider Services(DuckDbQueryService service, DataQueryOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<DataQueryOptions>>();
        monitor.SetupGet(value => value.CurrentValue).Returns(options);
        return new ServiceCollection().AddSingleton(service).AddSingleton(monitor.Object).BuildServiceProvider();
    }
    private static DefaultHttpContext Context(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewHistoricalData;
        context.Items[LoginSessionMiddleware.CurrentUserKey] = "operator-a";
        context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
        context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
        return context;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
