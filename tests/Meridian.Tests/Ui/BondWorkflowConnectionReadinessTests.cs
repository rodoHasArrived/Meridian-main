using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

[Trait("Category", "Unit")]
public sealed class BondWorkflowConnectionReadinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void VerifiedConnection_DoesNotPromoteFixingWorkflowOrBroadenItsEntitlement()
    {
        var connection = new ProviderConnectionDto("conn", "fred", "FRED", "DataVendor", "ReadOnly", true,
            null, null, "account-a", null, [], null, true, "tenant-a");
        var credential = new ProviderConnectionRowDto("fred", "FRED", ProviderConnectionCapabilityDto.Data,
            ProviderCredentialStateDto.Verified, ProviderCredentialSourceDto.LocalEncryptedStore, ProviderVerificationStateDto.Verified,
            ProviderContinuityHealthDto.Healthy, false, Now, Now, null, null, null, "live", "account-a", [], "", "");
        var entitlement = new DataVendorEntitlementDto(Guid.NewGuid(), "fred", DataVendorDataType.RateFixings, "contract",
            Now.AddDays(-1), Now.AddDays(30), null, false, null, 5, DataVendorEntitlementStatus.Active, "operator", Now)
        { AccountId = "account-a" };

        var rows = BondWorkflowConnectionReadinessService.BuildRows(connection, credential, [entitlement], Now, "account-a");
        var fixing = rows.Single(row => row.Capability == "ObservedFixings");
        fixing.Configured.Should().BeTrue();
        fixing.Entitled.Should().BeTrue();
        fixing.EvidenceStatus.Should().Be(BondProviderEvidenceStatus.LiveUntested);
        fixing.VerifiedAtUtc.Should().BeNull();
        rows.Single(row => row.Capability == "InflationIndexes").Entitled.Should().BeNull();
        rows.Single(row => row.Capability == "Quotes").Implemented.Should().BeFalse();
    }

    [Fact]
    public void AnotherAccountsOrExpiredEntitlement_CannotEnableTheCurrentInput()
    {
        var connection = new ProviderConnectionDto("conn", "openfigi", "FIGI", "DataVendor", "ReadOnly", true,
            null, null, "account-a", null, [], null, false, "tenant-a");
        var expired = new DataVendorEntitlementDto(Guid.NewGuid(), "openfigi", DataVendorDataType.Identifiers, "contract",
            Now.AddDays(-30), Now.AddDays(-1), null, false, null, 5, DataVendorEntitlementStatus.Expired, "operator", Now)
        { AccountId = "account-a" };
        var elsewhere = expired with { AccountId = "account-b", EffectiveTo = Now.AddDays(30), Status = DataVendorEntitlementStatus.Active };
        var row = BondWorkflowConnectionReadinessService.BuildRows(connection, null, [expired, elsewhere], Now, "account-a")
            .Single(item => item.Capability == "IdentifierDiscovery");
        row.Entitled.Should().BeFalse();
        row.Configured.Should().BeFalse();
        row.Usable.Should().BeFalse();
    }
}
