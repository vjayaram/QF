using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qf.BuildingBlocks.Hosting;
using Testcontainers.MsSql;

namespace Qf.ClientRegistry.Api.IntegrationTests;

public sealed partial class ClientRegistryApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        var master = new SqlConnectionStringBuilder(_sql.GetConnectionString());
        await using (var conn = new SqlConnection(master.ConnectionString))
        {
            await conn.OpenAsync();
            await using var create = new SqlCommand("CREATE DATABASE ClientRegistry;", conn);
            await create.ExecuteNonQueryAsync();
        }

        master.InitialCatalog = "ClientRegistry";
        _connectionString = master.ConnectionString;
        await ApplySchemaAsync(_connectionString);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTest");
        builder.UseSetting("ConnectionStrings:ClientRegistry", _connectionString);
        builder.UseSetting("AzureAd:TenantId", "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId", "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("ServiceBus:ConnectionString", "Endpoint=sb://localhost;SharedAccessKeyName=k;SharedAccessKey=dGVzdA==;UseDevelopmentEmulator=true;");
        builder.ConfigureTestServices(services =>
        {
            // No broker in this test: the outbox rows are asserted directly instead.
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                services.Remove(hosted);
            }

            services.AddAuthentication(TestAuthHandler.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                o.DefaultChallengeScheme = TestAuthHandler.Scheme;
                o.DefaultScheme = TestAuthHandler.Scheme;
            });
        });
    }

    /// <summary>Applies the SQL Database Project scripts (tables, then security), splitting on GO.</summary>
    private static async Task ApplySchemaAsync(string connectionString)
    {
        var root = FindRepoRoot();
        var dir = Path.Combine(root, "database", "ClientRegistry");
        var scripts = Directory.GetFiles(Path.Combine(dir, "Tables"), "*.sql")
            .OrderBy(f => f.Contains("Legacy", StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(f => f, StringComparer.Ordinal)
            .Concat(Directory.GetFiles(Path.Combine(dir, "Security"), "*.sql"));

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        foreach (var script in scripts)
        {
            foreach (var batch in GoSeparator().Split(await File.ReadAllTextAsync(script)).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await using var cmd = new SqlCommand(batch, conn);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "models")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}

/// <summary>Authenticates from test headers so tests can vary tenant and roles per request.</summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";
    public const string TenantHeader = "X-Test-Tenant";
    public const string RolesHeader = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TenantHeader, out var tenant))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("oid", "test-user"), new(TenantClaims.TenantId, tenant.ToString()) };
        claims.AddRange(Request.Headers[RolesHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => new Claim(ClaimTypes.Role, r)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
    }
}
