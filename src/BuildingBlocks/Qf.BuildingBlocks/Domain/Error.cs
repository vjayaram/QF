namespace Qf.BuildingBlocks.Domain;

public enum ErrorKind
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    BusinessRule = 4,
    Forbidden = 5,
}

/// <summary>A machine-readable failure. Codes are stable and safe to return to API callers (no PHI).</summary>
public sealed record Error(string Code, string Message, ErrorKind Kind)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Rule(string code, string message) => new(code, message, ErrorKind.BusinessRule);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);
}
