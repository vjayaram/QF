using Qf.BuildingBlocks.Domain;

namespace Qf.Scheduling.Domain;

/// <summary>Hand-written invariants and behaviour for the generated TimeSlot value object.</summary>
public sealed partial record TimeSlot
{
    public static readonly TimeSpan MinimumLength = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaximumLength = TimeSpan.FromHours(8);

    public TimeSpan Duration => End - Start;

    /// <summary>Half-open intervals: a 9:00-10:00 slot does not overlap 10:00-11:00.</summary>
    public bool Overlaps(TimeSlot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Start < other.End && other.Start < End;
    }

    static partial void ValidateInvariants(DateTimeOffset start, DateTimeOffset end, List<Error> errors)
    {
        if (end <= start)
        {
            errors.Add(Error.Validation("TimeSlot.End.BeforeStart", "End must be after start."));
            return;
        }

        var length = end - start;
        if (length < MinimumLength)
        {
            errors.Add(Error.Validation("TimeSlot.TooShort", $"An appointment must be at least {MinimumLength.TotalMinutes} minutes."));
        }

        if (length > MaximumLength)
        {
            errors.Add(Error.Validation("TimeSlot.TooLong", $"An appointment cannot exceed {MaximumLength.TotalHours} hours."));
        }
    }
}
