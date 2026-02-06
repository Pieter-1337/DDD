# RabbitMQ & MassTransit Setup

## Current Implementation Status

**Phase 5 Progress:**
- [x] Phase 5 documentation created (all 6 documents)
- [x] `BuildingBlocks.Messaging` project created (empty, ready for implementation)
- [x] `Shared/IntegrationEvents` project created (empty, ready for implementation)
- [ ] RabbitMQ Docker setup
- [ ] Integration event base classes implementation
- [ ] MassTransit configuration
- [ ] Event publishers and consumers
- [ ] Idempotency handling
- [ ] Error handling and dead letter queues

**Next Steps:**
1. Follow the implementation steps in this document to populate the empty projects
2. Set up RabbitMQ in Docker
3. Implement integration event infrastructure
4. Create first integration event and test end-to-end

## Overview

This document covers setting up the messaging infrastructure:
- **RabbitMQ** - The message broker (runs in Docker)
- **MassTransit** - .NET abstraction over message brokers

### Key Architectural Decisions

This setup uses THREE distinct projects for messaging:

| Project | Purpose | Contains |
|---------|---------|----------|
| **BuildingBlocks.Messaging** | Reusable abstractions | `IntegrationEventBase`, `IIntegrationEvent`, MassTransit helpers |
| **Shared/IntegrationEvents** | Integration event definitions | `AppointmentScheduledIntegrationEvent`, `PatientCreatedIntegrationEvent` |
| **[BC].Infrastructure** | BC-specific implementation | MassTransit configuration, publishers, consumers |

**Why this separation?**
- **BuildingBlocks.Messaging** = Technical infrastructure you could reuse in ANY domain
- **Shared/IntegrationEvents** = Your healthcare domain's public API between bounded contexts
- **Bounded contexts don't reference each other**, only the shared IntegrationEvents project
- Clear separation: reusable abstractions vs domain-specific events

**Think of it this way:**
```
BuildingBlocks.Messaging  = Generic envelope system (tracking numbers, timestamps)
Shared/IntegrationEvents  = The actual letters (healthcare events)
Scheduling/Billing/etc.   = The people writing and reading the letters
```

---

## Project Structure for Messaging

### Architecture Decision: BuildingBlocks.Messaging + Shared/IntegrationEvents

In this phase, we create two distinct projects for messaging infrastructure:
- **BuildingBlocks.Messaging** - Contains ONLY reusable abstractions and configuration helpers
- **Shared/IntegrationEvents** - Contains integration event definitions (the public API between bounded contexts)

This separation follows Domain-Driven Design principles and keeps BuildingBlocks truly generic and reusable.

### Why Separate Abstractions from Event Definitions?

```
src/
├── BuildingBlocks/
│   ├── BuildingBlocks.Messaging/          # Reusable messaging infrastructure (NEW)
│   │   ├── Abstractions/                  # IIntegrationEvent, IntegrationEventBase
│   │   └── Configuration/                 # MassTransit setup helpers
│   │
│   └── BuildingBlocks.Infrastructure/     # Persistence infrastructure (EXISTING)
│       ├── Persistence/                   # EF Core base classes
│       ├── Repositories/                  # Repository patterns
│       └── ...
│
├── Shared/
│   └── IntegrationEvents/                 # Integration event definitions (NEW)
│       ├── Scheduling/                    # AppointmentScheduledIntegrationEvent, etc.
│       ├── Billing/                       # PaymentProcessedIntegrationEvent, etc.
│       └── MedicalRecords/                # RecordUpdatedIntegrationEvent, etc.
│
├── Scheduling/
│   ├── Scheduling.Domain/
│   ├── Scheduling.Application/
│   └── Scheduling.Infrastructure/         # References: BuildingBlocks.Messaging, Shared/IntegrationEvents
│       ├── Persistence/                   # EF Core, repositories (internal)
│       └── Messaging/                     # Event publishers and consumers
│
├── Billing/
│   ├── Billing.Domain/
│   ├── Billing.Application/
│   └── Billing.Infrastructure/            # References: BuildingBlocks.Messaging, Shared/IntegrationEvents
│       ├── Persistence/
│       └── Messaging/
│
└── MedicalRecords/
    └── MedicalRecords.Infrastructure/     # References: BuildingBlocks.Messaging, Shared/IntegrationEvents
```

### The DDD Reasoning

| Concern | BuildingBlocks.Messaging | Shared/IntegrationEvents | BuildingBlocks.Infrastructure |
|---------|-------------------------|--------------------------|------------------------------|
| **Scope** | Reusable abstractions | Cross-bounded-context API | Internal to each bounded context |
| **Purpose** | Messaging infrastructure | Integration event definitions | Persistence implementation details |
| **Shared Kernel** | Yes - abstractions only | Yes - event definitions | No - each BC owns its persistence |
| **Consumers** | All bounded contexts | All bounded contexts | Each BC's Infrastructure project |
| **Examples** | `IIntegrationEvent`, `IntegrationEventBase`, MassTransit helpers | `AppointmentScheduledIntegrationEvent`, `PaymentProcessedIntegrationEvent` | `DbContext` base classes, repository patterns, EF Core configuration |
| **Domain Content** | NO - purely technical | YES - business events | NO - purely technical |

### Domain-Driven Design Principles

**1. Shared Kernel Pattern**

Integration events are the **public API** between bounded contexts. We separate the reusable abstractions from the actual event definitions to keep BuildingBlocks truly generic.

```
Shared Kernel - Abstractions (BuildingBlocks.Messaging):
- IntegrationEventBase base class (provides EventId, CorrelationId, etc.)
- IIntegrationEvent interface (marker interface)
- MassTransit configuration helpers
- NO domain-specific content

Shared Kernel - IntegrationEvents (Shared/IntegrationEvents project):
- AppointmentScheduledIntegrationEvent
- PatientCreatedIntegrationEvent
- PaymentProcessedIntegrationEvent
- All integration event definitions

NOT Shared Kernel (BuildingBlocks.Infrastructure):
- DbContext implementations
- Repository implementations
- EF Core configurations
- These are internal to each bounded context
```

**Why Separate Abstractions from IntegrationEvents?**

- **BuildingBlocks** = Reusable infrastructure that could work in ANY domain
- **Shared/IntegrationEvents** = Your specific domain's integration events
- If you started a new project (e.g., e-commerce), you'd reuse BuildingBlocks.Messaging but have different IntegrationEvents
- BuildingBlocks should NEVER contain domain-specific content

**2. Bounded Context Autonomy**

