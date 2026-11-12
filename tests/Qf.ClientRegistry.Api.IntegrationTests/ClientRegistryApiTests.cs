using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qf.BuildingBlocks.Application;
using Qf.ClientRegistry.Infrastructure.Persistence;

namespace Qf.ClientRegistry.Api.IntegrationTests;

public sealed class ClientRegistryApiTests(ClientRegistryApiFactory factory) : IClassFixture<ClientRegistryApiFactory>
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-0000-4000-8000-000000000002");
    private const string AllRoles = "Clients.Read,Clients.Write,Phi.Read";

    private const string Register = """
        mutation($mrn: String!) {
          registerClient(input: { mrn: $mrn, givenName: "Jane", familyName: "Doe", dateOfBirth: "1985-03-14" }) {
            clientId
            errors { code }
          }
        }
        """;

    private const string ReadClient = """
        query($id: UUID!) { clientById(id: $id) { id status mrn name { given family } dateOfBirth } }
        """;

    [Fact]
    public async Task Anonymous_IsRejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/graphql", new { query = "{ __typename }" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_ThenRead_WithPhiRole_ReturnsDemographics_AndWritesOutbox()
    {
        var id = await RegisterAsync(TenantA, "MRN-INT-1");

        var json = await PostAsync(TenantA, AllRoles, ReadClient, new { id });

        var client = json.GetProperty("data").GetProperty("clientById");
        Assert.Equal("ACTIVE", client.GetProperty("status").GetString());
        Assert.Equal("Jane", client.GetProperty("name").GetProperty("given").GetString());
        Assert.Equal("MRN-INT-1", client.GetProperty("mrn").GetString());

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantScope>().Set(TenantA, "test");
        var db = scope.ServiceProvider.GetRequiredService<ClientRegistryDbContext>();
        Assert.True(await db.OutboxMessages.AnyAsync(m => m.Subject == "ClientRegistered.v1" && m.TenantId == TenantA));
    }

    [Fact]
    public async Task WithoutPhiRole_PhiFieldsAreDenied_ButStatusIsReturned()
    {
        var id = await RegisterAsync(TenantA, "MRN-INT-2");

        var json = await PostAsync(TenantA, "Clients.Read", ReadClient, new { id });

        var errors = json.GetProperty("errors").EnumerateArray().ToList();
        Assert.Contains(errors, e => e.GetProperty("extensions").GetProperty("code").GetString() == "AUTH_NOT_AUTHORIZED");
        Assert.DoesNotContain("Jane", json.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherTenant_CannotSeeClient()
    {
        var id = await RegisterAsync(TenantA, "MRN-INT-3");

        var json = await PostAsync(TenantB, AllRoles, ReadClient, new { id });

        Assert.Equal(JsonValueKind.Null, json.GetProperty("data").GetProperty("clientById").ValueKind);
    }

    [Fact]
    public async Task DuplicateMrn_ReturnsUserError()
    {
        await RegisterAsync(TenantA, "MRN-INT-4");

        var json = await PostAsync(TenantA, AllRoles, Register, new { mrn = "MRN-INT-4" });

        var payload = json.GetProperty("data").GetProperty("registerClient");
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("clientId").ValueKind);
        Assert.Equal("Client.Mrn.Duplicate", payload.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task FilteringOnPhi_IsNotOffered()
    {
        var json = await PostAsync(TenantA, AllRoles, """{ clients(where: { dateOfBirth: { eq: "1985-03-14" } }) { totalCount } }""", null);

        Assert.True(json.TryGetProperty("errors", out _));
    }

    private async Task<Guid> RegisterAsync(Guid tenant, string mrn)
    {
        var json = await PostAsync(tenant, AllRoles, Register, new { mrn });
        return json.GetProperty("data").GetProperty("registerClient").GetProperty("clientId").GetGuid();
    }

    private async Task<JsonElement> PostAsync(Guid tenant, string roles, string query, object? variables)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenant.ToString());
        http.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        var response = await http.PostAsJsonAsync("/graphql", new { query, variables });
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
