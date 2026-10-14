namespace BZ.Howly.Api.Workers;

/// <summary>
/// Checks for steps past their deadline when the API starts and then every <c>OverdueSteps:Interval</c> (1 hour by default).
/// <c>OverdueSteps:Enabled = false</c> turns it off. Every instance runs this timer; <see cref="OverdueStepsRunner"/>
/// makes it one round per interval for all of them.
/// </summary>
public sealed class OverdueStepsWorker(
    OverdueStepsRunner runner,
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
        if (interval <= TimeSpan.Zero)
        {
            logger.LogError("OverdueSteps:Interval must be positive; the overdue check is off.");
            return;
        }

        using var timer = new PeriodicTimer(interval, timeProvider);
        do
        {
            try
            {
                await runner.RunOnceAsync(timeProvider.GetUtcNow(), interval, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // The next round tries again; one failure must not stop the checks (nor the API) for good.
                logger.LogError(exception, "Checking overdue steps failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