Each bounded context should be autonomous in its persistence strategy:
- **Scheduling** might use EF Core with SQL Server
- **Billing** might use Dapper with PostgreSQL
- **MedicalRecords** might use Cosmos DB

By keeping persistence infrastructure separate, each context can make independent technology choices while sharing:
- Messaging abstractions (BuildingBlocks.Messaging)
- Integration event definitions (Shared/IntegrationEvents)

**3. Cross-BC Communication via IntegrationEvents**

```
Cross-BC Concerns (via IntegrationEvents):  Internal Concerns (Persistence):
──────────────────────────────────────────  ─────────────────────────────────

Scheduling ←──[Events]──→ Billing          Scheduling → SQL Server
     ↕                                           ↓
     │                                      EF Core DbContext
     └────[Events]────→ MedicalRecords           ↓
                                            Repositories

All BCs reference IntegrationEvents        Each BC manages its own
Events = the public API between BCs        persistence independently
Bounded contexts never reference
each other directly
```

**Key Insight**: Bounded contexts communicate through integration events (Shared/IntegrationEvents), not through direct project references. This preserves autonomy and prevents tight coupling.

### Industry Example: eShopOnContainers

Microsoft's reference architecture [eShopOnContainers](https://github.com/dotnet-architecture/eShopOnContainers) follows this pattern:

```
eShopOnContainers/
├── BuildingBlocks/
│   ├── EventBus/                    # Cross-service messaging
│   │   ├── EventBus/                # Abstractions
│   │   ├── EventBusRabbitMQ/        # RabbitMQ implementation
│   │   └── EventBusServiceBus/      # Azure Service Bus implementation
│   │
│   └── ... (other building blocks, NOT including per-service persistence)
│
└── Services/
    ├── Ordering/
    │   └── Ordering.Infrastructure/  # Owns its persistence
    ├── Catalog/
    │   └── Catalog.Infrastructure/   # Owns its persistence
    └── Basket/
        └── Basket.Infrastructure/    # Owns its persistence
```

**Key Insight**: Microsoft separates EventBus (cross-service) from per-service Infrastructure projects.

### Benefits of This Structure

| Benefit | Description |
|---------|-------------|
| **Clear Separation** | Abstractions (technical) vs IntegrationEvents (domain) are explicitly separated |
| **Reusability** | BuildingBlocks.Messaging can be reused in other projects/domains |
| **No Coupling** | Bounded contexts don't reference each other, only Shared/IntegrationEvents |
| **Independent Evolution** | Persistence can evolve per-BC without affecting others |
| **Testability** | Can mock messaging infrastructure separately from persistence |
| **Microservice-Ready** | In Phase 6, each BC can become a microservice with its own Infrastructure |
| **Explicit Public API** | IntegrationEvents project = the public API between bounded contexts |
| **Versioning Ready** | IntegrationEvents can be versioned independently, or published as NuGet packages |

### When You Implement This

**Step 1: Create BuildingBlocks.Messaging Project**

```bash
dotnet new classlib -n BuildingBlocks.Messaging -o BuildingBlocks/BuildingBlocks.Messaging
dotnet sln add BuildingBlocks/BuildingBlocks.Messaging
```

**Step 2: Create Shared/IntegrationEvents Project**

```bash
dotnet new classlib -n IntegrationEvents -o Shared/IntegrationEvents
dotnet sln add Shared/IntegrationEvents
```

**Step 3: Add MassTransit Dependencies to BuildingBlocks.Messaging**

```bash
cd BuildingBlocks/BuildingBlocks.Messaging
dotnet add package MassTransit
dotnet add package MassTransit.RabbitMQ
```

**Step 4: Set Up Project References**

```bash
# IntegrationEvents references BuildingBlocks.Messaging (for IntegrationEventBase)
cd Shared/IntegrationEvents
dotnet add reference ../../BuildingBlocks/BuildingBlocks.Messaging

# Scheduling.Infrastructure references both
cd ../../Core/Scheduling/Scheduling.Infrastructure
dotnet add reference ../../../BuildingBlocks/BuildingBlocks.Messaging
dotnet add reference ../../../Shared/IntegrationEvents

# Future bounded contexts will follow the same pattern
```

### What Goes Where?

**BuildingBlocks.Messaging (Abstractions Only):**
- `IntegrationEventBase` base class
- `IIntegrationEvent` interface (marker interface)
- MassTransit configuration helpers
- Message serialization/correlation concerns
- **NO actual event definitions**

**Shared/IntegrationEvents (Integration Event Definitions):**
- `AppointmentScheduledIntegrationEvent`
- `PatientCreatedIntegrationEvent`
- `PaymentProcessedIntegrationEvent`
- All integration event DTOs
- Organized by bounded context: `Scheduling/`, `Billing/`, etc.

**Scheduling.Infrastructure/Messaging:**
- MassTransit consumer registrations
- Consumer implementations (handlers for incoming events)
- Event publishers (wrapping `IPublishEndpoint`)
- BC-specific messaging configuration

**BuildingBlocks.Infrastructure:**
- `DbContext` base classes
- Repository pattern base classes
- EF Core interceptors
- Persistence-related helpers
- Unit of Work pattern

---

## Clean Architecture Layer Dependencies

### Which Layer References Which BuildingBlocks Project?

Understanding the reference dependencies between Clean Architecture layers and BuildingBlocks projects is critical to maintaining proper separation of concerns and following the Dependency Inversion Principle.

#### The Architecture Diagram

```
Scheduling.Domain
    └── ❌ No BuildingBlocks references (pure domain)

Scheduling.Application
    └── ✅ BuildingBlocks.Messaging (IEventBus abstraction only)

Scheduling.Infrastructure
    ├── ✅ BuildingBlocks.Messaging (MassTransit implementation)
    ├── ✅ BuildingBlocks.Infrastructure (EF Core, repositories)
    └── Implements → IEventBus interface
```

#### Reference Table by Layer

| Project | BuildingBlocks.Infrastructure | BuildingBlocks.Messaging | Shared/IntegrationEvents |
|---------|------------------------------|-------------------------|--------------------------|
| **{BC}.Domain** | ❌ No | ❌ No | ❌ No |
| **{BC}.Application** | ❌ No | ✅ Yes (IEventBus interface) | ✅ Yes (for event DTOs) |
| **{BC}.Infrastructure** | ✅ Yes (EF Core, Repository base) | ✅ Yes (MassTransit config) | ✅ Yes (publishers/consumers) |

#### Why This Separation Exists

This architecture follows Clean Architecture and Dependency Inversion Principle:

