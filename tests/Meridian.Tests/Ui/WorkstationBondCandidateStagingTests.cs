using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.TestHost;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task BondCandidateStaging_RetainsIdentityAndMissingTermsForReview()
    {
        var originalRoot = Environment.GetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "bond-candidate", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", root);
        try
        {
            await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ModifySecurityMaster);
            using var content = BuildUploadContent("asset-information", """
                asset_id,symbol,asset_name,asset_class,issuer,currency,country,maturity_date,coupon_rate,source_system,figi,composite_figi,exchange,observed_at_utc,review_reason
                "BBGTEST","912TEST","Synthetic Treasury","Bond","","USD","US","","","alpaca","BBGTEST","","OTC","2026-11-13T00:00:00Z","Reviewed source identity and missing terms"
                """);
            using var response = await app.GetTestClient().PostAsync(UiApiRoutes.WorkstationDataUploadPreview, content);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<DataUploadPreviewResultDto>(ServerJsonOptions);
            result!.Status.Should().Be("ReadyForReview");
            result.UploadedBy.Should().Be("ops-user");
            result.PreviewRows.Single()["source_system"].Should().Be("alpaca");
            result.PreviewRows.Single()["review_reason"].Should().Be("Reviewed source identity and missing terms");
            result.PreviewRows.Single()["coupon_rate"].Should().BeEmpty();
            result.PreviewRows.Single()["maturity_date"].Should().BeEmpty();
            File.Exists(Path.Combine(root, result.UploadId, result.FileName)).Should().BeTrue();
        }
        finally { Environment.SetEnvironmentVariable("MERIDIAN_DATA_UPLOAD_ROOT", originalRoot); }
    }

    [Fact]
    public async Task BondCandidateStaging_ViewPermissionCannotRetainImport()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ViewSecurityMaster);
        using var content = BuildUploadContent("asset-information", "asset_id,symbol,asset_name,asset_class\nBBGTEST,912TEST,Synthetic Treasury,Bond");
        using var response = await app.GetTestClient().PostAsync(UiApiRoutes.WorkstationDataUploadPreview, content);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
