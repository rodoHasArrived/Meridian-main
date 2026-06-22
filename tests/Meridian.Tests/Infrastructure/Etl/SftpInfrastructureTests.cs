using FluentAssertions;
using Meridian.Contracts.Etl;
using Meridian.Infrastructure.Etl;
using Meridian.Infrastructure.Etl.Sftp;
using Meridian.Storage.Etl;

namespace Meridian.Tests.Infrastructure.Etl;

public sealed class SftpInfrastructureTests : IDisposable
{
    private const string Fingerprint = "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-sftp-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ListFilesAsync_WithoutPinnedHostKey_ShouldFailClosedBeforeConnecting()
    {
        var factory = new RecordingSftpClientFactory(new RecordingSftpClient());
        var reader = new SftpFileSourceReader(new EtlStagingStore(_root), factory);
        var source = new EtlSourceDefinition
        {
            Kind = EtlSourceKind.Sftp,
            Location = "sftp://custodian.example.com/inbound",
            Username = "ops",
            SecretRef = "password"
        };

        var act = () => reader.ListFilesAsync(source);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*hostKeySha256Fingerprint is required*");
        factory.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task ListFilesAsync_WithPinnedHostKey_ShouldPassNormalizedConnectionOptionsAndDisconnect()
    {
        var client = new RecordingSftpClient
        {
            Entries =
            [
                new RecordingSftpFileEntry("positions.csv", "/inbound/positions.csv", 42),
                new RecordingSftpFileEntry("readme.txt", "/inbound/readme.txt", 8),
                new RecordingSftpFileEntry("archive", "/inbound/archive", 0, isDirectory: true)
            ]
        };
        var factory = new RecordingSftpClientFactory(client);
        var reader = new SftpFileSourceReader(new EtlStagingStore(_root), factory);
        var source = new EtlSourceDefinition
        {
            Kind = EtlSourceKind.Sftp,
            Location = "sftp://custodian.example.com:2222/inbound",
            FilePattern = "*.csv",
            Username = "ops",
            SecretRef = "password",
            HostKeySha256Fingerprint = "SHA256:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF"
        };

        var files = await reader.ListFilesAsync(source);

        files.Should().ContainSingle().Which.Path.Should().Be("/inbound/positions.csv");
        factory.Options.Should().NotBeNull();
        factory.Options!.Host.Should().Be("custodian.example.com");
        factory.Options.Port.Should().Be(2222);
        factory.Options.HostKeySha256Fingerprint.Should().Be(Fingerprint);
        client.ConnectCalls.Should().Be(1);
        client.DisconnectCalls.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_WhenUploadFails_ShouldDisconnectSession()
    {
        var localPath = Path.Combine(_root, "extract.csv");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(localPath, "id,value");
        var client = new RecordingSftpClient { ThrowOnUpload = true };
        var factory = new RecordingSftpClientFactory(client);
        var publisher = new SftpFilePublisher(factory);
        var destination = new EtlDestinationDefinition
        {
            Kind = EtlDestinationKind.Sftp,
            Location = "sftp://partner.example.com/drop",
            Username = "ops",
            SecretRef = "password",
            HostKeySha256Fingerprint = Fingerprint
        };

        var act = () => publisher.PublishAsync(destination, localPath);

        await act.Should().ThrowAsync<IOException>();
        client.ConnectCalls.Should().Be(1);
        client.DisconnectCalls.Should().Be(1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingSftpClientFactory(RecordingSftpClient client) : ISftpClientFactory
    {
        public int CreateCalls { get; private set; }
        public SftpConnectionOptions? Options { get; private set; }

        public ISftpClient Create(SftpConnectionOptions options)
        {
            CreateCalls++;
            Options = options;
            return client;
        }
    }

    private sealed class RecordingSftpClient : ISftpClient
    {
        public IReadOnlyList<ISftpFileEntry> Entries { get; init; } = [];
        public bool ThrowOnUpload { get; init; }
        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }

        public void Connect() => ConnectCalls++;
        public void Disconnect() => DisconnectCalls++;
        public IEnumerable<ISftpFileEntry> ListDirectory(string path) => Entries;
        public void DownloadFile(string path, Stream output) => throw new NotSupportedException();
        public void UploadFile(Stream input, string path, bool canOverwrite)
        {
            if (ThrowOnUpload)
                throw new IOException("simulated upload failure");
        }
        public bool Exists(string path) => true;
        public void CreateDirectory(string path) { }
        public void Dispose() { }
    }

    private sealed record RecordingSftpFileEntry(
        string Name,
        string FullName,
        long Length,
        bool IsDirectory = false,
        bool IsSymbolicLink = false) : ISftpFileEntry
    {
        public DateTime LastWriteTimeUtc { get; init; } = DateTime.UtcNow;
    }
}