**Domain Layer (Pure Business Logic)**
- **References**: NONE
- **Why**: Domain is the innermost layer with NO external dependencies
- **Contains**: Entities, aggregates, value objects, domain events (NOT integration events)
- **Rule**: Domain must remain pure and framework-agnostic

**Application Layer (Use Cases / Orchestration)**
- **References**: `BuildingBlocks.Messaging` (abstractions only)
- **Why**: Application layer orchestrates use cases and may need to publish integration events
- **Contains**: Command handlers, query handlers, DTOs, interfaces
- **Key Point**: Application depends on `IEventBus` abstraction, NOT the implementation
- **Example Usage**:
  ```csharp
  // Scheduling.Application/Commands/CreatePatientCommandHandler.cs
  public class CreatePatientCommandHandler
  {
      private readonly IEventBus _eventBus; // ← Abstraction from BuildingBlocks.Messaging

      public async Task<Result> Handle(CreatePatientCommand command)
      {
          // ... business logic ...

          // Publish integration event (doesn't know HOW it's published)
          await _eventBus.Publish(new PatientCreatedIntegrationEvent
          {
              PatientId = patient.Id,
              Email = patient.Email.Value
          });
      }
  }
  ```

**Infrastructure Layer (Implementations)**
- **References**:
  - `BuildingBlocks.Messaging` (implements IEventBus with MassTransit)
  - `BuildingBlocks.Infrastructure` (uses EF Core base classes, repositories)
  - `Shared/IntegrationEvents` (publishes and consumes integration events)
- **Why**: Infrastructure provides concrete implementations for abstractions defined in inner layers
- **Contains**:
  - MassTransit configuration and setup
  - Event publishers (implementing `IEventBus`)
  - Event consumers (handlers for incoming integration events)
  - EF Core DbContext, repository implementations
  - External service integrations
- **Example Usage**:
  ```csharp
  // Scheduling.Infrastructure/Messaging/MassTransitEventBus.cs
  public class MassTransitEventBus : IEventBus // ← Implements abstraction
  {
      private readonly IPublishEndpoint _publishEndpoint; // ← MassTransit dependency

      public async Task Publish<TEvent>(TEvent @event) where TEvent : IntegrationEventBase
      {
          await _publishEndpoint.Publish(@event); // ← MassTransit implementation detail
      }
  }
  ```

#### Dependency Flow Visualization

```
┌─────────────────────────────────────────────────────────────┐
│                      Application Layer                      │
│  ┌────────────────────────────────────────────────────┐     │
│  │ CreatePatientCommandHandler                        │     │
│  │                                                     │     │
│  │ _eventBus.Publish(new PatientCreated...);         │     │
│  │        ↓                                           │     │
│  │  Depends on IEventBus (abstraction)               │     │
│  └────────────────────────────────────────────────────┘     │
│                           ↓                                  │
│              References BuildingBlocks.Messaging             │
│                    (IEventBus interface)                     │
└─────────────────────────────────────────────────────────────┘
                            ↓
                   Dependency Inversion
                            ↓
┌─────────────────────────────────────────────────────────────┐
│                   Infrastructure Layer                       │
│  ┌────────────────────────────────────────────────────┐     │
│  │ MassTransitEventBus : IEventBus                   │     │
│  │                                                     │     │
│  │ Implements → IEventBus interface                  │     │
│  │ Uses → IPublishEndpoint (MassTransit)             │     │
│  │ Configures → RabbitMQ connection                  │     │
│  └────────────────────────────────────────────────────┘     │
│                           ↓                                  │
│              References BuildingBlocks.Messaging             │
│            References BuildingBlocks.Infrastructure          │
│                References Shared/IntegrationEvents           │
└─────────────────────────────────────────────────────────────┘
```

#### Why This Justifies Two BuildingBlocks Projects

**BuildingBlocks.Messaging (Abstractions)**
- **Purpose**: Define interfaces and base classes for messaging
- **Consumed by**: Application layer (abstractions), Infrastructure layer (implementations)
- **Contains**: `IEventBus`, `IntegrationEventBase`, messaging interfaces
- **Why separate**: Application layer needs messaging abstractions WITHOUT depending on implementation details

**BuildingBlocks.Infrastructure (Persistence)**
- **Purpose**: Define interfaces and base classes for persistence
- **Consumed by**: Infrastructure layer ONLY (EF Core, repositories)
- **Contains**: `IRepository<T>`, `IUnitOfWork`, EF Core base classes
- **Why separate**: Application layer should NOT know about persistence implementation details

**The Key Insight**

```
Application Layer Needs:
  ✅ IEventBus (to publish events) → BuildingBlocks.Messaging
  ❌ NOT DbContext, EF Core, repositories → BuildingBlocks.Infrastructure

Infrastructure Layer Needs:
  ✅ IEventBus (to implement it) → BuildingBlocks.Messaging
  ✅ IRepository, DbContext base classes → BuildingBlocks.Infrastructure
  ✅ Integration events to publish/consume → Shared/IntegrationEvents
```

If we merged BuildingBlocks.Messaging and BuildingBlocks.Infrastructure into a single project:
- **Problem**: Application layer would need to reference the combined BuildingBlocks project
- **Violation**: Application layer would now have access to EF Core, DbContext, and persistence details
- **Result**: Breaks Clean Architecture principle that Application should NOT depend on Infrastructure concerns

#### Real-World Example: Outbox Pattern

The Outbox Pattern demonstrates WHY Infrastructure needs BOTH BuildingBlocks projects:

```csharp
// Scheduling.Infrastructure/Messaging/OutboxProcessor.cs
public class OutboxProcessor
{
    private readonly ApplicationDbContext _dbContext;         // ← BuildingBlocks.Infrastructure (EF Core)
    private readonly IPublishEndpoint _publishEndpoint;       // ← BuildingBlocks.Messaging (MassTransit)

    public async Task ProcessOutboxMessages()
    {
        // 1. Read unpublished events from database (persistence concern)
        var outboxMessages = await _dbContext.OutboxMessages
            .Where(x => !x.IsPublished)
            .ToListAsync();                                    // ← EF Core

        // 2. Publish each event to message broker (messaging concern)
        foreach (var message in outboxMessages)
        {
            var @event = JsonSerializer.Deserialize<IntegrationEventBase>(message.Payload);
            await _publishEndpoint.Publish(@event);            // ← MassTransit

            message.IsPublished = true;
        }

        // 3. Save changes back to database (persistence concern)
        await _dbContext.SaveChangesAsync();                   // ← EF Core
    }
}
```

