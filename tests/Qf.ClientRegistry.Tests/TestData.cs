using Qf.ClientRegistry.Domain;
using Qf.TestKit;

namespace Qf.ClientRegistry.Tests;

internal static class TestData
{
    public static MedicalRecordNumber Mrn(string value = "MRN-1001") => MedicalRecordNumber.Create(value).Value;

    public static PersonName Name(string given = "Jane", string family = "Doe", string? middle = null) =>
        PersonName.Create(given, family, middle).Value;

    public static LegacyIdentity Legacy(LegacySystem system = LegacySystem.CareLogic, string externalId = "CL01:1001") =>
        LegacyIdentity.Create(system, externalId).Value;

    public static Client NewClient(
        TimeProvider clock,
        Guid? tenant = null,
        string mrn = "MRN-1001",
        string given = "Jane",
        string family = "Doe",
        DateOnly? dob = null)
    {
        var client = Client.Register(tenant ?? FakeTenant.A, Mrn(mrn), Name(given, family), dob ?? new DateOnly(1985, 3, 14), clock).Value;
        client.ClearDomainEvents();
        return client;
    }
}
