using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Export;
using Meridian.Identity.Auth;
using Meridian.Storage.Export;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

public sealed class ExportEndpointsTests
{
    [Fact]
    public async Task MapExportEndpoints_Preview_ShouldReturnReadOnlyProfileScope()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        using var preview = await ReadJsonAsync(
            client,
            "/api/export/preview?profile=audit-pack&symbols=SPY,QQQ&eventTypes=Trade&sampleSize=999");

        preview.RootElement.GetProperty("previewOnly").GetBoolean().Should().BeTrue();
        preview.RootElement.GetProperty("profileId").GetString().Should().Be("audit-pack");
        preview.RootElement.GetProperty("symbols").EnumerateArray()
            .Select(symbol => symbol.GetString())
            .Should()
            .Equal("SPY", "QQQ");
        preview.RootElement.GetProperty("eventTypes")[0].GetString().Should().Be("Trade");
        preview.RootElement.GetProperty("sampleSize").GetInt32().Should().Be(500);
        preview.RootElement.GetProperty("canRunExport").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task MapExportEndpoints_AnalysisWithoutService_ShouldReturnUnavailable()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/export/analysis", new
        {
            profileId = " audit-pack ",
            symbols = new[] { " SPY ", "", "spy", "QQQ" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await response.Content.ReadFromJsonAsync<ExportAnalysisApiResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.Success.Should().BeFalse();
        payload.Status.Should().Be("unavailable");
        payload.ProfileId.Should().Be("audit-pack");
        payload.Error.Should().Be("Export service not available");
        payload.Files.Should().BeEmpty();
        payload.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task MapExportEndpoints_FormatsWithoutService_ShouldFailClosed()
    {
        await using var app = await CreateAppAsync();

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.ExportFormats);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task MapExportEndpoints_Formats_ShouldRoundTripTypedExecutableCapabilities()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            await using var app = await CreateAppAsync(new AnalysisExportService(dataRoot));

            var response = await app.GetTestClient().GetAsync(UiApiRoutes.ExportFormats);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var payload = await response.Content.ReadFromJsonAsync<ExportFormatsResponse>(JsonOptions);
            payload.Should().NotBeNull();
            payload!.Formats.Should().NotBeNull();
            payload.Formats!.Select(format => format.Extension)
                .Should().BeEquivalentTo(".csv", ".parquet", ".xlsx", ".arrow");
            payload.Formats.Should().OnlyContain(format => !format.SupportsCompression);
        }
        finally
        {
            DeleteDirectory(dataRoot);
        }
    }