**Why this needs BOTH BuildingBlocks projects:**
- Needs persistence (EF Core, DbContext) → BuildingBlocks.Infrastructure
- Needs messaging (MassTransit, IPublishEndpoint) → BuildingBlocks.Messaging
- Demonstrates that Infrastructure layer bridges persistence and messaging concerns
- Application layer only sees `IEventBus.Publish()` - doesn't know about outbox pattern

#### Benefits of This Architecture

| Benefit | Description |
|---------|-------------|
| **Dependency Inversion** | Application depends on abstractions (`IEventBus`), not implementations (MassTransit) |
| **Testability** | Application layer can be tested with a fake `IEventBus` implementation |
| **Flexibility** | Can swap MassTransit for Azure Service Bus without changing Application layer |
| **Clear Boundaries** | Application knows "what" (publish event), Infrastructure knows "how" (MassTransit + RabbitMQ) |
| **Separation of Concerns** | Messaging abstractions (BuildingBlocks.Messaging) separate from persistence (BuildingBlocks.Infrastructure) |

#### Summary: The Architecture Rules

1. **Domain Layer**: No external dependencies. Period.
2. **Application Layer**:
   - References `BuildingBlocks.Messaging` for `IEventBus` abstraction
   - Does NOT reference `BuildingBlocks.Infrastructure` (no persistence concerns)
   - Calls `IEventBus.Publish(event)` without knowing the implementation
3. **Infrastructure Layer**:
   - References `BuildingBlocks.Messaging` (implements `IEventBus` with MassTransit)
   - References `BuildingBlocks.Infrastructure` (implements repositories, uses EF Core)
   - References `Shared/IntegrationEvents` (publishes and consumes integration events)
   - Bridges persistence and messaging concerns (e.g., Outbox Pattern)

This architecture ensures:
- Application layer remains focused on business use cases, not infrastructure details
- Infrastructure layer can change technology choices (swap MassTransit for Azure Service Bus) without affecting Application layer
- Clean separation between messaging concerns (BuildingBlocks.Messaging) and persistence concerns (BuildingBlocks.Infrastructure)
- Follows Clean Architecture and SOLID principles (especially Dependency Inversion)

---

## Understanding the Architecture: A Learning Perspective

Let's solidify why we separate BuildingBlocks.Messaging from Shared/IntegrationEvents with a practical example.

**Question: Why not just put all events in BuildingBlocks.Messaging?**

**Problem with putting events in BuildingBlocks:**

```csharp
// BAD: BuildingBlocks.Messaging/Events/PatientCreatedIntegrationEvent.cs
// This creates problems:

namespace BuildingBlocks.Messaging.Events;

public record PatientCreatedIntegrationEvent : IntegrationEventBase
{
    public Guid PatientId { get; init; }
    public string Email { get; init; } = string.Empty;
    // ... healthcare-specific properties
}
```

**Why this is wrong:**
1. BuildingBlocks now contains healthcare domain concepts (Patient, Email)
2. You can't reuse BuildingBlocks in a different domain (e-commerce, banking, etc.)
3. BuildingBlocks becomes a "God project" that contains everything
4. Violates the principle: "BuildingBlocks = reusable infrastructure, NOT domain content"

**Correct approach with Shared/IntegrationEvents:**

```csharp
// GOOD: BuildingBlocks.Messaging/Abstractions/IntegrationEventBase.cs
// Reusable in ANY domain

namespace BuildingBlocks.Messaging.Abstractions;

public abstract record IntegrationEventBase
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
}

// GOOD: Shared/IntegrationEvents/Scheduling/PatientCreatedIntegrationEvent.cs
// Healthcare-specific

namespace IntegrationEvents.Scheduling;

public record PatientCreatedIntegrationEvent : IntegrationEventBase
{
    public Guid PatientId { get; init; }
    public string Email { get; init; } = string.Empty;
    // ... healthcare-specific properties
}
```

**Why this is correct:**
1. BuildingBlocks.Messaging = Pure technical infrastructure (EventId, CorrelationId, MassTransit setup)
2. Shared/IntegrationEvents = Your healthcare domain's public API between bounded contexts
3. If you start a new project (e-commerce), you copy BuildingBlocks.Messaging, but write new IntegrationEvents
4. Clear separation of concerns: technical vs domain

**Real-World Analogy:**

Think of BuildingBlocks.Messaging as a **generic envelope system**:
- The envelope provides structure: sender, recipient, timestamp, tracking number
- The letter inside (Shared/IntegrationEvents) contains the actual message content
- You can use the same envelope system for love letters, business letters, or legal documents
- But you wouldn't put specific letter content into the envelope manufacturing company's catalog!

**This is a .NET Learning Project:**

We use a single Shared/IntegrationEvents project (not per-BC projects, not NuGet packages, not schema registry) because:
- You're working in a monolithic .NET solution (all code compiles together)
- Simple structure helps you focus on DDD concepts, not deployment complexity
- Easy to navigate: all integration events in one place
- When you move to microservices (Phase 6 or beyond), you can:
  - Split IntegrationEvents into per-BC projects
  - Publish as NuGet packages
  - Migrate to a schema registry (if going polyglot)

**Key Takeaway:**
- BuildingBlocks = "I could use this in ANY project"
- Shared/IntegrationEvents = "This is specific to MY healthcare domain"
- Bounded Contexts = "This is specific to MY scheduling/billing/records concerns"

---

## Why MassTransit?

MassTransit is a .NET library that provides:

| Feature | Description |
|---------|-------------|
| **Broker abstraction** | Switch between RabbitMQ, Azure Service Bus, Amazon SQS |
| **Message serialization** | JSON, XML, BSON out of the box |
| **Retry policies** | Configurable retry with exponential backoff |
| **Error handling** | Dead letter queues, fault handling |
| **Saga support** | State machine-based orchestration |
| **Testing** | In-memory transport for tests |

Without MassTransit, you'd write low-level RabbitMQ code:

```csharp
// Raw RabbitMQ - lots of boilerplate
var factory = new ConnectionFactory { HostName = "localhost" };
using var connection = factory.CreateConnection();
using var channel = connection.CreateModel();

channel.QueueDeclare("patient-created", durable: true, exclusive: false, autoDelete: false);
var body = JsonSerializer.SerializeToUtf8Bytes(message);
channel.BasicPublish(exchange: "", routingKey: "patient-created", body: body);
```

With MassTransit:

```csharp
// MassTransit - clean and simple
await _publishEndpoint.Publish(new PatientCreatedIntegrationEvent { ... });
```

---

## Running RabbitMQ - Three Options

You have three options for running RabbitMQ:

| Option | Pros | Cons |
|--------|------|------|
| **Docker** | Isolated, easy cleanup, matches prod | Need to learn Docker basics |
| **Windows Installer** | Native, no Docker needed | Installs Erlang system-wide, harder cleanup |
| **Cloud (CloudAMQP)** | No local install, free tier | Requires internet, slight latency |

Choose what works for you. All three work with MassTransit.

---

## Option 1: Docker (Recommended)

### What is Docker?

Docker runs applications in isolated "containers" - like lightweight virtual machines.

```
Without Docker:                    With Docker:
───────────────                    ────────────
Install Erlang on Windows          docker-compose up
Install RabbitMQ on Windows        (Downloads and runs everything)
Configure environment variables
Start RabbitMQ service             docker-compose down
Uninstall when done (messy)        (Clean removal, nothing left behind)
```

**Key concepts:**
- **Image** - A template (like `rabbitmq:3-management`)
- **Container** - A running instance of an image
- **docker-compose** - Tool to run multiple containers together

### Install Docker Desktop

1. Download from https://www.docker.com/products/docker-desktop/
2. Install and restart Windows
3. Open Docker Desktop (it runs in the background)
4. Verify in terminal:
   ```bash
   docker --version
   # Docker version 24.x.x
   ```

### docker-compose.yml

Create a `docker-compose.yml` file in your solution root:

```yaml
services:
  rabbitmq:
    image: rabbitmq:3-management
    container_name: ddd-rabbitmq
    restart: unless-stopped
    ports:
      - "0.0.0.0:5672:5672"    # AMQP port (messaging)
      - "0.0.0.0:15672:15672"  # Management UI
    environment:
      - RABBITMQ_DEFAULT_USER=guest
      - RABBITMQ_DEFAULT_PASS=guest
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "check_running"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  rabbitmq_data:
```

**Why `0.0.0.0:` prefix?**

The `0.0.0.0:5672:5672` format explicitly binds container ports to all network interfaces on the Windows host. With Docker Desktop on Windows, this ensures the ports are accessible from:
- Your .NET application (connecting via `localhost:5672`)
- Other containers in the Docker network
- External tools (like RabbitMQ clients or management tools)

Without the `0.0.0.0:` prefix, Docker Desktop may bind to IPv6 (`::1`) or other interfaces, potentially causing connection issues from Windows applications.

**Note**: This project uses SQL Server LocalDB (configured in Phase 2) for the database, not Docker. Only RabbitMQ runs in Docker.

### What About Data Persistence?

Will you lose your messages (including DLQ) if Docker stops or you restart your PC?

**No** - the `volumes` in docker-compose.yml persist data to disk:

```
WITHOUT volumes:                    WITH volumes (our setup):
────────────────                    ─────────────────────────

┌─────────────┐                     ┌─────────────┐
│  Container  │                     │  Container  │
│  (RabbitMQ) │                     │  (RabbitMQ) │
│             │                     │      │      │
│  [messages] │ ← Lost on stop      │      │      │
└─────────────┘                     └──────┼──────┘
                                           │
                                           ▼
                                    ┌─────────────┐
                                    │   Volume    │ ← Persists on disk
                                    │(rabbitmq_   │
                                    │   data)     │
                                    └─────────────┘
```

This line in docker-compose.yml saves RabbitMQ data:
```yaml
volumes:
  - rabbitmq_data:/var/lib/rabbitmq
```

**When is data preserved vs lost?**

| Action | Messages Preserved? |
|--------|---------------------|
| Restart PC | Yes |
| `docker-compose stop` | Yes |
| `docker-compose down` | Yes |
| `docker-compose down -v` | **No** (the `-v` deletes volumes) |

**Verify your volumes exist:**

```bash
# List volumes
docker volume ls
# DRIVER    VOLUME NAME
# local     ddd_rabbitmq_data

# See where it's stored on disk
docker volume inspect ddd_rabbitmq_data
```

### Start Services

**Important: Container Lifecycle**

Containers stop when:
- You restart your PC
- You run `docker-compose stop` or `docker-compose down`
- Docker Desktop is closed/restarted

**You need to start containers manually after each PC restart**, unless you configure auto-start (see below).

#### Basic Container Management

```bash
# Start all services (runs in background with -d flag)
docker-compose up -d

# Check if services are running
docker-compose ps
# OR
docker ps

# View RabbitMQ logs
docker-compose logs rabbitmq

# Access RabbitMQ Management UI
# http://localhost:15672
# Username: guest
# Password: guest
```

#### Auto-Start Options

**Option A: Docker Desktop Auto-Start**

1. Open Docker Desktop
2. Go to Settings → General
3. Enable "Start Docker Desktop when you log in"
4. Configure restart policy in docker-compose.yml (see Option B)

**Option B: Container Restart Policy**

Update your `docker-compose.yml` to automatically restart containers when Docker starts:

```yaml
services:
  rabbitmq:
    image: rabbitmq:3-management
    container_name: ddd-rabbitmq
    restart: unless-stopped  # ← Add this line
    ports:
      - "0.0.0.0:5672:5672"    # AMQP port
      - "0.0.0.0:15672:15672"  # Management UI
    # ... rest of config
```

**Recommended: `unless-stopped`** - Containers restart when Docker starts, but respect manual stops.

#### Making "F5 Just Work" - Automatic Docker Startup in Visual Studio

**The Problem**: You press F5 in Visual Studio, but RabbitMQ isn't running. Your app fails with connection errors.

**The Solution**: This project uses MSBuild targets to automatically check and start Docker containers before every debug session.

**Step 1: Create `scripts/ensure-docker.ps1`**

This PowerShell script checks if Docker and RabbitMQ are running, starting them if needed. Create this file in your solution root:

```powershell
# scripts/ensure-docker.ps1
param(
    [int]$TimeoutSeconds = 60
)

Write-Host "Ensuring Docker containers are running..." -ForegroundColor Cyan

# Check if Docker Desktop is running
$dockerProcess = Get-Process "Docker Desktop" -ErrorAction SilentlyContinue
if (-not $dockerProcess) {
    Write-Host "ERROR: Docker Desktop is not running." -ForegroundColor Red
    Write-Host "Please start Docker Desktop and try again." -ForegroundColor Yellow
    exit 1
}

# Check if RabbitMQ container is running
$rabbitmqRunning = docker ps --filter "name=ddd-rabbitmq" --filter "status=running" --format "{{.Names}}"

if ($rabbitmqRunning) {
    Write-Host "RabbitMQ container already running." -ForegroundColor Green
    exit 0
}

# Start RabbitMQ container
Write-Host "Starting RabbitMQ container..." -ForegroundColor Yellow
Push-Location $PSScriptRoot\..
docker-compose up -d
Pop-Location

# Wait for RabbitMQ health check
Write-Host "Waiting for RabbitMQ to be ready..." -ForegroundColor Yellow
$elapsed = 0
while ($elapsed -lt $TimeoutSeconds) {
    $health = docker inspect --format="{{.State.Health.Status}}" ddd-rabbitmq 2>$null
    if ($health -eq "healthy") {
        Write-Host "RabbitMQ is ready!" -ForegroundColor Green
        exit 0
    }
    Start-Sleep -Seconds 2
    $elapsed += 2
}

Write-Host "ERROR: RabbitMQ health check timed out after $TimeoutSeconds seconds." -ForegroundColor Red
exit 1
```

