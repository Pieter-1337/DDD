# Billing Bounded Context - Cross-BC Communication

## Overview

This document covers adding a second bounded context (Billing) to demonstrate cross-bounded-context communication via integration events. The Billing context reacts to events from the Scheduling context, creating a `BillingProfile` when a new patient is created.

**Why a second bounded context?**
- Demonstrates real-world event-driven architecture
- Shows how bounded contexts remain decoupled yet coordinated
- Illustrates the Shared IntegrationEvents project pattern
- Provides a template for adding additional bounded contexts

---

## Architecture: Scheduling to Billing Communication

```
+-----------------------------------------------------------------------------+
|                              SCHEDULING CONTEXT                              |
+-----------------------------------------------------------------------------+
|                                                                              |
|  +------------------+     +------------------------+     +----------------+  |
|  |  PatientController|---->|CreatePatientCommandHandler|---->| Patient.Create |  |
|  +------------------+     +------------------------+     +--------+-------+  |
|                                                                  |           |
|                                                    AddDomainEvent(PatientCreatedEvent)
|                                                                  |           |
|                           +------------------------+             v           |
|                           |PatientCreatedEventHandler|<---- MediatR dispatch |
|                           +------------------------+                         |
|                                      |                                       |
|                     _uow.QueueIntegrationEvent(PatientCreatedIntegrationEvent)
|                                      |                                       |
+--------------------------------------+---------------------------------------+
                                       |
                                       v
                           +------------------------+
                           |       RabbitMQ         |
                           | PatientCreatedIntegration|
                           |         Event          |
                           +------------------------+
                                       |
                                       v
+-----------------------------------------------------------------------------+
|                               BILLING CONTEXT                                |
+-----------------------------------------------------------------------------+
|                                                                              |
|  +----------------------------------------+                                  |
|  |PatientCreatedIntegrationEventHandler    |                                  |
|  +--------------------+-------------------+                                  |
|                       |                                                      |
|                       v                                                      |
|  +----------------------------------------+     +------------------------+   |
|  | CreateBillingProfileCommandHandler      |---->|  BillingProfile.Create |   |
|  +----------------------------------------+     +------------------------+   |
|                                                                              |
+-----------------------------------------------------------------------------+
```

### Event Flow Summary

1. **Scheduling**: `Patient.Create()` raises `PatientCreatedEvent` (domain event)
2. **Scheduling**: `PatientCreatedEventHandler` queues `PatientCreatedIntegrationEvent`
3. **MassTransit**: Event published to RabbitMQ after transaction commits
4. **Billing**: `PatientCreatedIntegrationEventHandler` consumes the event
5. **Billing**: Handler sends `CreateBillingProfileCommand` via MediatR
6. **Billing**: `BillingProfile` entity created with reference to external patient ID

---

## Billing Context Overview

The Billing context manages financial aspects of patient care:

| Concept | Description |
|---------|-------------|
| **BillingProfile** | Aggregate root - represents a patient's billing information |
| **ExternalPatientId** | Reference to the patient in Scheduling context (not a foreign key) |
| **PaymentMethod** | Value object - stored payment method details |
| **Invoice** | Entity - generated for appointments/services |

### Bounded Context Boundaries

```
+---------------------------+     +---------------------------+
|     SCHEDULING CONTEXT    |     |      BILLING CONTEXT      |
+---------------------------+     +---------------------------+
| Patient                   |     | BillingProfile            |
|   - PatientId (Guid)      |     |   - BillingProfileId      |
|   - FirstName             |     |   - ExternalPatientId     |
|   - LastName              |     |   - Email                 |
|   - Email                 |     |   - FullName              |
|   - DateOfBirth           |     |   - BillingAddress        |
|                           |     |   - PaymentMethod         |
| Appointment               |     | Invoice                   |
|   - AppointmentId         |     |   - InvoiceId             |
|   - PatientId             |     |   - BillingProfileId      |
|   - DoctorId              |     |   - Amount                |
|   - ScheduledAt           |     |   - Status                |
+---------------------------+     +---------------------------+
        |                                    ^
        | PatientCreatedIntegrationEvent     |
        +----------------------------------->+
```

