namespace Qf.Contracts;

/// <summary>Service Bus topology shared by publishers and subscribers (also provisioned by Terraform).</summary>
public static class Topics
{
    public const string ClientRegistryEvents = "client-registry-events";
    public const string SchedulingEvents = "scheduling-events";
}

/// <summary>Message Subject values. The suffix is the contract version.</summary>
public static class Subjects
{
    public const string ClientMerged = "ClientMerged.v1";
    public const string ClientRegistered = "ClientRegistered.v1";
    public const string AppointmentCompleted = "AppointmentCompleted.v1";
}

/// <summary>Every integration event carries its id (= Service Bus MessageId) and tenant.</summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }

    Guid TenantId { get; }
}
