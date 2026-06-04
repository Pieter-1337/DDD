using BuildingBlocks.Application;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Infrastructure.Auth;
using BuildingBlocks.Infrastructure.MassTransit.Configuration;
using BuildingBlocks.Infrastructure.Wolverine;
using BuildingBlocks.WebApplications.Filters;
using BuildingBlocks.WebApplications.Json;
using BuildingBlocks.WebApplications.OpenApi;
using MassTransit;
using Scheduling.Application;
using Scheduling.Infrastructure;
using Scheduling.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire ServiceDefaults (OpenTelemetry, health checks, resilience)
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
    options.Filters.Add<ExceptionToJsonFilter>();
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new SmartEnumJsonConverterFactory());
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Add SQL Server health check
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sqlserver", tags: ["ready"]);

// Add infrastructure
builder.Services.AddSchedulingInfrastructure(connectionString);
builder.Services.AddSchedulingApplication();
builder.Services.AddDefaultPipelineBehaviors();

// Dev-only: create + migrate the Scheduling database on startup so booting a new
// worktree slot requires no manual 'dotnet ef database update' step.
builder.Services.AddHostedService<DatabaseMigrator>();

// Add event-driven messaging (configurable: Wolverine or MassTransit)
var messagingFramework = MessagingFrameworkSelector.Resolve(builder.Configuration);

if (messagingFramework == MessagingFrameworkNames.Wolverine)
{
    builder.AddWolverineEventBus<SchedulingDbContext>(connectionString, "wolverine_scheduling", opts =>
    {
        opts.Discovery.IncludeAssembly(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
    });
}
else
{
    builder.Services.AddMassTransitEventBus<SchedulingDbContext>(builder.Configuration, configure =>
    {
        // Register consumers from bounded context assemblies
        configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
    });
}

// Add cookie auth
builder.Services.AddOidcCookieAuth(builder.Configuration);

// Add cors — origins read from config so AppHost can inject slot-derived values via
// Cors__AllowedOrigins__0/1. Slot-1 defaults are in appsettings.json.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["https://localhost:7003", "https://localhost:7004", "https://localhost:7010"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Spa", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});


//Mind the order here!
var app = builder.Build();

// Map Aspire default endpoints (health checks)
app.MapDefaultEndpoints();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseOpenApiWithScalar("Scheduling API");
}
app.UseHttpsRedirection();
app.UseCors("Spa");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
