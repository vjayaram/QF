using Qf.ClientRegistry.Application.Commands;
using Qf.ClientRegistry.Domain;
using Qf.TestKit;

namespace Qf.ClientRegistry.Tests.Application;

public class ImportLegacyClientHandlerTests
{
    private static readonly DateOnly Dob = new(1985, 3, 14);
    private readonly FakeClock _clock = new();
    private readonly InMemoryClientRepository _clients = new(FakeTenant.A);
    private readonly RecordingReviewInbox _reviews = new();
    private readonly FakeUnitOfWork _uow = new();

    private ImportLegacyClientHandler Handler() => new(_clients, _reviews, _uow, new FakeTenant(FakeTenant.A), _clock);

    private static LegacyClientSnapshot Snapshot(
        string externalId = "CL01:1001",
        string? mrn = "MRN-1001",
        string given = "Jane",
        string family = "Doe",
        bool active = true,
        LegacySystem system = LegacySystem.CareLogic) =>
        new(system, externalId, mrn, given, family, null, Dob, active);

    [Fact]
    public async Task NewPerson_IsRegisteredAndLinked()
    {
        var result = await Handler().HandleAsync(new(Snapshot()), CancellationToken.None);

        Assert.Equal(ImportDecision.Registered, result.Value.Decision);
        var client = Assert.Single(_clients.Store);
        Assert.Equal(client.Id, result.Value.ClientId);
        Assert.Single(client.LegacyIdentities);
        Assert.Equal(1, _uow.Saves);
    }

    [Fact]
    public async Task Replay_IsIdempotent()
    {
        await Handler().HandleAsync(new(Snapshot()), CancellationToken.None);

        var replay = await Handler().HandleAsync(new(Snapshot()), CancellationToken.None);

        Assert.Equal(ImportDecision.AlreadyLinked, replay.Value.Decision);
        Assert.Single(_clients.Store);
        Assert.Equal(1, _uow.Saves);
    }

    [Fact]
    public async Task CertainMatchFromAnotherEhr_LinksToExistingClient()
    {
        await Handler().HandleAsync(new(Snapshot()), CancellationToken.None);

        var fromCredible = await Handler().HandleAsync(
            new(Snapshot(externalId: "CR01:55", system: LegacySystem.Credible, given: "JANE", family: "DOE")),
            CancellationToken.None);

        Assert.Equal(ImportDecision.LinkedToExisting, fromCredible.Value.Decision);
        var client = Assert.Single(_clients.Store);
        Assert.Equal(2, client.LegacyIdentities.Count);
    }

    [Fact]
    public async Task ProbableMatch_IsQueuedForHumanReview_NotMerged()
    {
        await Handler().HandleAsync(new(Snapshot()), CancellationToken.None);

        var probable = await Handler().HandleAsync(
            new(Snapshot(externalId: "IS01:9", system: LegacySystem.InSync, mrn: null, given: "J")),
            CancellationToken.None);

        Assert.Equal(ImportDecision.QueuedForReview, probable.Value.Decision);
        Assert.Null(probable.Value.ClientId);
        var item = Assert.Single(_reviews.Items);
        Assert.Equal(_clients.Store[0].Id, Assert.Single(item.CandidateClientIds));
        Assert.Single(_clients.Store);
    }

    [Fact]
    public async Task MissingMrn_IsDerivedFromSourceKey_AndInactiveIsPreserved()
    {
        var result = await Handler().HandleAsync(
            new(Snapshot(externalId: "IS01:00042", system: LegacySystem.InSync, mrn: null, active: false)),
            CancellationToken.None);

        Assert.Equal(ImportDecision.Registered, result.Value.Decision);
        var client = Assert.Single(_clients.Store);
        Assert.Equal("IS-IS0100042", client.Mrn.Value);
        Assert.Equal(ClientStatus.Inactive, client.Status);
    }

    [Fact]
    public async Task InvalidSnapshot_ReturnsErrors()
    {
        var result = await Handler().HandleAsync(new(Snapshot(given: "")), CancellationToken.None);

        Assert.Contains(result.Errors, e => e.Code == "PersonName.Given.Required");
        Assert.Empty(_clients.Store);
    }

    [Fact]
    public void DerivedMrn_IsTruncatedToColumnLength()
    {
        var mrn = ImportLegacyClientHandler.DeriveMrn(Snapshot(externalId: new string('9', 60), system: LegacySystem.Credible));

        Assert.Equal(32, mrn.Length);
        Assert.StartsWith("CR-", mrn, StringComparison.Ordinal);
    }
}
