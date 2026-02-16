# Observability with .NET Aspire

## Overview

.NET Aspire provides built-in observability through the Aspire Dashboard - a unified interface for viewing logs, traces, and metrics across all your services. This document covers:

- Aspire Dashboard overview
- Structured logging with OpenTelemetry
- Distributed tracing across services
- Metrics collection
- Health checks configuration
- Correlation IDs across service boundaries
- Troubleshooting with the dashboard

---

## The Aspire Dashboard

The Aspire Dashboard is automatically available when running your Aspire app and provides a single pane of glass for all telemetry data.

```
+-----------------------------------------------------------------------------------+
|  .NET Aspire Dashboard                                        http://localhost:18888 |
+-----------------------------------------------------------------------------------+
|                                                                                   |
|  +-------------+  +-------------+  +-------------+  +-------------+              |
|  |  Resources  |  |    Logs     |  |   Traces    |  |   Metrics   |              |
|  +-------------+  +-------------+  +-------------+  +-------------+              |
|                                                                                   |
|  Resources (4)                                                                   |
|  +-----------------------------------------------------------------------+       |
|  | Name            | Type      | State    | Endpoints                   |       |
|  |-----------------|-----------|----------|------------------------------|       |
|  | webapi          | Project   | Running  | https://localhost:5001       |       |
|  | scheduling-db   | Container | Running  | localhost:1433               |       |
|  | rabbitmq        | Container | Running  | localhost:5672, :15672       |       |
|  | billing-worker  | Project   | Running  | -                            |       |
|  +-----------------------------------------------------------------------+       |
|                                                                                   |
+-----------------------------------------------------------------------------------+
```

### Accessing the Dashboard

When you run your Aspire AppHost:

```bash
dotnet run --project AppHost/AppHost.csproj
```

The console output shows the dashboard URL:

```
info: Aspire.Hosting.DistributedApplication[0]
      Now listening on: https://localhost:18888
      Login to the dashboard at: https://localhost:18888/login?t=abc123...
```

### Dashboard Tabs

| Tab | Purpose |
|-----|---------|
| **Resources** | View all services, containers, and their health status |
| **Console** | Live stdout/stderr from each resource |
| **Structured Logs** | Query and filter structured log entries |
| **Traces** | View distributed traces across service boundaries |
| **Metrics** | Real-time and historical metrics charts |

---

## ServiceDefaults for Telemetry

The `ServiceDefaults` project configures OpenTelemetry for all services in your solution.

### Project Structure

```
ServiceDefaults/
+-- ServiceDefaults.csproj
+-- Extensions.cs
```

### ServiceDefaults Configuration

```csharp
// ServiceDefaults/Extensions.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddRuntimeInstrumentation()
                       .AddMeter("MassTransit");
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddEntityFrameworkCoreInstrumentation()
                       .AddSource("MassTransit");
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
```

### Using ServiceDefaults in Your Projects

```csharp
// WebApi/Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// ... rest of your configuration

var app = builder.Build();

// Map health check endpoints
app.MapDefaultEndpoints();

app.Run();
```

---

## Structured Logging

Aspire Dashboard shows structured logs from all services with full querying capabilities.

### Writing Structured Logs

```csharp
public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    private readonly ILogger<CreatePatientCommandHandler> _logger;

    public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
    {
        // Structured logging with named parameters
        _logger.LogInformation(
            "Creating patient {FirstName} {LastName} with email {Email}",
            cmd.FirstName,
            cmd.LastName,
            cmd.Email);

        var patient = Patient.Create(cmd.FirstName, cmd.LastName, cmd.Email, cmd.DateOfBirth);

        _logger.LogInformation(
            "Patient created successfully with ID {PatientId}",
            patient.Id);

        return patient.Id;
    }
}
```

### Log Levels

| Level | Use For | Example |
|-------|---------|---------|
| `Trace` | Detailed debugging | Method entry/exit |
| `Debug` | Development debugging | Variable values |
| `Information` | Normal operations | "Patient created" |
| `Warning` | Unexpected but handled | "Retry attempt 2/3" |
| `Error` | Failures requiring attention | "Database connection failed" |
| `Critical` | System failures | "Message broker unavailable" |

### Viewing Logs in the Dashboard

