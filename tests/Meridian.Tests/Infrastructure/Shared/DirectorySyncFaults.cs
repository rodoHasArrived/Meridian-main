using Meridian.Storage.Archival;

namespace Meridian.Tests.Infrastructure;

/// <summary>
/// Scopes <see cref="AtomicFileWriter.DirectorySyncFaultInjector"/> to the calling test's async
/// flow. Call it from the async test method itself (not a constructor) and dispose it before
/// the retry, so the injection never leaks into another test.
/// </summary>
internal static class DirectorySyncFaults
{
    /// <summary>Fails every directory fsync of <paramref name="directory"/> with EIO.</summary>
    public static IDisposable FailIn(string directory) => FailWhen(dir => SameDirectory(dir, directory));

    /// <summary>Fails a directory fsync with EIO whenever <paramref name="predicate"/> holds.</summary>
    public static IDisposable FailWhen(Func<string, bool> predicate)
    {
        AtomicFileWriter.DirectorySyncFaultInjector.Value = dir => predicate(dir) ? ("fsync", 5) : null;
        return new Reset();
    }

    public static bool SameDirectory(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private sealed class Reset : IDisposable
    {
        public void Dispose() => AtomicFileWriter.DirectorySyncFaultInjector.Value = null;
    }
}
