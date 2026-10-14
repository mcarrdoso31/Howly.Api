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
