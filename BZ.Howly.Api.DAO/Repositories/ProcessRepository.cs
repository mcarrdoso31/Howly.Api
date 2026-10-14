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

    public async Task<IReadOnlyList<Process>> ListOverdueAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await context.Processes
            .Where(p => p.Status == ProcessStatus.InProgress && p.ActiveStepDueAt != null && p.ActiveStepDueAt < now)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Process>> ListOpenAssignedToAsync(Guid assigneeId, CancellationToken cancellationToken = default) =>
        await context.Processes
            .Where(p => p.AssigneeId == assigneeId && (p.Status == ProcessStatus.InProgress || p.Status == ProcessStatus.Paused))
            .ToListAsync(cancellationToken);

    public Task<int> CountOpenSubprocessesAsync(Guid parentId, CancellationToken cancellationToken = default) =>
        context.Processes.CountAsync(
            p => p.ParentId == parentId && (p.Status == ProcessStatus.InProgress || p.Status == ProcessStatus.Paused), cancellationToken);

    public async Task<(IReadOnlyList<ProcessListItemResponse> Items, int TotalCount)> SearchAsync(
        ProcessListQuery query, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var rows =
            from process in context.Processes
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
                // What the customer still has to send in the current step.
                DocumentsToSend = context.ProcessDocuments.Count(d =>
                    d.ProcessId == x.process.Id && d.StepId == x.process.ActiveStepId && d.Kind == DocumentKind.Requested &&
                    (d.Status == DocumentStatus.AwaitingUpload || d.Status == DocumentStatus.Rejected)),
            })
            .ToListAsync(cancellationToken);

        var items = page.Select(x => new ProcessListItemResponse(
                x.Id, x.Title, x.CustomerId, x.CustomerName, x.ProcessTypeName, x.Status, x.WaitingOn, x.ActiveStepName,
                x.ActiveStepAgencyName, x.ActiveStepStartedAt, x.ActiveStepDueAt, x.AssigneeId, x.AssigneeName,
                x.AvatarUpdatedAt?.ToUnixTimeMilliseconds().ToString(), x.StepsDone, x.StepsTotal, x.ParentId, x.UpdatedAt, x.DocumentsToSend))
            .ToList();

        return (items, total);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
}
