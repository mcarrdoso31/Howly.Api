using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Processes;

/// <summary>
/// A template the staff configure (e.g. "Emissão de MTR"): the ordered steps a process of this kind goes through.
/// Editing it never touches processes already opened, which keep their own copy of the steps.
/// </summary>
public sealed class ProcessType
{
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 1000;
    public const int MaxNotesLength = 2000;
    public const int MaxSteps = 30;
    public const int MaxDurationDays = 365;
    public const int MaxDocumentsPerStep = 30;

    private readonly List<StepTemplate> _steps = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyList<StepTemplate> Steps => _steps.OrderBy(s => s.Position).ToList();
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ProcessType() { }

    /// <summary>Field errors keyed like the request body: "name", "description", "steps" and "steps[i].field".</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(string? name, string? description, IReadOnlyList<StepTemplateDetails>? steps)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
            errors["name"] = [$"Name is required and must have at most {MaxNameLength} characters."];
        if (description?.Trim().Length > MaxDescriptionLength)
            errors["description"] = [$"Description must have at most {MaxDescriptionLength} characters."];

        steps ??= [];
        if (steps.Count is 0 or > MaxSteps)
            errors["steps"] = [$"Between 1 and {MaxSteps} steps are required."];

        // Steps are told apart by name on the timeline and in notifications.
        var stepNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var prefix = $"steps[{i}]";

            if (string.IsNullOrWhiteSpace(step.Name) || step.Name.Trim().Length > MaxNameLength)
                errors[$"{prefix}.name"] = [$"Name is required and must have at most {MaxNameLength} characters."];
            else if (!stepNames.Add(step.Name.Trim()))
                errors[$"{prefix}.name"] = ["Each step must have a different name."];
            if (step.Description?.Trim().Length > MaxDescriptionLength)
                errors[$"{prefix}.description"] = [$"Description must have at most {MaxDescriptionLength} characters."];
            if (step.Owner is not { } owner || !Enum.IsDefined(owner))
                errors[$"{prefix}.owner"] = ["Owner must be Customer, Staff, Agency or Payment."];
            // The agency can be left for each process to set: only its length is checked here.
            if (step.Owner == StepOwner.Agency && step.AgencyName?.Trim().Length > MaxNameLength)
                errors[$"{prefix}.agencyName"] = [$"Agency name must have at most {MaxNameLength} characters."];
            if (step.DurationDays is < 1 or > MaxDurationDays)
                errors[$"{prefix}.durationDays"] = [$"Duration must be between 1 and {MaxDurationDays} days."];
            if (step.Notes?.Trim().Length > MaxNotesLength)
                errors[$"{prefix}.notes"] = [$"Notes must have at most {MaxNotesLength} characters."];

            ValidateNames(errors, $"{prefix}.documents", (step.Documents ?? []).Select(d => d?.Name).ToList());
            ValidateNames(errors, $"{prefix}.deliverables", step.Deliverables ?? []);
        }

        return errors;
    }

    /// <summary>Document and deliverable names: required, short, at most 30 per step and no repeats (they name the fields).</summary>
    private static void ValidateNames(Dictionary<string, string[]> errors, string prefix, IReadOnlyList<string?> names)
    {
        if (names.Count > MaxDocumentsPerStep)
            errors[prefix] = [$"At most {MaxDocumentsPerStep} items per step."];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i]?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > ProcessDocument.MaxNameLength)
                errors[$"{prefix}[{i}]"] = [$"Name is required and must have at most {ProcessDocument.MaxNameLength} characters."];
            else if (!seen.Add(name))
                errors[$"{prefix}[{i}]"] = ["Each item of the step must have a different name."];
        }
    }

    public static ProcessType Create(string name, string? description, IReadOnlyList<StepTemplateDetails> steps, DateTimeOffset now)
    {
        var type = new ProcessType { Id = Ids.New(), IsActive = true, CreatedAt = now };
        type.Update(name, description, steps, now);
        return type;
    }

    /// <summary>
    /// Replaces the name, description and the whole step list. Steps given with the id of one of this type's steps keep
    /// that identity (edited in place); the others are new, and the steps left out are removed.
    /// </summary>
    public void Update(string name, string? description, IReadOnlyList<StepTemplateDetails> steps, DateTimeOffset now)
    {
        var errors = Validate(name, description, steps);
        if (errors.Count > 0)
            throw new ArgumentException($"Invalid process type: {string.Join(", ", errors.Keys)}.");

        Name = name.Trim();
        Description = ProcessText.Blank(description);
        var current = _steps.ToDictionary(s => s.Id);
        var kept = new List<StepTemplate>();
        for (var position = 0; position < steps.Count; position++)
        {
            // An id repeated in the request keeps its identity only once.
            if (steps[position].Id is { } id && current.Remove(id, out var existing))
            {
                existing.Apply(steps[position], position);
                kept.Add(existing);
            }
            else
            {
                kept.Add(StepTemplate.FromValidated(steps[position], position));
            }
        }

        _steps.Clear();
        _steps.AddRange(kept);
        UpdatedAt = now;
    }

    /// <summary>Hidden from new processes; processes already opened are not affected.</summary>
    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
