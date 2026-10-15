using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Tenancy;
using Microsoft.Extensions.Logging;

namespace BZ.Howly.Api.Services.Processes;

public sealed class PauseReturnService(
    IProcessRepository processRepository,
    INotificationService notificationService,
    ITenantContext tenantContext,
    ILogger<PauseReturnService> logger) : IPauseReturnService
{
    public async Task<int> NotifyDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // "Expected back on 10/01" is a local day: it arrives at midnight in the consultancy's time zone.
        var today = (tenantContext.Tenant?.Profile ?? TenantProfile.Empty).LocalDate(now);
        var told = 0;
        foreach (var process in await processRepository.ListPausedDueAsync(today, cancellationToken))
        {
            var eventsBefore = process.Events.Count;
            if (!process.MarkPauseReturnDue(today, now))
                continue;

            await notificationService.NotifyAsync(process, eventsBefore, Actor.System.Id, cancellationToken);
            told++;
        }

        if (told > 0)
        {
            await processRepository.SaveChangesAsync(cancellationToken);
            logger.LogInformation("{Count} paused processes reached their expected return.", told);
        }

        return told;
    }
}
