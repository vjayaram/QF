using Qf.BuildingBlocks.Domain;

namespace Qf.BuildingBlocks.Hosting.GraphQL;

/// <summary>Business errors returned in mutation payloads (not as GraphQL errors), so clients can branch on Code.</summary>
public sealed record UserError(string Code, string Message, ErrorKind Kind);

public static class ResultExtensions
{
    public static IReadOnlyList<UserError> ToUserErrors(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return [.. result.Errors.Select(e => new UserError(e.Code, e.Message, e.Kind))];
    }
}
