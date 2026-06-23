namespace Meridian.Infrastructure.Etl.Sftp;

internal sealed record SftpRemoteLocation(string Host, int Port, string RemotePath)
{
    public static SftpRemoteLocation ParseRequired(string? location, string purpose)
    {
        if (string.IsNullOrWhiteSpace(location))
            throw new InvalidOperationException($"SFTP {purpose} location is required.");

        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"SFTP {purpose} location must be an absolute sftp:// URI.");

        if (!uri.Scheme.Equals("sftp", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SFTP {purpose} location must use the sftp:// scheme.");

        if (string.IsNullOrWhiteSpace(uri.Host))
            throw new InvalidOperationException($"SFTP {purpose} location must include a host.");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException($"SFTP {purpose} location must not include user info.");

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException($"SFTP {purpose} location must not include query or fragment components.");

        var port = uri.Port > 0 ? uri.Port : 22;
        if (port <= 0 || port > 65535)
            throw new InvalidOperationException("SFTP port must be between 1 and 65535.");

        var path = uri.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            throw new InvalidOperationException($"SFTP {purpose} location must include an absolute remote path.");

        return new SftpRemoteLocation(uri.Host, port, NormalizePath(path));
    }

    public bool ContainsFile(string remoteFilePath)
    {
        var filePath = NormalizePath(remoteFilePath);
        return filePath.Equals(RemotePath, StringComparison.Ordinal)
            || filePath.StartsWith(RemotePath.TrimEnd('/') + "/", StringComparison.Ordinal);
    }

    public static string Combine(string left, string right)
        => NormalizePath(left.TrimEnd('/') + "/" + right.TrimStart('/'));

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("SFTP remote path is required.");

        var normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            throw new InvalidOperationException("SFTP remote paths must be absolute.");

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static s => s is "." or ".."))
            throw new InvalidOperationException("SFTP remote paths must not contain traversal segments.");

        return "/" + string.Join('/', segments);
    }
}