```
+-----------------------------------------------------------------------------------+
|  Structured Logs                                                                   |
+-----------------------------------------------------------------------------------+
|  Filter: Level >= Information  |  Resource: All  |  Time: Last 15 min             |
+-----------------------------------------------------------------------------------+
|                                                                                   |
|  Timestamp           | Level | Resource      | Message                            |
|  --------------------|-------|---------------|-----------------------------------|
|  14:32:05.123        | Info  | webapi        | Creating patient John Doe with... |
|  14:32:05.156        | Info  | webapi        | Patient created successfully w... |
|  14:32:05.189        | Info  | webapi        | Publishing integration event Pa...|
|  14:32:05.245        | Info  | billing-worker| Handling PatientCreatedIntegrat...|
|  14:32:05.312        | Info  | billing-worker| Created billing profile for pa... |
|                                                                                   |
|  +-- Expand log entry --------------------------------------------------------+  |
|  |  Timestamp: 2024-02-13T14:32:05.123Z                                        |  |
|  |  Level: Information                                                         |  |
|  |  Category: Scheduling.Application.Patients.Commands.CreatePatientCommand...|  |
|  |  Message: Creating patient John Doe with email john@example.com             |  |
|  |  Properties:                                                                |  |
|  |    FirstName: John                                                          |  |
|  |    LastName: Doe                                                            |  |
|  |    Email: john@example.com                                                  |  |
|  |    TraceId: abc123def456...                                                 |  |
|  |    SpanId: 789xyz...                                                        |  |
|  +----------------------------------------------------------------------------+  |
|                                                                                   |
+-----------------------------------------------------------------------------------+
```

### Log Filtering

The dashboard supports powerful filtering:

```
Level >= Warning                    # Only warnings and errors
Resource = "webapi"                 # From specific service
Message contains "patient"          # Text search
Properties["PatientId"] = "abc-123" # By structured property
```

---

## Distributed Tracing

Traces show the complete request flow across all services, including message broker interactions.

### Trace Flow: Scheduling -> RabbitMQ -> Billing

```
+-----------------------------------------------------------------------------------+
|  Trace: Create Patient (TraceId: abc123...)                      Duration: 245ms  |
+-----------------------------------------------------------------------------------+
|                                                                                   |
|  webapi                                                                          |
|  |                                                                               |
|  +-- POST /api/patients (125ms)                                                  |
|      |                                                                           |
|      +-- CreatePatientCommandHandler (98ms)                                      |
|          |                                                                       |
|          +-- EF Core: INSERT INTO Patients (23ms)                                |
|          |                                                                       |
|          +-- MassTransit: Publish PatientCreatedIntegrationEvent (12ms)          |
|                                                                                   |
|  rabbitmq                                                                        |
|  |                                                                               |
|  +-- PatientCreatedIntegrationEvent (exchange) (2ms)                             |
|                                                                                   |
|  billing-worker                                                                  |
|  |                                                                               |
|  +-- Consume PatientCreatedIntegrationEvent (118ms)                              |
|      |                                                                           |
|      +-- PatientCreatedIntegrationEventHandler (95ms)                            |
|          |                                                                       |
|          +-- EF Core: INSERT INTO BillingProfiles (18ms)                         |
|                                                                                   |
+-----------------------------------------------------------------------------------+
```

### How Tracing Works

OpenTelemetry propagates trace context automatically:

1. **HTTP Request** - TraceId created when request arrives
2. **Database Calls** - EF Core instrumentation adds spans
3. **Message Publishing** - MassTransit adds TraceId to message headers
4. **Message Consuming** - MassTransit extracts TraceId and continues trace

```csharp
// This happens automatically with MassTransit + OpenTelemetry
// No code required - just configure instrumentation

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("MassTransit");  // Enables MassTransit tracing
    });
```

### Viewing Traces in the Dashboard

```
+-----------------------------------------------------------------------------------+
|  Traces                                                                           |
+-----------------------------------------------------------------------------------+
|  Filter: Duration > 100ms  |  Resource: All  |  Time: Last 15 min                |
+-----------------------------------------------------------------------------------+
|                                                                                   |
|  TraceId           | Name                    | Duration | Spans | Resources      |
|  ------------------|-------------------------|----------|-------|----------------|
|  abc123...         | POST /api/patients      | 245ms    | 8     | webapi, billing|
|  def456...         | POST /api/appointments  | 312ms    | 12    | webapi, billing|
|  ghi789...         | GET /api/patients/{id}  | 45ms     | 3     | webapi         |
|                                                                                   |
+-----------------------------------------------------------------------------------+
```

### Adding Custom Spans

