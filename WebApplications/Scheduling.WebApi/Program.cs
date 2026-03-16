using BuildingBlocks.Application;
using BuildingBlocks.Infrastructure.MassTransit.Configuration;
using BuildingBlocks.WebApplications.Filters;
using BuildingBlocks.WebApplications.Json;
using BuildingBlocks.WebApplications.OpenApi;
using MassTransit;
using Scheduling.Application;
using Scheduling.Infrastructure;

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

// Add MassTransit for event-driven messaging
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    // Register consumers from bounded context assemblies
    configure.AddConsumers(typeof(Scheduling.Infrastructure.ServiceCollectionExtensions).Assembly);
});

// Add cors
builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy => policy
        .WithOrigins("https//localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// Map Aspire default endpoints (health checks)
app.MapDefaultEndpoints();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseOpenApiWithScalar("Scheduling API");
}
app.UseHttpsRedirection();
app.UseAuthorization();
app.UseCors("Angular");
app.MapControllers();
app.Run();
