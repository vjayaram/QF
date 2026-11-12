using Qf.BuildingBlocks.Hosting;
using Qf.Scheduling.Api.GraphQL;
using Qf.Scheduling.Infrastructure;
using Qf.Scheduling.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddQfObservability();
builder.AddQfSecurity();
builder.Services.AddScheduling(builder.Configuration);
builder.Services.AddQfHealthChecks().AddDbContextCheck<SchedulingDbContext>(tags: ["ready"]);

builder.Services
    .AddGraphQLServer()
    .AddAuthorization()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddGeneratedTypes()
    .AddType<ClientRefType>()
    .AddTypeExtension<AppointmentExtensions>()
    .AddMaxExecutionDepthRule(8)
    .DisableIntrospection(disable: !builder.Environment.IsDevelopment())
    .ModifyRequestOptions(o => o.IncludeExceptionDetails = builder.Environment.IsDevelopment());

var app = builder.Build();

app.UseAuthentication();
app.UseQfTenancy();
app.UseAuthorization();
app.MapQfHealth();
app.MapGraphQL().RequireAuthorization();

await app.RunWithGraphQLCommandsAsync(args).ConfigureAwait(false);

/// <summary>Entry point marker for WebApplicationFactory in integration tests.</summary>
public partial class Program;
