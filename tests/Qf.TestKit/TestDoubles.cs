using Qf.BuildingBlocks.Application;

namespace Qf.TestKit;

/// <summary>Controllable clock. Domain code takes TimeProvider, so tests never depend on wall-clock time.</summary>
public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public static readonly DateTimeOffset Default = new(2026, 11, 5, 15, 0, 0, TimeSpan.Zero);

    public FakeClock()
        : this(Default)
    {
    }

    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

public sealed class FakeTenant(Guid tenantId) : ITenantContext
{
    public static readonly Guid A = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000001");
    public static readonly Guid B = Guid.Parse("b2b2b2b2-0000-4000-8000-000000000002");

    public Guid TenantId { get; } = tenantId;

    public string UserId => "test-user";
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
