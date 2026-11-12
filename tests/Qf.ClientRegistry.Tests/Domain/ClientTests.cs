using Qf.BuildingBlocks.Domain;
using Qf.ClientRegistry.Domain;
using Qf.TestKit;

namespace Qf.ClientRegistry.Tests.Domain;

public class ClientTests
{
    private readonly FakeClock _clock = new();

    [Fact]
    public void Register_CreatesActiveClient_AndRaisesEvent()
    {
        var result = Client.Register(FakeTenant.A, TestData.Mrn(), TestData.Name(), new DateOnly(1985, 3, 14), _clock);

        Assert.True(result.IsSuccess);
        var client = result.Value;
        Assert.Equal(ClientStatus.Active, client.Status);
        Assert.Equal(FakeTenant.A, client.TenantId);
        Assert.NotEqual(Guid.Empty, client.Id);
        var evt = Assert.IsType<ClientRegistered>(Assert.Single(client.DomainEvents));
        Assert.Equal(client.Id, evt.ClientId);
        Assert.Equal(_clock.Now, evt.OccurredAt);
    }

    [Fact]
    public void Register_RejectsFutureAndImplausibleBirthDates_AndEmptyTenant()
    {
        var future = Client.Register(FakeTenant.A, TestData.Mrn(), TestData.Name(), new DateOnly(2030, 1, 1), _clock);
        var ancient = Client.Register(FakeTenant.A, TestData.Mrn(), TestData.Name(), new DateOnly(1850, 1, 1), _clock);
        var noTenant = Client.Register(Guid.Empty, TestData.Mrn(), TestData.Name(), new DateOnly(1985, 1, 1), _clock);

        Assert.Contains(future.Errors, e => e.Code == "Client.DateOfBirth.InFuture");
        Assert.Contains(ancient.Errors, e => e.Code == "Client.DateOfBirth.Implausible");
        Assert.Contains(noTenant.Errors, e => e.Code == "Client.TenantId.Required");
    }

    [Fact]
    public void LinkLegacyIdentity_AddsIdentity_AndIsIdempotent()
    {
        var client = TestData.NewClient(_clock);
        var identity = TestData.Legacy();

        Assert.True(client.LinkLegacyIdentity(identity, _clock).IsSuccess);
        Assert.True(client.LinkLegacyIdentity(identity, _clock).IsSuccess);

        Assert.Single(client.LegacyIdentities);
        Assert.IsType<LegacyIdentityLinked>(Assert.Single(client.DomainEvents));
    }

    [Fact]
    public void LinkLegacyIdentity_AllowsOneRecordPerSourceSystem()
    {
        var client = TestData.NewClient(_clock);
        client.LinkLegacyIdentity(TestData.Legacy(LegacySystem.CareLogic, "CL01:1"), _clock);

        var second = client.LinkLegacyIdentity(TestData.Legacy(LegacySystem.CareLogic, "CL01:2"), _clock);
        var otherSystem = client.LinkLegacyIdentity(TestData.Legacy(LegacySystem.Credible, "CR01:9"), _clock);

        Assert.Equal("Client.LegacyIdentity.SourceAlreadyLinked", Assert.Single(second.Errors).Code);
        Assert.Equal(ErrorKind.Conflict, second.Errors[0].Kind);
        Assert.True(otherSystem.IsSuccess);
        Assert.Equal(2, client.LegacyIdentities.Count);
    }

    [Fact]
    public void Deactivate_ThenReactivate_FollowsModelledLifecycle()
    {
        var client = TestData.NewClient(_clock);

        Assert.True(client.Deactivate(_clock).IsSuccess);
        Assert.Equal(ClientStatus.Inactive, client.Status);
        Assert.Equal("Client.Status.InvalidTransition", Assert.Single(client.Deactivate(_clock).Errors).Code);
        Assert.True(client.Reactivate(_clock).IsSuccess);
        Assert.Equal(ClientStatus.Active, client.Status);
        Assert.Collection(
            client.DomainEvents,
            e => Assert.IsType<ClientDeactivated>(e),
            e => Assert.IsType<ClientReactivated>(e));
    }

