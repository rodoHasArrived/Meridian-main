using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Contracts;

/// <summary>
/// Extension methods for adding contract verification to the DI container.
/// </summary>
public static class ContractVerificationExtensions
{
    /// <summary>
    /// Adds contract verification services to the DI container.
    /// </summary>
    public static IServiceCollection AddContractVerification(this IServiceCollection services)
    {
        services.AddSingleton<ContractVerificationService>();
        services.AddHostedService<ContractVerificationHostedService>();
        return services;
    }

    /// <summary>
    /// Verifies contracts at startup and logs results.
    /// </summary>
    public static IHost VerifyContracts(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<ContractVerificationService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ContractVerificationService>>();

        var assemblies = new[]
        {
            typeof(ContractVerificationService).Assembly,
        };

        var success = verifier.VerifyContracts(assemblies);

        if (!success)
        {
            logger.LogWarning(
                "Contract verification completed with {Count} violations",
                verifier.Violations.Count);
        }

        return host;
    }
}

/// <summary>
/// Background service that verifies contracts on startup.
/// </summary>
public sealed class ContractVerificationHostedService : IHostedService
{
    private readonly ContractVerificationService _verifier;
    private readonly ILogger<ContractVerificationHostedService> _logger;

    public ContractVerificationHostedService(
        ContractVerificationService verifier,
        ILogger<ContractVerificationHostedService> logger)
    {
        _verifier = verifier;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting contract verification...");

        var assemblies = new[]
        {
            typeof(ContractVerificationService).Assembly,
        };

        var success = _verifier.VerifyContracts(assemblies);

        if (success)
        {
            _logger.LogInformation("All contracts verified successfully");
        }
        else
        {
            _logger.LogWarning(
                "Contract verification found {Count} issues - review logs for details",
                _verifier.Violations.Count);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
