namespace Meridian.Infrastructure.Etl.Sftp;

/// <summary>
/// Abstraction over a remote SFTP file entry, independent of any SSH library type.
/// </summary>
public interface ISftpFileEntry
{
    string Name { get; }
    string FullName { get; }
    bool IsDirectory { get; }
    bool IsSymbolicLink { get; }
    long Length { get; }
    DateTime LastWriteTimeUtc { get; }
}

/// <summary>
/// Abstraction over an active SFTP connection, independent of any SSH library type.
/// </summary>
public interface ISftpClient : IDisposable
{
    void Connect();
    void Disconnect();
    IEnumerable<ISftpFileEntry> ListDirectory(string path);
    void DownloadFile(string path, Stream output);
    void UploadFile(Stream input, string path, bool canOverwrite);
    bool Exists(string path);
    void CreateDirectory(string path);
}

/// <summary>
/// Creates <see cref="ISftpClient"/> sessions.
/// Build with <c>EnableSftp=true</c> and reference <c>Renci.SshNet >= 2024.2.0</c>
/// to activate real SFTP connectivity.
/// </summary>
public interface ISftpClientFactory
{
    ISftpClient Create(string host, int port, string username, string password);
}

#if SFTP
public sealed class SftpClientFactory : ISftpClientFactory
{
    public ISftpClient Create(string host, int port, string username, string password)
        => new SshNetSftpClient(new Renci.SshNet.SftpClient(host, port, username, password));
}

internal sealed class SshNetSftpClient(Renci.SshNet.SftpClient inner) : ISftpClient
{
    public void Connect() => inner.Connect();
    public void Disconnect() => inner.Disconnect();
    public void DownloadFile(string path, Stream output) => inner.DownloadFile(path, output);
    public void UploadFile(Stream input, string path, bool canOverwrite) => inner.UploadFile(input, path, canOverwrite);
    public bool Exists(string path) => inner.Exists(path);
    public void CreateDirectory(string path) => inner.CreateDirectory(path);
    public void Dispose() => inner.Dispose();

    public IEnumerable<ISftpFileEntry> ListDirectory(string path)
        => inner.ListDirectory(path).Select(static f => (ISftpFileEntry)new SshNetFileEntry(f));

    private sealed class SshNetFileEntry(Renci.SshNet.SftpFile file) : ISftpFileEntry
    {
        public string Name => file.Name;
        public string FullName => file.FullName;
        public bool IsDirectory => file.IsDirectory;
        public bool IsSymbolicLink => file.IsSymbolicLink;
        public long Length => file.Length;
        public DateTime LastWriteTimeUtc => file.LastWriteTimeUtc;
    }
}
#else
/// <summary>
/// Stub factory used when <c>EnableSftp=false</c> (the default).
/// All calls throw <see cref="NotSupportedException"/> at runtime.
/// </summary>
public sealed class SftpClientFactory : ISftpClientFactory
{
    public ISftpClient Create(string host, int port, string username, string password)
        => throw new NotSupportedException(
            "SFTP support is disabled. Build with /p:EnableSftp=true and ensure " +
            "Renci.SshNet >= 2024.2.0 is available in the NuGet feed.");
}
#endif