```csharp
using System.Diagnostics;

public class ComplexDomainService
{
    private static readonly ActivitySource ActivitySource = new("Healthcare.Scheduling");

    public async Task<Result> ProcessComplexOperation(Guid patientId)
    {
        // Create a custom span
        using var activity = ActivitySource.StartActivity("ProcessComplexOperation");
        activity?.SetTag("patient.id", patientId.ToString());

        // Step 1
        using (var step1 = ActivitySource.StartActivity("ValidatePatient"))
        {
            await ValidatePatient(patientId);
            step1?.SetTag("validation.result", "passed");
        }

        // Step 2
        using (var step2 = ActivitySource.StartActivity("ApplyBusinessRules"))
        {
            await ApplyBusinessRules(patientId);
        }

        activity?.SetTag("operation.result", "success");
        return Result.Success();
    }
}
```

Register the activity source:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("Healthcare.Scheduling");  // Custom source
    });
```

---

## Correlation IDs Across Service Boundaries

Correlation IDs enable tracking a single business operation across all services.

### How Correlation Works

```
+-----------------------------------------------------------------------------+
|                           CORRELATION FLOW                                    |
+-----------------------------------------------------------------------------+

     HTTP Request
     X-Correlation-Id: abc-123
          |
          v
    +-----------+
    |   WebApi  |  Logs: CorrelationId=abc-123
    +-----------+
          |
          | MassTransit adds CorrelationId to message headers
          v
    +-----------+
    | RabbitMQ  |  Message Headers: { "MT-CorrelationId": "abc-123" }
    +-----------+
          |
          v
    +-----------+
    |  Billing  |  Logs: CorrelationId=abc-123
    +-----------+
```

### MassTransit Correlation

MassTransit handles correlation automatically:

```csharp
// When publishing, MassTransit preserves the current CorrelationId
_uow.QueueIntegrationEvent(new PatientCreatedIntegrationEvent
{
    PatientId = patient.Id,
    // CorrelationId is automatically propagated
});

// When consuming, MassTransit restores the CorrelationId
protected override async Task HandleAsync(
    PatientCreatedIntegrationEvent message,
    CancellationToken cancellationToken)
{
    // ConsumeContext has the CorrelationId
    // Logger automatically includes it via OpenTelemetry
    Logger.LogInformation("Processing patient {PatientId}", message.PatientId);
}
```

### Adding Correlation ID Middleware

```csharp
// Middleware to extract/generate CorrelationId from HTTP headers
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = "X-Correlation-Id";

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        // Add to current activity for OpenTelemetry
        Activity.Current?.SetTag("correlation.id", correlationId);

        using (context.RequestServices.GetRequiredService<ILogger<CorrelationIdMiddleware>>()
            .BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}

// Register in Program.cs
app.UseMiddleware<CorrelationIdMiddleware>();
```

### Searching by Correlation ID

In the Aspire Dashboard, search across all services:

```
Properties["CorrelationId"] = "abc-123"
```

This shows all log entries from all services that participated in the operation.

---

## Metrics Collection

Aspire Dashboard displays real-time metrics from all services.

### Built-in Metrics

ServiceDefaults configures these automatically:

| Source | Metrics |
|--------|---------|
| **ASP.NET Core** | Request duration, active requests, errors |
| **HttpClient** | Outbound request duration, connection pool |
| **Runtime** | GC collections, memory, thread pool |
| **MassTransit** | Messages consumed, faults, duration |

### Viewing Metrics in the Dashboard

```
+-----------------------------------------------------------------------------------+
|  Metrics                                                                          |
+-----------------------------------------------------------------------------------+
|  Resource: webapi  |  Time Range: Last 1 hour                                     |
+-----------------------------------------------------------------------------------+
|                                                                                   |
|  HTTP Request Duration (http_server_request_duration)                            |
|  +-----------------------------------------------------------------------+       |
|  |     ^                                                                  |       |
|  | 200 |    *                                                             |       |
|  | 150 |   * *     *                                                      |       |
|  | 100 |  *   *   * *    *  *                                             |       |
|  |  50 | *     * *   *  * ** *  *  *  *  *                                |       |
|  |   0 +-----------------------------------------------------------> t   |       |
|  +-----------------------------------------------------------------------+       |
|  p50: 45ms  |  p95: 125ms  |  p99: 245ms                                         |
|                                                                                   |
|  MassTransit Message Consumption                                                 |
|  +-----------------------------------------------------------------------+       |
|  | Messages/sec: 24.5                                                     |       |
|  | Avg Duration: 85ms                                                     |       |
|  | Error Rate: 0.1%                                                       |       |
|  +-----------------------------------------------------------------------+       |
|                                                                                   |
+-----------------------------------------------------------------------------------+
```

### Custom Metrics

```csharp
using System.Diagnostics.Metrics;

public class PatientMetrics
{
    private readonly Counter<long> _patientsCreated;
    private readonly Histogram<double> _patientCreationDuration;

