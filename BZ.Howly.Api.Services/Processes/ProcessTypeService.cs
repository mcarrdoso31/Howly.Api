using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Services.Processes;

public sealed class ProcessTypeService(IProcessTypeRepository repository, IAgencyRepository agencies, TimeProvider timeProvider) : IProcessTypeService
{
    public Task<IReadOnlyList<ProcessTypeListItemResponse>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        repository.ListAsync(includeInactive, cancellationToken);

    public async Task<OperationResult<ProcessTypeResponse>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var type = await repository.FindByIdAsync(id, cancellationToken);
        return type is null ? OperationResult<ProcessTypeResponse>.NotFound() : OperationResult<ProcessTypeResponse>.Success(ProcessTypeResponse.From(type));
    }

    public async Task<OperationResult<ProcessTypeResponse>> CreateAsync(ProcessTypeRequest request, CancellationToken cancellationToken = default)
    {
        var (steps, errors) = await StepsAsync(request, kept: new HashSet<Guid>(), cancellationToken);
        if (errors.Count > 0)
            return OperationResult<ProcessTypeResponse>.Invalid(errors);

        var type = ProcessType.Create(request.Name!, request.Description, steps, timeProvider.GetUtcNow());
        await repository.AddAsync(type, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return OperationResult<ProcessTypeResponse>.Success(ProcessTypeResponse.From(type));
    }

    public async Task<OperationResult<ProcessTypeResponse>> UpdateAsync(Guid id, ProcessTypeRequest request, CancellationToken cancellationToken = default)
    {
        var type = await repository.FindByIdAsync(id, cancellationToken);
        if (type is null)
            return OperationResult<ProcessTypeResponse>.NotFound();

        // An agency deactivated after it was chosen stays valid where it already was.
        var kept = type.Steps.Where(s => s.AgencyId is not null).Select(s => s.AgencyId!.Value).ToHashSet();
        var (steps, errors) = await StepsAsync(request, kept, cancellationToken);
        if (errors.Count > 0)
            return OperationResult<ProcessTypeResponse>.Invalid(errors);

        type.Update(request.Name!, request.Description, steps, timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);

        return OperationResult<ProcessTypeResponse>.Success(ProcessTypeResponse.From(type));
    }

    public async Task<OperationResult<ProcessTypeResponse>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var type = await repository.FindByIdAsync(id, cancellationToken);
        if (type is null)
            return OperationResult<ProcessTypeResponse>.NotFound();

        if (isActive)
            type.Reactivate();
        else
            type.Deactivate();
        await repository.SaveChangesAsync(cancellationToken);

        return OperationResult<ProcessTypeResponse>.Success(ProcessTypeResponse.From(type));
    }

    /// <summary>
    /// The steps of the request, validated, with each chosen agency checked (it exists in this consultancy and is
    /// active, unless the type already had it) and its acronym as the agency name.
    /// </summary>
    private async Task<(IReadOnlyList<StepTemplateDetails> Steps, IReadOnlyDictionary<string, string[]> Errors)> StepsAsync(
        ProcessTypeRequest request, IReadOnlySet<Guid> kept, CancellationToken cancellationToken)
    {
        var steps = request.StepDetails().ToList();
        var errors = new Dictionary<string, string[]>(ProcessType.Validate(request.Name, request.Description, steps));

        var chosen = steps.Where(s => s.Owner == StepOwner.Agency && s.AgencyId is not null).Select(s => s.AgencyId!.Value).ToHashSet();
        var found = (await agencies.FindManyAsync(chosen, cancellationToken)).ToDictionary(a => a.Id);
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Owner != StepOwner.Agency || steps[i].AgencyId is not { } agencyId)
                continue;
            if (!found.TryGetValue(agencyId, out var agency) || (!agency.IsActive && !kept.Contains(agencyId)))
                errors[$"steps[{i}].agencyId"] = ["Choose an active agency of the catalog."];
            else
                steps[i] = steps[i] with { AgencyName = agency.DisplayName };
        }

        return (steps, errors);
    }
}
