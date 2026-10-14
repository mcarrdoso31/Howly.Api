using BZ.Howly.Api.Core.Interfaces.Service;

namespace BZ.Howly.Api.Workers;

/// <summary>
/// Checks for steps past their deadline when the API starts and then every <c>OverdueSteps:Interval</c> (1 hour by default).
/// <c>OverdueSteps:Enabled = false</c> turns it off.
/// </summary>
public sealed class OverdueStepsWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<OverdueStepsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off in the integration tests, which run the check themselves with the time they need.
        if (!configuration.GetValue("OverdueSteps:Enabled", true))
            return;

        var interval = configuration.GetValue("OverdueSteps:Interval", TimeSpan.FromHours(1));
        using var timer = new PeriodicTimer(interval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IOverdueStepService>().NotifyOverdueAsync(timeProvider.GetUtcNow(), stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The next round tries again; one failure must not stop the checks for good.
                logger.LogError(exception, "Checking overdue steps failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
