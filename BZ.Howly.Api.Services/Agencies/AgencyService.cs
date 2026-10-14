using BZ.Howly.Api.Core.Agencies;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Services.Agencies;

public sealed class AgencyService(IAgencyRepository repository, TimeProvider timeProvider) : IAgencyService
{
    public async Task<IReadOnlyList<AgencyResponse>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        (await repository.ListAsync(includeInactive, cancellationToken)).Select(AgencyResponse.From).ToList();

    public async Task<OperationResult<AgencyResponse>> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await repository.FindAsync(id, cancellationToken) is { } agency
            ? OperationResult<AgencyResponse>.Success(AgencyResponse.From(agency))
            : OperationResult<AgencyResponse>.NotFound();

    public async Task<OperationResult<AgencyResponse>> CreateAsync(AgencyRequest request, CancellationToken cancellationToken = default)
    {
        var details = request.ToDetails();
        var errors = Agency.Validate(details);
        if (errors.Count > 0)
            return OperationResult<AgencyResponse>.Invalid(errors);

        var agency = Agency.Create(details, timeProvider.GetUtcNow());
        await repository.AddAsync(agency, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return OperationResult<AgencyResponse>.Success(AgencyResponse.From(agency));
    }

    /// <remarks>Steps already pointing to it keep the acronym they copied; new choices show the new one.</remarks>
    public async Task<OperationResult<AgencyResponse>> UpdateAsync(Guid id, AgencyRequest request, CancellationToken cancellationToken = default)
    {
        var details = request.ToDetails();
        var errors = Agency.Validate(details);
        if (errors.Count > 0)
            return OperationResult<AgencyResponse>.Invalid(errors);

        var agency = await repository.FindAsync(id, cancellationToken);
        if (agency is null)
            return OperationResult<AgencyResponse>.NotFound();

        agency.Update(details, timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        return OperationResult<AgencyResponse>.Success(AgencyResponse.From(agency));
    }

    public async Task<OperationResult<AgencyResponse>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var agency = await repository.FindAsync(id, cancellationToken);
        if (agency is null)
            return OperationResult<AgencyResponse>.NotFound();

        if (isActive)
            agency.Reactivate(timeProvider.GetUtcNow());
        else
            agency.Deactivate(timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        return OperationResult<AgencyResponse>.Success(AgencyResponse.From(agency));
    }
}
