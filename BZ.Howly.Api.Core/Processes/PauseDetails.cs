namespace BZ.Howly.Api.Core.Processes;

/// <summary>Who a paused process waits on.</summary>
public enum PauseWaitingOn
{
    /// <summary>An agency of the catalog (IBAMA, IAT…).</summary>
    Agency,

    Customer,

    /// <summary>Anyone else, named by the staff (a laboratory, a notary…).</summary>
    Other,
}

/// <summary>What a pause may tell besides its reason, all optional.</summary>
/// <param name="AgencyId">Waiting on an agency: which one.</param>
/// <param name="WaitingOnName">The agency acronym (set by the service) or the name typed for "other"; ignored for the customer.</param>
/// <param name="ExpectedReturnOn">The day it is expected back, in the consultancy's local date.</param>
public sealed record PauseDetails(PauseWaitingOn? WaitingOn, Guid? AgencyId, string? WaitingOnName, DateOnly? ExpectedReturnOn)
{
    public const int MaxNameLength = 120;

    /// <summary>Field errors keyed like the request body ("expectedReturnOn", "agencyId", "waitingOnName"…).</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(PauseDetails? details, DateOnly today)
    {
        try
        {
            Normalize(details, today);
            return new Dictionary<string, string[]>();
        }
        catch (ArgumentException exception)
        {
            return new Dictionary<string, string[]> { [exception.ParamName ?? "pause"] = [exception.Message] };
        }
    }

    /// <summary>The details as stored, or an <see cref="ArgumentException"/> naming the field that is wrong.</summary>
    internal static (PauseWaitingOn? WaitingOn, Guid? AgencyId, string? Name, DateOnly? ExpectedReturnOn) Normalize(PauseDetails? details, DateOnly today)
    {
        if (details is null)
            return (null, null, null, null);
        if (details.ExpectedReturnOn is { } expected && expected < today)
            throw new ArgumentException("The expected return cannot be in the past.", "expectedReturnOn");

        var name = string.IsNullOrWhiteSpace(details.WaitingOnName) ? null : details.WaitingOnName.Trim();
        return details.WaitingOn switch
        {
            null => (null, null, null, details.ExpectedReturnOn),
            PauseWaitingOn.Customer => (PauseWaitingOn.Customer, null, null, details.ExpectedReturnOn),
            PauseWaitingOn.Agency when details.AgencyId is { } agencyId && name is { Length: <= MaxNameLength } =>
                (PauseWaitingOn.Agency, agencyId, name, details.ExpectedReturnOn),
            PauseWaitingOn.Agency => throw new ArgumentException("Choose the agency the process waits on.", "agencyId"),
            PauseWaitingOn.Other when name is { Length: <= MaxNameLength } => (PauseWaitingOn.Other, null, name, details.ExpectedReturnOn),
            PauseWaitingOn.Other => throw new ArgumentException($"Say who the process waits on, with at most {MaxNameLength} characters.", "waitingOnName"),
            _ => throw new ArgumentException("Unknown waiting-on.", "waitingOn"),
        };
    }
}
