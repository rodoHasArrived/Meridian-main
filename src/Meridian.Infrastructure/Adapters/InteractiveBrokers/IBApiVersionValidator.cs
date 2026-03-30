using Serilog;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Validates the IB API server and client library versions at startup.
/// Throws <see cref="IBApiVersionMismatchException"/> when the connected TWS/Gateway
/// server version is below the minimum required by Meridian, and logs a warning when
/// the server version exceeds the highest version tested in CI.
/// </summary>
/// <remarks>
/// Version numbers used here are IB EClient/EWrapper "server version" integers exchanged
/// during the initial TCP handshake (EClientSocket.ServerVersion after Connect).
/// They are NOT the same as the human-readable TWS release numbers shown in the TWS
/// title bar (e.g. "10.19").
///
/// Meridian version compatibility:
/// <list type="table">
///   <item><term>Server version &lt; 70</term><description>Not supported — missing reqTickByTickData, reqSmartComponents, and other modern API methods.</description></item>
///   <item><term>70 – 178</term><description>Supported and tested.</description></item>
///   <item><term>&gt; 178</term><description>Untested — Meridian will continue but log a warning.</description></item>
/// </list>
/// </remarks>
public static class IBApiVersionValidator
{
    /// <summary>
    /// Minimum IB server version Meridian requires. Introduced with TWS 966+.
    /// Below this version, reqTickByTickData and other critical methods are unavailable.
    /// </summary>
    public const int MinSupportedServerVersion = 70;

    /// <summary>
    /// The highest IB server version verified in Meridian CI. Server versions above
    /// this are accepted but trigger a warning so operators can update this constant
    /// after confirming compatibility.
    /// </summary>
    public const int MaxTestedServerVersion = 178;

    /// <summary>
    /// Minimum IB client API library version (IBApi DLL) that Meridian is built against.
    /// This maps to the TWS API installer version 10.19.
    /// </summary>
    public const int MinSupportedClientVersion = 178;

    private static readonly ILogger _log = Log.ForContext(typeof(IBApiVersionValidator));

    /// <summary>
    /// Validates the <paramref name="serverVersion"/> returned by TWS/Gateway after
    /// connection. Call this immediately after <c>EClientSocket.Connect</c> succeeds.
    /// </summary>
    /// <param name="serverVersion">
    /// The integer server version from <c>EClientSocket.ServerVersion</c>.
    /// </param>
    /// <param name="clientVersion">
    /// The integer client version from the IBApi DLL (pass <c>EClient.MIN_VERSION</c>
    /// or the constant defined in the official SDK).
    /// </param>
    /// <exception cref="IBApiVersionMismatchException">
    /// Thrown when <paramref name="serverVersion"/> is below <see cref="MinSupportedServerVersion"/>.
    /// </exception>
    public static void ValidateServerVersion(int serverVersion, int clientVersion)
    {
        if (serverVersion < MinSupportedServerVersion)
        {
            var message =
                $"IB server version {serverVersion} is below the minimum supported version {MinSupportedServerVersion}. "
                + "Upgrade TWS or IB Gateway to version 966 or later (API server version >= 70). "
                + $"See {IBBuildGuidance.SetupGuidePath} for installation guidance.";

            _log.Error(
                "IB API version mismatch: server={ServerVersion} (min={Min}), client={ClientVersion}",
                serverVersion, MinSupportedServerVersion, clientVersion);

            throw new IBApiVersionMismatchException(serverVersion, clientVersion, message);
        }

        if (serverVersion > MaxTestedServerVersion)
        {
            _log.Warning(
                "IB server version {ServerVersion} exceeds the highest tested version {MaxTested}. "
                + "Meridian will continue, but some API behaviour may differ. "
                + "Update {Constant} in {Class} after confirming compatibility",
                serverVersion, MaxTestedServerVersion,
                nameof(MaxTestedServerVersion), nameof(IBApiVersionValidator));
        }
        else
        {
            _log.Information(
                "IB API version check passed: server={ServerVersion}, client={ClientVersion}",
                serverVersion, clientVersion);
        }
    }

    /// <summary>
    /// Returns a human-readable summary of the version requirements, suitable for
    /// inclusion in startup logs or operator runbooks.
    /// </summary>
    public static string BuildVersionRequirementsMessage() =>
        $"Meridian requires IB server version >= {MinSupportedServerVersion} (TWS 966+, API 10.x). "
        + $"Tested up to server version {MaxTestedServerVersion} (TWS 10.19). "
        + $"See {IBBuildGuidance.SetupGuidePath} for version compatibility details.";
}

/// <summary>
/// Thrown when the connected TWS/Gateway server version is incompatible with Meridian.
/// </summary>
public sealed class IBApiVersionMismatchException : IBApiException
{
    /// <summary>Server version reported by TWS/Gateway.</summary>
    public int ServerVersion { get; }

    /// <summary>Client library version compiled into this build.</summary>
    public int ClientVersion { get; }

    public IBApiVersionMismatchException(int serverVersion, int clientVersion, string message)
        : base(errorCode: 0, message)
    {
        ServerVersion = serverVersion;
        ClientVersion = clientVersion;
    }
}
