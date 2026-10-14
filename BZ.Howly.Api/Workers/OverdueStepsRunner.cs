using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Tenancy;

namespace BZ.Howly.Api.Workers;

/// <summary>
/// One round of the overdue check, going through every active consultancy in a scope of its own tenant (one failing
/// does not stop the others). Coordinated across instances by <see cref="IJobLock"/>: one round at a time and, given
/// the interval, one round per interval however many instances tick.
/// </summary>
public sealed class OverdueStepsRunner(IServiceScopeFactory scopeFactory, ILogger<OverdueStepsRunner> logger)
{
    private const string LockName = "howly:overdue-steps";

    /// <param name="interval">The worker's interval; without it the round always runs (manual runs, tests).</param>
    public async Task RunOnceAsync(DateTimeOffset now, TimeSpan? interval = null, CancellationToken cancellationToken = default)
    {
        // A little under the interval: the timers of different instances drift, and a round should not be lost for seconds.
        var minimumGap = interval * 0.9;
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var held = await scope.ServiceProvider.GetRequiredService<IJobLock>().TryStartRoundAsync(LockName, now, minimumGap, cancellationToken);
        if (held is null)
        {
            logger.LogDebug("Overdue check skipped: another instance is running it, or this interval's round already ran.");
            return;
        }

        foreach (var tenant in await scope.ServiceProvider.GetRequiredService<ITenantRepository>().ListActiveConsultanciesAsync(cancellationToken))
        {
            try
            {
                await using var tenantScope = scopeFactory.CreateAsyncScope();
                tenantScope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenant);
                await tenantScope.ServiceProvider.GetRequiredService<IOverdueStepService>().NotifyOverdueAsync(now, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Checking overdue steps failed for {Tenant}.", tenant.Slug);
            }
        }
    }
}
