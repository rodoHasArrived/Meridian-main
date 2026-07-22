using FluentAssertions;
using Meridian.Contracts.Etl;
using Meridian.Infrastructure.Etl;
using Meridian.Infrastructure.Etl.Sftp;
using Meridian.Storage.Etl;
using System.Text;

namespace Meridian.Tests.Infrastructure.Etl;

public sealed class SftpInfrastructureTests : IDisposable
{
    private const string Fingerprint = "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF";
    private const string OpenSshFingerprint = "SHA256:ABEiM0RVZneImaq7zN3u/wARIjNEVWZ3iJmqu8zd7v8";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-sftp-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void NormalizeSha256Fingerprint_WithOpenSshBase64_ShouldReturnHexFingerprint()
    {
        var normalized = SftpConnectionOptions.NormalizeSha256Fingerprint(OpenSshFingerprint);

        normalized.Should().Be(Fingerprint);
    }

    [Fact]
    public async Task ListFilesAsync_WithoutPinnedHostKey_ShouldFailClosedBeforeConnecting()
    {
        var factory = new RecordingSftpClientFactory(new RecordingSftpClient());
        var reader = new SftpFileSourceReader(new EtlStagingStore(_root), factory);
        var source = CreateSource(hostKeyFingerprint: null);

        var act = () => reader.ListFilesAsync(source);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*hostKeySha256Fingerprint is required*");
        factory.CreateCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("ftp://custodian.example.com/inbound", "*sftp:// scheme*")]
    [InlineData("sftp://ops@custodian.example.com/inbound", "*must not include user info*")]
    [InlineData("sftp://custodian.example.com", "*must include an absolute remote path*")]
    [InlineData("sftp://custodian.example.com/", "*must include an absolute remote path*")]
    [InlineData("sftp://custodian.example.com/inbound?x=1", "*must not include query or fragment*")]
    [InlineData("sftp://custodian.example.com/inbound#files", "*must not include query or fragment*")]
    public async Task ListFilesAsync_WithInvalidUri_ShouldFailClosedBeforeConnecting(string location, string message)
    {
        var factory = new RecordingSftpClientFactory(new RecordingSftpClient());
        var reader = new SftpFileSourceReader(new EtlStagingStore(_root), factory);
        var source = CreateSource(location: location);

        var act = () => reader.ListFilesAsync(source);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
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
                new RecordingSftpFileEntry("archive", "/inbound/archive", 0, IsDirectory: true)
            ]
        };
        var factory = new RecordingSftpClientFactory(client);
        var reader = new SftpFileSourceReader(new EtlStagingStore(_root), factory);
        var source = CreateSource(
            location: "sftp://custodian.example.com:2222/inbound",
            hostKeyFingerprint: "SHA256:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF");

        var files = await reader.ListFilesAsync(source);

        files.Should().ContainSingle().Which.Path.Should().Be("/inbound/positions.csv");
        factory.Options.Should().NotBeNull();
        factory.Options!.Host.Should().Be("custodian.example.com");
        factory.Options.Port.Should().Be(2222);
        factory.Options.HostKeySha256Fingerprint.Should().Be(Fingerprint);
        client.ListPaths.Should().Equal("/inbound");
        client.ConnectCalls.Should().Be(1);
        client.DisconnectCalls.Should().Be(1);
    }

    [Fact]
    public async Task StageFileAsync_WithRemoteFileOutsideSourceRoot_ShouldFailClosedBeforeConnecting()
    {
        var factory = new RecordingSftpClientFactory(new RecordingSftpClient());
        var reader = new SftpFileSourceReader(new EtlStagingStore(_root), factory);
        var source = CreateSource(location: "sftp://custodian.example.com/inbound");
        var file = new EtlRemoteFile
        {
            Path = "/archive/positions.csv",
            Name = "positions.csv"
        };

        var act = () => reader.StageFileAsync("job-1", source, file);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*under the configured source path*");
        factory.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task StageFileAsync_WithTraversalFileName_ShouldFailClosedAtStagingBoundary()
    {
        var client = new RecordingSftpClient { DownloadPayload = "id,value" };
        var reader = new SftpFileSourceReader(new EtlStagingStore(_root), new RecordingSftpClientFactory(client));
        var file = new EtlRemoteFile
        {
            Path = "/inbound/evil.csv",
            Name = "../evil.csv"
        };

        var act = () => reader.StageFileAsync("job-1", CreateSource(), file);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must not contain path segments*");
        client.ConnectCalls.Should().Be(1);
        client.DownloadPaths.Should().Equal("/inbound/evil.csv");
        client.DisconnectCalls.Should().Be(1);
    }

    [Fact]
    public async Task ListFilesAsync_WhenCancellationAlreadyRequested_ShouldFailBeforeCreatingClient()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var factory = new RecordingSftpClientFactory(new RecordingSftpClient());
        var reader = new SftpFileSourceReader(new EtlStagingStore(_root), factory);

        var act = () => reader.ListFilesAsync(CreateSource(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        factory.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_WhenCancellationAlreadyRequested_ShouldFailBeforeCreatingClient()
    {
        var localPath = await CreateLocalFileAsync("extract.csv");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var factory = new RecordingSftpClientFactory(new RecordingSftpClient());
        var publisher = new SftpFilePublisher(factory);

        var act = () => publisher.PublishAsync(CreateDestination(), localPath, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        factory.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_WhenUploadFails_ShouldDisconnectSession()
    {
        var localPath = await CreateLocalFileAsync("extract.csv");
        var client = new RecordingSftpClient { ThrowOnUpload = true };
        var factory = new RecordingSftpClientFactory(client);
        var publisher = new SftpFilePublisher(factory);

        var act = () => publisher.PublishAsync(CreateDestination(), localPath);

        await act.Should().ThrowAsync<IOException>();
        client.ConnectCalls.Should().Be(1);
        client.RenameCalls.Should().BeEmpty();
        client.DisconnectCalls.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_ForSingleFile_ShouldUploadTemporaryFileThenRenameWithOverwriteSetting()
    {
        var localPath = await CreateLocalFileAsync("extract.csv");
        var client = new RecordingSftpClient();
        var publisher = new SftpFilePublisher(new RecordingSftpClientFactory(client));
        var destination = CreateDestination(overwriteIfExists: false);

        await publisher.PublishAsync(destination, localPath);

        client.UploadedPaths.Should().ContainSingle(path => path.StartsWith("/drop/extract.csv.meridian-upload-", StringComparison.Ordinal));
        client.RenameCalls.Should().ContainSingle();
        var rename = client.RenameCalls.Single();
        rename.OldPath.Should().Be(client.UploadedPaths.Single());
        rename.NewPath.Should().Be("/drop/extract.csv");
        rename.CanOverwrite.Should().BeFalse();
        client.DeletedPaths.Should().BeEmpty();
        client.DisconnectCalls.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_ForDirectory_ShouldUploadEachFileAtomicallyWithOverwriteEnabled()
    {
        var outbound = Path.Combine(_root, "outbound");
        Directory.CreateDirectory(Path.Combine(outbound, "nested"));
        await File.WriteAllTextAsync(Path.Combine(outbound, "root.csv"), "root");
        await File.WriteAllTextAsync(Path.Combine(outbound, "nested", "child.csv"), "child");
        var client = new RecordingSftpClient();
        var publisher = new SftpFilePublisher(new RecordingSftpClientFactory(client));

        await publisher.PublishAsync(CreateDestination(), outbound);

        client.UploadedPaths.Should().HaveCount(2);
        client.RenameCalls.Select(call => call.NewPath).Should().BeEquivalentTo(
            "/drop/root.csv",
            "/drop/nested/child.csv");
        client.RenameCalls.Should().OnlyContain(call => call.CanOverwrite);
    }

    [Fact]
    public async Task PublishAsync_WhenRenameFails_ShouldDeleteTemporaryRemoteFileAndDisconnect()
    {
        var localPath = await CreateLocalFileAsync("extract.csv");
        var client = new RecordingSftpClient { ThrowOnRename = true };
        var publisher = new SftpFilePublisher(new RecordingSftpClientFactory(client));

        var act = () => publisher.PublishAsync(CreateDestination(), localPath);

        await act.Should().ThrowAsync<IOException>();
        client.UploadedPaths.Should().ContainSingle();
        client.DeletedPaths.Should().Equal(client.UploadedPaths);
        client.DisconnectCalls.Should().Be(1);
    }

    [Fact]
    public async Task PostProcessFileAsync_ForNestedArchivePath_CreatesRemoteParentsBeforeRename()
    {
        var client = new RecordingSftpClient();
        var reader = new SftpFileSourceReader(new EtlStagingStore(_root), new RecordingSftpClientFactory(client));
        var source = CreateSource(
            postProcessingAction: EtlSourcePostProcessingAction.MoveToArchive,
            archiveLocation: "/archive/2026/06");
        var file = new EtlRemoteFile
        {
            Path = "/inbound/positions.csv",
            Name = "positions.csv"
        };

        await reader.PostProcessFileAsync(source, file, succeeded: true);

        client.CreatedDirectories.Should().Equal("/archive", "/archive/2026", "/archive/2026/06");
        client.RenameCalls.Should().ContainSingle(call =>
            call.OldPath == "/inbound/positions.csv" &&
            call.NewPath == "/archive/2026/06/positions.csv" &&
            call.CanOverwrite);
        client.DisconnectCalls.Should().Be(1);
    }

    [Fact]
    public async Task PostProcessFileAsync_ForErrorAction_MovesOnlyFailedFiles()
    {
        var client = new RecordingSftpClient();
        var reader = new SftpFileSourceReader(new EtlStagingStore(_root), new RecordingSftpClientFactory(client));
        var source = CreateSource(
            postProcessingAction: EtlSourcePostProcessingAction.MoveToError,
            errorLocation: "/error");
        var file = new EtlRemoteFile
        {
            Path = "/inbound/positions.csv",
            Name = "positions.csv"
        };

        await reader.PostProcessFileAsync(source, file, succeeded: true);
        await reader.PostProcessFileAsync(source, file, succeeded: false);

        client.RenameCalls.Should().ContainSingle(call =>
            call.OldPath == "/inbound/positions.csv" &&
            call.NewPath == "/error/positions.csv" &&
            call.CanOverwrite);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<string> CreateLocalFileAsync(string name)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        await File.WriteAllTextAsync(path, "id,value");
        return path;
    }

    private static EtlSourceDefinition CreateSource(
        string location = "sftp://custodian.example.com/inbound",
        string? hostKeyFingerprint = Fingerprint,
        EtlSourcePostProcessingAction postProcessingAction = EtlSourcePostProcessingAction.LeaveInPlace,
        string? archiveLocation = null,
        string? errorLocation = null)
        => new()
        {
            Kind = EtlSourceKind.Sftp,
            Location = location,
            FilePattern = "*.csv",
            Username = "ops",
            SecretRef = "password",
            HostKeySha256Fingerprint = hostKeyFingerprint,
            PostProcessingAction = postProcessingAction,
            ArchiveLocation = archiveLocation,
            ErrorLocation = errorLocation
        };

    private static EtlDestinationDefinition CreateDestination(bool overwriteIfExists = true)
        => new()
        {
            Kind = EtlDestinationKind.Sftp,
            Location = "sftp://partner.example.com/drop",
            Username = "ops",
            SecretRef = "password",
            HostKeySha256Fingerprint = Fingerprint,
            OverwriteIfExists = overwriteIfExists
        };

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
        private readonly HashSet<string> _remoteFiles = new(StringComparer.Ordinal);

        public IReadOnlyList<ISftpFileEntry> Entries { get; init; } = [];
        public string DownloadPayload { get; init; } = string.Empty;
        public bool ThrowOnUpload { get; init; }
        public bool ThrowOnRename { get; init; }
        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }
        public List<string> ListPaths { get; } = [];
        public List<string> DownloadPaths { get; } = [];
        public List<string> UploadedPaths { get; } = [];
        public List<(string OldPath, string NewPath, bool CanOverwrite)> RenameCalls { get; } = [];
        public List<string> DeletedPaths { get; } = [];
        public List<string> CreatedDirectories { get; } = [];

        public void Connect() => ConnectCalls++;
        public void Disconnect() => DisconnectCalls++;

        public IEnumerable<ISftpFileEntry> ListDirectory(string path)
        {
            ListPaths.Add(path);
            return Entries;
        }

        public void DownloadFile(string path, Stream output)
        {
            DownloadPaths.Add(path);
            var bytes = Encoding.UTF8.GetBytes(DownloadPayload);
            output.Write(bytes);
        }

        public void UploadFile(Stream input, string path, bool canOverwrite)
        {
            if (ThrowOnUpload)
                throw new IOException("simulated upload failure");

            UploadedPaths.Add(path);
            _remoteFiles.Add(path);
        }

        public void RenameFile(string oldPath, string newPath, bool canOverwrite)
        {
            RenameCalls.Add((oldPath, newPath, canOverwrite));
            if (ThrowOnRename)
                throw new IOException("simulated rename failure");

            _remoteFiles.Remove(oldPath);
            _remoteFiles.Add(newPath);
        }

        public void DeleteFile(string path)
        {
            DeletedPaths.Add(path);
            _remoteFiles.Remove(path);
        }

        public bool Exists(string path) => _remoteFiles.Contains(path);
        public void CreateDirectory(string path)
        {
            CreatedDirectories.Add(path);
            _remoteFiles.Add(path);
        }
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