**Key principle**: The Billing context does NOT reference `Patient` directly. It maintains its own `BillingProfile` entity and stores the `ExternalPatientId` for correlation. This keeps contexts decoupled.

---

## Project Structure

Create the Billing bounded context with the same Clean Architecture layers as Scheduling:

```
Core/
+-- Billing/
    +-- Billing.Domain/
    |   +-- BillingProfiles/
    |   |   +-- BillingProfile.cs              # Aggregate root
    |   |   +-- BillingProfileId.cs            # Strongly-typed ID
    |   |   +-- PaymentMethod.cs               # Value object
    |   |   +-- Events/
    |   |       +-- BillingProfileCreatedEvent.cs
    |   +-- Invoices/
    |       +-- Invoice.cs
    |       +-- InvoiceStatus.cs
    |
    +-- Billing.Application/
    |   +-- BillingProfiles/
    |   |   +-- Commands/
    |   |   |   +-- CreateBillingProfileCommand.cs
    |   |   |   +-- CreateBillingProfileCommandHandler.cs
    |   |   |   +-- CreateBillingProfileCommandValidator.cs
    |   |   +-- Queries/
    |   |       +-- GetBillingProfileByPatientIdQuery.cs
    |   |       +-- GetBillingProfileByPatientIdQueryHandler.cs
    |   +-- ServiceCollectionExtensions.cs
    |
    +-- Billing.Infrastructure/
    |   +-- Persistence/
    |   |   +-- BillingDbContext.cs
    |   |   +-- Configurations/
    |   |       +-- BillingProfileConfiguration.cs
    |   +-- Consumers/
    |   |   +-- PatientCreatedIntegrationEventHandler.cs
    |   +-- ServiceCollectionExtensions.cs
    |
    +-- Billing.WebApi/                        # Separate API host (for Aspire)
        +-- Controllers/
        |   +-- BillingProfilesController.cs
        +-- Program.cs
        +-- appsettings.json
```

---

## Step-by-Step Implementation

### Step 1: Create Projects

```bash
# Navigate to solution root
cd C:\projects\DDD\DDD

# Create Billing.Domain
dotnet new classlib -n Billing.Domain -o Core/Billing/Billing.Domain
dotnet sln add Core/Billing/Billing.Domain

# Create Billing.Application
dotnet new classlib -n Billing.Application -o Core/Billing/Billing.Application
dotnet sln add Core/Billing/Billing.Application

# Create Billing.Infrastructure
dotnet new classlib -n Billing.Infrastructure -o Core/Billing/Billing.Infrastructure
dotnet sln add Core/Billing/Billing.Infrastructure

# Create Billing.WebApi (separate host for Aspire)
dotnet new webapi -n Billing.WebApi -o Core/Billing/Billing.WebApi
dotnet sln add Core/Billing/Billing.WebApi
```

### Step 2: Add Project References

```bash
# Billing.Domain references BuildingBlocks.Domain
cd Core/Billing/Billing.Domain
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.Domain

# Billing.Application references Billing.Domain and BuildingBlocks.Application
cd ../Billing.Application
dotnet add reference ../Billing.Domain
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.Application

# Billing.Infrastructure references Billing.Application, BuildingBlocks.Infrastructure, and IntegrationEvents
cd ../Billing.Infrastructure
dotnet add reference ../Billing.Application
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.Infrastructure.EfCore
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.Infrastructure.MassTransit
dotnet add reference ../../../Shared/IntegrationEvents

# Billing.WebApi references Billing.Infrastructure and BuildingBlocks
cd ../Billing.WebApi
dotnet add reference ../Billing.Infrastructure
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.WebApplications
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.Infrastructure.MassTransit
```

### Step 3: Create BillingProfile Entity

**Billing.Domain/BillingProfiles/BillingProfile.cs**:

```csharp
using BuildingBlocks.Domain;
using Billing.Domain.BillingProfiles.Events;

namespace Billing.Domain.BillingProfiles;

/// <summary>
/// Aggregate root representing a patient's billing information.
/// Created when a PatientCreatedIntegrationEvent is received from Scheduling.
/// </summary>
public class BillingProfile : Entity
{
    /// <summary>
    /// Reference to the patient in the Scheduling bounded context.
    /// This is NOT a foreign key - it's a correlation ID for cross-context lookups.
    /// </summary>
    public Guid ExternalPatientId { get; private set; }

    /// <summary>
    /// Contact email for billing communications.
    /// Copied from the integration event - not synchronized automatically.
    /// </summary>
    public string Email { get; private set; } = null!;

    /// <summary>
    /// Display name for invoices and communications.
    /// </summary>
    public string FullName { get; private set; } = null!;

    /// <summary>
    /// Billing address (optional, can be set later).
    /// </summary>
    public string? BillingAddress { get; private set; }

    /// <summary>
    /// When the billing profile was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    // Private constructor for EF Core
    private BillingProfile() { }

    /// <summary>
    /// Creates a new billing profile for a patient.
    /// </summary>
    public static BillingProfile Create(
        Guid externalPatientId,
        string email,
        string fullName)
    {
        var profile = new BillingProfile
        {
            Id = Guid.NewGuid(),
            ExternalPatientId = externalPatientId,
            Email = email,
            FullName = fullName,
            CreatedAt = DateTime.UtcNow
        };

        profile.AddDomainEvent(new BillingProfileCreatedEvent(
            profile.Id,
            externalPatientId,
            email,
            fullName));

        return profile;
    }

    /// <summary>
    /// Updates the billing address.
    /// </summary>
    public void UpdateBillingAddress(string address)
    {
        BillingAddress = address;
    }
}
```

**Billing.Domain/BillingProfiles/Events/BillingProfileCreatedEvent.cs**:

```csharp
using BuildingBlocks.Domain.Events;

namespace Billing.Domain.BillingProfiles.Events;

/// <summary>
/// Domain event raised when a billing profile is created.
/// </summary>
public record BillingProfileCreatedEvent(
    Guid BillingProfileId,
    Guid ExternalPatientId,
    string Email,
    string FullName
) : IDomainEvent;
```

### Step 4: Create Command and Handler

**Billing.Application/BillingProfiles/Commands/CreateBillingProfileCommand.cs**:

```csharp
using BuildingBlocks.Application.Cqrs;

namespace Billing.Application.BillingProfiles.Commands;

/// <summary>
/// Command to create a billing profile for a patient.
/// Typically triggered by PatientCreatedIntegrationEvent.
/// </summary>
public record CreateBillingProfileCommand : Command<Guid>
{
    /// <summary>
    /// The patient's ID from the Scheduling context.
    /// </summary>
    public required Guid ExternalPatientId { get; init; }

    /// <summary>
    /// Contact email for billing.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Patient's full name for invoices.
    /// </summary>
    public required string FullName { get; init; }
}
```

**Billing.Application/BillingProfiles/Commands/CreateBillingProfileCommandHandler.cs**:

```csharp
using Billing.Domain.BillingProfiles;
using BuildingBlocks.Application.Interfaces;
using MediatR;

namespace Billing.Application.BillingProfiles.Commands;

public class CreateBillingProfileCommandHandler
    : IRequestHandler<CreateBillingProfileCommand, Guid>
{
    private readonly IUnitOfWork _uow;

    public CreateBillingProfileCommandHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<Guid> Handle(
        CreateBillingProfileCommand request,
        CancellationToken cancellationToken)
    {
        // Check if profile already exists (idempotency)
        var existingProfile = await _uow
            .RepositoryFor<BillingProfile>()
            .FindAsync(bp => bp.ExternalPatientId == request.ExternalPatientId, cancellationToken);

        if (existingProfile != null)
        {
            // Already processed - return existing ID (idempotent)
            return existingProfile.Id;
        }

        // Create new billing profile
        var profile = BillingProfile.Create(
            request.ExternalPatientId,
            request.Email,
            request.FullName);

        _uow.RepositoryFor<BillingProfile>().Add(profile);
        await _uow.SaveChangesAsync(cancellationToken);

        return profile.Id;
    }
}
```

**Billing.Application/BillingProfiles/Commands/CreateBillingProfileCommandValidator.cs**:

