using BuildingBlocks.Application;
using BuildingBlocks.Infrastructure.Wolverine;
using BuildingBlocks.WebApplications.Filters;
using BuildingBlocks.WebApplications.Json;
using BuildingBlocks.WebApplications.OpenApi;
using Billing.Application;
using Billing.Infrastructure;
using IntegrationEvents.Scheduling;

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

//Add infrastructure
builder.Services.AddBillingInfrastructure(connectionString);
builder.Services.AddBillingApplication();
builder.Services.AddDefaultPipelineBehaviors();

// Add Wolverine for event-driven messaging (consuming from MassTransit publisher)
builder.AddWolverineEventBus(connectionString, opts =>
{
    opts.Discovery.IncludeAssembly(typeof(Billing.Infrastructure.ServiceCollectionExtensions).Assembly);

    opts.ListenToMassTransitQueue<PatientCreatedIntegrationEvent>("billing-patient-created");
});

// Add cors
builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy => policy
        .WithOrigins("https://localhost:7003")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// Map Aspire default endpoints (health checks)
app.MapDefaultEndpoints();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseOpenApiWithScalar("Billing API");
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.UseCors("Angular");
app.MapControllers();
app.Run();
