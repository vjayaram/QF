using Qf.BuildingBlocks.Hosting;
using Qf.ClientRegistry.Api.GraphQL;
using Qf.ClientRegistry.Infrastructure;
using Qf.ClientRegistry.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddQfObservability();
builder.AddQfSecurity();
builder.Services.AddClientRegistry(builder.Configuration);
builder.Services.AddQfHealthChecks().AddDbContextCheck<ClientRegistryDbContext>(tags: ["ready"]);

builder.Services
    .AddGraphQLServer()
    .AddAuthorization()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddGeneratedTypes()
    .AddFiltering()
    .AddSorting()
    .AddMaxExecutionDepthRule(8)
    .DisableIntrospection(disable: !builder.Environment.IsDevelopment())
    .ModifyRequestOptions(o => o.IncludeExceptionDetails = builder.Environment.IsDevelopment());

var app = builder.Build();

app.UseAuthentication();
app.UseQfTenancy();
app.UseAuthorization();
app.MapQfHealth();
app.MapGraphQL().RequireAuthorization();

// `dotnet run -- schema export --output schema.graphql` is used by CI to compose the Fusion gateway.
await app.RunWithGraphQLCommandsAsync(args).ConfigureAwait(false);

/// <summary>Entry point marker for WebApplicationFactory in integration tests.</summary>
public partial class Program;