**Step 2: Create `Directory.Build.targets`**

This project uses MSBuild targets to automatically run the Docker script. Create `Directory.Build.targets` in your solution root (same directory as the .sln file):

```xml
<Project>

  <!--
    Docker startup targets for WebApi projects.
    These targets ensure Docker services (RabbitMQ, SQL Server) are running before build/run.
    Automatically inherited by all projects in the solution.

    Strategy: Use a SINGLE target that runs on EVERY Build, even cached builds.
    - We remove the Inputs/Outputs incremental build optimization
    - The script itself is idempotent (fast exit if containers already running)
    - This ensures F5 ALWAYS checks and starts containers if needed
  -->

  <Target Name="EnsureDockerServices" BeforeTargets="Build">
    <Exec Command="powershell -ExecutionPolicy Bypass -File &quot;$(MSBuildThisFileDirectory)scripts\ensure-docker.ps1&quot;"
          IgnoreExitCode="false" />
  </Target>

</Project>
```

**How It Works**

`Directory.Build.targets` is automatically imported by MSBuild into ALL projects in the solution tree. This approach:

| Feature | Benefit |
|---------|---------|
| **Automatic inheritance** | Works for ALL projects in the solution |
| **No manual configuration** | Add once, works everywhere |
| **Future-proof** | New microservices in Phase 6 inherit automatically |
| **Visual Studio specific** | Uses MSBuild, the native build system |

**Why a Single Target?**

This implementation uses a single target that runs on **every** build:
- No incremental build optimization (no Inputs/Outputs timestamp caching)
- The PowerShell script itself is idempotent and exits quickly if containers are already running
- Ensures Docker is always checked before every F5, even on cached builds
- Simple and reliable approach for local development

**Path Resolution: $(MSBuildThisFileDirectory)**

The target uses `$(MSBuildThisFileDirectory)` instead of `$(SolutionDir)` because:
- `$(MSBuildThisFileDirectory)` - Reliable path to Directory.Build.targets (the solution root)
- `$(SolutionDir)` - Can be unreliable in some MSBuild scenarios (especially dotnet CLI builds)

**Step 3: Test It**

1. Ensure Docker Desktop is running
2. Press F5 in Visual Studio
3. Watch the Build Output window - you'll see the Docker script output
4. Your app will start with RabbitMQ already running

This single-target approach guarantees Docker containers are always running when you debug, whether you made code changes or not. The configuration is complete and will automatically apply to all future API projects.

**Future: .NET Aspire (Phase 6)**

.NET Aspire is Microsoft's orchestration stack that automatically manages Docker containers, service discovery, and observability. We'll explore it in Phase 6 when working with multiple microservices. For now, the MSBuild approach keeps things simple while you learn DDD/CQRS patterns.

#### Checking if Services Are Already Running

Before starting containers, check if they're already running:

```bash
# Check all running containers
docker ps

# Check specific services
docker-compose ps

# Expected output when running:
# NAME            IMAGE                 STATUS
# ddd-rabbitmq    rabbitmq:3-management Up 2 hours
```

If status shows "Up", RabbitMQ is already running - no need to start again.

**Note**: You'll only see the RabbitMQ container here. SQL Server LocalDB (configured in Phase 2) runs as a Windows service, not in Docker.

### Docker Commands Cheat Sheet

```bash
# Start containers in background (detached mode)
docker-compose up -d

# Start containers in foreground (shows logs, blocks terminal)
docker-compose up
# Press Ctrl+C to stop containers

# Check if containers are running
docker-compose ps
docker ps  # All containers, not just current project

# Stop containers (keeps data)
docker-compose stop

# Start stopped containers
docker-compose start

# Stop and remove containers (keeps data in volumes)
docker-compose down

# Stop, remove containers AND delete data
docker-compose down -v

# View logs
docker-compose logs rabbitmq
docker-compose logs -f rabbitmq  # Follow/stream logs (real-time)

# Restart a specific service
docker-compose restart rabbitmq

# Execute commands inside running container
docker exec -it ddd-rabbitmq bash
```

**Key Differences:**

| Command | What Happens | Use When |
|---------|--------------|----------|
| `docker-compose up` | Starts containers, shows logs in terminal | Debugging, watching logs |
| `docker-compose up -d` | Starts containers in background (detached) | Normal development (recommended) |
| `docker-compose stop` | Stops containers, doesn't remove them | Temporarily stop services |
| `docker-compose down` | Stops and removes containers, keeps volumes | Clean up when done for the day |
| `docker-compose down -v` | Stops, removes containers AND deletes data | Complete reset (careful!) |

---

## NuGet Packages

Add these packages to your Infrastructure project:

```bash
# Core MassTransit
dotnet add package MassTransit

# RabbitMQ transport
dotnet add package MassTransit.RabbitMQ
```

For the Application project (if you define contracts there):

```bash
# Only if you need IBus/IPublishEndpoint interfaces
dotnet add package MassTransit.Abstractions
```

---

## Implementation Structure

Now that we've established the architectural reasoning (see "Project Structure for Messaging" above), here's the concrete implementation layout:

```
src/
├── BuildingBlocks/
│   ├── BuildingBlocks.Messaging/           # Reusable abstractions (NEW)
│   │   ├── Abstractions/
│   │   │   ├── IntegrationEventBase.cs    # Base class for all integration events
│   │   │   └── IIntegrationEvent.cs       # (Optional) Marker interface
│   │   └── Configuration/
│   │       └── MassTransitExtensions.cs   # Configuration helpers
│   │
│   └── BuildingBlocks.Infrastructure/      # Persistence (EXISTING, separate)
│       └── ...
│
├── Shared/
│   └── IntegrationEvents/                  # Integration event definitions (NEW)
│       ├── Scheduling/
│       │   ├── AppointmentScheduledIntegrationEvent.cs
│       │   └── PatientCreatedIntegrationEvent.cs
│       ├── Billing/
│       │   └── PaymentProcessedIntegrationEvent.cs
│       └── MedicalRecords/
│           └── RecordUpdatedIntegrationEvent.cs
│
├── Scheduling/
│   ├── Scheduling.Domain/
│   ├── Scheduling.Application/
│   └── Scheduling.Infrastructure/
│       ├── Persistence/                    # EF Core, repositories (internal)
│       └── Messaging/                      # MassTransit setup and consumers
│           ├── MassTransitConfiguration.cs
│           ├── Publishers/
│           │   └── ...
│           └── Consumers/
│               └── ...
│
└── Billing/                                # Future Phase 6
    ├── Billing.Domain/
    ├── Billing.Application/
    └── Billing.Infrastructure/
        ├── Persistence/
        └── Messaging/
```

