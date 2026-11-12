namespace Qf.BuildingBlocks.Hosting.GraphQL;

/// <summary>
/// Authorization policies. Each maps to an Entra ID app role on the API app registration,
/// assigned to security groups per agency (tenant). Least privilege: reading PHI is its own role.
/// </summary>
public static class GraphQLPolicies
{
    public const string ClientsRead = "clients.read";
    public const string ClientsWrite = "clients.write";
    public const string ClientsMerge = "clients.merge";
    public const string PhiRead = "phi.read";
    public const string SchedulingRead = "scheduling.read";
    public const string SchedulingWrite = "scheduling.write";

    internal static readonly IReadOnlyDictionary<string, string> AppRoles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ClientsRead] = "Clients.Read",
        [ClientsWrite] = "Clients.Write",
        [ClientsMerge] = "Clients.Merge",
        [PhiRead] = "Phi.Read",
        [SchedulingRead] = "Scheduling.Read",
        [SchedulingWrite] = "Scheduling.Write",
    };
}
