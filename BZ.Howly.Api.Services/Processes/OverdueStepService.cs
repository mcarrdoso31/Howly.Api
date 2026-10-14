using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Processes;
using Microsoft.Extensions.Logging;

namespace BZ.Howly.Api.Services.Processes;

public sealed class OverdueStepService(
    IProcessRepository processRepository,
    INotificationService notificationService,
    ILogger<OverdueStepService> logger) : IOverdueStepService
{
    public async Task<int> NotifyOverdueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var told = 0;
        foreach (var process in await processRepository.ListOverdueAsync(now, cancellationToken))
        {
            var eventsBefore = process.Events.Count;
            if (!process.MarkOverdue(now))
                continue;

            await notificationService.NotifyAsync(process, eventsBefore, Actor.System.Id, cancellationToken);
            told++;
        }

        if (told > 0)
        {
            await processRepository.SaveChangesAsync(cancellationToken);
            logger.LogInformation("{Count} processes got an overdue step warning.", told);
        }

        return told;
    }
}