```csharp
using FluentValidation;

namespace Billing.Application.BillingProfiles.Commands;

public class CreateBillingProfileCommandValidator
    : AbstractValidator<CreateBillingProfileCommand>
{
    public CreateBillingProfileCommandValidator()
    {
        RuleFor(x => x.ExternalPatientId)
            .NotEmpty()
            .WithMessage("External patient ID is required");

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .WithMessage("Valid email is required");

        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(200)
            .WithMessage("Full name is required (max 200 characters)");
    }
}
```

### Step 5: Consume PatientCreatedIntegrationEvent

**Billing.Infrastructure/Consumers/PatientCreatedIntegrationEventHandler.cs**:

```csharp
using Billing.Application.BillingProfiles.Commands;
using BuildingBlocks.Infrastructure.MassTransit;
using IntegrationEvents.Scheduling;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Billing.Infrastructure.Consumers;

/// <summary>
/// Handles PatientCreatedIntegrationEvent from the Scheduling context.
/// Creates a BillingProfile for the new patient.
/// </summary>
public class PatientCreatedIntegrationEventHandler
    : IntegrationEventHandler<PatientCreatedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public PatientCreatedIntegrationEventHandler(
        IMediator mediator,
        ILogger<PatientCreatedIntegrationEventHandler> logger) : base(logger)
    {
        _mediator = mediator;
    }

    protected override async Task HandleAsync(
        PatientCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation(
            "Creating billing profile for patient {PatientId} ({FullName})",
            message.PatientId,
            $"{message.FirstName} {message.LastName}");

        var command = new CreateBillingProfileCommand
        {
            ExternalPatientId = message.PatientId,
            Email = message.Email,
            FullName = $"{message.FirstName} {message.LastName}"
        };

        var billingProfileId = await _mediator.Send(command, cancellationToken);

        Logger.LogInformation(
            "Created billing profile {BillingProfileId} for patient {PatientId}",
            billingProfileId,
            message.PatientId);
    }
}
```

### Step 6: Create EF Core Configuration

**Billing.Infrastructure/Persistence/BillingDbContext.cs**:

```csharp
using Billing.Domain.BillingProfiles;
using Microsoft.EntityFrameworkCore;

namespace Billing.Infrastructure.Persistence;

public class BillingDbContext : DbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options)
        : base(options)
    {
    }

    public DbSet<BillingProfile> BillingProfiles => Set<BillingProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

**Billing.Infrastructure/Persistence/Configurations/BillingProfileConfiguration.cs**:

```csharp
using Billing.Domain.BillingProfiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billing.Infrastructure.Persistence.Configurations;

