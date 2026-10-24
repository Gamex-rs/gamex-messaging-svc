using Gamex.Messaging.Api.Auth;
using Gamex.Messaging.Api.Endpoints;
using Gamex.Messaging.Api.Hubs;
using Gamex.Messaging.Application;
using Gamex.Messaging.Application.Abstractions;
using Gamex.Messaging.Infrastructure;
using Gamex.Messaging.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddGamexAuth(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapConversationEndpoints();
app.MapHub<MessagingHub>(AuthSetup.HubPath);

if (app.Configuration.GetValue("DB_MIGRATE_ON_STARTUP", app.Environment.IsDevelopment()))
{
    await app.Services.MigrateDatabaseAsync();
}

await app.RunAsync();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
