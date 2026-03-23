using Renci.SshNet;

namespace Meridian.Infrastructure.Etl.Sftp;

public interface ISftpClientFactory
{
    SftpClient Create(string host, int port, string username, string password);
}

public sealed class SftpClientFactory : ISftpClientFactory
{
    public SftpClient Create(string host, int port, string username, string password)
        => new(host, port, username, password);
}