    [Fact]
    public void Rename_ReplacesName_UnlessMerged()
    {
        var client = TestData.NewClient(_clock);
        var survivor = TestData.NewClient(_clock, mrn: "MRN-2002");

        Assert.True(client.Rename(TestData.Name("Janet", "Doe")).IsSuccess);
        Assert.Equal("Janet", client.Name.Given);

        client.MergeInto(survivor, _clock);
        Assert.Equal("Client.Merged", Assert.Single(client.Rename(TestData.Name("X", "Y")).Errors).Code);
    }

    [Fact]
    public void MergeInto_MovesLegacyIdentities_AndRaisesClientMerged()
    {
        var duplicate = TestData.NewClient(_clock, mrn: "MRN-1001");
        var survivor = TestData.NewClient(_clock, mrn: "MRN-2002");
        duplicate.LinkLegacyIdentity(TestData.Legacy(LegacySystem.Credible, "CR01:77"), _clock);
        duplicate.ClearDomainEvents();

        var result = duplicate.MergeInto(survivor, _clock);

        Assert.True(result.IsSuccess);
        Assert.Equal(ClientStatus.Merged, duplicate.Status);
        Assert.Equal(survivor.Id, duplicate.MergedIntoClientId);
        Assert.Empty(duplicate.LegacyIdentities);
        Assert.Equal(LegacySystem.Credible, Assert.Single(survivor.LegacyIdentities).SourceSystem);
        var merged = Assert.IsType<ClientMerged>(Assert.Single(duplicate.DomainEvents));
        Assert.Equal(survivor.Id, merged.SurvivorClientId);
        Assert.IsType<LegacyIdentityLinked>(Assert.Single(survivor.DomainEvents));
    }

    [Fact]
    public void MergeInto_EnforcesSafetyRules()
    {
        var a = TestData.NewClient(_clock, mrn: "MRN-1001");
        var otherTenant = TestData.NewClient(_clock, tenant: FakeTenant.B, mrn: "MRN-3003");

        Assert.Equal("Client.Merge.Self", Assert.Single(a.MergeInto(a, _clock).Errors).Code);
        var cross = Assert.Single(a.MergeInto(otherTenant, _clock).Errors);
        Assert.Equal("Client.Merge.CrossTenant", cross.Code);
        Assert.Equal(ErrorKind.Forbidden, cross.Kind);
    }

    [Fact]
    public void MergeInto_RefusesWhenBothHaveDifferentRecordsInSameSystem()
    {
        var a = TestData.NewClient(_clock, mrn: "MRN-1001");
        var b = TestData.NewClient(_clock, mrn: "MRN-2002");
        a.LinkLegacyIdentity(TestData.Legacy(LegacySystem.InSync, "IS01:1"), _clock);
        b.LinkLegacyIdentity(TestData.Legacy(LegacySystem.InSync, "IS01:2"), _clock);

        var result = a.MergeInto(b, _clock);

        Assert.Equal("Client.Merge.LegacyConflict", Assert.Single(result.Errors).Code);
        Assert.Equal(ClientStatus.Active, a.Status);
    }

    [Fact]
    public void MergedClient_IsFrozen()
    {
        var a = TestData.NewClient(_clock, mrn: "MRN-1001");
        var b = TestData.NewClient(_clock, mrn: "MRN-2002");
        var c = TestData.NewClient(_clock, mrn: "MRN-3003");
        a.MergeInto(b, _clock);

        Assert.Equal("Client.Merged", Assert.Single(a.LinkLegacyIdentity(TestData.Legacy(), _clock).Errors).Code);
        Assert.Equal("Client.Status.InvalidTransition", Assert.Single(a.Reactivate(_clock).Errors).Code);
        Assert.Equal("Client.Merge.SurvivorMerged", Assert.Single(c.MergeInto(a, _clock).Errors).Code);
    }

    [Fact]
    public void StatusTransitions_MatchTheModel()
    {
        Assert.True(ClientStatusTransitions.CanTransition(ClientStatus.Active, ClientStatus.Merged));
        Assert.True(ClientStatusTransitions.CanTransition(ClientStatus.Inactive, ClientStatus.Active));
        Assert.False(ClientStatusTransitions.CanTransition(ClientStatus.Merged, ClientStatus.Active));
        Assert.Empty(ClientStatusTransitions.AllowedFrom(ClientStatus.Merged));
        Assert.Equal(new[] { ClientStatus.Inactive, ClientStatus.Merged }, ClientStatusTransitions.AllowedFrom(ClientStatus.Active));
    }
}
