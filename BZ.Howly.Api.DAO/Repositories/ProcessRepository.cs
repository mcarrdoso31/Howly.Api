using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.DAO.Context;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.DAO.Repositories;

public sealed class ProcessRepository(HowlyDbContext context) : IProcessRepository
{
    public async Task AddAsync(Process process, CancellationToken cancellationToken = default) =>
        await context.Processes.AddAsync(process, cancellationToken);

    public Task<Process?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Processes.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListExistingIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        ids.Count == 0 ? [] : await context.Processes.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Process>> ListOverdueAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await context.Processes
            .Where(p => p.Status == ProcessStatus.InProgress && p.ActiveStepDueAt != null && p.ActiveStepDueAt < now)
            // Only the ones not warned yet: late processes piling up do not make every round heavier.
            .Where(p => p.Steps.Any(s => s.Id == p.ActiveStepId && s.OverdueNotifiedAt == null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Process>> ListOpenAssignedToAsync(Guid assigneeId, CancellationToken cancellationToken = default) =>
        await context.Processes
            .Where(p => p.AssigneeId == assigneeId && (p.Status == ProcessStatus.InProgress || p.Status == ProcessStatus.Paused))
            .ToListAsync(cancellationToken);

    public async Task<ProcessSummaryResponse> SummarizeAsync(Guid? assigneeId, DateTimeOffset now, DateOnly today, CancellationToken cancellationToken = default)
    {
        // Completed and cancelled processes are history. In progress ones wait on someone (paused ones wait on nobody,
        // their WaitingOn is null); paused ones are counted apart, with those past their expected return.
        var open = context.Processes.Where(p =>
            (p.Status == ProcessStatus.InProgress || p.Status == ProcessStatus.Paused) && (assigneeId == null || p.AssigneeId == assigneeId));
        var counts = await open
            .GroupBy(_ => 1)
            .Select(g => new ProcessSummaryResponse(
                g.Count(p => p.Status == ProcessStatus.InProgress),
                g.Count(p => p.Status == ProcessStatus.InProgress && p.ActiveStepDueAt != null && p.ActiveStepDueAt < now),
                g.Count(p => p.WaitingOn == StepOwner.Customer),
                g.Count(p => p.WaitingOn == StepOwner.Staff),
                g.Count(p => p.WaitingOn == StepOwner.Agency),
                g.Count(p => p.WaitingOn == StepOwner.Payment),
                g.Count(p => p.Status == ProcessStatus.Paused),
                g.Count(p => p.Status == ProcessStatus.Paused && p.ExpectedReturnOn != null && p.ExpectedReturnOn < today)))
            .SingleOrDefaultAsync(cancellationToken);
        // Renewals come from any license not cancelled, usually completed processes.
        var renewalsDue = await RenewalsDue(context.Processes, today)
            .CountAsync(p => assigneeId == null || p.AssigneeId == assigneeId, cancellationToken);
        return (counts ?? new ProcessSummaryResponse(0, 0, 0, 0, 0, 0, 0, 0)) with { RenewalsDue = renewalsDue };
    }

    /// <summary>The renewal should have started and nobody opened it yet.</summary>
    private static IQueryable<Process> RenewalsDue(IQueryable<Process> processes, DateOnly today) =>
        processes.Where(p => p.Status != ProcessStatus.Cancelled && p.RenewalStartsOn != null && p.RenewalStartsOn <= today && p.RenewalProcessId == null);

    public async Task<IReadOnlyList<Process>> ListRenewalsDueAsync(DateOnly today, CancellationToken cancellationToken = default) =>
        await RenewalsDue(context.Processes, today).Where(p => p.RenewalReminderSentAt == null).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Process>> ListPausedDueAsync(DateOnly today, CancellationToken cancellationToken = default) =>
        await context.Processes
            .Where(p => p.Status == ProcessStatus.Paused && p.ExpectedReturnOn != null && p.ExpectedReturnOn <= today && p.ReturnReminderSentAt == null)
            .ToListAsync(cancellationToken);

    public Task<int> CountOpenSubprocessesAsync(Guid parentId, CancellationToken cancellationToken = default) =>
        context.Processes.CountAsync(
            p => p.ParentId == parentId && (p.Status == ProcessStatus.InProgress || p.Status == ProcessStatus.Paused), cancellationToken);

    public async Task<(IReadOnlyList<ProcessListItemResponse> Items, int TotalCount)> SearchAsync(
        ProcessListQuery query, DateTimeOffset now, DateOnly today, CancellationToken cancellationToken = default)
    {
        var processes = query.RenewalDue ? RenewalsDue(context.Processes, today) : context.Processes;
        var rows =
            from process in processes
            join customer in context.Customers on process.CustomerId equals customer.Id
            join assignee in context.Users on process.AssigneeId equals assignee.Id
            select new { process, customer, assignee };

        if (query.CustomerId is { } customerId)
            rows = rows.Where(x => x.process.CustomerId == customerId);
        if (query.ParentId is { } parentId)
            rows = rows.Where(x => x.process.ParentId == parentId);
        if (query.Status is { } status)
            rows = rows.Where(x => x.process.Status == status);
        if (query.WaitingOn is { } waitingOn)
            rows = rows.Where(x => x.process.WaitingOn == waitingOn);
        if (query.AssigneeId is { } assigneeId)
            rows = rows.Where(x => x.process.AssigneeId == assigneeId);
        if (query.Overdue)
            rows = rows.Where(x => x.process.ActiveStepDueAt != null && x.process.ActiveStepDueAt < now);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ToUpper on both sides keeps the search case-insensitive on SQL Server, SQLite and PostgreSQL alike.
            var term = query.Search.Trim().ToUpperInvariant();
            rows = rows.Where(x =>
                x.process.Title.ToUpper().Contains(term) ||
                x.process.ProcessTypeName.ToUpper().Contains(term) ||
                x.customer.CompanyName.ToUpper().Contains(term) ||
                (x.customer.TradeName != null && x.customer.TradeName.ToUpper().Contains(term)));
        }

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderByDescending(x => x.process.UpdatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new
            {
                x.process.Id,
                x.process.Title,
                x.process.CustomerId,
                CustomerName = x.customer.TradeName ?? x.customer.CompanyName,
                x.process.ProcessTypeName,
                x.process.Status,
                x.process.WaitingOn,
                x.process.ActiveStepName,
                ActiveStepAgencyName = x.process.Steps.Where(s => s.Status == StepStatus.Active).Select(s => s.AgencyName).FirstOrDefault(),
                x.process.ActiveStepStartedAt,
                x.process.ActiveStepDueAt,
                x.process.AssigneeId,
                AssigneeName = x.assignee.DisplayName,
                x.assignee.AvatarUpdatedAt,
                StepsDone = x.process.Steps.Count(s => s.Status == StepStatus.Done || s.Status == StepStatus.Skipped),
                StepsTotal = x.process.Steps.Count,
                x.process.ParentId,
                x.process.UpdatedAt,
                x.process.PauseWaitingOnName,
                x.process.ExpectedReturnOn,
                x.process.LicenseExpiresOn,
                x.process.RenewalStartsOn,
                // What the customer still has to send in the current step.
                DocumentsToSend = context.ProcessDocuments.Count(d =>
                    d.ProcessId == x.process.Id && d.StepId == x.process.ActiveStepId && d.Kind == DocumentKind.Requested &&
                    (d.Status == DocumentStatus.AwaitingUpload || d.Status == DocumentStatus.Rejected)),
            })
            .ToListAsync(cancellationToken);

        var items = page.Select(x => new ProcessListItemResponse(
                x.Id, x.Title, x.CustomerId, x.CustomerName, x.ProcessTypeName, x.Status, x.WaitingOn, x.ActiveStepName,
                x.ActiveStepAgencyName, x.ActiveStepStartedAt, x.ActiveStepDueAt, x.AssigneeId, x.AssigneeName,
                x.AvatarUpdatedAt?.ToUnixTimeMilliseconds().ToString(), x.StepsDone, x.StepsTotal, x.ParentId, x.UpdatedAt, x.DocumentsToSend,
                x.PauseWaitingOnName, x.ExpectedReturnOn, x.LicenseExpiresOn, x.RenewalStartsOn))
            .ToList();

        return (items, total);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
}
