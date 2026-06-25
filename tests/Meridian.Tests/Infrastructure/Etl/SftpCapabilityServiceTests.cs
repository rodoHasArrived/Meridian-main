using FluentAssertions;
using Meridian.Contracts.Etl;
using Meridian.Infrastructure.Etl.Sftp;

namespace Meridian.Tests.Infrastructure.Etl;

public sealed class SftpCapabilityServiceTests
{
    [Fact]
    public void Evaluate_WhenRequiredSettingsAreMissing_ReturnsActionableIssues()
    {
        var service = new SftpCapabilityService();

        var status = service.Evaluate(new EtlSourceDefinition
        {
            Kind = EtlSourceKind.Sftp,
            Location = "relative/path"
        });

        status.Ready.Should().BeFalse();
        status.Issues.Should().Contain(issue => issue.Contains("sftp://", StringComparison.OrdinalIgnoreCase));
        status.Issues.Should().Contain(issue => issue.Contains("username", StringComparison.OrdinalIgnoreCase));
        status.Issues.Should().Contain(issue => issue.Contains("hostKeySha256Fingerprint", StringComparison.OrdinalIgnoreCase));
    }
}