    [Theory]
    [InlineData("python-pandas", "parquet", ".parquet")]
    [InlineData("excel", "xlsx", ".xlsx")]
    [InlineData("arrow-feather", "arrow", ".arrow")]
    public async Task MapExportEndpoints_Analysis_ShouldReturnActualGeneratedFormat(
        string profileId,
        string format,
        string extension)
    {
        var dataRoot = CreateDataRoot();
        string? outputDirectory = null;

        try
        {
            await using var app = await CreateAppAsync(new AnalysisExportService(dataRoot));
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.ExportAnalysis,
                new ExportAnalysisApiRequest(
                    profileId,
                    new[] { "SPY" },
                    format,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var payload = await response.Content.ReadFromJsonAsync<ExportAnalysisApiResponse>(JsonOptions);
            payload.Should().NotBeNull();
            payload!.Success.Should().BeTrue();
            payload.ProfileId.Should().Be(profileId);
            payload.Files.Should().ContainSingle();
            payload.Files[0].Format.Should().Be(format);
            payload.Files[0].Path.Should().EndWith(extension);

            outputDirectory = payload.OutputDirectory;
            outputDirectory.Should().NotBeNullOrWhiteSpace();
            File.Exists(Path.Combine(outputDirectory!, payload.Files[0].Path)).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(outputDirectory);
            DeleteDirectory(dataRoot);
        }
    }

    [Fact]
    public void CreateExportResponse_WithProfileFormatMismatch_ShouldFailClosed()
    {
        var result = ExportResult.CreateSuccess("excel", "managed-output");
        result.Files =
        [
            new ExportedFile
            {
                Path = Path.Combine("managed-output", "SPY.csv"),
                RelativePath = "SPY.csv",
                Symbol = "SPY",
                Format = "csv",
                SizeBytes = 128,
                RecordCount = 2
            }
        ];
        result.FilesGenerated = 1;
        result.TotalRecords = 2;
        result.TotalBytes = 128;
        result.CompletedAt = DateTime.UtcNow;

        var response = ExportEndpoints.CreateExportResponse(result, ExportProfile.Excel);

        response.Success.Should().BeFalse();
        response.Status.Should().Be("failed");
        response.Error.Should().Contain("expected 'xlsx'");
        response.Error.Should().Contain("reported 'csv'");
        response.Files.Should().ContainSingle(file => file.Format == "csv");
    }

    [Theory]
    [InlineData("excel", "parquet", "produces 'xlsx'")]
    [InlineData("python-pandas", "xlsx", "produces 'parquet'")]
    [InlineData("python-pandas", "hdf5", "Unsupported export format")]
    public async Task MapExportEndpoints_AnalysisWithMismatchedOrUnsupportedFormat_ShouldReturnBadRequest(
        string profileId,
        string format,
        string expectedError)
    {
        var dataRoot = CreateDataRoot();

        try
        {
            await using var app = await CreateAppAsync(new AnalysisExportService(dataRoot));
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.ExportAnalysis,
                new ExportAnalysisApiRequest(profileId, new[] { "SPY" }, format, null, null));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var payload = await response.Content.ReadFromJsonAsync<ExportAnalysisApiResponse>(JsonOptions);
            payload.Should().NotBeNull();
            payload!.Success.Should().BeFalse();
            payload.Status.Should().Be("invalid");
            payload.Error.Should().Contain(expectedError);
            payload.Files.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectory(dataRoot);
        }
    }

    [Fact]
    public async Task MapExportEndpoints_AnalysisWithUnknownProfile_ShouldReturnBadRequest()
    {
        var dataRoot = CreateDataRoot();

        try
        {
            await using var app = await CreateAppAsync(new AnalysisExportService(dataRoot));
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(
                UiApiRoutes.ExportAnalysis,
                new ExportAnalysisApiRequest("missing-profile", new[] { "SPY" }, "parquet", null, null));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var payload = await response.Content.ReadFromJsonAsync<ExportAnalysisApiResponse>(JsonOptions);
            payload.Should().NotBeNull();
            payload!.Status.Should().Be("invalid");
            payload.Error.Should().Contain("Unknown export profile");
        }
        finally
        {
            DeleteDirectory(dataRoot);
        }
    }