### Where to Put Integration Events?

For this learning project, we use a **shared IntegrationEvents project approach**:

**Our Approach: Shared/IntegrationEvents Project**

```
BuildingBlocks.Messaging/
└── Abstractions/
    └── IntegrationEventBase.cs             # Base class (provides EventId, CorrelationId, etc.)

Shared/IntegrationEvents/
├── Scheduling/
│   ├── AppointmentScheduledIntegrationEvent.cs
│   └── PatientCreatedIntegrationEvent.cs
├── Billing/
│   └── PaymentProcessedIntegrationEvent.cs
└── MedicalRecords/
    └── RecordUpdatedIntegrationEvent.cs

Scheduling.Infrastructure → References Shared/IntegrationEvents (publishes and consumes events)
Billing.Infrastructure → References Shared/IntegrationEvents (publishes and consumes events)
```

**Why This Approach?**

| Benefit | Explanation |
|---------|-------------|
| **Clean Separation** | BuildingBlocks = technical abstractions, IntegrationEvents = business events |
| **No Coupling** | Bounded contexts don't reference each other, only the shared IntegrationEvents |
| **Single Source of Truth** | Each event defined once, used by multiple BCs |
| **Simple for Learning** | All events in one place, easy to navigate |
| **Polyglot-Ready** | If you move to other languages later, IntegrationEvents can become a schema registry |
| **DDD-Aligned** | Events are the public API between bounded contexts |

**Alternative Approaches (for reference):**

**Option A: Per-BC IntegrationEvents Projects (Enterprise Pattern)**
```
Scheduling.IntegrationEvents/
└── PatientCreatedIntegrationEvent.cs

Billing.IntegrationEvents/
└── PaymentProcessedIntegrationEvent.cs

Each BC publishes a NuGet package with its events.
Other BCs install the NuGet packages they need.

+ Production-ready pattern
+ Independent versioning per BC
+ Can evolve events independently
- More ceremony for a learning project
- Overkill for a monolithic .NET solution
```

**Option B: Each BC Owns Events in Application Layer**
```
Scheduling.Application/IntegrationEvents/
└── PatientCreatedIntegrationEvent.cs

Billing.Application → References Scheduling.Application to consume events

+ Clear ownership (Scheduling owns PatientCreatedIntegrationEvent)
+ Events live with the BC that publishes them
- Creates coupling (Billing references Scheduling)
- Violates bounded context independence
- Hard to evolve independently
```

**Option C: Schema Registry (Polyglot Microservices)**
```
Events defined in JSON Schema, Avro, or Protobuf
Stored in a schema registry (Confluent, AWS Glue, etc.)
Code generated in each language (C#, Java, Python)

+ Language-agnostic
+ Production-grade microservices pattern
+ Strong versioning support
- Overkill for a .NET learning project
- Adds complexity with code generation
```

**Our Choice:** Single Shared/IntegrationEvents project balances simplicity (all events in one place) with good DDD practices (no BC-to-BC coupling). Perfect for a .NET monolithic learning project. If you move to polyglot microservices later, migrate to Option C (schema registry).

---

## Base Integration Event

Create a base class in `BuildingBlocks.Messaging` that all integration events will inherit from. This base class contains ONLY technical infrastructure concerns, NOT domain-specific content.

```csharp
// BuildingBlocks.Messaging/Abstractions/IntegrationEventBase.cs
namespace BuildingBlocks.Messaging.Abstractions;

/// <summary>
/// Base class for all integration events.
/// Integration events cross bounded context boundaries via message broker.
/// </summary>
/// <remarks>
/// This class is part of the Shared Kernel (abstractions only).
/// It provides technical infrastructure for distributed messaging:
/// - EventId enables idempotent message handling
/// - OccurredAt provides event ordering and auditing
/// - CorrelationId enables distributed tracing across bounded contexts
///
/// IMPORTANT: This class should contain NO domain-specific logic.
/// Actual event definitions belong in the Shared/IntegrationEvents project.
/// </remarks>
public abstract record IntegrationEventBase
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// Used for idempotent message handling - consumers can track processed EventIds
    /// to avoid processing the same event multiple times.
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// When the event was created (UTC).
    /// Useful for event ordering, auditing, and troubleshooting.
    /// </summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// Links this event to the original request/transaction that triggered it.
    /// Populated from HttpContext or command context.
    /// </summary>
    public string? CorrelationId { get; init; }
}
```

**Why This Lives in BuildingBlocks.Messaging:**

- Provides reusable technical infrastructure for ALL integration events
- Contains NO domain-specific content (could work in any domain)
- Part of the messaging Shared Kernel (abstractions only)
- Changes to this class affect all BCs (should be rare and carefully managed)

**Usage Example:**

```csharp
// Shared/IntegrationEvents/Scheduling/PatientCreatedIntegrationEvent.cs
using BuildingBlocks.Messaging.Abstractions;

namespace IntegrationEvents.Scheduling;

/// <summary>
/// Published when a new patient is created in the Scheduling bounded context.
/// Consumed by Billing and MedicalRecords bounded contexts.
/// </summary>
public record PatientCreatedIntegrationEvent : IntegrationEventBase
{
    public Guid PatientId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public DateTime DateOfBirth { get; init; }
}
```

**Key Insight:**
- `IntegrationEventBase` (BuildingBlocks.Messaging) = Reusable technical abstraction
- `PatientCreatedIntegrationEvent` (Shared/IntegrationEvents) = Your specific domain event
- BuildingBlocks could be reused in an e-commerce project, a banking project, etc.
- IntegrationEvents are specific to your healthcare domain

---

## MassTransit Configuration

### Basic Setup

