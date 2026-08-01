using FluentAssertions;
using Meridian.Contracts.Etl;
using Meridian.Infrastructure.Etl;
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

    [Fact]
    public void Evaluate_Destination_AppliesTheSameReadinessRulesAsASource()
    {
        var service = new SftpCapabilityService();

        var status = service.Evaluate(new EtlDestinationDefinition
        {
            Kind = EtlDestinationKind.Sftp,
            Location = "relative/path"
        });

        // A publishing destination that cannot be reached is the same failure as a source that
        // cannot be read; before this overload existed only the read side was ever evaluated.
        status.Ready.Should().BeFalse();
        status.Issues.Should().Contain(issue => issue.Contains("sftp://", StringComparison.OrdinalIgnoreCase));
        status.Issues.Should().Contain(issue => issue.Contains("username", StringComparison.OrdinalIgnoreCase));
        status.Issues.Should().Contain(issue => issue.Contains("secretRef", StringComparison.OrdinalIgnoreCase));
        status.Issues.Should().Contain(issue => issue.Contains("hostKeySha256Fingerprint", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("not-a-sha256-fingerprint")]
    [InlineData("SHA256:!!!not-base64!!!")]
    [InlineData("0011223344")]
    public void Evaluate_Destination_WithAMalformedFingerprint_IsNotReady(string fingerprint)
    {
        var service = new SftpCapabilityService();

        var status = service.Evaluate(CompleteDestination(hostKeyFingerprint: fingerprint));

        // A non-blank but unparsable fingerprint previously reported Ready, so an export job was
        // accepted and then rejected by SftpConnectionOptions.Create before connecting.
        status.HasHostKeyFingerprint.Should().BeFalse();
        status.Ready.Should().BeFalse();
        status.Issues.Should().Contain(issue =>
            issue.Contains("not a valid SHA-256 host key fingerprint", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF")]
    [InlineData("SHA256:ABEiM0RVZneImaq7zN3u/wARIjNEVWZ3iJmqu8zd7v8")]
    public void Evaluate_Destination_WithAWellFormedFingerprint_AcceptsIt(string fingerprint)
    {
        var service = new SftpCapabilityService();

        var status = service.Evaluate(CompleteDestination(hostKeyFingerprint: fingerprint));

        status.HasHostKeyFingerprint.Should().BeTrue();
        status.Issues.Should().NotContain(issue =>
            issue.Contains("hostKeySha256Fingerprint", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("sftp://partner.example.com/")]
    [InlineData("sftp://meridian-ops@partner.example.com/inbound")]
    [InlineData("sftp://partner.example.com/inbound?mode=binary")]
    [InlineData("sftp://partner.example.com/inbound#drop")]
    [InlineData("sftp:///inbound")]
    public void Evaluate_Destination_WithAUriTheTransferPathRejects_IsNotReady(string location)
    {
        var service = new SftpCapabilityService();

        var status = service.Evaluate(CompleteDestination(location: location));

        // Readiness previously checked only the sftp:// scheme, so each of these was approved
        // and then rejected by SftpRemoteLocation.ParseRequired before a connection was opened.
        status.HasSftpUri.Should().BeFalse();
        status.Ready.Should().BeFalse();
        status.Issues.Should().Contain(issue => issue.Contains("SFTP destination location", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_Destination_WithAnUnsetEnvSecretRef_IsNotReady()
    {
        var service = new SftpCapabilityService();

        var status = service.Evaluate(CompleteDestination(secretRef: "env:MERIDIAN_TEST_SFTP_DEFINITELY_UNSET"));

        // EnvironmentSftpCredentialResolver throws for an unset variable, so accepting this as
        // ready meant taking an export job that could never open a connection.
        status.HasSecretRef.Should().BeFalse();
        status.Ready.Should().BeFalse();
        status.Issues.Should().Contain(issue =>
            issue.Contains("MERIDIAN_TEST_SFTP_DEFINITELY_UNSET", StringComparison.Ordinal)
            && issue.Contains("unset or empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_Destination_WithAnEmptyEnvReference_IsNotReady()
    {
        var service = new SftpCapabilityService();

        var status = service.Evaluate(CompleteDestination(secretRef: "env:"));

        status.HasSecretRef.Should().BeFalse();
        status.Issues.Should().Contain(issue =>
            issue.Contains("names no environment variable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_Destination_WithAResolvableEnvSecretRef_AcceptsIt()
    {
        const string variable = "MERIDIAN_TEST_SFTP_PREFLIGHT";
        Environment.SetEnvironmentVariable(variable, "resolved-secret");
        try
        {
            var service = new SftpCapabilityService();

            var status = service.Evaluate(CompleteDestination(secretRef: $"env:{variable}"));

            status.HasSecretRef.Should().BeTrue();
            status.Issues.Should().NotContain(issue =>
                issue.Contains("secretRef", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Evaluate_Destination_WhenRealSftpIsDisabled_IsNotReadyEvenWhenFullyConfigured()
    {
        var service = new SftpCapabilityService();

        var status = service.Evaluate(CompleteDestination());

        status.RealSftpEnabled.Should().Be(service.RealSftpEnabled);
        if (!service.RealSftpEnabled)
        {
            status.Ready.Should().BeFalse();
            status.Issues.Should().Contain(issue => issue.Contains("disabled in this build", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            status.Ready.Should().BeTrue();
        }
    }

    [Fact]
    public async Task PublishAsync_WhenCapabilityIsNotReady_FailsClosedWithTheReadinessIssues()
    {
        var factory = new ThrowingSftpClientFactory();
        var publisher = new SftpFilePublisher(
            factory,
            new EnvironmentSftpCredentialResolver(),
            new StubCapabilityService(ready: false, issues: ["Real SFTP support is disabled in this build."]));

        var act = async () => await publisher.PublishAsync(CompleteDestination(), "/tmp/export", CancellationToken.None);

        // The operator must learn the capability is absent, not receive a transport error from
        // the disabled-build stub after the export job has already been accepted.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not available for this destination*disabled in this build*");
        factory.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_ResolvesTheDestinationSecretThroughTheSharedCredentialModel()
    {
        const string variable = "MERIDIAN_TEST_SFTP_PASSWORD";
        Environment.SetEnvironmentVariable(variable, "resolved-secret");
        try
        {
            var factory = new ThrowingSftpClientFactory();
            var publisher = new SftpFilePublisher(
                factory,
                new EnvironmentSftpCredentialResolver(),
                new StubCapabilityService(ready: true, issues: []));

            var destination = CompleteDestination(secretRef: $"env:{variable}");

            // The client factory throws, so publishing cannot complete; the assertion is on the
            // credential material the publisher handed it. Passing SecretRef straight through
            // sent the literal text "env:MERIDIAN_TEST_SFTP_PASSWORD" as the password.
            var act = async () => await publisher.PublishAsync(destination, "/tmp/export", CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();

            factory.LastOptions.Should().NotBeNull();
            factory.LastOptions!.Password.Should().Be("resolved-secret");
            factory.LastOptions.Username.Should().Be("meridian-ops");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task ResolveAsync_SourceAndDestination_ResolveIdenticalSecretMaterial()
    {
        const string variable = "MERIDIAN_TEST_SFTP_SHARED";
        Environment.SetEnvironmentVariable(variable, "shared-secret");
        try
        {
            var resolver = new EnvironmentSftpCredentialResolver();

            var fromSource = await resolver.ResolveAsync(new EtlSourceDefinition
            {
                Kind = EtlSourceKind.Sftp,
                Location = "sftp://partner.example.com/outbound",
                Username = "meridian-ops",
                SecretRef = $"env:{variable}"
            });

            var fromDestination = await resolver.ResolveAsync(CompleteDestination(secretRef: $"env:{variable}"));

            fromDestination.Should().Be(fromSource);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task ResolveAsync_Destination_WithoutASecretRef_FailsWithTheDestinationRole()
    {
        var resolver = new EnvironmentSftpCredentialResolver();

        var act = async () => await resolver.ResolveAsync(CompleteDestination(secretRef: null));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*destination secretRef is required*");
    }

    private static EtlDestinationDefinition CompleteDestination(
        string? secretRef = "literal-secret",
        // Deliberately low-entropy so secret scanners do not read a 64-char hex default sitting
        // next to the word "Key" as a leaked credential. Still a well-formed SHA-256 fingerprint.
        string? hostKeyFingerprint = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef",
        string? location = "sftp://partner.example.com/inbound") => new()
        {
            Kind = EtlDestinationKind.Sftp,
            Location = location,
            Username = "meridian-ops",
            SecretRef = secretRef,
            HostKeySha256Fingerprint = hostKeyFingerprint
        };

    private sealed class ThrowingSftpClientFactory : ISftpClientFactory
    {
        public int CreateCalls { get; private set; }

        public SftpConnectionOptions? LastOptions { get; private set; }

        public ISftpClient Create(SftpConnectionOptions options)
        {
            CreateCalls++;
            LastOptions = options;
            throw new InvalidOperationException("connection not attempted in tests");
        }
    }

    private sealed class StubCapabilityService(bool ready, IReadOnlyList<string> issues) : ISftpCapabilityService
    {
        public bool RealSftpEnabled => ready;

        public SftpCapabilityStatus Evaluate(EtlSourceDefinition source) => Status();

        public SftpCapabilityStatus Evaluate(EtlDestinationDefinition destination) => Status();

        private SftpCapabilityStatus Status() => new(
            ready, true, true, true, true, true, ready, issues);
    }
}