    [Fact]
    public async Task MapExportEndpoints_AnalysisWithNoMatchingData_ShouldReturnFailedWithoutArtifacts()
    {
        var dataRoot = CreateEmptyDataRoot();
        string? outputDirectory = null;

        try
        {
            await using var app = await CreateAppAsync(new AnalysisExportService(dataRoot));
            var response = await app.GetTestClient().PostAsJsonAsync(
                UiApiRoutes.ExportAnalysis,
                new ExportAnalysisApiRequest(
                    "python-pandas",
                    new[] { "SPY" },
                    "parquet",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var payload = await response.Content.ReadFromJsonAsync<ExportAnalysisApiResponse>(JsonOptions);
            payload.Should().NotBeNull();
            payload!.Success.Should().BeFalse();
            payload.Status.Should().Be("failed");
            payload.Error.Should().Contain("No source data");
            payload.FilesGenerated.Should().Be(0);
            payload.Files.Should().BeEmpty();
            outputDirectory = payload.OutputDirectory;
        }
        finally
        {
            DeleteDirectory(outputDirectory);
            DeleteDirectory(dataRoot);
        }
    }

    [Fact]
    public async Task MapExportEndpoints_Orderflow_ShouldReturnPayloadSuccessAndActualArtifactFormat()
    {
        var dataRoot = CreateDataRoot();
        string? outputDirectory = null;

        try
        {
            await using var app = await CreateAppAsync(new AnalysisExportService(dataRoot));
            var response = await app.GetTestClient().PostAsJsonAsync(
                UiApiRoutes.ExportOrderflow,
                new
                {
                    symbols = new[] { "SPY" },
                    fromDate = "2026-01-01",
                    toDate = "2026-01-05",
                    aggregation = "raw",
                    format = "xlsx"
                });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var payload = await response.Content.ReadFromJsonAsync<SpecializedExportApiResponse>(JsonOptions);
            payload.Should().NotBeNull();
            payload!.Success.Should().BeTrue();
            payload.Status.Should().Be("completed");
            payload.Format.Should().Be("xlsx");
            payload.Files.Should().ContainSingle();
            payload.Files[0].Format.Should().Be("xlsx");
            payload.Files[0].Path.Should().EndWith(".xlsx");
            outputDirectory = payload.OutputDirectory;
        }
        finally
        {
            DeleteDirectory(outputDirectory);
            DeleteDirectory(dataRoot);
        }
    }

    [Fact]
    public async Task MapExportEndpoints_QualityReport_ShouldFailClosedWithoutRawArtifacts()
    {
        var dataRoot = CreateDataRoot();

        try
        {
            await using var app = await CreateAppAsync(new AnalysisExportService(dataRoot));
            var response = await app.GetTestClient().PostAsJsonAsync(
                UiApiRoutes.ExportQualityReport,
                new
                {
                    symbols = new[] { "SPY" },
                    format = "csv",
                    includeCharts = false,
                    includeMetadata = true
                });

            response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
            var payload = await response.Content.ReadFromJsonAsync<SpecializedExportApiResponse>(JsonOptions);
            payload.Should().NotBeNull();
            payload!.Success.Should().BeFalse();
            payload.Status.Should().Be("unavailable");
            payload.Format.Should().Be("csv");
            payload.Error.Should().Contain("not connected");
            payload.Error.Should().Contain("No raw analysis extract was produced");
            payload.FilesGenerated.Should().Be(0);
            payload.TotalRecords.Should().Be(0);
            payload.TotalBytes.Should().Be(0);
            payload.OutputDirectory.Should().BeNull();
            payload.Files.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectory(dataRoot);
        }
    }

    public static TheoryData<string, string> SpecializedNoDataRequests => new()
    {
        {
            UiApiRoutes.ExportOrderflow,
            """{"symbols":["SPY"],"format":"parquet","aggregation":"raw","includeMetadata":true}"""
        },
        {
            UiApiRoutes.ExportIntegrity,
            """{"symbols":["SPY"],"eventTypes":["Integrity"],"format":"csv","includeMetadata":true}"""
        },
        {
            UiApiRoutes.ExportStrategyPackage,
            """{"symbols":["SPY"],"format":"parquet","includeMetadata":true,"includeQualityReport":false}"""
        }
    };

    [Theory]
    [MemberData(nameof(SpecializedNoDataRequests))]
    public async Task MapExportEndpoints_SpecializedRouteWithNoData_ShouldReturnFailedWithoutArtifacts(
        string route,
        string requestJson)
    {
        var dataRoot = CreateEmptyDataRoot();
        string? outputDirectory = null;

        try
        {
            await using var app = await CreateAppAsync(new AnalysisExportService(dataRoot));
            using var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
            var response = await app.GetTestClient().PostAsync(route, content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var payload = await response.Content.ReadFromJsonAsync<SpecializedExportApiResponse>(JsonOptions);
            payload.Should().NotBeNull();
            payload!.Success.Should().BeFalse();
            payload.Status.Should().Be("failed");
            payload.FilesGenerated.Should().Be(0);
            payload.Files.Should().BeEmpty();
            payload.Error.Should().Contain("No source data");
            outputDirectory = payload.OutputDirectory;
        }
        finally
        {
            DeleteDirectory(outputDirectory);
            DeleteDirectory(dataRoot);
        }
    }

    public static TheoryData<string, string, string> UnsupportedSpecializedOptions => new()
    {
        {
            UiApiRoutes.ExportOrderflow,
            """{"symbols":["SPY"],"format":"parquet","aggregation":"Minute"}""",
            "aggregation"
        },
        {
            UiApiRoutes.ExportIntegrity,
            """{"symbols":["SPY"],"format":"csv","outputPath":"caller-path"}""",
            "outputPath"
        },
        {
            UiApiRoutes.ExportStrategyPackage,
            """{"symbols":["SPY"],"format":"parquet","name":"model-alpha"}""",
            "name"
        }
    };

    [Theory]
    [MemberData(nameof(UnsupportedSpecializedOptions))]
    public async Task MapExportEndpoints_SpecializedUnsupportedOption_ShouldReturnBadRequest(
        string route,
        string requestJson,
        string expectedError)
    {
        var dataRoot = CreateDataRoot();

        try
        {
            await using var app = await CreateAppAsync(new AnalysisExportService(dataRoot));
            using var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
            var response = await app.GetTestClient().PostAsync(route, content);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var payload = await response.Content.ReadFromJsonAsync<SpecializedExportApiResponse>(JsonOptions);
            payload.Should().NotBeNull();
            payload!.Success.Should().BeFalse();
            payload.Status.Should().Be("invalid");
            payload.Error.Should().ContainEquivalentOf(expectedError);
            payload.Files.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectory(dataRoot);
        }
    }

    [Theory]
    [InlineData("/api/export/orderflow", "jsonl")]
    [InlineData("/api/export/strategy-package", "hdf5")]
    public async Task MapExportEndpoints_UnsupportedSpecializedFormat_ShouldReturnBadRequest(
        string route,
        string format)
    {
        var dataRoot = CreateDataRoot();

        try
        {
            await using var app = await CreateAppAsync(new AnalysisExportService(dataRoot));
            var client = app.GetTestClient();

            var response = await client.PostAsJsonAsync(route, new
            {
                symbols = new[] { "SPY" },
                includeMetadata = true,
                format
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            payload.RootElement.GetProperty("status").GetString().Should().Be("invalid");
            payload.RootElement.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            DeleteDirectory(dataRoot);
        }
    }

    [Fact]
    public async Task MapExportEndpoints_StrategyPackageRoute_ShouldRetainResearchCompatibilityAlias()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var strategyResponse = await client.PostAsJsonAsync(UiApiRoutes.ExportStrategyPackage, new
        {
            symbols = new[] { "SPY" },
            includeMetadata = true
        });
        var researchResponse = await client.PostAsJsonAsync(UiApiRoutes.ExportResearchPackage, new
        {
            symbols = new[] { "SPY" },
            includeMetadata = true
        });

        strategyResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        researchResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        using var strategyPayload = await JsonDocument.ParseAsync(await strategyResponse.Content.ReadAsStreamAsync());
        using var researchPayload = await JsonDocument.ParseAsync(await researchResponse.Content.ReadAsStreamAsync());
        strategyPayload.RootElement.GetProperty("error").GetString().Should().Be("Export service not available");
        researchPayload.RootElement.GetProperty("error").GetString().Should().Be("Export service not available");
    }

    private static async Task<WebApplication> CreateAppAsync(AnalysisExportService? exportService = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        if (exportService is not null)
            builder.Services.AddSingleton(exportService);

        var app = builder.Build();
        // Export mutations now require ExportData. This minimal app composes no login
        // middleware, so seed the permission the way LoginSessionMiddleware would, with a
        // header override so a test can still exercise the unauthorized path.
        app.Use((context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "export-operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] =
                context.Request.Headers.TryGetValue("X-Test-Permissions", out var configured) &&
                Enum.TryParse<UserPermission>(configured.ToString(), out var parsed)
                    ? parsed
                    : UserPermission.ExportData;
            return next();
        });
        app.MapExportEndpoints(JsonOptions);

        await app.StartAsync();
        return app;
    }

    private static string CreateDataRoot()
    {
        var dataRoot = CreateEmptyDataRoot();
        File.WriteAllText(
            Path.Combine(dataRoot, "SPY.Trade.jsonl"),
            """
            {"Timestamp":"2026-01-03T10:00:00Z","Symbol":"SPY","Price":450.25,"Size":100}
            {"Timestamp":"2026-01-03T10:00:01Z","Symbol":"SPY","Price":450.50,"Size":200}
            """);
        return dataRoot;
    }

    private static string CreateEmptyDataRoot()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-export-endpoint-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        return dataRoot;
    }

    private static void DeleteDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