    public PatientMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Healthcare.Scheduling");

        _patientsCreated = meter.CreateCounter<long>(
            "patients_created_total",
            description: "Total number of patients created");

        _patientCreationDuration = meter.CreateHistogram<double>(
            "patient_creation_duration_ms",
            unit: "ms",
            description: "Time to create a patient");
    }

    public void RecordPatientCreated() => _patientsCreated.Add(1);

    public void RecordCreationDuration(double durationMs) =>
        _patientCreationDuration.Record(durationMs);
}

// Register
builder.Services.AddSingleton<PatientMetrics>();

// Configure OpenTelemetry to collect custom meter
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Healthcare.Scheduling");
    });
```

Using the metrics:

```csharp
public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    private readonly PatientMetrics _metrics;
    private readonly Stopwatch _stopwatch = new();

    public async Task<Guid> Handle(CreatePatientCommand cmd, CancellationToken ct)
    {
        _stopwatch.Restart();

        var patient = Patient.Create(...);
        await _uow.SaveChangesAsync(ct);

        _stopwatch.Stop();
        _metrics.RecordPatientCreated();
        _metrics.RecordCreationDuration(_stopwatch.ElapsedMilliseconds);

        return patient.Id;
    }
}
```

---

## Health Checks

Health checks enable monitoring service health and readiness.

### Configuring Health Checks

```csharp
// ServiceDefaults/Extensions.cs
public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
{
    builder.Services.AddHealthChecks()
        // Basic liveness check
        .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

    return builder;
}
```

### Adding Custom Health Checks

```csharp
// Check database connectivity
// NOTE: In this project, SQL Server health checks use the connection string from user secrets
// RabbitMQ health checks are provided automatically by MassTransit
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
    .AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "sqlserver",
        tags: ["ready", "db"]);
    // MassTransit automatically adds RabbitMQ health checks (no manual AddRabbitMQ needed)
```

### Health Check Endpoints

```csharp
app.MapHealthChecks("/health");  // All checks

app.MapHealthChecks("/alive", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live")  // Liveness only
});

app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")  // Readiness checks
});
```

### Health in the Dashboard

```
+-----------------------------------------------------------------------------------+
|  Resources                                                                        |
+-----------------------------------------------------------------------------------+
|                                                                                   |
|  Name            | Type      | State    | Health  | Endpoints                    |
|  ----------------|-----------|----------|---------|------------------------------|
|  webapi          | Project   | Running  | Healthy | https://localhost:5001       |
|  scheduling-db   | Container | Running  | Healthy | localhost:1433               |
|  rabbitmq        | Container | Running  | Healthy | localhost:5672               |
|  billing-worker  | Project   | Running  | Healthy | -                            |
|                                                                                   |
|  +-- Expand webapi health details ----------------------------------------+      |
|  |  /health: Healthy                                                       |      |
|  |    - self: Healthy                                                      |      |
|  |    - sqlserver: Healthy (response time: 12ms)                           |      |
|  |    - rabbitmq: Healthy (response time: 5ms)                             |      |
|  +-------------------------------------------------------------------------+      |
|                                                                                   |
+-----------------------------------------------------------------------------------+
```

---

## Troubleshooting with the Dashboard

### Scenario 1: Slow Requests

**Symptom:** Users report slow API responses.

**Investigation Steps:**

1. Go to **Traces** tab
2. Filter: `Duration > 500ms`
3. Click on a slow trace
4. Identify the slowest span (bottleneck)

```
Trace: POST /api/appointments (1.2s)
|
+-- CreateAppointmentCommandHandler (1.15s)    <-- Bottleneck!
    |
    +-- EF Core: SELECT (checking conflicts) (1.1s)  <-- Root cause!
```

**Solution:** Add an index for the conflict check query.

### Scenario 2: Message Processing Failures

**Symptom:** Billing profiles not being created.

**Investigation Steps:**

1. Go to **Structured Logs** tab
2. Filter: `Level >= Error AND Resource = "billing-worker"`
3. Find the error and note the TraceId
4. Go to **Traces** and search by TraceId
5. See the full flow and where it failed

```
Log Entry:
  Level: Error
  Message: Error handling PatientCreatedIntegrationEvent with EventId abc-123
  Exception: SqlException: Cannot insert duplicate key...
  TraceId: xyz-789
