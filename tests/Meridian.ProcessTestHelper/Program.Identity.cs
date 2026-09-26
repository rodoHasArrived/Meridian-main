using Meridian.Identity;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Meridian.ProcessTestHelper;

internal static partial class Program
{
    private static async Task<int> CreateSessionAndObserveRevocationAsync(IReadOnlyList<string> args)
    {
        RequireArgumentCount(args, 6);
        var sessions = new LoginSessionService(new SessionHostEnvironment(), new UserProfileRegistry(),
            new LoginSessionStoreOptions(args[1]));
        await File.WriteAllTextAsync(args[2], "ready").ConfigureAwait(false);
        await WaitForFileAsync(args[3], TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        var token = sessions.CreateSession("operator", "operator-password")
            ?? throw new InvalidOperationException("Process login failed.");
        await File.WriteAllTextAsync(args[4], token).ConfigureAwait(false);
        await WaitForFileAsync(args[5], TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        if (sessions.ValidateSession(token))
            throw new InvalidOperationException("A running process accepted a revoked session.");
        if (sessions.CreateSession("operator", "operator-password") is null || sessions.ValidateSession(token))
            throw new InvalidOperationException("A subsequent login resurrected a revoked session.");
        return 0;
    }

    private sealed class SessionHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "IdentityProcessTest";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
