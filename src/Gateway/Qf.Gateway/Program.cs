using Qf.BuildingBlocks.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddQfObservability();
builder.AddQfSecurity();
builder.Services.AddQfHealthChecks();

// Forward the caller's Entra ID token to subgraphs: each subgraph re-validates it and applies
// its own field-level policies. The gateway never holds broader privileges than the caller.
builder.Services.AddHeaderPropagation(o => o.Headers.Add("Authorization"));
builder.Services.AddHttpClient("Fusion").AddHeaderPropagation();

builder.Services
    .AddFusionGatewayServer()
    .ConfigureFromFile("./gateway.fgp");

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .WithMethods("POST", "GET")
    .WithHeaders("Authorization", "Content-Type", "GraphQL-Preflight")));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseHeaderPropagation();
app.MapQfHealth();
app.MapGraphQL().RequireAuthorization();

await app.RunAsync().ConfigureAwait(false);
