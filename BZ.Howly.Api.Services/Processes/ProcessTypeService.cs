using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Services.Processes;

public sealed class ProcessTypeService(IProcessTypeRepository repository, TimeProvider timeProvider) : IProcessTypeService
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
        var steps = request.StepDetails();
        var errors = ProcessType.Validate(request.Name, request.Description, steps);
        if (errors.Count > 0)
            return OperationResult<ProcessTypeResponse>.Invalid(errors);

        var type = ProcessType.Create(request.Name!, request.Description, steps, timeProvider.GetUtcNow());
        await repository.AddAsync(type, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return OperationResult<ProcessTypeResponse>.Success(ProcessTypeResponse.From(type));
    }

    public async Task<OperationResult<ProcessTypeResponse>> UpdateAsync(Guid id, ProcessTypeRequest request, CancellationToken cancellationToken = default)
    {
        var steps = request.StepDetails();
        var errors = ProcessType.Validate(request.Name, request.Description, steps);
        if (errors.Count > 0)
            return OperationResult<ProcessTypeResponse>.Invalid(errors);

        var type = await repository.FindByIdAsync(id, cancellationToken);
        if (type is null)
            return OperationResult<ProcessTypeResponse>.NotFound();

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
}
