using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.DTO.Request;

/// <param name="AssigneeId">The staff member responsible; the person opening it when omitted.</param>
/// <param name="ParentId">Opens a subprocess (e.g. one license of a PGRS) of a process of the same customer.</param>
public sealed record OpenProcessRequest(Guid CustomerId, Guid ProcessTypeId, string? Title, Guid? AssigneeId, Guid? ParentId);

/// <summary>Body of advance, skip and comments; the comment is optional except for comments.</summary>
public sealed record ProcessCommentRequest(string? Comment);

public sealed record RenameProcessRequest(string? Title);

/// <summary>Back to an earlier step; the comment (why) is required.</summary>
public sealed record ReturnProcessRequest(Guid StepId, string? Comment);

/// <summary>Body of pause and cancel; the reason is required and shown to the customer.</summary>
public sealed record ProcessReasonRequest(string? Reason);

public sealed record AssignProcessRequest(Guid AssigneeId);

/// <summary>Body of pause: the reason (required, shown to the customer) and, optionally, who it waits on and when it is expected back.</summary>
/// <param name="AgencyId">With <c>WaitingOn = Agency</c>: an active agency of the catalog.</param>
/// <param name="WaitingOnName">With <c>WaitingOn = Other</c>: who (a laboratory, a notary…).</param>
/// <param name="ExpectedReturnOn">The expected day back (the consultancy's local date); not in the past.</param>
public sealed record PauseProcessRequest(
    string? Reason, PauseWaitingOn? WaitingOn = null, Guid? AgencyId = null, string? WaitingOnName = null, DateOnly? ExpectedReturnOn = null);
