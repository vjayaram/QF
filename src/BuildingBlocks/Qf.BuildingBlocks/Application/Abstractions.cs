using Qf.BuildingBlocks.Domain;

namespace Qf.BuildingBlocks.Application;

/// <summary>Marker for a state-changing request handled by exactly one handler.</summary>
public interface ICommand<TResult>;

/// <summary>
/// Plain handler interface instead of a mediator library: explicit DI wiring, no reflection,
/// no commercial-licence dependency, trivially unit-testable.
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Who is calling, resolved from the validated Entra ID token. Never from request input.</summary>
public interface ITenantContext
{
    Guid TenantId { get; }

    string UserId { get; }
}