```

### Scenario 3: Service Not Receiving Messages

**Symptom:** Billing worker shows no activity.

**Investigation Steps:**

1. Check **Resources** - Is billing-worker running?
2. Check **Structured Logs** for webapi - Are events being published?
3. Check RabbitMQ Management UI - Are messages in queues?
4. Check **Traces** - Is the publish span succeeding?

```
+-- MassTransit: Publish PatientCreatedIntegrationEvent (12ms) [SUCCESS]

But no corresponding consume span in billing-worker...

Check: Is the consumer registered?
Check: Is the queue bound to the exchange?
```

### Scenario 4: Memory Issues

**Symptom:** Service becoming unresponsive.

**Investigation Steps:**

1. Go to **Metrics** tab
2. Select the affected resource
3. View memory and GC metrics

```
process_runtime_dotnet_gc_heap_size
|
| ^
| |       * * * * * * * * <-- Continuous growth = memory leak
| |     *
| |   *
| | *
+--------------------------------> time
```

**Look for:**
- Continuously growing heap size
- Frequent Gen2 GC collections
- High memory pressure

### Common Dashboard Queries

| Problem | Where to Look | Filter/Search |
|---------|---------------|---------------|
| Errors | Logs | `Level >= Error` |
| Slow requests | Traces | `Duration > 500ms` |
| Specific operation | Traces | TraceId or CorrelationId |
| Service health | Resources | Check health column |
| Message failures | Logs | `Message contains "Error handling"` |
| Database issues | Traces | Look for long EF Core spans |

---

## Best Practices

### Logging Best Practices

```csharp
// DO: Use structured logging with named parameters
_logger.LogInformation("Patient {PatientId} created by {UserId}", patientId, userId);

// DON'T: String concatenation
_logger.LogInformation($"Patient {patientId} created by {userId}");

// DO: Include relevant context
_logger.LogError(ex, "Failed to create patient {PatientId} with email {Email}", id, email);

// DON'T: Swallow exceptions silently
catch (Exception) { } // Never do this!

// DO: Use appropriate log levels
_logger.LogDebug("Entering method with parameters: {@Parameters}", parameters);
_logger.LogInformation("Patient created successfully");
_logger.LogWarning("Retry attempt {Attempt}/{MaxAttempts}", attempt, maxAttempts);
_logger.LogError(ex, "Failed to process message");
```

### Tracing Best Practices

```csharp
// DO: Add meaningful tags to activities
activity?.SetTag("patient.id", patientId.ToString());
activity?.SetTag("operation.type", "create");

// DO: Use consistent naming
using var activity = ActivitySource.StartActivity("CreatePatient");
using var activity = ActivitySource.StartActivity("ValidatePatient");

// DON'T: Create activities for trivial operations
using var activity = ActivitySource.StartActivity("AddToList"); // Too granular
```

### Metrics Best Practices

```csharp
// DO: Use meaningful metric names
meter.CreateCounter<long>("patients_created_total");
meter.CreateHistogram<double>("appointment_scheduling_duration_ms");

// DON'T: Generic names
meter.CreateCounter<long>("counter1");

// DO: Add relevant tags for filtering
_patientsCreated.Add(1, new KeyValuePair<string, object?>("status", "active"));
```

---

## Verification Checklist

- [ ] ServiceDefaults project created and configured
- [ ] All projects reference ServiceDefaults
- [ ] `builder.AddServiceDefaults()` called in each project
- [ ] `app.MapDefaultEndpoints()` called for health checks
- [ ] OpenTelemetry exporters configured for OTLP
- [ ] MassTransit instrumentation enabled (`AddSource("MassTransit")`)
- [ ] EF Core instrumentation enabled
- [ ] Custom activity sources registered (if using)
- [ ] Custom meters registered (if using)
- [ ] Health checks added for dependencies (SQL Server, RabbitMQ)
- [ ] Dashboard accessible and showing all resources
- [ ] Logs visible in Structured Logs tab
- [ ] Traces showing cross-service correlation
- [ ] Metrics charts displaying data

---

## Quick Reference

### Dashboard URL

```
https://localhost:18888
```

### Common Log Filters

```
Level >= Warning                           # Warnings and errors
Resource = "webapi"                        # Specific service
Properties["PatientId"] = "abc-123"       # By property
Message contains "failed"                  # Text search
```

### Common Trace Filters

```
Duration > 500ms                           # Slow operations
Resource = "billing-worker"                # Specific service
Name = "POST /api/patients"                # Specific endpoint
```

### Health Check Endpoints

```
/health   - All health checks
/alive    - Liveness check (is the process running?)
/ready    - Readiness checks (are dependencies available?)
```

---

This concludes Phase 6 documentation. Return to [01-aspire-introduction.md](./01-aspire-introduction.md) for an overview of .NET Aspire integration.
