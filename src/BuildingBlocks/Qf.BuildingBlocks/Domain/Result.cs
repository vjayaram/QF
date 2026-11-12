namespace Qf.BuildingBlocks.Domain;

/// <summary>
/// Outcome of an operation that can fail for expected (business) reasons.
/// Exceptions are reserved for programming errors and infrastructure faults.
/// </summary>
public class Result
{
    protected Result(IReadOnlyList<Error> errors) => Errors = errors;

    public IReadOnlyList<Error> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;

    public bool IsFailure => !IsSuccess;

    public static Result Success() => new([]);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new([error]);
    }

    public static Result Failure(IEnumerable<Error> errors) => new(RequireAny(errors));

    protected static IReadOnlyList<Error> RequireAny(IEnumerable<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var list = errors.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("A failed result needs at least one error.", nameof(errors));
        }

        return list;
    }
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value)
        : base([]) => _value = value;

    private Result(IReadOnlyList<Error> errors)
        : base(errors)
    {
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result: {Errors[0].Code}.");

    public static Result<T> Success(T value) => new(value);

    public static new Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new([error]);
    }

    public static new Result<T> Failure(IEnumerable<Error> errors) => new(RequireAny(errors));

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}

/// <summary>Return type for commands that produce no value.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value;
}
