using FluentAssertions;
using Meridian.Application.Integrations;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Application.Integrations;

public sealed class ProviderIntegrationOpenApiImportServiceTests : IDisposable
{
    private readonly string testRoot;

    public ProviderIntegrationOpenApiImportServiceTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), $"mdc_provider_openapi_import_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
    }

    [Fact]
    public async Task ImportAsync_SavesDraftManifestFromOpenApiPositionsEndpoint()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationOpenApiImportService(store);
        var request = CreateRequest();

        var result = await service.ImportAsync("tenant-alpha", request);

        var tenantStore = ((IProviderIntegrationTenantManifestStoreFactory)store).ForTenant("tenant-alpha");
        var saved = await tenantStore.GetManifestAsync(request.ManifestId);
        result.Imported.Should().BeTrue();
        result.Issues.Should().NotContain(issue => issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        result.Manifest.IntegrationType.Should().Be(IntegrationTypeDto.OpenApiRest);
        result.Manifest.Endpoints.Should().ContainSingle(endpoint =>
            endpoint.Capability == ProviderCapabilityKindDto.Positions &&
            endpoint.Path == "/v1/accounts/{accountId}/positions" &&
            endpoint.Response.RecordsPath == "$.positions");
        result.Manifest.FieldMappings.Should().Contain(mapping =>
            mapping.TargetField == "security.cusip" &&
            mapping.SourcePath == "$.cusip" &&
            mapping.Confidence == ProviderMappingConfidenceDto.High);
        result.Readiness.RequiredEvidence.Should().Contain("endpoint-test");
        saved.Should().BeEquivalentTo(result.Manifest);
        (await store.GetManifestAsync(request.ManifestId)).Should().BeNull();
    }

    [Fact]
    public async Task ImportAsync_ReimportAdvancesFromSelectedCurrentVersionAndPreservesHistory()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationOpenApiImportService(store);
        var request = CreateRequest();
        var original = (await service.ImportAsync(request)).Manifest;
        var current = original with { ManifestVersion = 7, DisplayName = "Reviewed provider draft" };
        await store.SaveManifestVersionAsync(current);
        (await store.CompareExchangeCurrentManifestAsync(
            current.ManifestId,
            ProviderIntegrationManifestIdentity.Create(original),
            ProviderIntegrationManifestIdentity.Create(current))).Should().BeTrue();

        var result = await service.ImportAsync(request with
        {
            DisplayName = "Reimported provider draft",
            ExpectedManifestReference = ProviderIntegrationManifestIdentity.Create(current)
        });

        result.Imported.Should().BeTrue();
        result.Manifest.ManifestVersion.Should().Be(8);
        result.Manifest.DisplayName.Should().Be("Reimported provider draft");
        (await store.GetManifestAsync(request.ManifestId)).Should().BeEquivalentTo(result.Manifest);
        (await store.GetManifestVersionAsync(request.ManifestId, 1)).Should().BeEquivalentTo(original);
        (await store.GetManifestVersionAsync(request.ManifestId, 7)).Should().BeEquivalentTo(current);
        (await store.GetManifestVersionAsync(request.ManifestId, 2)).Should().BeNull();
    }

    [Fact]
    public async Task ImportAsync_ReimportRequiresExpectedCurrentReference()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationOpenApiImportService(store);
        var request = CreateRequest();
        var original = (await service.ImportAsync(request)).Manifest;

        var act = () => service.ImportAsync(request with { DisplayName = "Unreviewed replacement" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*current manifest reference*");
        (await store.GetManifestAsync(request.ManifestId)).Should().BeEquivalentTo(original);
        (await store.GetManifestVersionAsync(request.ManifestId, 2)).Should().BeNull();
    }

    [Fact]
    public async Task ImportAsync_ReimportRejectsStaleExpectedReference()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationOpenApiImportService(store);
        var request = CreateRequest();
        var original = (await service.ImportAsync(request)).Manifest;
        var originalReference = ProviderIntegrationManifestIdentity.Create(original);
        var current = (await service.ImportAsync(request with
        {
            DisplayName = "Updated provider draft",
            ExpectedManifestReference = originalReference
        })).Manifest;

        var act = () => service.ImportAsync(request with
        {
            DisplayName = "Stale provider draft",
            ExpectedManifestReference = originalReference
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*manifest changed*");
        (await store.GetManifestAsync(request.ManifestId)).Should().BeEquivalentTo(current);
        (await store.GetManifestVersionAsync(request.ManifestId, 3)).Should().BeNull();
    }

    [Fact]
    public async Task ImportAsync_ReturnsCriticalIssueAndDoesNotSaveInvalidDocument()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationOpenApiImportService(store);
        var request = CreateRequest(openApiDocumentJson: "{}");

        var result = await service.ImportAsync("tenant-alpha", request);

        result.Imported.Should().BeFalse();
        result.Issues.Should().Contain(issue =>
            issue.Code == "openapi.document.invalid" &&
            issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
        (await ((IProviderIntegrationTenantManifestStoreFactory)store)
            .ForTenant("tenant-alpha")
            .GetManifestAsync(request.ManifestId)).Should().BeNull();
    }

    [Fact]
    public async Task ImportAsync_ObservesCancellationBeforeWritingDraft()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationOpenApiImportService(store);
        var request = CreateRequest();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.ImportAsync("tenant-alpha", request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await ((IProviderIntegrationTenantManifestStoreFactory)store)
            .ForTenant("tenant-alpha")
            .GetManifestAsync(request.ManifestId)).Should().BeNull();
    }

    [Fact]
    public async Task ImportAsync_FlagsTradingCapabilitiesForCertifiedAdapterReview()
    {
        var store = new FileProviderIntegrationManifestStore(testRoot);
        var service = new ProviderIntegrationOpenApiImportService(store);
        var request = CreateRequest(
            capabilities: [ProviderCapabilityKindDto.OrderPlacement],
            openApiDocumentJson: """
            {
              "openapi": "3.0.0",
              "info": { "title": "Trading API", "version": "1.0" },
              "paths": {
                "/v1/orders": {
                  "post": {
                    "operationId": "placeOrder",
                    "summary": "Place order",
                    "responses": {
                      "200": {
                        "content": {
                          "application/json": {
                            "schema": {
                              "type": "object",
                              "properties": {
                                "orders": {
                                  "type": "array",
                                  "items": {
                                    "type": "object",
                                    "properties": {
                                      "account_id": { "type": "string" },
                                      "id": { "type": "string" },
                                      "side": { "type": "string" },
                                      "quantity": { "type": "number" }
                                    }
                                  }
                                }
                              }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """);

        var result = await service.ImportAsync("tenant-alpha", request);

        result.Imported.Should().BeTrue();
        result.Manifest.Capabilities.Should().ContainSingle()
            .Which.RequiresCertifiedAdapter.Should().BeTrue();
        result.Readiness.Issues.Should().Contain(issue =>
            issue.Code == "provider-manifest.certified-adapter-required" &&
            issue.Severity == ProviderIntegrationIssueSeverityDto.Critical);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static ProviderIntegrationOpenApiImportRequestDto CreateRequest(
        IReadOnlyList<ProviderCapabilityKindDto>? capabilities = null,
        string? openApiDocumentJson = null)
        => new(
            "manifest-openapi-custodian-v1",
            "openapi-custodian",
            "OpenAPI Custodian",
            "test",
            ProviderIntegrationAuthTypeDto.OAuth2,
            "https://api.example.com/oauth/token",
            ["positions.read"],
            capabilities ?? [ProviderCapabilityKindDto.Positions],
            openApiDocumentJson ?? PositionsOpenApiDocument(),
            "operator@example.com",
            DateTimeOffset.Parse("2026-06-16T12:00:00Z"),
            "Imported from provider OpenAPI spec.");

    private static string PositionsOpenApiDocument()
        => """
        {
          "openapi": "3.0.0",
          "info": { "title": "Custodian API", "version": "1.0" },
          "servers": [{ "url": "https://api.example.com" }],
          "paths": {
            "/v1/accounts/{accountId}/positions": {
              "get": {
                "operationId": "listPositions",
                "summary": "List account positions",
                "parameters": [
                  { "name": "accountId", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "updated_since", "in": "query", "schema": { "type": "string" } }
                ],
                "responses": {
                  "200": {
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "properties": {
                            "positions": {
                              "type": "array",
                              "items": { "$ref": "#/components/schemas/Position" }
                            },
                            "nextCursor": { "type": "string" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "Position": {
                "type": "object",
                "properties": {
                  "account_id": { "type": "string" },
                  "cusip": { "type": "string" },
                  "quantity": { "type": "number" },
                  "as_of_date": { "type": "string", "format": "date" },
                  "currency": { "type": "string" }
                }
              }
            }
          }
        }
        """;
}