```csharp
// Scheduling.Infrastructure/Messaging/MassTransitConfiguration.cs
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Infrastructure.Messaging;

public static class MassTransitConfiguration
{
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMassTransit(x =>
        {
            // Register consumers from this assembly
            x.AddConsumers(typeof(MassTransitConfiguration).Assembly);

            x.UsingRabbitMq((context, cfg) =>
            {
                var rabbitMqSettings = configuration.GetSection("RabbitMQ");

                cfg.Host(
                    rabbitMqSettings["Host"] ?? "localhost",
                    rabbitMqSettings["VirtualHost"] ?? "/",
                    h =>
                    {
                        h.Username(rabbitMqSettings["Username"] ?? "guest");
                        h.Password(rabbitMqSettings["Password"] ?? "guest");
                    });

                // Configure endpoints for all registered consumers
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
```

### Configuration in appsettings.json

```json
{
  "RabbitMQ": {
    "Host": "localhost",
    "VirtualHost": "/",
    "Username": "guest",
    "Password": "guest"
  }
}
```

### Register in Program.cs

```csharp
// Program.cs
builder.Services.AddMessaging(builder.Configuration);
```

---

## Testing the Connection

### Health Check

```csharp
// Add health check for RabbitMQ
builder.Services.AddHealthChecks()
    .AddRabbitMQ(
        rabbitConnectionString: "amqp://guest:guest@localhost:5672",
        name: "rabbitmq",
        tags: new[] { "messaging", "rabbitmq" });
```

### Simple Publisher Test

Create a minimal endpoint to test publishing:

```csharp
// In a controller or minimal API
using IntegrationEvents.Scheduling;

app.MapPost("/test-publish", async (IPublishEndpoint publishEndpoint) =>
{
    await publishEndpoint.Publish(new PatientCreatedIntegrationEvent
    {
        PatientId = Guid.NewGuid(),
        Email = "test@example.com",
        FullName = "Test Patient",
        DateOfBirth = new DateTime(1985, 6, 15)
    });

    return Results.Ok("Message published");
});
```

Check RabbitMQ Management UI at `http://localhost:15672`:
- Queues tab shows created queues
- Exchanges tab shows the exchange
- Messages can be viewed in queue details

---

## MassTransit Conventions

MassTransit uses conventions for queue/exchange naming:

```
Event: PatientCreatedIntegrationEvent
  └── Exchange: IntegrationEvents.Scheduling:PatientCreatedIntegrationEvent
      └── Queue: patient-created-integration-event (for each consumer)
```

### Customizing Endpoint Names

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    // Use kebab-case for queue names
    cfg.ConfigureEndpoints(context, new KebabCaseEndpointNameFormatter(false));
});
```

---

## Retry Configuration

Configure retry policies for resilience:

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    cfg.Host(...);

    // Global retry policy
    cfg.UseMessageRetry(r =>
    {
        r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30)
        );

        // Don't retry certain exceptions
        r.Ignore<ValidationException>();
        r.Ignore<ArgumentException>();
    });

    cfg.ConfigureEndpoints(context);
});
```

---

## In-Memory Transport for Testing

For integration tests, use the in-memory transport:

```csharp
// In test setup
services.AddMassTransitTestHarness(x =>
{
    x.AddConsumer<PatientCreatedIntegrationEventConsumer>();
});
```

Usage in tests:

```csharp
using IntegrationEvents.Scheduling;

[TestMethod]
public async Task Should_Consume_PatientCreated_Event()
{
    // Arrange
    var harness = _serviceProvider.GetRequiredService<ITestHarness>();
    await harness.Start();

    var @event = new PatientCreatedIntegrationEvent
    {
        PatientId = Guid.NewGuid(),
        Email = "test@test.com",
        FullName = "Test Patient",
        DateOfBirth = new DateTime(1985, 6, 15)
    };

    // Act
    await harness.Bus.Publish(@event);

    // Assert
    Assert.IsTrue(await harness.Consumed.Any<PatientCreatedIntegrationEvent>());
}
```

---

## Verification Checklist

- [ ] RabbitMQ running (Docker, Windows service, or CloudAMQP)
- [ ] RabbitMQ Management UI accessible (http://localhost:15672 or cloud dashboard)
- [ ] Can log into Management UI with credentials
- [ ] **BuildingBlocks.Messaging** project created with abstractions only
- [ ] **Shared/IntegrationEvents** project created for integration event definitions
- [ ] MassTransit NuGet packages installed in BuildingBlocks.Messaging
- [ ] `IntegrationEventBase` base class created in BuildingBlocks.Messaging/Abstractions
- [ ] IntegrationEvents project references BuildingBlocks.Messaging
- [ ] Bounded context Infrastructure projects reference both BuildingBlocks.Messaging and Shared/IntegrationEvents
- [ ] MassTransit configured with RabbitMQ transport
- [ ] appsettings.json has correct RabbitMQ connection details
- [ ] Health check for RabbitMQ (optional but recommended)
- [ ] Test publish endpoint working
- [ ] Messages visible in RabbitMQ Management UI

---

## Summary: Project Structure Recap

**Three-Project Messaging Architecture:**

```
1. BuildingBlocks.Messaging (Abstractions)
   - IntegrationEventBase class
   - MassTransit configuration helpers
   - NO domain-specific content
   - Could be reused in any domain (e-commerce, banking, etc.)

2. Shared/IntegrationEvents (Integration Event Definitions)
   - AppointmentScheduledIntegrationEvent
   - PatientCreatedIntegrationEvent
   - PaymentProcessedIntegrationEvent
   - The public API between bounded contexts
   - Domain-specific (healthcare)

3. [BoundedContext].Infrastructure (Implementation)
   - MassTransit setup
   - Event publishers
   - Event consumers
   - References both BuildingBlocks.Messaging and Shared/IntegrationEvents
```

**Key Rules:**
1. BuildingBlocks.Messaging = Reusable technical infrastructure ONLY
2. Shared/IntegrationEvents = Domain-specific integration events
3. Bounded contexts communicate via IntegrationEvents, NOT by referencing each other
4. This keeps bounded contexts autonomous and loosely coupled

**Why This Matters:**
- **Separation of Concerns**: Technical vs domain concerns are explicitly separated
- **Reusability**: BuildingBlocks can be reused across different domains
- **No Coupling**: Bounded contexts remain independent
- **Clear Public API**: IntegrationEvents define the integration interface between BCs
- **Easy to Navigate**: All integration events in one place (for this learning project)

**Future Evolution:**
- Phase 6: Multiple microservices will all reference the same IntegrationEvents project
- Beyond: Can migrate to per-BC IntegrationEvents (NuGet packages) or schema registry (polyglot)

---

> Next: [03-integration-events.md](./03-integration-events.md) - Defining and publishing integration events
