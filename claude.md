# DDD & Event-Driven Architecture Learning Project

## Overview
This is a comprehensive learning project to master Domain-Driven Design (DDD) and Event-Driven Architecture patterns. The project builds incrementally, implementing each concept step-by-step.

## Developer Context
- **Name**: Pieter
- **Tech Stack**: C# .NET 9, Blazor Server
- **Current Role**: .NET Developer
- **Azure Experience**: .NET, C#, Cosmos DB, SQL Server, Azure Service Bus, Azure App Configuration
- **Goal**: Deep dive into DDD, event-driven architecture, RabbitMQ, SQL Server/EF Core, CQRS

## Project Structure & Learning Path

### Phase 1: DDD Fundamentals
**Goal**: Build a solid foundation in tactical DDD patterns

**Concepts to implement**:
- Entities vs Value Objects
- Aggregates and Aggregate Roots
- Domain Events
- Repositories
- Domain Services
- Bounded Contexts

**Practice Domain**: Healthcare appointment scheduling
- Patient aggregate
- Appointment aggregate
- Doctor aggregate
- Bounded contexts: Scheduling, Billing, Medical Records

### Phase 2: Persistence with EF Core
**Goal**: Implement DDD patterns with SQL Server and Entity Framework Core

**Concepts to implement**:
- Repository pattern with EF Core
- Unit of Work pattern
- Mapping aggregates to tables
- Owned entities for value objects
- Domain event handling with EF interceptors
- Optimistic concurrency
- Aggregate persistence strategies

**Database**: SQL Server (LocalDB for development)

### Phase 3: CQRS Pattern
**Goal**: Separate read and write models

**Concepts to implement**:
- Command handlers (write side)
- Query handlers (read side)
- Separate read and write database models
- Command validation
- Query optimization
- MediatR for CQRS implementation

### Phase 4: Testing
**Goal**: Establish test infrastructure for confidence and regression prevention

**Concepts to implement**:
- Two-tier test base hierarchy (ValidatorTestBase, TestBase<TContext>)
- Validator unit tests with mocked IUnitOfWork
- Handler integration tests with SQLite in-memory
- Domain entity unit tests
- Transaction-based test isolation
- MSTest, Shouldly, Moq, NBuilder

### Phase 5: Event-Driven Architecture
**Goal**: Implement asynchronous communication between bounded contexts

**Concepts to implement**:
- Domain events vs Integration events
- RabbitMQ for message bus
- MassTransit for .NET integration
- Event publishing and subscribing
- Saga patterns for distributed transactions
- Event versioning and schema evolution
- Idempotent message handlers
- Dead letter queues and error handling

**Message Broker**: RabbitMQ (running in Docker)

### Phase 6: Integration
**Goal**: Build a cohesive system integrating all concepts

**System features**:
- Multiple microservices/bounded contexts
- Event-driven communication
- CQRS in each service
- Shared kernel for common domain concepts
- API Gateway pattern
- Health checks and observability

## Coding Standards

### C# Conventions
- Use C# 12 features (primary constructors, collection expressions)
- Prefer records for value objects
- Use required properties where applicable
- Follow async/await best practices
- Use nullable reference types

### Project Organization
```
src/
├── Core/
│   ├── Domain/              # Domain entities, aggregates, value objects
│   ├── Application/         # Application services, DTOs, interfaces
│   └── Infrastructure/      # EF Core, repositories, external services
├── Scheduling/              # Bounded context: Scheduling
├── Billing/                 # Bounded context: Billing
├── MedicalRecords/          # Bounded context: Medical Records
└── Shared/                  # Shared kernel
tests/
├── UnitTests/
├── IntegrationTests/
└── ArchitectureTests/       # Verify DDD rules
```

### DDD Rules to Enforce
- Aggregates accessed only through root
- No public setters on domain entities (encapsulation)
- Domain events for state changes
- Value objects are immutable
- Business logic in domain, not services
- Repositories only for aggregate roots

### Naming Conventions
- Commands: `CreateAppointmentCommand`, `CancelAppointmentCommand`
- Events: `AppointmentCreatedEvent`, `AppointmentCancelledEvent`
- Handlers: `CreateAppointmentCommandHandler`, `AppointmentCreatedEventHandler`
- Queries: `GetAppointmentByIdQuery`, `GetPatientAppointmentsQuery`

## Technology Decisions

### Core Stack
- **.NET 9** - Latest LTS
- **C# 12** - Latest language features
- **EF Core 9** - ORM for SQL Server
- **SQL Server** - Primary database
- **RabbitMQ** - Message broker
- **MassTransit** - .NET service bus abstraction
- **MediatR** - CQRS implementation
- **FluentValidation** - Command/query validation
- **Polly** - Resilience and transient fault handling

### Testing
- **xUnit** - Test framework
- **FluentAssertions** - Assertion library
- **Moq** - Mocking framework
- **Testcontainers** - Integration testing with Docker
- **ArchUnitNET** - Architecture rule testing

### Development Tools
- **Docker** - RabbitMQ, SQL Server
- **Swagger/OpenAPI/Scalar** - API documentation
- **Serilog** - Structured logging

## Docker Services
```yaml
# docker-compose.yml for local development
services:
  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
  
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourStrong@Password
    ports:
      - "1433:1433"
```

## Current Phase
**Current**: Phase 5 - Event-Driven Architecture

### Completed Phases
- ✅ Phase 1: DDD Fundamentals
- ✅ Phase 2: Persistence with EF Core
- ✅ Phase 3: CQRS Pattern
- ✅ Phase 4: Testing (30 tests passing)

## Learning Resources Referenced
- Pluralsight: "Domain-Driven Design Fundamentals" (Julie Lerman & Steve Smith)
- Pluralsight: "Domain-Driven Design in Practice" (Vladimir Khorikov)
- Pluralsight: "CQRS in Practice" (Vladimir Khorikov)
- Pluralsight: "Building Event-Driven Microservices with MassTransit" (Roland Guijt)
- Microsoft eBook: ".NET Microservices: Architecture for Containerized .NET Applications"

## Next Steps
1. Set up solution structure with Clean Architecture layers
2. Implement first aggregate (Patient) with proper encapsulation
3. Create value objects (PatientId, Email, PhoneNumber)
4. Implement repository pattern with in-memory implementation first
5. Add EF Core persistence
6. Progress through each phase incrementally

## Notes for Claude Code
- When suggesting code, follow the DDD tactical patterns strictly
- Prioritize domain modeling over technical concerns
- Ask clarifying questions about domain concepts
- Suggest tests for domain logic
- Point out violations of DDD principles
- Reference learning materials when explaining concepts