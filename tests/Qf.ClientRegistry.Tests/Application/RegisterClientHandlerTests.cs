using Qf.BuildingBlocks.Domain;
using Qf.ClientRegistry.Application.Commands;
using Qf.ClientRegistry.Domain;
using Qf.TestKit;

namespace Qf.ClientRegistry.Tests.Application;

public class RegisterClientHandlerTests
{
    private readonly FakeClock _clock = new();
    private readonly InMemoryClientRepository _clients = new(FakeTenant.A);
    private readonly FakeUnitOfWork _uow = new();

    private RegisterClientHandler Handler() => new(_clients, _uow, new FakeTenant(FakeTenant.A), _clock);

    [Fact]
    public async Task Registers_AndSavesOnce()
    {
        var result = await Handler().HandleAsync(new RegisterClientCommand("MRN-1001", "Jane", "Doe", null, new DateOnly(1985, 3, 14)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(_clients.Store);
        Assert.Equal(result.Value, stored.Id);
        Assert.Equal(FakeTenant.A, stored.TenantId);
        Assert.Equal(1, _uow.Saves);
    }

    [Fact]
    public async Task ReturnsAllInputErrors_WithoutSaving()
    {
        var result = await Handler().HandleAsync(new RegisterClientCommand("bad mrn", "", "Doe", null, new DateOnly(1985, 3, 14)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == "MedicalRecordNumber.Value.InvalidFormat");
        Assert.Contains(result.Errors, e => e.Code == "PersonName.Given.Required");
        Assert.Equal(0, _uow.Saves);
    }

    [Fact]
    public async Task RejectsDuplicateMrnInSameTenant()
    {
        _clients.Add(TestData.NewClient(_clock, mrn: "MRN-1001"));

        var result = await Handler().HandleAsync(new RegisterClientCommand("MRN-1001", "John", "Roe", null, new DateOnly(1990, 1, 1)), CancellationToken.None);

        var error = Assert.Single(result.Errors);
        Assert.Equal("Client.Mrn.Duplicate", error.Code);
        Assert.Equal(ErrorKind.Conflict, error.Kind);
    }

    [Fact]
    public async Task AllowsSameMrnInAnotherTenant()
    {
        _clients.Add(TestData.NewClient(_clock, tenant: FakeTenant.B, mrn: "MRN-1001"));

        var result = await Handler().HandleAsync(new RegisterClientCommand("MRN-1001", "John", "Roe", null, new DateOnly(1990, 1, 1)), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
