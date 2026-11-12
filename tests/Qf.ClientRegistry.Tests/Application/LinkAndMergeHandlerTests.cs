using Qf.ClientRegistry.Application.Commands;
using Qf.ClientRegistry.Domain;
using Qf.TestKit;

namespace Qf.ClientRegistry.Tests.Application;

public class LinkAndMergeHandlerTests
{
    private readonly FakeClock _clock = new();
    private readonly InMemoryClientRepository _clients = new(FakeTenant.A);
    private readonly FakeUnitOfWork _uow = new();

    [Fact]
    public async Task Link_RejectsIdentityOwnedByAnotherClient()
    {
        var owner = TestData.NewClient(_clock, mrn: "MRN-1001");
        var other = TestData.NewClient(_clock, mrn: "MRN-2002");
        owner.LinkLegacyIdentity(TestData.Legacy(LegacySystem.CareLogic, "CL01:5"), _clock);
        _clients.Add(owner);
        _clients.Add(other);

        var result = await new LinkLegacyIdentityHandler(_clients, _uow, _clock)
            .HandleAsync(new LinkLegacyIdentityCommand(other.Id, LegacySystem.CareLogic, "CL01:5"), CancellationToken.None);

        Assert.Equal("Client.LegacyIdentity.LinkedElsewhere", Assert.Single(result.Errors).Code);
        Assert.Equal(0, _uow.Saves);
    }

    [Fact]
    public async Task Link_ReportsUnknownClientAndInvalidInput()
    {
        var handler = new LinkLegacyIdentityHandler(_clients, _uow, _clock);

        var missing = await handler.HandleAsync(new LinkLegacyIdentityCommand(Guid.NewGuid(), LegacySystem.InSync, "IS:1"), CancellationToken.None);
        var invalid = await handler.HandleAsync(new LinkLegacyIdentityCommand(Guid.NewGuid(), LegacySystem.InSync, " "), CancellationToken.None);

        Assert.Equal("Client.NotFound", Assert.Single(missing.Errors).Code);
        Assert.Equal("LegacyIdentity.ExternalId.Required", Assert.Single(invalid.Errors).Code);
    }

    [Fact]
    public async Task Link_SavesOnSuccess()
    {
        var client = TestData.NewClient(_clock);
        _clients.Add(client);

        var result = await new LinkLegacyIdentityHandler(_clients, _uow, _clock)
            .HandleAsync(new LinkLegacyIdentityCommand(client.Id, LegacySystem.Credible, "CR01:7"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(client.LegacyIdentities);
        Assert.Equal(1, _uow.Saves);
    }

    [Fact]
    public async Task Merge_IsScopedToTenant()
    {
        var a = TestData.NewClient(_clock, mrn: "MRN-1001");
        var foreign = TestData.NewClient(_clock, tenant: FakeTenant.B, mrn: "MRN-9009");
        _clients.Add(a);
        _clients.Add(foreign);

        var result = await new MergeClientsHandler(_clients, _uow, _clock)
            .HandleAsync(new MergeClientsCommand(a.Id, foreign.Id), CancellationToken.None);

        Assert.Equal("Client.NotFound", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task Merge_SavesBothAggregatesTogether()
    {
        var a = TestData.NewClient(_clock, mrn: "MRN-1001");
        var b = TestData.NewClient(_clock, mrn: "MRN-2002");
        _clients.Add(a);
        _clients.Add(b);

        var result = await new MergeClientsHandler(_clients, _uow, _clock)
            .HandleAsync(new MergeClientsCommand(a.Id, b.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ClientStatus.Merged, a.Status);
        Assert.Equal(1, _uow.Saves);
    }
}
