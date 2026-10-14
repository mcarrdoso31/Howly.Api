using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Tenancy;

namespace BZ.Howly.Api.Workers;

/// <summary>
/// One round of the overdue check: once across all instances (shared lock), going through every active consultancy in
/// a scope of its own tenant. One consultancy failing does not stop the others.
/// </summary>
public sealed class OverdueStepsRunner(IServiceScopeFactory scopeFactory, ILogger<OverdueStepsRunner> logger)
{
    private const string LockName = "howly:overdue-steps";

    public async Task RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var held = await scope.ServiceProvider.GetRequiredService<IJobLock>().TryAcquireAsync(LockName, cancellationToken);
        if (held is null)
        {
            logger.LogDebug("Overdue check skipped: another instance is running it.");
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