public class BillingProfileConfiguration : IEntityTypeConfiguration<BillingProfile>
{
    public void Configure(EntityTypeBuilder<BillingProfile> builder)
    {
        builder.ToTable("BillingProfiles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExternalPatientId)
            .IsRequired();

        // Index for quick lookups by external patient ID
        builder.HasIndex(x => x.ExternalPatientId)
            .IsUnique();

        builder.Property(x => x.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.FullName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.BillingAddress)
            .HasMaxLength(500);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        // Ignore domain events (handled by EfCoreUnitOfWork)
        builder.Ignore(x => x.DomainEvents);
    }
}
```

### Step 7: Create Service Collection Extensions

**Billing.Application/ServiceCollectionExtensions.cs**:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBillingApplication(this IServiceCollection services)
    {
        // MediatR handlers are registered via AddMediatR in WebApi
        // FluentValidation validators are registered via AddValidatorsFromAssembly

        return services;
    }
}
```

**Billing.Infrastructure/ServiceCollectionExtensions.cs**:

```csharp
using Billing.Infrastructure.Persistence;
using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Infrastructure.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBillingInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<BillingDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork<BillingDbContext>>();

        return services;
    }
}
```

### Step 8: Create Billing.WebApi

**Billing.WebApi/Program.cs**:

```csharp
using Billing.Application;
using Billing.Infrastructure;
using BuildingBlocks.Application;
using BuildingBlocks.Infrastructure.MassTransit.Configuration;
using BuildingBlocks.WebApplications.Filters;
using BuildingBlocks.WebApplications.Json;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
    options.Filters.Add<ExceptionToJsonFilter>();
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new SmartEnumJsonConverterFactory());
});

builder.Services.AddOpenApi();

// Add Billing infrastructure
// Connection string from user secrets (shared UserSecretsId with AppHost)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddBillingInfrastructure(connectionString);
builder.Services.AddBillingApplication();
builder.Services.AddDefaultPipelineBehaviors();

// Add MassTransit for event consumption
// RabbitMQ connection string injected by Aspire via WithReference(messaging)
builder.Services.AddMassTransitEventBus(builder.Configuration, configure =>
{
    // Register consumers from Billing.Infrastructure
    configure.AddConsumers(typeof(Billing.Infrastructure.ServiceCollectionExtensions).Assembly);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

**Billing.WebApi/appsettings.json**:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

**Note:**
- `ConnectionStrings` section removed (moved to user secrets)
- `RabbitMQ` section removed (injected by Aspire)
- Connection strings are now in user secrets (shared `UserSecretsId` with AppHost)

---

## Registering in Aspire AppHost

.NET Aspire orchestrates the services. Register Billing.WebApi in the AppHost.

**AppHost/Program.cs**:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
// NOTE: In this project, only RabbitMQ is managed by Aspire.
// SQL Server runs locally and uses connection strings from user secrets.
var messagingPassword = builder.AddParameter("messaging-password");
var messaging = builder.AddRabbitMQ("messaging", password: messagingPassword)
    .WithManagementPlugin();

// Services
// Each service reads its SQL Server connection string from user secrets (DefaultConnection)
var scheduling = builder.AddProject<Projects.Scheduling_WebApi>("scheduling-api")
    .WithReference(messaging);

var billing = builder.AddProject<Projects.Billing_WebApi>("billing-api")
    .WithReference(messaging);

builder.Build().Run();
```

### Configuration with User Secrets

In this project, SQL Server is NOT managed by Aspire. Connection strings come from user secrets:

```csharp
// In Billing.WebApi/Program.cs
// SQL Server connection from user secrets (shared across all WebApi projects)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
```

Aspire injects (via `WithReference(messaging)`):
- `ConnectionStrings:messaging` - RabbitMQ connection (Aspire-managed)

User secrets provide (via `dotnet user-secrets set`):
- `ConnectionStrings:DefaultConnection` - SQL Server connection (NOT Aspire-managed)

---

## Service-to-Service Communication Patterns

### Pattern 1: Event-Driven (Preferred)

```
Scheduling                    RabbitMQ                    Billing
    |                            |                           |
    | Publish(PatientCreated)    |                           |
    |--------------------------->|                           |
    |                            | Deliver to subscribers    |
    |                            |-------------------------->|
    |                            |                           | Create BillingProfile
    |                            |                           |
```

**Use for:**
- Data synchronization (creating corresponding entities)
- Notifications and triggers
- Eventual consistency scenarios

### Pattern 2: Request-Response (Use Sparingly)

```
Billing                       RabbitMQ                    Scheduling
    |                            |                           |
    | GetPatientRequest          |                           |
    |--------------------------->|-------------------------->|
    |                            |                           |
    |                            |<--------------------------|
    |<---------------------------|   GetPatientResponse      |
    |                            |                           |
```

**Use for:**
- Real-time data needs
- When you need a response before proceeding

**Implementation:**

```csharp
// In Billing context - request client
public class SomeService
{
    private readonly IRequestClient<GetPatientRequest> _client;

    public async Task<PatientData> GetPatientFromScheduling(Guid patientId)
    {
        var response = await _client.GetResponse<GetPatientResponse>(
            new GetPatientRequest { PatientId = patientId });
        return response.Message;
    }
}

// In Scheduling context - consumer
public class GetPatientRequestConsumer : IConsumer<GetPatientRequest>
{
    public async Task Consume(ConsumeContext<GetPatientRequest> context)
    {
        var patient = await _repository.GetById(context.Message.PatientId);
        await context.RespondAsync(new GetPatientResponse { ... });
    }
}
```

### Pattern 3: HTTP API (Direct Calls)

```
Billing                                                  Scheduling
    |                                                        |
    | GET /api/patients/{id}                                 |
    |------------------------------------------------------->|
    |<-------------------------------------------------------|
    |                        Patient JSON                    |
```

**Use for:**
- Admin/management operations
- When message broker overhead is unnecessary
- Synchronous queries with caching

**Implementation with Aspire Service Discovery:**

```csharp
// In Billing.WebApi/Program.cs
builder.Services.AddHttpClient<ISchedulingClient, SchedulingClient>(client =>
{
    // Aspire injects the service URL
    client.BaseAddress = new Uri("http://scheduling-api");
});

// Client implementation
public class SchedulingClient : ISchedulingClient
{
    private readonly HttpClient _httpClient;

    public async Task<PatientDto?> GetPatientAsync(Guid patientId)
    {
        var response = await _httpClient.GetAsync($"/api/patients/{patientId}");
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<PatientDto>();
    }
}
```

---

## Shared IntegrationEvents Project

Integration events are defined in a shared project that both contexts reference:

```
Shared/
+-- IntegrationEvents/
    +-- IntegrationEvents.csproj
    +-- Scheduling/
    |   +-- PatientCreatedIntegrationEvent.cs
    |   +-- PatientSuspendedIntegrationEvent.cs
    |   +-- AppointmentScheduledIntegrationEvent.cs
    +-- Billing/
        +-- InvoiceCreatedIntegrationEvent.cs
        +-- PaymentReceivedIntegrationEvent.cs
```

### Why a Shared Project?

| Approach | Pros | Cons |
|----------|------|------|
| **Shared project** | Type safety, compile-time checks, easy refactoring | Coupling through shared code |
| **Contracts per context** | Maximum isolation | Duplication, no compile-time checks |
| **Schema registry** | Version management, language-agnostic | Complexity, infrastructure overhead |

For a modular monolith, a shared project is pragmatic. For true microservices, consider a schema registry or contract-per-context.

### Event Naming Conventions

```csharp
// Pattern: {BoundedContext}{Entity}{Action}IntegrationEvent
namespace IntegrationEvents.Scheduling;

public record PatientCreatedIntegrationEvent : IntegrationEventBase { ... }
public record PatientSuspendedIntegrationEvent : IntegrationEventBase { ... }
public record AppointmentScheduledIntegrationEvent : IntegrationEventBase { ... }
public record AppointmentCancelledIntegrationEvent : IntegrationEventBase { ... }
```

---

## Full Event Flow Diagram

```
+-----------------------------------------------------------------------------+
|                        COMPLETE EVENT FLOW                                   |
+-----------------------------------------------------------------------------+

1. USER ACTION
   +----------+
   | POST     |
   | /patients|
   +----+-----+
        |
        v
2. SCHEDULING CONTEXT
   +------------------------------------------------------------------+
   |                                                                   |
   |  +--------------------+     +---------------------------+         |
   |  | PatientController  |---->| CreatePatientCommand      |         |
   |  +--------------------+     +---------------------------+         |
   |                                        |                          |
   |                                        v                          |
   |  +------------------------------------------------------------------+
   |  | TransactionBehavior                                              |
   |  +------------------------------------------------------------------+
   |  |  +-- BeginTransactionAsync()                                     |
   |  |                                                                  |
   |  |  +-- CreatePatientCommandHandler                                 |
   |  |  |       +-- Patient.Create()                                    |
   |  |  |       |       +-- AddDomainEvent(PatientCreatedEvent)         |
   |  |  |       +-- _uow.RepositoryFor<Patient>().Add(patient)          |
   |  |  |       +-- await _uow.SaveChangesAsync()                       |
   |  |  |               |                                               |
   |  |  |               +-- DispatchDomainEventsAsync()                 |
   |  |  |               |       +-- PatientCreatedEventHandler          |
   |  |  |               |               +-- Log                         |
   |  |  |               |               +-- QueueIntegrationEvent()     |
   |  |  |               +-- _context.SaveChangesAsync()                 |
   |  |  |                                                               |
   |  |  +-- CloseTransactionAsync()                                     |
   |  |       +-- CommitAsync()                                          |
   |  |       +-- PublishIntegrationEventsAsync() ---------------------->+ 3.
   |  +------------------------------------------------------------------+
   +------------------------------------------------------------------+
        |
        v
3. RABBITMQ
   +------------------------------------------------------------------+
   |  Exchange: IntegrationEvents.Scheduling:PatientCreatedIntegration |
   |                                                                   |
   |  Queues:                                                          |
   |    +-- scheduling-patient-created (Scheduling context, if any)    |
   |    +-- billing-patient-created (Billing context) ----------------->+ 4.
   +------------------------------------------------------------------+
        |
        v
4. BILLING CONTEXT
   +------------------------------------------------------------------+
   |                                                                   |
   |  +----------------------------------------+                       |
   |  | PatientCreatedIntegrationEventHandler  |                       |
   |  +----------------------------------------+                       |
   |              |                                                    |
   |              v                                                    |
   |  +----------------------------------------+                       |
   |  | CreateBillingProfileCommand            |                       |
   |  +----------------------------------------+                       |
   |              |                                                    |
   |              v                                                    |
   |  +----------------------------------------+                       |
   |  | CreateBillingProfileCommandHandler     |                       |
   |  |     +-- Check idempotency              |                       |
   |  |     +-- BillingProfile.Create()        |                       |
   |  |     +-- _uow.SaveChangesAsync()        |                       |
   |  +----------------------------------------+                       |
   |              |                                                    |
   |              v                                                    |
   |  +----------------------------------------+                       |
   |  | BillingDbContext                       |                       |
   |  |     INSERT INTO BillingProfiles        |                       |
   |  +----------------------------------------+                       |
   |                                                                   |
   +------------------------------------------------------------------+
```

---

## Verification Checklist

### Project Structure
- [ ] Billing.Domain project created with BillingProfile entity
- [ ] Billing.Application project created with commands/handlers
- [ ] Billing.Infrastructure project created with EF Core and consumers
- [ ] Billing.WebApi project created as separate API host
- [ ] All project references configured correctly

### Integration Events
- [ ] PatientCreatedIntegrationEvent in Shared/IntegrationEvents/Scheduling
- [ ] Billing.Infrastructure references IntegrationEvents project
- [ ] Handler inherits from IntegrationEventHandler<T>

### Event Flow
- [ ] Creating a patient in Scheduling publishes PatientCreatedIntegrationEvent
- [ ] Billing context consumes the event
- [ ] BillingProfile created in Billing database
- [ ] Handler is idempotent (re-processing same event is safe)

### Aspire Integration
- [ ] Billing.WebApi registered in AppHost
- [ ] RabbitMQ reference added to Billing service
- [ ] SQL Server database reference added
- [ ] Services start and communicate correctly

### RabbitMQ Verification
- [ ] RabbitMQ Management UI shows billing queue
- [ ] Messages flow from Scheduling to Billing
- [ ] No messages stuck in queue (consumer is processing)

---

## Common Issues and Solutions

| Issue | Cause | Solution |
|-------|-------|----------|
| Handler not receiving messages | Consumer not registered | Add `configure.AddConsumers(typeof(Billing.Infrastructure.ServiceCollectionExtensions).Assembly)` |
| Duplicate BillingProfiles | Handler not idempotent | Check for existing profile by ExternalPatientId before creating |
| Connection refused to RabbitMQ | RabbitMQ not running or wrong config | Verify docker-compose up, check appsettings.json |
| EF Core "no service for type IUnitOfWork" | Wrong DI registration | Ensure AddBillingInfrastructure called with correct DbContext |
| Event not published | TransactionBehavior not wrapping command | Ensure command inherits from Command<T> |

---

## Summary

You have learned how to:

1. **Add a second bounded context** that remains decoupled from the first
2. **Consume integration events** using `IntegrationEventHandler<T>`
3. **Create corresponding entities** across bounded contexts (Patient -> BillingProfile)
4. **Maintain idempotency** in event handlers
5. **Register services in Aspire** for orchestrated deployment
6. **Use the shared IntegrationEvents project** for type-safe event contracts

The Billing context is now fully reactive to the Scheduling context, automatically creating billing profiles for new patients while remaining completely decoupled at the code level.

---

> Next: [05-observability.md](./05-observability.md) - Adding distributed tracing and logging with .NET Aspire
